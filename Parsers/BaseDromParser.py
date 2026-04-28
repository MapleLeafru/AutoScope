import sys
import json
import time
import re

from selenium import webdriver
from selenium.webdriver.common.by import By

sys.stdout.reconfigure(encoding='utf-8')

# =========================================================
# INPUT
# =========================================================

input_data = json.loads(sys.stdin.read())

settings = input_data.get("parserSettings", {})

START_URL = settings.get("startUrl")
MAX_CARS = settings.get("maxCars")
BATCH_SIZE = settings.get("batchSize")

if not START_URL:
    raise Exception("START_URL is required")
if not MAX_CARS:
    raise Exception("MAX_CARS is required")
if not BATCH_SIZE:
    raise Exception("BATCH_SIZE is required")

# =========================================================
# SETTINGS (temporary)
# =========================================================

#START_URL = "https://auto.drom.ru/toyota/camry/"
#START_URL = "http://auto.drom.ru/subaru/levorg/"
#MAX_CARS = 6
#BATCH_SIZE = 2

# =========================================================
# 0. UTILS
# =========================================================

# Безопасный перевод в int
def safe_int(value):
    if not value:
        return None
    value = re.sub(r"\D", "", value)
    return int(value) if value else None

# Чтение двигателя
def parse_engine(engine_raw):
    if not engine_raw:
        return None, None, None, None

    parts = [p.strip().lower() for p in engine_raw.split(",")]

    fuel_type = None
    volume = None
    octane = None
    powertrain = None

    for part in parts:

        # ОБЪЁМ
        vol_match = re.search(r"(\d+(\.\d+)?)", part)
        if re.search(r"л", part) and vol_match:
            volume = float(vol_match.group(1))
            continue

        # ОКТАН (редко, но на будущее)
        oct_match = re.search(r"\b(80|92|95|98|100)\b", part)
        if oct_match:
            octane = int(oct_match.group(1))
            continue

        # ОСНОВНОЕ ТОПЛИВО
        if part in ["бензин", "дизель", "электро", "газ"]:
            fuel_type = part
            continue

        # ДОПОЛНИТЕЛЬНО
        if "гибрид" in part:
            powertrain = "гибрид"
            continue

        if "ГБО" in part:
            powertrain = "гбо"
            continue

        # fallback
        if not fuel_type:
            fuel_type = part
        else:
            powertrain = part

    return fuel_type, volume, octane, powertrain

# =========================================================
# DRIVER
# =========================================================

driver = webdriver.Chrome()


# =========================================================
# 1. COLLECT LINKS
# =========================================================

try:
    links = []
    url = START_URL

    while len(links) < MAX_CARS:

        driver.get(url)
        time.sleep(2)

        elements = driver.find_elements(By.CSS_SELECTOR, 'a[data-ftid="bull_title"]')

        for el in elements:
            link = el.get_attribute("href")

            if link and link.startswith("/"):
                link = "https://auto.drom.ru" + link

            if link and link not in links:
                links.append(link)

            if len(links) >= MAX_CARS:
                break

        try:
            next_button = driver.find_element(
                By.CSS_SELECTOR,
                'a[data-ftid="component_pagination-item-next"]'
            )
            url = next_button.get_attribute("href")
        except:
            break

    # =========================================================
    # 2. PARSE ADS (with batching)
    # =========================================================

    cars = []
    batch = []

    for link in links:

        driver.get(link)
        time.sleep(2)

        # TITLE
        try:
            title_full = driver.find_element(By.TAG_NAME, "h1").text
        except:
            title_full = ""

        model_raw = None
        year = None
        city = None

        m = re.match(r"Продажа (.+?), (\d{4}) год (?:в|во) (.+)", title_full)

        if m:
            model_raw = m.group(1).strip()
            year = int(m.group(2))
            city = m.group(3).strip()

        # SPLIT BRAND / MODEL
        brand = None
        model = None

        if model_raw:
            parts = model_raw.split(" ", 1)
            brand = parts[0]
            if len(parts) > 1:
                model = parts[1]
            else:
                model = parts[0]

        # PRICE
        try:
            price_text = driver.find_element(
                By.CSS_SELECTOR,
                'div[data-ftid="bulletin-price"]'
            ).text

            price = int(re.sub(r"\D", "", price_text))
        except:
            price = None

        # DESCRIPTION
        description = None

        try:
            description = driver.find_element(
                By.CSS_SELECTOR,
                'div[data-ftid="info-full"] span[data-ftid="value"]'
            ).text
        except:
            try:
                description = driver.find_element(
                    By.CSS_SELECTOR,
                    'div[data-ftid="info-short"] span[data-ftid="value"]'
                ).text
            except:
                description = None

        # SPECS
        specs = {}

        try:
            rows = driver.find_elements(
                By.CSS_SELECTOR,
                'table[data-ftid="bulletin-specifications"] tr'
            )

            for row in rows:
                try:
                    key = row.find_element(
                        By.CSS_SELECTOR,
                        'th[data-ftid="property"]'
                    ).text.strip()

                    value = row.find_element(
                        By.CSS_SELECTOR,
                        'td[data-ftid="value"]'
                    ).text.strip()

                    specs[key] = value
                except:
                    continue
        except:
            pass

        # CLEAN NUMBERS

        mileage = safe_int(specs.get("Пробег"))
        power = safe_int(specs.get("Мощность"))

        #mileage = specs.get("Пробег")
        #power = specs.get("Мощность")

        #if mileage:
        #    mileage_clean = re.sub(r"\D", "", mileage)
        #    mileage = int(mileage_clean) if mileage_clean else None                 # mileage = safe_int(specs.get("Пробег"))
        #
        #if power:
        #    power_clean = re.sub(r"\D", "", power)
        #    power = int(power_clean) if power_clean else None                       # mileage = safe_int(specs.get("Пробег"))

        # ENGINE PARSING
        engine_raw = specs.get("Двигатель")
        fuel_type, engine_volume, octane, powertrain = parse_engine(engine_raw)

        car = {
            # ADS
            "source": "drom",
            "url": link,

            # SNAPSHOT
            "brand": brand,
            "model": model,
            "price": price,
            "year": year,

            "sale_region": city,

            "mileage": mileage,
            "transmission": specs.get("Коробка передач"),
            "drive_type": specs.get("Привод"),
            "color": specs.get("Цвет"),
            "steering_wheel": specs.get("Руль"),

            "engine_power": power,
            "engine_volume": engine_volume,

            "fuel_type": fuel_type,
            "octane": octane,
            "powertrain": powertrain,

            "description": description,

            ## optional
            ## "engine_volume": None,
            ## "fuel_type": None,
            ## "body_type": None,
            ## "brand_origin_country": None,
            ## "license_plate": None
        }

        ## if not car["url"]:
        ##     continue

        batch.append(car)

        if len(batch) >= BATCH_SIZE:
            cars.extend(batch)
            batch = []

    # остаток
    if batch:
        cars.extend(batch)

    driver.quit()

finally:
    driver.quit()

# =========================================================
# OUTPUT
# =========================================================

print(json.dumps(cars, ensure_ascii=False))