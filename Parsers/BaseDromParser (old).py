from selenium import webdriver
from selenium.webdriver.common.by import By
import pandas as pd
import time
import re

# -----------------------------
# НАСТРОЙКИ
# -----------------------------

#MAX_CARS = 150
#START_URL = "https://auto.drom.ru/subaru/levorg/"
#FILE_NAME = "levorg_drom.csv"

#MAX_CARS = 50
#START_URL = "https://auto.drom.ru/toyota/land_cruiser_prado/"
#FILE_NAME = "land_cruiser_prado_drom.csv"

#MAX_CARS = 10
#START_URL = "https://auto.drom.ru/subaru/forester/"
#FILE_NAME = "subaru_forester_drom.csv"

MAX_CARS = 10
START_URL = "https://auto.drom.ru/toyota/camry/"
FILE_NAME = "toyota_camry_drom.csv"

driver = webdriver.Chrome()

# -----------------------------
# 1. СБОР ССЫЛОК
# -----------------------------

links = []
url = START_URL

print("Collecting links...")

while len(links) < MAX_CARS:

    driver.get(url)
    time.sleep(3)

    elements = driver.find_elements(By.CSS_SELECTOR, 'a[data-ftid="bull_title"]')

    for el in elements:

        link = el.get_attribute("href")

        if link and link.startswith("/"):
            link = "https://auto.drom.ru" + link

        if link and link not in links:
            links.append(link)

        if len(links) >= MAX_CARS:
            break

    print(f"Links collected: {len(links)}")

    try:
        next_button = driver.find_element(
            By.CSS_SELECTOR,
            'a[data-ftid="component_pagination-item-next"]'
        )
        url = next_button.get_attribute("href")

    except:
        print("No more pages")
        break

print("Total links:", len(links))


# -----------------------------
# 2. ПАРСИНГ ОБЪЯВЛЕНИЙ
# -----------------------------

cars = []

for i, link in enumerate(links):

    print(f"Parsing {i+1}/{len(links)}")

    driver.get(link)
    time.sleep(2)

    # TITLE
    try:
        title_full = driver.find_element(By.TAG_NAME, "h1").text
    except:
        title_full = ""

    model = year = city = None

    m = re.match(r"Продажа (.+?), (\d{4}) год (?:в|во) (.+)", title_full)

    if m:
        model = m.group(1).strip()
        year = int(m.group(2))
        city = m.group(3).strip()

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

    # -----------------------------
    # ОЧИСТКА ЧИСЕЛ
    # -----------------------------

    mileage = specs.get("Пробег")
    power = specs.get("Мощность")

    if mileage:
        mileage = int(re.sub(r"\D", "", mileage))

    if power:
        power = int(re.sub(r"\D", "", power))

    car = {

        "model": model,
        "year": year,
        "city": city,
        "price": price,
        "mileage": mileage,
        "engine": specs.get("Двигатель"),
        "power": power,
        "transmission": specs.get("Коробка передач"),
        "drivetrain": specs.get("Привод"),
        "color": specs.get("Цвет"),
        "owners": specs.get("Владельцы"),
        "wheel": specs.get("Руль"),
        "generation": specs.get("Поколение"),
        "description": description,
        "link": link

    }

    cars.append(car)


driver.quit()

# -----------------------------
# 3. СОХРАНЕНИЕ CSV
# -----------------------------

df = pd.DataFrame(cars)

df.to_csv(FILE_NAME, index=False)

print("Saved:", len(df), "cars")
print(df)