# -*- coding: utf-8 -*-
import sys
import os
import sqlite3
import json

# Добавляем корень проекта в PYTHONPATH, чтобы импорты работали при запуске из C#.
ROOT_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
sys.path.append(ROOT_DIR)

from Utils.ConfigLoader import ConfigLoader

sys.stdout.reconfigure(encoding="utf-8")


# Строит SQL-описание одного столбца по параметрам из JSON-конфига.
def build_column_sql(name, params):
    column_parts = [name, params.get("type", "TEXT")]

    if params.get("primary_key"):
        column_parts.append("PRIMARY KEY")

    if params.get("autoincrement"):
        column_parts.append("AUTOINCREMENT")

    if params.get("not_null"):
        column_parts.append("NOT NULL")

    if params.get("unique"):
        column_parts.append("UNIQUE")

    if "default" in params:
        column_parts.append(_build_default_sql(params["default"]))

    if "check" in params:
        column_parts.append(f"CHECK({params['check']})")

    return " ".join(column_parts)


# Строит SQL-часть DEFAULT для столбца SQLite.
def _build_default_sql(default_value):
    if default_value is None:
        return "DEFAULT NULL"

    if isinstance(default_value, str):
        upper_value = default_value.upper()

        if upper_value in ["CURRENT_TIMESTAMP", "TRUE", "FALSE"]:
            return f"DEFAULT {upper_value}"

        return f"DEFAULT '{default_value}'"

    return f"DEFAULT {default_value}"


# Генерирует CREATE TABLE по имени таблицы и описанию её столбцов.
def generate_create_table_sql(table_name, columns):
    column_sql_parts = []

    for column_name, params in columns.items():
        column_sql_parts.append(build_column_sql(column_name, params))

    columns_sql = ",\n  ".join(column_sql_parts)
    return f"CREATE TABLE IF NOT EXISTS {table_name} (\n  {columns_sql}\n);"


# Создаёт новую базу данных по базовому конфигу AutoScope.
def db_create(db_path, db_file_name):
    if not db_file_name:
        print("ERROR: не указано имя базы")
        return

    os.makedirs(db_path, exist_ok=True)

    config_name = "BaseDataBaseConfig"
    name, ext = os.path.splitext(db_file_name)

    if "." not in name:
        db_file_name = f"{name}.{config_name}{ext}"

    db_file = os.path.join(db_path, db_file_name)

    if os.path.exists(db_file):
        print(f"DB_CREATE: база {db_file_name} уже существует, пересоздаём...")
        os.remove(db_file)

    print(f"DB_CREATE: используется конфиг: {config_name}")

    config = ConfigLoader.load_database_config(db_file)
    if not config:
        print("WARNING: конфиг пустой, база создана без таблиц")
        return

    conn = sqlite3.connect(db_file)

    try:
        cursor = conn.cursor()

        for table_name, columns in config.items():
            if not isinstance(columns, dict):
                continue

            sql = generate_create_table_sql(table_name, columns)
            cursor.execute(sql)

        conn.commit()

    finally:
        conn.close()

    print(f"DB_CREATE: база {db_file_name} создана")


# Удаляет выбранный файл базы данных из папки Databases.
def db_delete(db_path, db_file_name):
    if not db_file_name:
        print("ERROR: не указано имя базы")
        return

    db_file = os.path.join(db_path, db_file_name)

    if not os.path.exists(db_file):
        print(f"DB_DELETE: база {db_file_name} не найдена")
        return

    os.remove(db_file)
    print(f"DB_DELETE: база {db_file_name} удалена")


# Читает JSON-запрос от C#-клиента.
def read_request():
    raw_input = sys.stdin.read()

    if not raw_input:
        return {}

    try:
        return json.loads(raw_input)
    except json.JSONDecodeError as e:
        print(f"JSON ERROR: {e}")
        return {}


# Выбирает команду DatabaseManager и запускает нужное действие.
def main():
    request = read_request()

    db_path = request.get("dbPath")
    command = request.get("command")
    db_file_name = request.get("dbFileName")

    if command == "dbCreate":
        db_create(db_path, db_file_name)
    elif command == "dbDelete":
        db_delete(db_path, db_file_name)
    else:
        print(f"ERROR: неизвестная команда '{command}'")


if __name__ == "__main__":
    main()
