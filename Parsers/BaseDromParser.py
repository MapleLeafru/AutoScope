# -*- coding: utf-8 -*-
import sys
import json
import re
import time
import html as html_lib
from urllib.parse import urljoin, urlparse

import requests
from bs4 import BeautifulSoup

sys.stdout.reconfigure(encoding="utf-8")
sys.stderr.reconfigure(encoding="utf-8", errors="replace")

# Новый базовый Drom-парсер без Selenium.
# Использует requests + BeautifulSoup и отдаёт данные батчами в JSON Lines.
# stdout: только JSON-батчи с объявлениями.
# stderr: логи и progress-события.

USER_AGENT = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
    "AppleWebKit/537.36 (KHTML, like Gecko) "
    "Chrome/120.0 Safari/537.36"
)

DEFAULT_TIMEOUT_SECONDS = 20
DEFAULT_REQUEST_DELAY_SECONDS = 1.2
DEFAULT_RETRY_COUNT = 3
DEFAULT_RATE_LIMIT_DELAY_SECONDS = 5.0


STAGE_TITLES = {
    "collect_links": "Сбор ссылок",
    "parse_ads": "Обработка объявлений",
    "rate_limit": "Ограничение запросов",
    "done": "Завершение",
    "error": "Ошибка",
}


# Пишет служебные сообщения в stderr, чтобы не ломать JSON-вывод stdout.
def log(message):
    print(f"[BaseDromParser] {message}", file=sys.stderr, flush=True)


# Отправляет progress-событие в stderr.
def progress(stage, current, total, message, stage_title=None):
    # stage — технический идентификатор этапа.
    # stageTitle — человекочитаемое название этапа для консольного вывода AutoScope.
    percent = 0

    if total and total > 0:
        percent = int(round((current / total) * 100))
        percent = max(0, min(100, percent))

    payload = {
        "stage": stage,
        "stageTitle": stage_title or STAGE_TITLES.get(stage, stage),
        "current": current,
        "total": total,
        "percent": percent,
        "message": message,
    }

    print(f"[PROGRESS] {json.dumps(payload, ensure_ascii=False)}", file=sys.stderr, flush=True)


# Отправляет один батч объявлений в stdout.
def emit(batch):
    print(json.dumps(batch, ensure_ascii=False), flush=True)


# Безопасно переводит строку с числами в int.
def safe_int(value):
    if value is None:
        return None

    value = str(value)
    value = re.sub(r"\D", "", value)
    return int(value) if value else None


# Очищает текст: декодирует entities и нормализует пробелы.
def clean_text(value):
    if value is None:
        return None

    value = html_lib.unescape(str(value))
    value = value.replace("\xa0", " ")
    value = re.sub(r"\s+", " ", value).strip()

    return value if value else None


# Берёт текст из BeautifulSoup-элемента.
def element_text(element):
    if not element:
        return None

    return clean_text(element.get_text(" ", strip=True))


# Разбирает строку двигателя на топливо, объём, октан и тип силовой установки.
# Значения топлива/силовой установки оставляем в формулировках площадки.
def parse_engine(engine_raw):
    if not engine_raw:
        return None, None, None, None

    parts = [p.strip().lower() for p in str(engine_raw).split(",")]

    fuel_type = None
    volume = None
    octane = None
    powertrain = None

    for part in parts:
        vol_match = re.search(r"(\d+(?:[.,]\d+)?)", part)
        if "л" in part and vol_match:
            volume = float(vol_match.group(1).replace(",", "."))
            continue

        oct_match = re.search(r"\b(80|92|95|98|100)\b", part)
        if oct_match:
            octane = int(oct_match.group(1))
            continue

        if part in ["бензин", "дизель", "электро", "газ"]:
            fuel_type = part
            continue

        if "гибрид" in part:
            powertrain = "гибрид"
            continue

        if "гбо" in part:
            powertrain = "гбо"
            continue

        if not fuel_type:
            fuel_type = part
        else:
            powertrain = part

    return fuel_type, volume, octane, powertrain


# Читает JSON-запрос, который передаёт InputPipelineManager.
def read_input_settings():
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
    if batch_size is None:
        raise RuntimeError("STREAM_BATCH_SIZE is required")

    max_cars = int(max_cars)
    batch_size = int(batch_size)

    if max_cars < 0:
        raise RuntimeError("MAX_CARS cannot be negative")
    if batch_size <= 0:
        raise RuntimeError("STREAM_BATCH_SIZE must be greater than zero")

    return {
        "start_url": str(start_url),
        "max_cars": max_cars,
        "batch_size": batch_size,
        "request_delay": float(settings.get("requestDelaySeconds", DEFAULT_REQUEST_DELAY_SECONDS)),
        "retry_count": int(settings.get("retryCount", DEFAULT_RETRY_COUNT)),
        "rate_limit_delay": float(settings.get("rateLimitDelaySeconds", DEFAULT_RATE_LIMIT_DELAY_SECONDS)),
    }


# Создаёт HTTP-сессию с заголовками обычного браузера.
def create_session():
    session = requests.Session()
    session.headers.update(
        {
            "User-Agent": USER_AGENT,
            "Accept": "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
            "Accept-Language": "ru-RU,ru;q=0.9,en;q=0.8",
            "Connection": "keep-alive",
        }
    )
    return session


# Загружает HTML-страницу обычным HTTP-запросом.
def fetch_html(session, url, retry_count=DEFAULT_RETRY_COUNT, rate_limit_delay=DEFAULT_RATE_LIMIT_DELAY_SECONDS):
    last_error = None

    for attempt in range(retry_count + 1):
        try:
            response = session.get(url, timeout=DEFAULT_TIMEOUT_SECONDS)

            if response.status_code == 429:
                wait_seconds = int(rate_limit_delay * (2 ** attempt))
                last_error = f"HTTP 429 Too Many Requests: {url}"

                if attempt < retry_count:
                    progress(
                        "rate_limit",
                        attempt + 1,
                        retry_count + 1,
                        f"Drom временно ограничил запросы. Ждём {wait_seconds} сек. и пробуем снова",
                    )
                    time.sleep(wait_seconds)
                    continue

            response.raise_for_status()

            if not response.encoding:
                response.encoding = response.apparent_encoding or "utf-8"

            return response.text

        except requests.RequestException as error:
            last_error = f"{type(error).__name__}: {error}: {url}"

        if attempt < retry_count:
            wait_seconds = 0.5 * (attempt + 1)
            time.sleep(wait_seconds)

    raise RuntimeError(last_error or f"Failed to fetch: {url}")


# Проверяет, похожа ли ссылка на карточку объявления Drom.
def is_drom_ad_url(url):
    parsed = urlparse(url)
    path = parsed.path.lower()

    if not path.endswith(".html"):
        return False

    return bool(re.search(r"/\d+\.html$", path))


# Собирает ссылки на карточки объявлений со страницы выдачи.
def extract_ad_links(page_html, base_url):
    soup = BeautifulSoup(page_html, "lxml")
    links = []
    seen = set()

    # Основной способ: ссылки-заголовки объявлений.
    for element in soup.select('a[data-ftid="bull_title"]'):
        href = element.get("href")
        if not href:
            continue

        absolute_url = urljoin(base_url, href).split("?")[0]
        if is_drom_ad_url(absolute_url) and absolute_url not in seen:
            seen.add(absolute_url)
            links.append(absolute_url)

    # Запасной способ: любые ссылки, похожие на карточки Drom.
    if not links:
        for element in soup.find_all("a", href=True):
            absolute_url = urljoin(base_url, element.get("href")).split("?")[0]
            if is_drom_ad_url(absolute_url) and absolute_url not in seen:
                seen.add(absolute_url)
                links.append(absolute_url)

    return links


# Находит ссылку на следующую страницу выдачи.
def extract_next_page_url(page_html, base_url):
    soup = BeautifulSoup(page_html, "lxml")
    next_button = soup.select_one('a[data-ftid="component_pagination-item-next"]')

    if next_button and next_button.get("href"):
        return urljoin(base_url, next_button.get("href"))

    return None


# Собирает ссылки на объявления. Если стартовая ссылка уже ведёт на карточку, возвращает её одну.
def collect_ad_links(session, start_url, max_cars, request_delay, retry_count, rate_limit_delay):
    collect_all = max_cars == 0
    requested_total = max_cars if not collect_all else 0

    if is_drom_ad_url(start_url):
        progress("collect_links", 1, 1, "Сбор ссылок завершён: стартовая ссылка является карточкой объявления")
        return [start_url]

    links = []
    seen = set()
    current_url = start_url
    page_number = 1

    if collect_all:
        progress("collect_links", 0, 0, "Начат сбор всех доступных ссылок")
    else:
        progress("collect_links", 0, requested_total, "Начат сбор ссылок")

    while current_url and (collect_all or len(links) < max_cars):
        log(f"Loading list page {page_number}: {current_url}")
        page_html = fetch_html(session, current_url, retry_count, rate_limit_delay)

        new_links_on_page = 0
        page_links = extract_ad_links(page_html, current_url)
        for link in page_links:
            if link not in seen:
                seen.add(link)
                links.append(link)
                new_links_on_page += 1

                if collect_all:
                    progress(
                        "collect_links",
                        len(links),
                        0,
                        f"Собрано ссылок: {len(links)}",
                    )
                else:
                    progress(
                        "collect_links",
                        min(len(links), max_cars),
                        max_cars,
                        f"Собрано ссылок: {min(len(links), max_cars)} из {max_cars}",
                    )

            if not collect_all and len(links) >= max_cars:
                break

        next_url = extract_next_page_url(page_html, current_url)
        if not next_url or next_url == current_url:
            break

        # Если страница не дала новых ссылок, но пагинация продолжает вести дальше,
        # продолжаем обход: на некоторых выдачах площадка может повторять часть карточек.
        current_url = next_url
        page_number += 1
        time.sleep(request_delay)

    actual_total = len(links)
    progress(
        "collect_links",
        actual_total,
        actual_total,
        f"Сбор ссылок завершён: собрано {actual_total} ссылок",
    )

    return links if collect_all else links[:max_cars]


# Пытается достать цену из HTML.
def extract_price(soup):
    price_element = soup.select_one('[data-ftid="bulletin-price"]')
    price = safe_int(element_text(price_element))

    if price:
        return price

    # Запасной вариант для JSON-LD или встроенных данных.
    html_text = str(soup)
    for pattern in [
        r'"price"\s*:\s*"?(\d{4,})"?',
        r'"priceValue"\s*:\s*"?(\d{4,})"?',
    ]:
        match = re.search(pattern, html_text, flags=re.IGNORECASE)
        if match:
            return safe_int(match.group(1))

    return None


# Достаёт описание из полного или короткого блока описания.
def extract_description(soup):
    for selector in [
        '[data-ftid="info-full"] [data-ftid="value"]',
        '[data-ftid="info-short"] [data-ftid="value"]',
        '[data-ftid="info-full"]',
        '[data-ftid="info-short"]',
    ]:
        value = element_text(soup.select_one(selector))
        if value:
            return value

    return None


# Разбирает таблицу характеристик.
def extract_specs(soup):
    specs = {}
    rows = soup.select('table[data-ftid="bulletin-specifications"] tr')

    for row in rows:
        key = element_text(row.select_one('[data-ftid="property"]'))
        value = element_text(row.select_one('[data-ftid="value"]'))

        if key and value:
            specs[key] = value

    return specs


# Разбирает заголовок Drom на марку, модель, год и город.
def parse_title(title):
    brand = None
    model = None
    year = None
    city = None

    if not title:
        return brand, model, year, city

    match = re.match(r"Продажа (.+?),\s*(\d{4}) год (?:в|во)\s+(.+)", title)
    if not match:
        return brand, model, year, city

    model_raw = match.group(1).strip()
    year = int(match.group(2))
    city = match.group(3).strip()

    parts = model_raw.split(" ", 1)
    brand = parts[0]
    model = parts[1] if len(parts) > 1 else parts[0]

    return brand, model, year, city


# Парсит одну карточку объявления.
def parse_ad_page(url, page_html):
    soup = BeautifulSoup(page_html, "lxml")

    title = element_text(soup.find("h1"))
    brand, model, year, city = parse_title(title)
    specs = extract_specs(soup)

    engine_raw = specs.get("Двигатель")
    fuel_type, engine_volume, octane, powertrain = parse_engine(engine_raw)

    return {
        "source": "drom",
        "url": url,
        "brand": brand,
        "model": model,
        "price": extract_price(soup),
        "year": year,
        "sale_region": city,
        "mileage": safe_int(specs.get("Пробег")),
        "transmission": specs.get("Коробка передач"),
        "drive_type": specs.get("Привод"),
        "color": specs.get("Цвет"),
        "body_type": specs.get("Кузов"),
        "steering_wheel": specs.get("Руль"),
        "engine_power": safe_int(specs.get("Мощность")),
        "engine_volume": engine_volume,
        "engine_model": specs.get("Модель двигателя"),
        "fuel_type": fuel_type,
        "octane": octane,
        "powertrain": powertrain,
        "description": extract_description(soup),
    }


# Основной поток работы парсера.
def main():
    settings = read_input_settings()
    session = create_session()

    links = collect_ad_links(
        session=session,
        start_url=settings["start_url"],
        max_cars=settings["max_cars"],
        request_delay=settings["request_delay"],
        retry_count=settings["retry_count"],
        rate_limit_delay=settings["rate_limit_delay"],
    )

    log(f"Collected links: {len(links)}")

    if not links:
        progress("parse_ads", 0, settings["max_cars"], "Ссылки на объявления не найдены")
        return

    batch = []
    total_links = len(links)

    progress("parse_ads", 0, total_links, "Начата обработка карточек объявлений")

    for index, link in enumerate(links, start=1):
        try:
            log(f"Parsing ad {index}/{total_links}: {link}")
            page_html = fetch_html(session, link, settings["retry_count"], settings["rate_limit_delay"])
            car = parse_ad_page(link, page_html)

            if not car.get("url"):
                log(f"Skipped ad without url: {link}")
                continue

            batch.append(car)

            progress(
                "parse_ads",
                index,
                total_links,
                f"Обработано объявлений: {index} из {total_links}",
            )

            if len(batch) >= settings["batch_size"]:
                emit(batch)
                batch = []

            time.sleep(settings["request_delay"])

        except Exception as error:
            log(f"Skipped ad because of error: {link} | {error}")
            progress(
                "parse_ads",
                index,
                total_links,
                f"Ошибка при обработке объявления {index} из {total_links}",
            )

    if batch:
        emit(batch)

    progress("done", total_links, total_links, "Парсер завершил работу")


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        log(f"Fatal error: {error}")
        progress("error", 0, 0, str(error))
        sys.exit(1)
