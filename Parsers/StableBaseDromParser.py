# -*- coding: utf-8 -*-
import sys
import json
import time
import re
import os
import datetime
import html as html_lib
from urllib.parse import urljoin, urlparse

from bs4 import BeautifulSoup
from selenium import webdriver
from selenium.webdriver.common.by import By
from selenium.common.exceptions import WebDriverException, TimeoutException

sys.stdout.reconfigure(encoding="utf-8")
sys.stderr.reconfigure(encoding="utf-8", errors="replace")

# StableBaseDromParser
# Браузерный Drom-парсер на Selenium.
# Логика: 1) собрать ссылки из выдачи; 2) открыть каждую карточку и собрать подробные поля.
# stdout: только JSON-батчи.
# stderr: служебные логи и progress-события.

ROOT_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
DEBUG_DIR = os.path.join(ROOT_DIR, "Logs", "ParserDebug")

SOURCE_NAME = "drom"
STAGE_COLLECT_LINKS = "collect_links"
STAGE_PARSE_ADS = "parse_ads"
STAGE_ACCESS_LIMIT = "access_limit"
STAGE_DONE = "done"
STAGE_ERROR = "error"

STAGE_TITLES = {
    STAGE_COLLECT_LINKS: "Сбор ссылок",
    STAGE_PARSE_ADS: "Обработка объявлений",
    STAGE_ACCESS_LIMIT: "Сбор остановлен защитой сайта",
    STAGE_DONE: "Завершение",
    STAGE_ERROR: "Ошибка",
}

DEFAULT_REQUEST_DELAY_SECONDS = 2.0
DEFAULT_LISTING_PAGE_DELAY_SECONDS = 2.0
DEFAULT_CARD_DELAY_SECONDS = 2.0
DEFAULT_BROWSER_PAGE_LOAD_TIMEOUT_SECONDS = 40
DEFAULT_MANUAL_ACCESS_WAIT_SECONDS = 0


def log(message):
    print(f"[StableBaseDromParser] {message}", file=sys.stderr, flush=True)


def emit(batch):
    print(json.dumps(batch, ensure_ascii=False), flush=True)


def progress(stage, current, total, message, stage_index=None, stage_total=None):
    percent = 0
    if total:
        percent = int((current / total) * 100)
        if current >= total:
            percent = 100

    payload = {
        "stage": stage,
        "stageTitle": STAGE_TITLES.get(stage, stage),
        "current": current,
        "total": total,
        "percent": percent,
        "message": message,
    }

    if stage_index is not None:
        payload["stageIndex"] = stage_index
    if stage_total is not None:
        payload["stageTotal"] = stage_total

    print("[PROGRESS] " + json.dumps(payload, ensure_ascii=False), file=sys.stderr, flush=True)


def clean_text(value):
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
    if not element:
        return None
    return clean_text(element.get_text(" ", strip=True))


def safe_int(value):
    if value is None:
        return None
    value = str(value).replace("\xa0", " ")
    value = re.sub(r"\D", "", value)
    return int(value) if value else None


def safe_float(value):
    if value is None:
        return None
    match = re.search(r"(\d+(?:[.,]\d+)?)", str(value))
    if not match:
        return None
    return float(match.group(1).replace(",", "."))


def parse_engine(engine_raw):
    if not engine_raw:
        return None, None, None, None

    parts = [part.strip().lower() for part in str(engine_raw).split(",") if part.strip()]

    fuel_type = None
    volume = None
    octane = None
    powertrain = None

    for part in parts:
        if "л" in part and volume is None:
            volume = safe_float(part)
            continue

        octane_match = re.search(r"\b(80|92|95|98|100)\b", part)
        if octane_match:
            octane = int(octane_match.group(1))
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
        elif not powertrain:
            powertrain = part

    return fuel_type, volume, octane, powertrain


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
    if not batch_size:
        raise RuntimeError("STREAM_BATCH_SIZE is required")

    max_cars = int(max_cars)
    batch_size = int(batch_size)

    if max_cars < 0:
        raise RuntimeError("MAX_CARS must be >= 0")
    if batch_size <= 0:
        raise RuntimeError("STREAM_BATCH_SIZE must be > 0")

    return {
        "start_url": str(start_url),
        "max_cars": max_cars,
        "batch_size": batch_size,
        "request_delay": float(settings.get("requestDelaySeconds", DEFAULT_REQUEST_DELAY_SECONDS)),
        "listing_page_delay": float(settings.get("listingPageDelaySeconds", DEFAULT_LISTING_PAGE_DELAY_SECONDS)),
        "card_delay": float(settings.get("cardDelaySeconds", DEFAULT_CARD_DELAY_SECONDS)),
        "browser_page_load_timeout": int(settings.get("browserPageLoadTimeoutSeconds", DEFAULT_BROWSER_PAGE_LOAD_TIMEOUT_SECONDS)),
        "manual_access_wait": int(settings.get("manualAccessWaitSeconds", DEFAULT_MANUAL_ACCESS_WAIT_SECONDS)),
        "headless": bool(settings.get("headless", False)),
    }


def create_driver(settings):
    options = webdriver.ChromeOptions()
    options.add_argument("--lang=ru-RU")
    options.add_argument("--disable-notifications")
    options.add_argument("--start-maximized")

    if settings.get("headless"):
        options.add_argument("--headless=new")

    driver = webdriver.Chrome(options=options)
    driver.set_page_load_timeout(settings["browser_page_load_timeout"])
    return driver


def save_debug_html(driver, url, reason):
    os.makedirs(DEBUG_DIR, exist_ok=True)
    timestamp = datetime.datetime.now().strftime("%Y-%m-%d_%H-%M-%S")
    file_name = f"drom_stable_{reason}_{timestamp}.html"
    file_path = os.path.join(DEBUG_DIR, file_name)

    header = (
        "<!-- AutoScope Drom stable parser diagnostic dump\n"
        f"URL: {url}\n"
        f"Current URL: {getattr(driver, 'current_url', '')}\n"
        f"Reason: {reason}\n"
        f"Created at: {timestamp}\n"
        "-->\n"
    )

    with open(file_path, "w", encoding="utf-8-sig") as file:
        file.write(header)
        file.write(driver.page_source or "")

    log(f"Diagnostic HTML saved: {file_path}")
    return file_path


def get_page_title(driver):
    try:
        return clean_text(driver.title)
    except Exception:
        return None


def is_blocked_page(driver):
    page_html = driver.page_source or ""
    text = clean_text(BeautifulSoup(page_html, "html.parser").get_text(" ", strip=True)) or ""
    lower_text = text.lower()
    lower_url = (driver.current_url or "").lower()

    markers = [
        "подтвердите, что вы не робот",
        "captcha",
        "капча",
        "доступ ограничен",
        "слишком много запросов",
        "проверка безопасности",
    ]

    return any(marker in lower_text for marker in markers) or any(marker in lower_url for marker in ["captcha", "blocked"])


def wait_if_access_check(driver, settings, stage, current, total, stage_index, stage_total):
    if not is_blocked_page(driver):
        return False

    wait_seconds = settings.get("manual_access_wait", 0)
    if wait_seconds <= 0:
        return True

    progress(
        stage,
        current,
        total,
        f"Страница требует проверки. Можно пройти её в открытом браузере. Ожидание: {wait_seconds} сек.",
        stage_index,
        stage_total,
    )
    log(f"Waiting for manual access check: {wait_seconds} seconds")
    time.sleep(wait_seconds)
    return is_blocked_page(driver)


def open_page(driver, url, delay_seconds):
    driver.get(url)
    time.sleep(delay_seconds)


def extract_listing_links(page_html, base_url):
    soup = BeautifulSoup(page_html, "html.parser")
    links = []
    seen = set()

    for link in soup.select('a[data-ftid="bull_title"], a[href*="/cars/used/sale/"], a[href*="/auto/used/"]'):
        href = link.get("href")
        if not href:
            continue

        url = urljoin(base_url, href).split("?")[0].split("#")[0]
        if "drom.ru" not in url:
            continue

        if url not in seen:
            seen.add(url)
            links.append(url)

    return links


def extract_next_page_url(page_html, base_url):
    soup = BeautifulSoup(page_html, "html.parser")

    next_link = soup.select_one('a[data-ftid="component_pagination-item-next"]')
    if next_link and next_link.get("href"):
        return urljoin(base_url, next_link.get("href"))

    current_page = None
    for item in soup.select('[data-ftid="component_pagination-item"]'):
        text = element_text(item)
        if text and text.isdigit():
            classes = " ".join(item.get("class", []))
            if "active" in classes.lower() or item.name != "a":
                current_page = int(text)
                break

    candidates = []
    for link in soup.select('a[href]'):
        text = element_text(link)
        if text and text.isdigit():
            page_number = int(text)
            if current_page is None or page_number > current_page:
                candidates.append((page_number, urljoin(base_url, link.get("href"))))

    if candidates:
        candidates.sort(key=lambda item: item[0])
        return candidates[0][1]

    return None


def collect_links(driver, settings):
    links = []
    seen = set()
    visited_pages = set()
    current_url = settings["start_url"]
    target_total = settings["max_cars"] if settings["max_cars"] > 0 else 0

    progress(STAGE_COLLECT_LINKS, 0, target_total, "Начат сбор ссылок", 1, 2)

    while current_url:
        if current_url in visited_pages:
            break
        visited_pages.add(current_url)

        log(f"Loading list page: {current_url}")
        open_page(driver, current_url, settings["listing_page_delay"])

        if wait_if_access_check(driver, settings, STAGE_COLLECT_LINKS, len(links), target_total, 1, 2):
            debug_path = save_debug_html(driver, current_url, "blocked_listing_page")
            if links:
                message = (
                    f"Drom остановил сбор на странице выдачи. "
                    f"Сохраняем частично собранные ссылки: {len(links)}. Debug HTML: {debug_path}"
                )
                progress(STAGE_ACCESS_LIMIT, len(links), target_total, message, 1, 2)
                break
            raise RuntimeError(
                "Drom blocked the first listing page before links were collected. "
                f"Title: {get_page_title(driver) or 'заголовок не найден'}. Debug HTML: {debug_path}. URL: {current_url}"
            )

        page_links = extract_listing_links(driver.page_source, current_url)
        if not page_links:
            debug_path = save_debug_html(driver, current_url, "no_listing_links")
            if links:
                message = (
                    f"Drom вернул страницу без ссылок выдачи. "
                    f"Сохраняем частично собранные ссылки: {len(links)}. Debug HTML: {debug_path}"
                )
                progress(STAGE_ACCESS_LIMIT, len(links), target_total, message, 1, 2)
                break
            raise RuntimeError(
                "Drom page was loaded, but no ad links were found. "
                f"Title: {get_page_title(driver) or 'заголовок не найден'}. Debug HTML: {debug_path}. URL: {current_url}"
            )

        for link in page_links:
            if link not in seen:
                seen.add(link)
                links.append(link)

            if settings["max_cars"] > 0 and len(links) >= settings["max_cars"]:
                break

        progress_total = settings["max_cars"] if settings["max_cars"] > 0 else 0
        progress_current = min(len(links), progress_total) if progress_total else len(links)
        progress(STAGE_COLLECT_LINKS, progress_current, progress_total, f"Собрано ссылок: {len(links)}", 1, 2)

        if settings["max_cars"] > 0 and len(links) >= settings["max_cars"]:
            break

        next_url = extract_next_page_url(driver.page_source, current_url)
        if not next_url or next_url == current_url:
            break

        current_url = next_url
        time.sleep(settings["request_delay"])

    links = links[:settings["max_cars"]] if settings["max_cars"] > 0 else links
    progress(STAGE_COLLECT_LINKS, len(links), len(links), f"Сбор ссылок завершён: собрано {len(links)} ссылок", 1, 2)
    return links


def parse_title(title_full):
    brand = None
    model = None
    year = None
    city = None

    if not title_full:
        return brand, model, year, city

    patterns = [
        r"Продажа (.+?),\s*((?:19|20)\d{2}) год (?:в|во) (.+)",
        r"(.+?),\s*((?:19|20)\d{2}) год (?:в|во) (.+)",
    ]

    model_raw = None
    for pattern in patterns:
        match = re.search(pattern, title_full)
        if match:
            model_raw = match.group(1).strip()
            year = int(match.group(2))
            city = match.group(3).strip()
            break

    if not model_raw:
        model_raw = title_full.replace("Продажа", "").strip(" ,")
        year_match = re.search(r"\b((?:19|20)\d{2})\b", model_raw)
        if year_match:
            year = int(year_match.group(1))
            model_raw = model_raw[:year_match.start()].strip(" ,")

    if model_raw:
        parts = model_raw.split(" ", 1)
        brand = parts[0]
        model = parts[1] if len(parts) > 1 else parts[0]

    return brand, model, year, city


def extract_specs_from_page(page_html):
    soup = BeautifulSoup(page_html, "html.parser")
    specs = {}

    for row in soup.select('table[data-ftid="bulletin-specifications"] tr'):
        key = element_text(row.select_one('th[data-ftid="property"]'))
        value = element_text(row.select_one('td[data-ftid="value"]'))
        if key and value:
            specs[key] = value

    for item in soup.select('[data-ftid^="component_inline-bull-description"], [data-ftid^="component_inline-bull-specs"]'):
        text = element_text(item)
        if text and ":" in text:
            key, value = text.split(":", 1)
            specs.setdefault(key.strip(), value.strip())

    return specs


def parse_ad_page(driver, link):
    soup = BeautifulSoup(driver.page_source, "html.parser")

    title_full = element_text(soup.find("h1")) or ""
    brand, model, year, city = parse_title(title_full)

    price = safe_int(element_text(soup.select_one('div[data-ftid="bulletin-price"]')))

    description = element_text(soup.select_one('div[data-ftid="info-full"] span[data-ftid="value"]'))
    if not description:
        description = element_text(soup.select_one('div[data-ftid="info-short"] span[data-ftid="value"]'))

    specs = extract_specs_from_page(driver.page_source)

    mileage = safe_int(specs.get("Пробег"))
    power = safe_int(specs.get("Мощность"))
    engine_raw = specs.get("Двигатель")
    fuel_type, engine_volume, octane, powertrain = parse_engine(engine_raw)

    return {
        "source": SOURCE_NAME,
        "url": link,
        "brand": brand,
        "model": model,
        "price": price,
        "year": year,
        "sale_region": city,
        "license_plate": specs.get("Госномер"),
        "mileage": mileage,
        "transmission": specs.get("Коробка передач"),
        "drive_type": specs.get("Привод"),
        "color": specs.get("Цвет"),
        "body_type": specs.get("Кузов"),
        "steering_wheel": specs.get("Руль"),
        "engine_power": power,
        "engine_volume": engine_volume,
        "engine_model": specs.get("Модель двигателя"),
        "fuel_type": fuel_type,
        "octane": octane,
        "powertrain": powertrain,
        "description": description,
    }


def parse_ads(driver, links, settings):
    batch = []
    saved_count = 0
    total = len(links)

    progress(STAGE_PARSE_ADS, 0, total, "Начата обработка карточек объявлений", 2, 2)

    for index, link in enumerate(links, start=1):
        try:
            log(f"Parsing ad {index}/{total}: {link}")
            open_page(driver, link, settings["card_delay"])

            if wait_if_access_check(driver, settings, STAGE_PARSE_ADS, index, total, 2, 2):
                debug_path = save_debug_html(driver, link, "blocked_ad_page")
                message = (
                    f"Drom остановил обработку карточек. "
                    f"Сохраняем уже обработанные объявления: {saved_count} из {total}. Debug HTML: {debug_path}"
                )
                progress(STAGE_ACCESS_LIMIT, index, total, message, 2, 2)
                break

            car = parse_ad_page(driver, link)
            if car.get("url"):
                batch.append(car)
                saved_count += 1

            if len(batch) >= settings["batch_size"]:
                emit(batch)
                batch = []

            progress(STAGE_PARSE_ADS, index, total, f"Обработано объявлений: {index} из {total}", 2, 2)

        except Exception as error:
            message = f"Ошибка при обработке объявления {index} из {total}: {type(error).__name__}: {error}"
            log(f"Skipped ad because of error: {link} | {message}")
            progress(STAGE_PARSE_ADS, index, total, message, 2, 2)
            continue

    if batch:
        emit(batch)

    return saved_count


def main():
    settings = read_input_settings()
    driver = create_driver(settings)

    try:
        links = collect_links(driver, settings)
        if not links:
            raise RuntimeError("Drom parser did not collect any links")

        saved_count = parse_ads(driver, links, settings)

        if saved_count < len(links):
            progress(
                STAGE_DONE,
                saved_count,
                saved_count,
                f"Парсер завершил работу частично: сохранено {saved_count} объявлений из {len(links)} ссылок",
            )
        else:
            progress(STAGE_DONE, saved_count, saved_count, "Парсер успешно завершил работу")
    finally:
        try:
            driver.quit()
        except Exception:
            pass


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        log(f"Fatal error: {type(error).__name__}: {error}")
        progress(STAGE_ERROR, 0, 0, f"{type(error).__name__}: {error}")
        sys.exit(1)
