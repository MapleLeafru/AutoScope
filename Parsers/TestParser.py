import sys
import json
import random

# =========================================================
# MODE SWITCH
# =========================================================

random.seed(42)
SAFE_MODE = True   # ← нормальная работа
# SAFE_MODE = False  # ← включи для теста ошибок


# =========================================================
# INPUT
# =========================================================

input_data = json.loads(sys.stdin.read())


# =========================================================
# GENERATORS
# =========================================================

def generate_valid_data():
    return {
        "source": "test_parser",
        "url": f"https://example.com/ad/{random.randint(1, 1000)}",
        "brand": "BMW",
        "model": "X5",
        "price": random.randint(10000, 50000),
        "year": 2015,
        "mileage": random.randint(50000, 200000),
        "color": "black",
        "description": "Test car"
    }


def generate_invalid_data():

    data = {}

    # ❌ иногда вообще нет url
    if random.choice([True, False]):
        data["url"] = None
    else:
        data["url"] = f"https://example.com/ad/{random.randint(1, 1000)}"

    # ❌ неправильные типы
    data["price"] = random.choice([
        "cheap",        # строка вместо числа
        "123abc",       # мусор
        None
    ])

    # ❌ пропущенные поля
    if random.choice([True, False]):
        data["brand"] = "BMW"

    # ❌ лишние поля (которых нет в БД)
    data["unexpected_field"] = "???"

    # ❌ странные значения
    data["mileage"] = random.choice([
        "100000",   # строка вместо int
        -500,       # отрицательное
        None
    ])

    # ❌ кривые строки
    data["model"] = "   X5   "

    # ❌ иногда вообще пустой словарь
    if random.choice([False, False, True]):
        return {}

    return data


# =========================================================
# MAIN
# =========================================================

if SAFE_MODE:
    result = generate_valid_data()
else:
    result = generate_invalid_data()


# =========================================================
# OUTPUT
# =========================================================

print(json.dumps(result, ensure_ascii=False))