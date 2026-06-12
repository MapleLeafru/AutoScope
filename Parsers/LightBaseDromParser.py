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

# Лёгкий Drom-парсер без Selenium.
# Собирает данные прямо из карточек выдачи, без перехода в каждое объявление.
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
DEFAULT_LISTING_PAGE_DELAY_SECONDS = 1.5

ROOT_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
DEBUG_DIR = os.path.join(ROOT_DIR, "Logs", "ParserDebug")

STAGE_TITLES = {
    "listing_pages": "Сбор объявлений из выдачи",
    "listing_stop": "Сбор остановлен",
    "rate_limit": "Ограничение запросов",
    "done": "Завершение",
    "error": "Ошибка",
}

STAGE_NUMBERS = {
    "listing_pages": (1, 1),
}


def log(message):
    # Пишет служебные сообщения в stderr, чтобы не ломать JSON-вывод stdout.
    print(f"[LightBaseDromParser] {message}", file=sys.stderr, flush=True)


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


def safe_int(value):
    # Безопасно переводит строку с числами в int.
    if value is None:
        return None

    value = str(value).replace("\xa0", " ")
    value = re.sub(r"\D", "", value)
    return int(value) if value else None


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

    max_cars = int(max_cars)
    batch_size = int(batch_size)

    if max_cars < 0:
        raise RuntimeError("MAX_CARS must be 0 or greater")
    if batch_size <= 0:
        raise RuntimeError("STREAM_BATCH_SIZE must be greater than 0")

    return {
        "start_url": str(start_url),
        "max_cars": max_cars,
        "batch_size": batch_size,
        "request_delay": float(settings.get("requestDelaySeconds", DEFAULT_REQUEST_DELAY_SECONDS)),
        "listing_page_delay": float(settings.get("listingPageDelaySeconds", DEFAULT_LISTING_PAGE_DELAY_SECONDS)),
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
        }
    )
    return session


def save_debug_html(url, page_html, reason):
    # Сохраняет проблемную HTML-страницу для последующей диагностики.
    os.makedirs(DEBUG_DIR, exist_ok=True)

    timestamp = datetime.datetime.now().strftime("%Y-%m-%d_%H-%M-%S")
    file_name = f"drom_{reason}_{timestamp}.html"
    file_path = os.path.join(DEBUG_DIR, file_name)

    header = (
        "<!-- AutoScope Drom parser diagnostic dump\n"
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
    # Определяет страницы с капчей, авторизацией или заглушкой.
    text = clean_text(BeautifulSoup(page_html or "", "html.parser").get_text(" ", strip=True)) or ""
    lower_text = text.lower()
    lower_html = (page_html or "").lower()

    block_markers = [
        "captcha",
        "капча",
        "вы не робот",
        "подтвердите, что вы не робот",
        "доступ ограничен",
        "слишком много запросов",
        "проверка безопасности",
    ]

    if any(marker in lower_text for marker in block_markers) or any(marker in lower_html for marker in block_markers):
        debug_path = save_debug_html(url, page_html, "blocked_page")
        raise RuntimeError(
            "Drom returned captcha or access restriction page. "
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
                    f"Drom временно ограничил запросы. Ждём {int(wait_seconds)} сек. и пробуем снова",
                )
                time.sleep(wait_seconds)
                continue

            if response.status_code in [401, 403]:
                raise RuntimeError(
                    f"Drom returned HTTP {response.status_code}. "
                    "Возможна авторизация, капча или блокировка запроса."
                )

            response.raise_for_status()

            # Сохранённые страницы Drom часто cp1251, live-ответ может быть utf-8.
            if response.encoding and response.encoding.lower() not in ["iso-8859-1", "ascii"]:
                page_html = response.text
            else:
                response.encoding = response.apparent_encoding or "utf-8"
                page_html = response.text

            final_url = str(response.url or "")
            if final_url and final_url != url:
                log(f"Final response URL: {final_url}")

            detect_blocked_page(url, page_html)
            return page_html

        except requests.RequestException as error:
            last_error = f"{type(error).__name__}: {error}: {url}"
        except Exception as error:
            last_error = f"{type(error).__name__}: {error}: {url}"

        if attempt < retry_count:
            time.sleep(0.5)

    raise RuntimeError(last_error or f"Failed to fetch: {url}")


def is_drom_ad_url(url):
    # Проверяет, похожа ли ссылка на карточку объявления Drom.
    parsed = urlparse(url)
    return bool(re.search(r"/[^/]+/[^/]+/\d+\.html$", parsed.path.lower()))


def normalize_url(raw_url, base_url):
    # Нормализует ссылку относительно текущей страницы.
    if not raw_url:
        return None

    raw_url = html_lib.unescape(str(raw_url)).strip()
    absolute_url = urljoin(base_url, raw_url).split("?")[0].split("#")[0]
    return absolute_url


def find_listing_cards(page_html):
    # Находит контейнеры объявлений Drom на странице выдачи.
    soup = BeautifulSoup(page_html, "html.parser")
    cards = []

    for element in soup.find_all(attrs={"data-ftid": "bulls-list_bull"}):
        if element.find("a", href=lambda href: href and is_drom_ad_url(normalize_url(href, "https://auto.drom.ru/"))):
            cards.append(element)

    if cards:
        return cards

    # Резервный вариант на случай изменения data-ftid: ищем контейнеры вокруг ссылок на объявления.
    seen = set()
    for link in soup.find_all("a", href=True):
        url = normalize_url(link.get("href"), "https://auto.drom.ru/")
        if not is_drom_ad_url(url):
            continue

        parent = link
        for _ in range(5):
            parent = parent.parent if parent else None
            if not parent:
                break

            text = element_text(parent) or ""
            if "₽" in text or "руб" in text:
                marker = id(parent)
                if marker not in seen:
                    seen.add(marker)
                    cards.append(parent)
                break

    return cards


def get_drom_page_number(url):
    # Возвращает номер страницы выдачи Drom. Первая страница обычно без /page1/.
    match = re.search(r"/page(\d+)/?", urlparse(url).path)
    return int(match.group(1)) if match else 1


def extract_next_page_url(page_html, base_url):
    # Находит ссылку на следующую страницу выдачи Drom.
    # Сначала используем явную ссылку "следующая", потом аккуратный fallback по номеру страницы.
    soup = BeautifulSoup(page_html, "html.parser")

    for link in soup.find_all("a", href=True):
        text = (element_text(link) or "").lower()
        if "следующая" in text or "дальше" in text:
            return urljoin(base_url, link.get("href"))

    current_page = get_drom_page_number(base_url)
    next_page = current_page + 1

    numeric_links = []
    for link in soup.find_all("a", href=True):
        href = link.get("href") or ""
        text = element_text(link) or ""
        href_match = re.search(r"/page(\d+)/?", href)

        page_number = None
        if href_match:
            page_number = int(href_match.group(1))
        elif text.isdigit():
            page_number = int(text)

        if page_number is not None and page_number > current_page:
            numeric_links.append((page_number, urljoin(base_url, href)))

    if not numeric_links:
        return None

    # Берём ближайшую следующую страницу, а не первый попавшийся номер из пагинации.
    numeric_links.sort(key=lambda item: item[0])

    for page_number, url in numeric_links:
        if page_number == next_page:
            return url

    return numeric_links[0][1]


def extract_drom_ad_url(card, base_url):
    # Достаёт URL объявления из карточки выдачи.
    title_link = card.find("a", attrs={"data-ftid": "bull_title"}, href=True)
    if title_link:
        return normalize_url(title_link.get("href"), base_url)

    for link in card.find_all("a", href=True):
        url = normalize_url(link.get("href"), base_url)
        if is_drom_ad_url(url):
            return url

    return None


def parse_title(title, url):
    # Разбирает заголовок вида "Subaru Levorg, 2014".
    brand = None
    model = None
    year = None

    if title:
        year_match = re.search(r"\b((?:19|20)\d{2})\b", title)
        if year_match:
            year = int(year_match.group(1))

        title_without_year = re.sub(r",?\s*\b(?:19|20)\d{2}\b", "", title).strip(" ,")
        parts = title_without_year.split(" ", 1)
        if parts:
            brand = parts[0].strip() or None
        if len(parts) > 1:
            model = parts[1].strip() or None

    if (not brand or not model) and url:
        path_parts = [part for part in urlparse(url).path.split("/") if part]
        try:
            brand = brand or path_parts[-3].replace("-", " ").title()
            model = model or path_parts[-2].replace("-", " ").title()
        except Exception:
            pass

    return brand, model, year


def parse_engine(engine_raw):
    # Разбирает строку двигателя Drom: "1.6 л (170 л.с.)".
    if not engine_raw:
        return None, None

    lower_value = engine_raw.lower()
    engine_volume = None
    engine_power = None

    volume_match = re.search(r"(\d+(?:[.,]\d+)?)\s*л", lower_value)
    if volume_match:
        engine_volume = float(volume_match.group(1).replace(",", "."))

    power_match = re.search(r"(\d+)\s*л\.с", lower_value)
    if power_match:
        engine_power = int(power_match.group(1))

    return engine_volume, engine_power


def parse_description_items(card):
    # Достаёт характеристики из строки описания под заголовком.
    items = []
    for item in card.find_all(attrs={"data-ftid": "bull_description-item"}):
        value = element_text(item)
        if value:
            value = value.rstrip(",").strip()
            if value:
                items.append(value)

    if items:
        return items

    # Резервный вариант: пытаемся вырезать техническую часть из общего текста.
    text = element_text(card) or ""
    pattern = r"(\d+(?:[.,]\d+)?\s*л\s*\(\d+\s*л\.с\.\).*?\d[\d\s]*\s*км)"
    match = re.search(pattern, text, flags=re.IGNORECASE)
    if not match:
        return []

    return [part.strip(" ,") for part in match.group(1).split(",") if part.strip(" ,")]


def extract_price(card):
    # Достаёт цену из карточки выдачи.
    price_element = card.find(attrs={"data-ftid": "bull_price"})
    if price_element:
        return safe_int(element_text(price_element))

    text = element_text(card) or ""
    match = re.search(r"(\d[\d\s\xa0]{3,})\s*(?:₽|руб)", text)
    return safe_int(match.group(1)) if match else None


def extract_location(card):
    # Достаёт город/регион продажи.
    location = card.find(attrs={"data-ftid": "bull_location"})
    if location:
        return element_text(location)

    return None


def parse_listing_card(card, base_url):
    # Парсит одно объявление прямо из карточки выдачи Drom.
    url = extract_drom_ad_url(card, base_url)

    title_element = card.find(attrs={"data-ftid": "bull_title"})
    title = element_text(title_element)
    if not title:
        h3 = card.find("h3")
        title = element_text(h3)

    brand, model, year = parse_title(title, url)
    items = parse_description_items(card)

    engine_raw = items[0] if len(items) > 0 else None
    fuel_type = items[1] if len(items) > 1 else None
    transmission = items[2] if len(items) > 2 else None
    drive_type = items[3] if len(items) > 3 else None
    mileage = safe_int(items[4]) if len(items) > 4 else None

    engine_volume, engine_power = parse_engine(engine_raw)

    return {
        "source": "drom",
        "url": url,
        "brand": brand,
        "model": model,
        "price": extract_price(card),
        "year": year,
        "sale_region": extract_location(card),
        "license_plate": None,
        "mileage": mileage,
        "transmission": transmission,
        "drive_type": drive_type,
        "color": None,
        "body_type": None,
        "steering_wheel": None,
        "engine_power": engine_power,
        "engine_volume": engine_volume,
        "engine_model": None,
        "fuel_type": fuel_type,
        "octane": None,
        "powertrain": None,
        "description": None,
    }


def collect_listing_cars(session, settings):
    # Собирает объявления со страниц выдачи без перехода по карточкам.
    start_url = settings["start_url"]
    max_cars = settings["max_cars"]
    target_count = max_cars if max_cars > 0 else 0

    cars = []
    seen_urls = set()
    seen_pages = set()
    current_url = start_url
    partial_reason = None

    progress("listing_pages", 0, target_count, "Начат сбор объявлений из выдачи")

    while current_url:
        if current_url in seen_pages:
            partial_reason = "Drom вернул повторную ссылку на уже обработанную страницу. Сбор остановлен, чтобы не зациклиться."
            break

        seen_pages.add(current_url)
        log(f"Loading list page: {current_url}")

        page_html = fetch_html(session, current_url, settings["retry_count"], settings["rate_limit_delay"])
        cards = find_listing_cards(page_html)

        if not cards:
            debug_path = save_debug_html(current_url, page_html, "no_listing_items")
            title = get_page_title(page_html) or "заголовок не найден"

            if cars:
                expected_text = f" из {max_cars}" if max_cars > 0 else ""
                partial_reason = (
                    "Drom вернул страницу без карточек выдачи. "
                    f"Сохраняем частично собранные объявления: {len(cars)}{expected_text}. "
                    f"Title: {title}. Debug HTML: {debug_path}. URL: {current_url}"
                )
                progress(
                    "listing_stop",
                    len(cars),
                    target_count,
                    partial_reason,
                )
                break

            raise RuntimeError(
                "Drom page was loaded, but no listing cards were found. "
                "Возможна капча, заглушка или изменение разметки страницы. "
                f"Title: {title}. "
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
        progress("listing_pages", progress_current, progress_total, f"Собрано объявлений из выдачи: {len(cars)}")

        if max_cars > 0 and len(cars) >= max_cars:
            break

        next_url = extract_next_page_url(page_html, current_url)
        if not next_url or next_url == current_url:
            break

        current_url = next_url
        time.sleep(settings["listing_page_delay"])

    total = len(cars)

    if partial_reason:
        progress("listing_pages", total, target_count, f"Сбор объявлений из выдачи остановлен: собрано {total}")
    else:
        progress("listing_pages", total, total, f"Сбор объявлений из выдачи завершён: собрано {total}")

    result_cars = cars[:max_cars] if max_cars > 0 else cars
    return result_cars, partial_reason

def run_listing_mode(session, settings):
    # Обрабатывает страницу выдачи и сохраняет данные из карточек выдачи.
    cars, partial_reason = collect_listing_cars(session, settings)
    log(f"Collected listing cars: {len(cars)}")

    if not cars:
        progress("error", 0, 0, "Объявления в выдаче не найдены")
        raise RuntimeError(
            "Drom light parser did not collect any listing cars. "
            "Запуск остановлен, чтобы не считать пустой сбор успешным."
        )

    batch = []
    for car in cars:
        batch.append(car)

        if len(batch) >= settings["batch_size"]:
            emit(batch)
            batch = []

    if batch:
        emit(batch)

    total = len(cars)
    if partial_reason:
        progress("done", total, total, f"Парсер завершил работу частично: сохранено {total} объявлений. {partial_reason}")
    else:
        progress("done", total, total, f"Парсер успешно завершил работу: сохранено {total} объявлений")


def main():
    # Основной поток работы парсера.
    settings = read_input_settings()
    session = create_session()
    run_listing_mode(session, settings)


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        log(f"Fatal error: {error}")
        progress("error", 0, 0, str(error))
        sys.exit(1)
