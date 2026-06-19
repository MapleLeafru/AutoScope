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

# StableBaseAvitoParser
# Браузерный Avito-парсер на Selenium.
# Логика: 1) открыть выдачу и собрать ссылки; 2) открыть карточки и собрать подробные поля.
# В отличие от LightBaseAvitoParser, этот файл не использует requests и рассчитан на браузерный режим.
# Если Avito показывает проверку/ограничение, можно вручную пройти её в открытом окне браузера,
# если включён manualAccessWaitSeconds.

ROOT_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
DEBUG_DIR = os.path.join(ROOT_DIR, "Logs", "ParserDebug")

SOURCE_NAME = "avito"
STAGE_COLLECT_LINKS = "collect_links"
STAGE_PARSE_ADS = "parse_ads"
STAGE_ACCESS_LIMIT = "access_limit"
STAGE_DONE = "done"
STAGE_ERROR = "error"

STAGE_TITLES = {
    STAGE_COLLECT_LINKS: "Сбор ссылок",
    STAGE_PARSE_ADS: "Обработка объявлений",
    STAGE_ACCESS_LIMIT: "Сбор остановлен защитой Avito",
    STAGE_DONE: "Завершение",
    STAGE_ERROR: "Ошибка",
}

DEFAULT_REQUEST_DELAY_SECONDS = 4.0
DEFAULT_LISTING_PAGE_DELAY_SECONDS = 5.0
DEFAULT_CARD_DELAY_SECONDS = 4.0
DEFAULT_BROWSER_PAGE_LOAD_TIMEOUT_SECONDS = 45
DEFAULT_MANUAL_ACCESS_WAIT_SECONDS = 120

MULTIWORD_BRANDS = [
    "Land Rover", "Range Rover", "Alfa Romeo", "Aston Martin", "Great Wall",
    "Rolls-Royce", "Mercedes-Benz", "Mercedes Benz", "SsangYong",
    "Renault Samsung", "Lynk & Co", "Iran Khodro",
]


def log(message):
    print(f"[StableBaseAvitoParser] {message}", file=sys.stderr, flush=True)


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
        "warmup_url": str(settings.get("warmupUrl", "https://www.avito.ru/")),
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


def open_page(driver, url, delay_seconds):
    driver.get(url)
    time.sleep(delay_seconds)


def save_debug_html(driver, url, reason):
    os.makedirs(DEBUG_DIR, exist_ok=True)
    timestamp = datetime.datetime.now().strftime("%Y-%m-%d_%H-%M-%S")
    file_name = f"avito_stable_{reason}_{timestamp}.html"
    file_path = os.path.join(DEBUG_DIR, file_name)

    header = (
        "<!-- AutoScope Avito stable parser diagnostic dump\n"
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
    title = (get_page_title(driver) or "").lower()

    markers = [
        "доступ ограничен",
        "проблема с ip",
        "подтвердите, что вы не робот",
        "проверка безопасности",
        "captcha",
        "капча",
        "слишком много запросов",
    ]

    return (
        any(marker in lower_text for marker in markers)
        or any(marker in lower_url for marker in ["captcha", "blocked"])
        or any(marker in title for marker in ["доступ ограничен", "captcha"])
    )


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
        f"Страница требует проверки или доступ ограничен. Можно попробовать решить проблему в открытом браузере. Ожидание: {wait_seconds} сек.",
        stage_index,
        stage_total,
    )
    log(f"Waiting for manual access check: {wait_seconds} seconds")
    time.sleep(wait_seconds)
    return is_blocked_page(driver)


def warmup(driver, settings):
    warmup_url = settings.get("warmup_url")
    if not warmup_url:
        return

    try:
        open_page(driver, warmup_url, 2)
        log(f"Warmup finished. Title: {get_page_title(driver) or 'no title'}; current url: {driver.current_url}")
    except Exception as error:
        log(f"Warmup failed: {type(error).__name__}: {error}")


def is_avito_ad_url(url):
    if not url:
        return False
    parsed = urlparse(url)
    path = parsed.path.lower()
    return "/avtomobili/" in path and re.search(r"_\d+$", path) is not None


def add_link(links, seen, raw_url, base_url):
    if not raw_url:
        return

    url = urljoin(base_url, raw_url).split("?")[0].split("#")[0]
    if "avito.ru" not in url:
        return

    # В выдаче Avito объявления обычно имеют числовой id в конце URL.
    if not is_avito_ad_url(url):
        return

    if url not in seen:
        seen.add(url)
        links.append(url)


def extract_listing_links(page_html, base_url):
    soup = BeautifulSoup(page_html, "html.parser")
    links = []
    seen = set()

    selectors = [
        'a[data-marker="item-title"]',
        '[data-marker="item"] a[href*="/avtomobili/"]',
        'a[itemprop="url"]',
    ]

    for selector in selectors:
        for link in soup.select(selector):
            add_link(links, seen, link.get("href"), base_url)

    for match in re.findall(r"https?://www\.avito\.ru/[^\s\"'<>]+_\d+", page_html):
        add_link(links, seen, match, base_url)

    return links


def extract_next_page_url(page_html, base_url):
    soup = BeautifulSoup(page_html, "html.parser")

    selectors = [
        'a[data-marker="pagination-button/next"]',
        'a[rel="next"]',
        'a[aria-label*="Следующая"]',
    ]

    for selector in selectors:
        link = soup.select_one(selector)
        if link and link.get("href"):
            return urljoin(base_url, link.get("href"))

    for link in soup.select("a[href]"):
        text = element_text(link) or ""
        aria = link.get("aria-label") or ""
        if "Следующая" in text or "Следующая" in aria:
            return urljoin(base_url, link.get("href"))

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
                    f"Avito остановил сбор на странице выдачи. "
                    f"Сохраняем частично собранные ссылки: {len(links)}. Debug HTML: {debug_path}"
                )
                progress(STAGE_ACCESS_LIMIT, len(links), target_total, message, 1, 2)
                break
            raise RuntimeError(
                "Avito blocked the first listing page before links were collected. "
                f"Title: {get_page_title(driver) or 'заголовок не найден'}. Debug HTML: {debug_path}. URL: {current_url}"
            )

        page_links = extract_listing_links(driver.page_source, current_url)
        if not page_links:
            debug_path = save_debug_html(driver, current_url, "no_listing_links")
            if links:
                message = (
                    f"Avito вернул страницу без ссылок выдачи. "
                    f"Сохраняем частично собранные ссылки: {len(links)}. Debug HTML: {debug_path}"
                )
                progress(STAGE_ACCESS_LIMIT, len(links), target_total, message, 1, 2)
                break
            raise RuntimeError(
                "Avito page was loaded, but no ad links were found. "
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


def parse_engine(text):
    if not text:
        return None, None, None, None

    text = clean_text(text) or ""
    lower_text = text.lower()

    fuel_type = None
    engine_volume = None
    engine_power = None
    transmission = None

    volume_match = re.search(r"(\d+(?:[.,]\d+)?)\s*(?:л|l)\b", lower_text)
    if volume_match:
        engine_volume = float(volume_match.group(1).replace(",", "."))

    power_match = re.search(r"(\d+)\s*л\.?\s*с", lower_text)
    if not power_match:
        power_match = re.search(r"(\d+)\s*л\.\s*с", lower_text)
    if not power_match:
        power_match = re.search(r"(\d+)\s*лс", lower_text)
    if power_match:
        engine_power = int(power_match.group(1))

    transmission_match = re.search(r"\b(AT|MT|CVT|AMT|АКПП|МКПП)\b|вариатор|автомат|механика|робот", text, flags=re.IGNORECASE)
    if transmission_match:
        transmission = transmission_match.group(0)

    for fuel in ["бензин", "дизель", "электро", "гибрид", "газ"]:
        if fuel in lower_text:
            fuel_type = fuel
            break

    return fuel_type, engine_volume, engine_power, transmission


def parse_title_parts(title):
    brand = None
    model = None
    year = None
    mileage = None

    title = clean_text(title)
    if not title:
        return brand, model, year, mileage

    year_match = re.search(r"\b((?:19|20)\d{2})\b", title)
    if year_match:
        year = int(year_match.group(1))

    mileage_match = re.search(r"([\d\s\xa0]+)\s*км", title.lower())
    if mileage_match:
        mileage = safe_int(mileage_match.group(1))

    base = title.split(",", 1)[0].strip()
    base = re.sub(r"\b\d+(?:[.,]\d+)?\s*(?:AT|MT|CVT|AMT|АКПП|МКПП)?\b.*$", "", base, flags=re.IGNORECASE).strip()

    for item in sorted(MULTIWORD_BRANDS, key=len, reverse=True):
        if base.lower().startswith(item.lower() + " "):
            brand = item
            model = base[len(item):].strip()
            break

    if not brand:
        parts = base.split(" ", 1)
        brand = parts[0] if parts else None
        model = parts[1].strip() if len(parts) > 1 else None

    return brand, model, year, mileage


def collect_text_blocks(soup):
    blocks = []
    for selector in [
        '[data-marker*="item-view"]',
        '[class*="params"]',
        '[class*="Params"]',
        '[class*="style-item-params"]',
        'li',
    ]:
        for element in soup.select(selector):
            text = element_text(element)
            if text and len(text) < 300:
                blocks.append(text)
    return list(dict.fromkeys(blocks))


def find_value_by_label(blocks, labels):
    for block in blocks:
        lower = block.lower()
        for label in labels:
            label_lower = label.lower()
            if lower.startswith(label_lower):
                value = block[len(label):].strip(" :—-")
                if value:
                    return value
    return None


def parse_ad_page(driver, link):
    soup = BeautifulSoup(driver.page_source, "html.parser")

    title = element_text(soup.find("h1"))
    if not title:
        meta_title = soup.find("meta", attrs={"property": "og:title"})
        title = clean_text(meta_title.get("content")) if meta_title else None

    brand, model, year, mileage = parse_title_parts(title)

    price = None
    price_meta = soup.find("meta", attrs={"itemprop": "price"})
    if price_meta:
        price = safe_int(price_meta.get("content"))
    if price is None:
        price = safe_int(element_text(soup.select_one('[data-marker="item-view/item-price"]')))
    if price is None:
        price = safe_int(element_text(soup.select_one('[class*="price"]')))

    description = element_text(soup.select_one('[data-marker="item-view/item-description"]'))
    if not description:
        description_meta = soup.find("meta", attrs={"itemprop": "description"})
        description = clean_text(description_meta.get("content")) if description_meta else None

    sale_region = element_text(soup.select_one('[data-marker="item-view/item-address"]'))
    if not sale_region:
        sale_region = element_text(soup.select_one('[itemprop="address"]'))

    blocks = collect_text_blocks(soup)
    mileage = safe_int(find_value_by_label(blocks, ["Пробег"])) or mileage
    body_type = find_value_by_label(blocks, ["Тип кузова", "Кузов"])
    color = find_value_by_label(blocks, ["Цвет"])
    steering_wheel = find_value_by_label(blocks, ["Руль"])
    transmission = find_value_by_label(blocks, ["Коробка передач", "Трансмиссия"])
    drive_type = find_value_by_label(blocks, ["Привод"])
    engine_text = find_value_by_label(blocks, ["Двигатель", "Модификация"])

    full_text = " ".join([title or "", engine_text or ""] + blocks[:20])
    fuel_type, engine_volume, engine_power, title_transmission = parse_engine(full_text)

    if not transmission:
        transmission = title_transmission

    if not drive_type:
        for block in blocks:
            lower = block.lower()
            if any(item in lower for item in ["передний", "задний", "полный"]):
                drive_type = block
                break

    if not body_type:
        for block in blocks:
            lower = block.lower()
            if any(item in lower for item in ["седан", "хэтчбек", "универсал", "внедорожник", "купе", "лифтбек", "минивэн", "пикап", "кабриолет", "фургон"]):
                body_type = block
                break

    return {
        "source": SOURCE_NAME,
        "url": link,
        "brand": brand,
        "model": model,
        "price": price,
        "year": year,
        "sale_region": sale_region,
        "license_plate": None,
        "mileage": mileage,
        "transmission": transmission,
        "drive_type": drive_type,
        "color": color,
        "body_type": body_type,
        "steering_wheel": steering_wheel,
        "engine_power": engine_power,
        "engine_volume": engine_volume,
        "engine_model": None,
        "fuel_type": fuel_type,
        "octane": None,
        "powertrain": "гибрид" if fuel_type == "гибрид" else None,
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
                    f"Avito остановил обработку карточек. "
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
        warmup(driver, settings)
        links = collect_links(driver, settings)
        if not links:
            raise RuntimeError("Avito parser did not collect any links")

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
