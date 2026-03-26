import os
import sqlite3

CONFIG_FILE = r"AutoScope\Configs\BaseDataBaseConfig.txt"
DB_FOLDER = r"AutoScope\Databases"

def parse_config(config_file=CONFIG_FILE):
    """Читает конфигурацию и возвращает словарь таблиц и полей"""
    tables = {}
    current_table = None
    with open(config_file, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            if line.startswith("[") and line.endswith("]"):
                current_table = line[1:-1]
                tables[current_table] = {}
            elif "=" in line and current_table:
                key, value = map(str.strip, line.split("=", 1))
                tables[current_table][key] = value
    return tables

def create_database():
    """Создание новой базы данных с проверкой существующих"""
    os.makedirs(DB_FOLDER, exist_ok=True)

    # Сканируем существующие базы
    existing_dbs = [os.path.basename(f) for f in os.listdir(DB_FOLDER) if f.endswith(".db")]

    while True:
        db_name = input("Введите название новой базы данных (без .db): ").strip()
        if not db_name:
            print("Название не может быть пустым")
            continue
        db_file = os.path.join(DB_FOLDER, f"{db_name}.db")

        if f"{db_name}.db" in existing_dbs:
            answer = input(f"База {db_name}.db уже существует. Пересоздать? (y/n): ").lower()
            if answer != "y":
                continue
            else:
                os.remove(db_file)
        break

    # Создаём подключение
    conn = sqlite3.connect(db_file)
    cursor = conn.cursor()

    # Парсим конфиг
    tables = parse_config()
    for table, fields in tables.items():
        field_defs = ", ".join([f"{k} {v}" for k, v in fields.items()])
        sql = f"CREATE TABLE IF NOT EXISTS {table} ({field_defs});"
        cursor.execute(sql)

    conn.commit()
    conn.close()

    print(f"База {db_file} создана успешно")
    return db_file
