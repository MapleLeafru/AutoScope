# -*- coding: utf-8 -*-
import sys
import json
import re
import time
import os
import datetime
import html as html_lib
from urllib.parse import urljoin, urlparse

import requests
from bs4 import BeautifulSoup

sys.stdout.reconfigure(encoding="utf-8")
sys.stderr.reconfigure(encoding="utf-8", errors="replace")

# Лёгкий Auto.ru-парсер без Selenium.
# Основной режим: собирает данные прямо из карточек выдачи без перехода в объявления.
# Причина: Auto.ru часто отдаёт SmartCaptcha при переходе по отдельным карточкам через requests.
# stdout: только JSON-батчи с объявлениями.
# stderr: логи и progress-события.
# Значения площадки сохраняются без смысловой нормализации AutoScope.

USER_AGENT = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
    "AppleWebKit/537.36 (KHTML, like Gecko) "
    "Chrome/120.0 Safari/537.36"
)

DEFAULT_TIMEOUT_SECONDS = 25
DEFAULT_REQUEST_DELAY_SECONDS = 1.2
DEFAULT_RETRY_COUNT = 3
DEFAULT_RATE_LIMIT_DELAY_SECONDS = 5.0

STAGE_TITLES = {
    "listing_pages": "Сбор объявлений из выдачи",
    "single_ad": "Обработка карточки объявления",
    "blocked": "Сбор остановлен защитой Auto.ru",
    "rate_limit": "Ограничение запросов",
    "done": "Завершение",
    "error": "Ошибка",
}

STAGE_NUMBERS = {
    "listing_pages": (1, 1),
    "single_ad": (1, 1),
    "blocked": (1, 1),
}

ROOT_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
DEBUG_DIR = os.path.join(ROOT_DIR, "Logs", "ParserDebug")


class AutoRuBlockedError(RuntimeError):
    # Отдельная ошибка для капчи/авторизации, чтобы не делать бесполезные повторы.
    pass


def log(message):
    # Пишет служебные сообщения в stderr, чтобы не ломать JSON-вывод stdout.
    print(f"[LightBaseAutoRuParser] {message}", file=sys.stderr, flush=True)


def emit(batch):
    # Отправляет один батч объявлений в stdout.
    print(json.dumps(batch, ensure_ascii=False), flush=True)


def progress(stage, current, total, message, stage_title=None, stage_index=None, stage_total=None):
    # Отправляет событие прогресса в stderr.
    # stage — технический идентификатор этапа.
    # stageTitle — человекочитаемое название этапа для консольного вывода AutoScope.
    # stageIndex/stageTotal — положение рабочего этапа в общей схеме парсера.
    percent = 0
    if total:
        percent = int((current / total) * 100)
        if current >= total:
            percent = 100

    payload = {
        "stage": stage,
        "stageTitle": stage_title or STAGE_TITLES.get(stage, stage),
        "current": current,
        "total": total,
        "percent": percent,
        "message": message,
    }

    if stage_index is None or stage_total is None:
        stage_number = STAGE_NUMBERS.get(stage)
        if stage_number:
            stage_index, stage_total = stage_number

    if stage_index is not None and stage_total is not None:
        payload["stageIndex"] = stage_index
        payload["stageTotal"] = stage_total

    print("[PROGRESS] " + json.dumps(payload, ensure_ascii=False), file=sys.stderr, flush=True)


def safe_int(value):
    # Безопасно переводит строку с числами в int.
    if value is None:
        return None

    value = str(value)
    value = value.replace("\xa0", " ")
    value = re.sub(r"\D", "", value)
    return int(value) if value else None


def clean_text(value):
    # Убирает HTML-мусор, entities и лишние пробелы.
    if value is None:
        return None

    value = str(value)
    value = re.sub(r"<script.*?</script>", " ", value, flags=re.DOTALL | re.IGNORECASE)
    value = re.sub(r"<style.*?</style>", " ", value, flags=re.DOTALL | re.IGNORECASE)
    value = re.sub(r"<[^>]+>", " ", value)
    value = html_lib.unescape(value)
    value = value.replace("\xa0", " ")
    value = re.sub(r"\s+", " ", value).strip()

    return value if value else None


def element_text(element):
    # Возвращает очищенный текст BeautifulSoup-элемента.
    if not element:
        return None

    return clean_text(element.get_text(" ", strip=True))


def class_contains(fragment):
    # Возвращает функцию для поиска элементов по части CSS-класса.
    def matcher(value):
        if not value:
            return False

        if isinstance(value, list):
            return any(fragment in item for item in value)

        return fragment in str(value)

    return matcher


def read_input_settings():
    # Читает JSON-запрос, который передаёт InputPipelineManager.
    raw = sys.stdin.read()

    if not raw:
        raise RuntimeError("Parser received empty input")

    input_data = json.loads(raw)
    settings = input_data.get("parserSettings", {}) or {}

    start_url = settings.get("startUrl")
    max_cars = settings.get("maxCars")
    batch_size = settings.get("streamBatchSize")

    if not start_url:
        raise RuntimeError("START_URL is required")
    if max_cars is None:
        raise RuntimeError("MAX_CARS is required")
    if not batch_size:
        raise RuntimeError("STREAM_BATCH_SIZE is required")

    request_delay = float(settings.get("requestDelaySeconds", DEFAULT_REQUEST_DELAY_SECONDS))

    return {
        "start_url": str(start_url),
        "max_cars": int(max_cars),
        "batch_size": int(batch_size),
        "request_delay": request_delay,
        "listing_page_delay": float(settings.get("listingPageDelaySeconds", request_delay)),
        "retry_count": int(settings.get("retryCount", DEFAULT_RETRY_COUNT)),
        "rate_limit_delay": float(settings.get("rateLimitDelaySeconds", DEFAULT_RATE_LIMIT_DELAY_SECONDS)),
    }


def create_session():
    # Создаёт HTTP-сессию с заголовками, похожими на обычный браузер.
    session = requests.Session()
    session.headers.update(
        {
            "User-Agent": USER_AGENT,
            "Accept": "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8",
            "Accept-Language": "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7",
            "Cache-Control": "no-cache",
            "Pragma": "no-cache",
            "Connection": "keep-alive",
            "Upgrade-Insecure-Requests": "1",
            "Sec-Fetch-Dest": "document",
            "Sec-Fetch-Mode": "navigate",
            "Sec-Fetch-Site": "none",
            "Sec-Fetch-User": "?1",
            "sec-ch-ua": '"Google Chrome";v="120", "Chromium";v="120", "Not=A?Brand";v="99"',
            "sec-ch-ua-mobile": "?0",
            "sec-ch-ua-platform": '"Windows"',
        }
    )
    return session


def warmup_session(session):
    # Загружает главную страницу Auto.ru, чтобы сессия получила базовые cookies.
    try:
        response = session.get("https://auto.ru/", timeout=DEFAULT_TIMEOUT_SECONDS, allow_redirects=True)
        log(f"Warmup request status: {response.status_code}")
    except Exception as error:
        log(f"Warmup request failed: {error}")


def save_debug_html(url, page_html, reason):
    # Сохраняет проблемную HTML-страницу для последующей диагностики.
    os.makedirs(DEBUG_DIR, exist_ok=True)

    timestamp = datetime.datetime.now().strftime("%Y-%m-%d_%H-%M-%S")
    file_name = f"autoru_{reason}_{timestamp}.html"
    file_path = os.path.join(DEBUG_DIR, file_name)

    header = (
        "<!-- AutoScope Auto.ru parser diagnostic dump\n"
        f"URL: {url}\n"
        f"Reason: {reason}\n"
        f"Created at: {timestamp}\n"
        "-->\n"
    )

    with open(file_path, "w", encoding="utf-8-sig") as file:
        file.write(header)
        file.write(page_html or "")

    log(f"Diagnostic HTML saved: {file_path}")
    return file_path


def get_page_title(page_html):
    # Достаёт title страницы для диагностики.
    soup = BeautifulSoup(page_html or "", "html.parser")
    title = soup.find("title")
    return element_text(title)


def detect_blocked_page(url, page_html):
    # Определяет страницы авторизации, капчи и другие заглушки Auto.ru.
    text = clean_text(BeautifulSoup(page_html or "", "html.parser").get_text(" ", strip=True)) or ""
    lower_text = text.lower()
    lower_html = (page_html or "").lower()

    block_markers = [
        "подтвердите, что вы не робот",
        "вы не робот",
        "проверка безопасности",
        "smartcaptcha",
        "captcha",
        "войдите",
        "авторизуйтесь",
        "требуется авторизация",
        "доступ ограничен",
        "слишком много запросов",
    ]

    if any(marker in lower_text for marker in block_markers) or any(marker in lower_html for marker in block_markers):
        debug_path = save_debug_html(url, page_html, "blocked_page")
        raise AutoRuBlockedError(
            "Auto.ru returned authorization, captcha or access restriction page. "
            f"Title: {get_page_title(page_html) or 'заголовок не найден'}. "
            f"Debug HTML: {debug_path}"
        )


def fetch_html(session, url, retry_count, rate_limit_delay):
    # Загружает HTML-страницу с повторами при временных сетевых ошибках.
    last_error = None

    for attempt in range(retry_count + 1):
        try:
            response = session.get(url, timeout=DEFAULT_TIMEOUT_SECONDS, allow_redirects=True)

            if response.status_code == 429:
                wait_seconds = rate_limit_delay * (2 ** attempt)
                progress(
                    "rate_limit",
                    attempt + 1,
                    retry_count + 1,
                    f"Auto.ru временно ограничил запросы. Ждём {int(wait_seconds)} сек. и пробуем снова",
                )
                time.sleep(wait_seconds)
                continue

            if response.status_code in [401, 403]:
                raise AutoRuBlockedError(
                    f"Auto.ru returned HTTP {response.status_code}. "
                    "Возможна авторизация, капча или блокировка запроса."
                )

            response.raise_for_status()
            response.encoding = response.encoding or "utf-8"
            page_html = response.text

            final_url = str(response.url or "")
            if final_url and final_url != url:
                log(f"Final response URL: {final_url}")

            if any(marker in final_url.lower() for marker in ["/login", "/auth", "passport", "captcha"]):
                debug_path = save_debug_html(final_url, page_html, "blocked_or_redirected")
                raise AutoRuBlockedError(
                    "Auto.ru redirected request to authorization, captcha or access page. "
                    f"Debug HTML: {debug_path}"
                )

            detect_blocked_page(url, page_html)
            return page_html

        except AutoRuBlockedError:
            raise
        except requests.RequestException as error:
            last_error = f"{type(error).__name__}: {error}: {url}"
        except Exception as error:
            last_error = f"{type(error).__name__}: {error}: {url}"

        if attempt < retry_count:
            time.sleep(0.5)

    raise RuntimeError(last_error or f"Failed to fetch: {url}")


def is_auto_ru_ad_url(url):
    # Проверяет, похожа ли ссылка на карточку объявления Auto.ru.
    parsed = urlparse(url)
    path = parsed.path.lower()
    return "/cars/used/sale/" in path


def normalize_url_text(value):
    # Убирает HTML/JSON-экранирование, которое часто встречается внутри встроенного состояния страницы.
    if not value:
        return ""

    value = html_lib.unescape(str(value))
    value = value.replace("\\u002F", "/")
    value = value.replace("\\/", "/")

    return value


def add_ad_link(links, seen, raw_url, base_url):
    # Нормализует ссылку и добавляет её в список, если это карточка Auto.ru.
    if not raw_url:
        return

    raw_url = normalize_url_text(raw_url).strip()
    absolute_url = urljoin(base_url, raw_url).split("?")[0].split("#")[0]

    if not is_auto_ru_ad_url(absolute_url):
        return

    if absolute_url not in seen:
        seen.add(absolute_url)
        links.append(absolute_url)


def extract_ad_links(page_html, base_url):
    # Собирает уникальные ссылки на карточки объявлений со страницы выдачи.
    soup = BeautifulSoup(page_html, "html.parser")
    links = []
    seen = set()

    for link in soup.find_all("a", href=True):
        add_ad_link(links, seen, link.get("href"), base_url)

    normalized_html = normalize_url_text(page_html)
    patterns = [
        r"https?://auto\.ru/cars/used/sale/[^\s\"'<>]+/",
        r"/cars/used/sale/[^\s\"'<>]+/",
    ]

    for pattern in patterns:
        for match in re.findall(pattern, normalized_html, flags=re.IGNORECASE):
            add_ad_link(links, seen, match, base_url)

    return links


def extract_next_page_url(page_html, base_url):
    # Находит ссылку на следующую страницу выдачи.
    soup = BeautifulSoup(page_html, "html.parser")

    for link in soup.find_all("a", href=True):
        text = element_text(link) or ""
        classes = " ".join(link.get("class", []))

        if "ListingPagination__next" in classes or text.startswith("Следующая"):
            return urljoin(base_url, link.get("href"))

    return None


def find_listing_cards(page_html):
    # Находит контейнеры объявлений на странице выдачи.
    soup = BeautifulSoup(page_html, "html.parser")
    cards = []

    for element in soup.find_all("div"):
        classes = element.get("class") or []
        if any(cls.startswith("ListingItemUniversal-") for cls in classes):
            if element.find("a", href=lambda href: href and "/cars/used/sale/" in href):
                cards.append(element)

    return cards


def parse_title_parts(title, url=None):
    # Разделяет заголовок на марку и модель. Год обычно берётся отдельно.
    brand = None
    model = None

    if title:
        parts = title.split(" ", 1)
        brand = parts[0].strip() if parts else None
        model = parts[1].strip() if len(parts) > 1 else None

    if (not brand or not model) and url:
        path_parts = [part for part in urlparse(url).path.split("/") if part]
        try:
            sale_index = path_parts.index("sale")
            brand = brand or path_parts[sale_index + 1].replace("-", " ").title()
            model = model or path_parts[sale_index + 2].replace("-", " ").title()
        except Exception:
            pass

    return brand, model


def parse_engine(engine_raw):
    # Разбирает строку двигателя Auto.ru: "1.6 л, 170 л.с., бензин".
    if not engine_raw:
        return None, None, None, None

    engine_raw = clean_text(engine_raw) or ""
    lower_value = engine_raw.lower()

    engine_volume = None
    engine_power = None
    fuel_type = None
    powertrain = None

    volume_match = re.search(r"(\d+(?:[.,]\d+)?)\s*л", lower_value)
    if volume_match:
        engine_volume = float(volume_match.group(1).replace(",", "."))

    power_match = re.search(r"(\d+)\s*л\.?\s*с", lower_value)
    if power_match:
        engine_power = int(power_match.group(1))

    fuel_candidates = ["бензин", "дизель", "электро", "гибрид", "газ"]
    for fuel in fuel_candidates:
        if fuel in lower_value:
            fuel_type = fuel
            if fuel == "гибрид":
                powertrain = "гибрид"
            break

    return fuel_type, engine_volume, engine_power, powertrain


def extract_listing_price(card):
    # Достаёт цену из карточки выдачи.
    price_element = card.find(class_=class_contains("ListingItemUniversalPrice__title"))
    if not price_element:
        price_element = card.find(class_=class_contains("ListingItemUniversalPrice"))

    return safe_int(element_text(price_element))


def extract_listing_year(card):
    # Достаёт год выпуска из карточки выдачи.
    for text_node in card.find_all(string=True):
        text = clean_text(text_node)
        if text and re.fullmatch(r"(19|20)\d{2}", text):
            return int(text)

    return None


def extract_listing_mileage(card):
    # Достаёт пробег из карточки выдачи.
    for text_node in card.find_all(string=True):
        text = clean_text(text_node)
        if text and "км" in text.lower():
            return safe_int(text)

    return None


def extract_listing_region(card):
    # Достаёт регион/город из карточки выдачи.
    region = card.find(class_=class_contains("MetroListPlace__regionName"))
    if region:
        return element_text(region)

    seller_address = card.find(class_=class_contains("ListingItemUniversalSeller__sellerAddress"))
    if seller_address:
        text = element_text(seller_address)
        if text:
            return text.split(",")[0].strip()

    return None


def parse_listing_card(card, base_url):
    # Парсит одно объявление прямо из карточки выдачи Auto.ru.
    link_element = card.find("a", href=lambda href: href and "/cars/used/sale/" in href)
    url = urljoin(base_url, link_element.get("href")).split("?")[0].split("#")[0] if link_element else None

    title_element = card.find(class_=class_contains("ListingItemTitle__link"))
    if not title_element:
        title_element = link_element

    title = element_text(title_element)
    brand, model = parse_title_parts(title, url)

    color = element_text(card.find(class_=class_contains("ListingItemUniversalSpecs__subtitle")))

    specs_container = card.find(class_=class_contains("ListingItemUniversalSpecs__specs"))
    spec_values = []
    if specs_container:
        for spec in specs_container.find_all(class_=class_contains("ListingItemUniversalSpecs__spec"), recursive=False):
            value = element_text(spec)
            if value:
                spec_values.append(value)

    engine_raw = spec_values[0] if len(spec_values) > 0 else None
    body_type = spec_values[1] if len(spec_values) > 1 else None
    drive_type = spec_values[2] if len(spec_values) > 2 else None
    transmission = spec_values[3] if len(spec_values) > 3 else None

    fuel_type, engine_volume, engine_power, powertrain = parse_engine(engine_raw)

    return {
        "source": "auto.ru",
        "url": url,
        "brand": brand,
        "model": model,
        "price": extract_listing_price(card),
        "year": extract_listing_year(card),
        "sale_region": extract_listing_region(card),
        "license_plate": None,
        "mileage": extract_listing_mileage(card),
        "transmission": transmission,
        "drive_type": drive_type,
        "color": color,
        "body_type": body_type,
        "steering_wheel": None,
        "engine_power": engine_power,
        "engine_volume": engine_volume,
        "engine_model": None,
        "fuel_type": fuel_type,
        "octane": None,
        "powertrain": powertrain,
        "description": None,
    }


def collect_listing_cars(session, start_url, max_cars, listing_page_delay, retry_count, rate_limit_delay):
    # Собирает объявления со страниц выдачи без перехода по карточкам.
    target_count = max_cars if max_cars > 0 else 0
    cars = []
    seen_urls = set()
    seen_pages = set()
    current_url = start_url
    stopped_by_protection = False
    stop_message = None

    listing_stage_title = "Сбор объявлений из выдачи"
    progress("listing_pages", 0, target_count, "Начат сбор объявлений из выдачи", listing_stage_title)

    while current_url:
        if current_url in seen_pages:
            break

        seen_pages.add(current_url)
        log(f"Loading list page: {current_url}")

        try:
            page_html = fetch_html(session, current_url, retry_count, rate_limit_delay)
        except AutoRuBlockedError as error:
            if cars:
                stopped_by_protection = True
                if target_count:
                    stop_message = (
                        f"Auto.ru показал капчу на следующей странице. "
                        f"Сохраняем частично собранные объявления: {len(cars)} из {target_count}."
                    )
                else:
                    stop_message = (
                        f"Auto.ru показал капчу на следующей странице. "
                        f"Сохраняем частично собранные объявления: {len(cars)}."
                    )

                log(f"Listing collection stopped by Auto.ru protection: {error}")
                progress(
                    "blocked",
                    len(cars),
                    target_count,
                    stop_message,
                    "Сбор остановлен защитой Auto.ru",
                )
                break

            raise

        cards = find_listing_cards(page_html)
        links = extract_ad_links(page_html, current_url)

        if not cards and not links:
            debug_path = save_debug_html(current_url, page_html, "no_listing_items")
            raise RuntimeError(
                "Auto.ru page was loaded, but no listing cards were found. "
                "Возможна авторизация, антибот-заглушка или изменение разметки страницы. "
                f"Title: {get_page_title(page_html) or 'заголовок не найден'}. "
                f"Debug HTML: {debug_path}. "
                f"URL: {current_url}"
            )

        for card in cards:
            car = parse_listing_card(card, current_url)
            url = car.get("url")

            if not url or url in seen_urls:
                continue

            seen_urls.add(url)
            cars.append(car)

            if max_cars > 0 and len(cars) >= max_cars:
                break

        progress_total = max_cars if max_cars > 0 else 0
        progress_current = min(len(cars), progress_total) if progress_total else len(cars)
        progress(
            "listing_pages",
            progress_current,
            progress_total,
            f"Собрано объявлений из выдачи: {len(cars)}",
            listing_stage_title,
        )

        if max_cars > 0 and len(cars) >= max_cars:
            break

        next_url = extract_next_page_url(page_html, current_url)
        if not next_url or next_url == current_url:
            break

        current_url = next_url
        time.sleep(listing_page_delay)

    total = len(cars)

    if stopped_by_protection:
        if target_count:
            progress(
                "listing_pages",
                min(total, target_count),
                target_count,
                f"Сбор объявлений остановлен: собрано {total} из {target_count}",
                listing_stage_title,
            )
        else:
            progress(
                "listing_pages",
                total,
                total,
                f"Сбор объявлений остановлен: собрано {total}",
                listing_stage_title,
            )
    else:
        final_total = target_count if target_count else total
        final_current = min(total, final_total) if final_total else total
        progress(
            "listing_pages",
            final_current,
            final_total,
            f"Сбор объявлений из выдачи завершён: собрано {total} объявлений",
            listing_stage_title,
        )

    cars = cars[:max_cars] if max_cars > 0 else cars
    return cars, stop_message


def extract_summary_rows(soup):
    # Извлекает характеристики из блоков CardInfoSummary.
    specs = {}

    simple_rows = soup.find_all(class_=class_contains("CardInfoSummarySimpleRow"))
    for row in simple_rows:
        label_element = row.find(class_=class_contains("__label"))
        content_element = row.find(class_=class_contains("__content"))

        label = element_text(label_element)
        content = element_text(content_element)

        if label and content:
            specs[label] = content

    universal_rows = soup.find_all(class_=class_contains("CardInfoGroupedRow"))
    for row in universal_rows:
        label_element = row.find(class_=class_contains("__label"))
        content_element = row.find(class_=class_contains("__content"))

        label = element_text(label_element)
        content = element_text(content_element)

        if label and content:
            specs[label] = content

    return specs


def extract_card_title_parts(title, url=None):
    # Разделяет заголовок карточки объявления на марку, модель и год.
    brand = None
    model = None
    year = None

    if title:
        year_match = re.search(r"\b((?:19|20)\d{2})\b", title)
        if year_match:
            year = int(year_match.group(1))
            title = title[:year_match.start()].strip(" ,")

        parts = title.split(" ", 1)
        if parts:
            brand = parts[0].strip()
        if len(parts) > 1:
            model = parts[1].strip(" ,")

    if (not brand or not model) and url:
        fallback_brand, fallback_model = parse_title_parts(None, url)
        brand = brand or fallback_brand
        model = model or fallback_model

    return brand, model, year


def extract_sale_region(soup):
    # Пытается достать город/регион продажи из карточки объявления.
    candidates = [
        soup.find(class_=class_contains("MetroListPlace__regionName")),
        soup.find(class_=class_contains("SellerInfoPlace")),
        soup.find(class_=class_contains("CardSellerNamePlace")),
    ]

    for candidate in candidates:
        text = element_text(candidate)
        if text:
            return text.split(",")[0].strip()

    return None


def extract_description(soup):
    # Достаёт описание объявления из карточки.
    selectors = [
        ".CardDescriptionHTML",
        ".CardDescription__text",
        "[data-testid='description']",
    ]

    for selector in selectors:
        element = soup.select_one(selector)
        text = element_text(element)
        if text:
            return text

    return None


def parse_ad_page(url, page_html):
    # Парсит одну карточку объявления Auto.ru. Используется только для прямой ссылки на карточку.
    detect_blocked_page(url, page_html)
    soup = BeautifulSoup(page_html, "html.parser")

    title = element_text(soup.select_one("h1.CardHead__title"))
    if not title:
        title = element_text(soup.find("h1"))

    brand, model, title_year = extract_card_title_parts(title, url)
    specs = extract_summary_rows(soup)

    engine_raw = specs.get("Двигатель")
    fuel_type, engine_volume, engine_power, powertrain = parse_engine(engine_raw)

    return {
        "source": "auto.ru",
        "url": url,
        "brand": brand,
        "model": model,
        "price": safe_int(element_text(soup.select_one(".OfferPriceCaption__price"))),
        "year": safe_int(specs.get("Год выпуска")) or title_year,
        "sale_region": extract_sale_region(soup),
        "license_plate": specs.get("Госномер"),
        "mileage": safe_int(specs.get("Пробег")),
        "transmission": specs.get("Коробка"),
        "drive_type": specs.get("Привод"),
        "color": specs.get("Цвет"),
        "body_type": specs.get("Кузов"),
        "steering_wheel": specs.get("Руль"),
        "engine_power": engine_power,
        "engine_volume": engine_volume,
        "engine_model": specs.get("Модель двигателя"),
        "fuel_type": fuel_type,
        "octane": None,
        "powertrain": powertrain,
        "description": extract_description(soup),
    }


def run_single_ad_mode(session, start_url, settings):
    # Обрабатывает прямую ссылку на карточку объявления.
    single_stage_title = "Обработка карточки объявления"
    progress("single_ad", 0, 1, "Начата обработка карточки объявления", single_stage_title)

    page_html = fetch_html(session, start_url, settings["retry_count"], settings["rate_limit_delay"])
    car = parse_ad_page(start_url, page_html)
    emit([car])

    progress("single_ad", 1, 1, "Обработано объявлений: 1 из 1", single_stage_title)
    progress("done", 1, 1, "Парсер успешно завершил работу", "Завершение")


def run_listing_mode(session, settings):
    # Обрабатывает страницу выдачи и сохраняет данные из карточек выдачи.
    cars, stop_message = collect_listing_cars(
        session=session,
        start_url=settings["start_url"],
        max_cars=settings["max_cars"],
        listing_page_delay=settings["listing_page_delay"],
        retry_count=settings["retry_count"],
        rate_limit_delay=settings["rate_limit_delay"],
    )

    log(f"Collected listing cars: {len(cars)}")

    if not cars:
        progress("error", 0, 0, "Объявления в выдаче не найдены", "Ошибка")
        raise RuntimeError(
            "Auto.ru parser did not collect any listing cars. "
            "Запуск остановлен, чтобы не считать пустой сбор успешным."
        )

    batch = []
    total = len(cars)

    for car in cars:
        batch.append(car)

        if len(batch) >= settings["batch_size"]:
            emit(batch)
            batch = []

    if batch:
        emit(batch)

    if stop_message:
        progress(
            "done",
            total,
            total,
            f"Парсер завершил работу частично: сохранено {total} объявлений. {stop_message}",
            "Завершение",
        )
    else:
        progress("done", total, total, "Парсер успешно завершил работу", "Завершение")


def main():
    # Основной поток работы парсера.
    settings = read_input_settings()
    session = create_session()
    warmup_session(session)

    if is_auto_ru_ad_url(settings["start_url"]):
        run_single_ad_mode(session, settings["start_url"], settings)
    else:
        run_listing_mode(session, settings)


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        log(f"Fatal error: {error}")
        progress("error", 0, 0, str(error), "Ошибка")
        sys.exit(1)
