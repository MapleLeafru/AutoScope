import sys
import os
import sqlite3
import json

# Добавляем корень проекта в PYTHONPATH
ROOT_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
sys.path.append(ROOT_DIR)

from Utils.ConfigLoader import ConfigLoader

sys.stdout.reconfigure(encoding='utf-8')

# ===================== input json =====================

BaseDataBaseConfig_PATH = None
DB_PATH = None
COMMAND = "dev_command"
DB_FILE_NAME = "dev_test_ifyouseeitprogrammbug"

try:
    input_json_data = sys.stdin.read()
    input_json_data = json.loads(input_json_data)
    # print("DEBUG JSON:", input_json_data)                                                                                 # debag
except Exception as e:
    print("JSON ERROR:", e)
    input_json_data = {}

#BaseDataBaseConfig_PATH = os.path.join(
#    input_json_data.get("configPath", ""),
#    "BaseDataBaseConfig.json"
#)

DB_PATH = input_json_data.get("dbPath") 
COMMAND = input_json_data.get("command")
DB_FILE_NAME = input_json_data.get("dbFileName")

# ===================== CONFIG =====================

#def parse_config(config_file=BaseDataBaseConfig_PATH):
#    """Читает JSON конфигурацию таблиц"""
#    
#    if not os.path.exists(config_file):
#        print(f"CONFIG ERROR: файл не найден: {config_file}")
#        return {}
#
#    try:
#        with open(config_file, encoding="utf-8") as f:
#            return json.load(f)
#    except json.JSONDecodeError as e:
#        print(f"CONFIG ERROR: ошибка JSON: {e}")
#        return {}


# ===================== SQL BUILDER =====================

def build_column_sql(name, params):
    """Создаёт SQL для одного столбца"""
    col = [name, params.get("type", "TEXT")]

    if params.get("primary_key"):
        col.append("PRIMARY KEY")

    if params.get("autoincrement"):
        col.append("AUTOINCREMENT")

    if params.get("not_null"):
        col.append("NOT NULL")

    if params.get("unique"):
        col.append("UNIQUE")

    # --- DEFAULT ---
    if "default" in params:
        default = params["default"]

        if default is None:
            col.append("DEFAULT NULL")

        elif isinstance(default, str):
            upper = default.upper()

            # спец значения SQLite
            if upper in ["CURRENT_TIMESTAMP", "TRUE", "FALSE"]:
                col.append(f"DEFAULT {upper}")
            else:
                col.append(f"DEFAULT '{default}'")

        else:
            col.append(f"DEFAULT {default}")

    # --- CHECK ---
    if "check" in params:
        col.append(f"CHECK({params['check']})")

    return " ".join(col)


def generate_create_table_sql(table_name, columns):
    """Генерирует CREATE TABLE"""
    parts = []

    for col_name, params in columns.items():
        parts.append(build_column_sql(col_name, params))

    columns_sql = ",\n  ".join(parts)

    return f"CREATE TABLE IF NOT EXISTS {table_name} (\n  {columns_sql}\n);"


# ===================== DB COMMANDS =====================

def db_create(dbFileNameForCrate):
    """Создание базы данных"""
    if not dbFileNameForCrate:
        print("ERROR: не указано имя базы")
        return

    os.makedirs(DB_PATH, exist_ok=True)

    # добавляем имя конфига в название БД
    config_name = "BaseDataBaseConfig"
#    config_name = os.path.splitext(os.path.basename(BaseDataBaseConfig_PATH))[0]

    name, ext = os.path.splitext(dbFileNameForCrate)

    if "." not in name:
        dbFileNameForCrate = f"{name}.{config_name}{ext}"

    dbFile = os.path.join(DB_PATH, dbFileNameForCrate)

    # если уже существует — пересоздаём
    if os.path.exists(dbFile):
        print(f"DB_CREATE: база {dbFileNameForCrate} уже существует, пересоздаём...")
        os.remove(dbFile)

    print(f"DB_CREATE: используется конфиг: {config_name}")
    conn = sqlite3.connect(dbFile)
    cursor = conn.cursor()

#    config = parse_config()
    config = ConfigLoader.load_database_config(dbFile)
    if not config:
        print("CONFIG ERROR: не удалось загрузить конфиг через ConfigLoader")

    if not config:
        print("WARNING: конфиг пустой, база создана без таблиц")
    else:
        for table_name, columns in config.items():
            sql = generate_create_table_sql(table_name, columns)
            # print(f"\n[SQL]\n{sql}")  # удобно для дебага                                                                                 # debag
            cursor.execute(sql)

    conn.commit()
    conn.close()

    print(f"DB_CREATE: база {dbFileNameForCrate} создана")


def db_delete(dbFileNameForDelete):
    """Удаление базы данных"""
    if not dbFileNameForDelete:
        print("ERROR: не указано имя базы")
        return

    db_file = os.path.join(DB_PATH, dbFileNameForDelete)

    if not os.path.exists(db_file):
        print(f"DB_DELETE: база {dbFileNameForDelete} не найдена")
        return

    os.remove(db_file)
    print(f"DB_DELETE: база {dbFileNameForDelete} удалена")


# ===================== COMMAND HANDLER =====================

def main():

    if COMMAND == "dbCreate":
        db_create(DB_FILE_NAME)

    elif COMMAND == "dbDelete":
        db_delete(DB_FILE_NAME)

    else:
        print(f"ERROR: неизвестная команда '{COMMAND}'")


# ===================== ENTRY =====================

if __name__ == "__main__":
    main()