# -*- coding: utf-8 -*-
import sys
import os
import sqlite3

CONFIG_FILE = r"AutoScope\Configs\BaseDataBaseConfig.txt"
DB_FOLDER = r"AutoScope\Databases"


# ===================== CONFIG =====================

def parse_config(config_file=CONFIG_FILE):
    """Читает конфигурацию таблиц"""
    tables = {}
    current_table = None

    if not os.path.exists(config_file):
        print(f"CONFIG ERROR: файл не найден: {config_file}")
        return tables

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


# ===================== DB COMMANDS =====================

def db_list():
    """Вывод списка баз данных"""
    os.makedirs(DB_FOLDER, exist_ok=True)

    dbs = [f for f in os.listdir(DB_FOLDER) if f.endswith(".db")]

    if not dbs:
        print("DB_LIST: пусто")
        return

    print("DB_LIST:")
    for db in dbs:
        print(db)


def db_create(db_name):
    """Создание базы данных"""
    if not db_name:
        print("ERROR: не указано имя базы")
        return

    os.makedirs(DB_FOLDER, exist_ok=True)

    db_file = os.path.join(DB_FOLDER, f"{db_name}.db")

    # если уже существует — пересоздаём
    if os.path.exists(db_file):
        print(f"DB_CREATE: база {db_name}.db уже существует, пересоздаём...")
        os.remove(db_file)

    conn = sqlite3.connect(db_file)
    cursor = conn.cursor()

    tables = parse_config()

    if not tables:
        print("WARNING: конфиг пустой, база создана без таблиц")
    else:
        for table, fields in tables.items():
            field_defs = ", ".join([f"{k} {v}" for k, v in fields.items()])
            sql = f"CREATE TABLE IF NOT EXISTS {table} ({field_defs});"
            cursor.execute(sql)

    conn.commit()
    conn.close()

    print(f"DB_CREATE: база {db_name}.db создана")


def db_delete(db_name):
    """Удаление базы данных"""
    if not db_name:
        print("ERROR: не указано имя базы")
        return

    db_file = os.path.join(DB_FOLDER, f"{db_name}.db")

    if not os.path.exists(db_file):
        print(f"DB_DELETE: база {db_name}.db не найдена")
        return

    os.remove(db_file)
    print(f"DB_DELETE: база {db_name}.db удалена")


# ===================== COMMAND HANDLER =====================

def main():
    if len(sys.argv) < 2:
        print("ERROR: не указана команда")
        print("Использование:")
        print("  dbCreate <name>")
        print("  dbDelete <name>")
        print("  dbList")
        return

    command = sys.argv[1]

    if command == "dbCreate":
        if len(sys.argv) < 3:
            print("ERROR: укажите имя базы")
            return
        db_create(sys.argv[2])

    elif command == "dbDelete":
        if len(sys.argv) < 3:
            print("ERROR: укажите имя базы")
            return
        db_delete(sys.argv[2])

    elif command == "dbList":
        db_list()

    else:
        print(f"ERROR: неизвестная команда '{command}'")


# ===================== ENTRY =====================

if __name__ == "__main__":
    main()