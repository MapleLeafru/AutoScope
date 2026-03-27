import sys
import os
import sqlite3
import json

sys.stdout.reconfigure(encoding='utf-8')

#BaseDataBaseConfig_PATH = sys.argv[3] + "\BaseDataBaseConfig.txt"
#DB_PATH = sys.argv[4]

# ===================== CONFIG =====================

#def parse_config(config_file=BaseDataBaseConfig_PATH):
def parse_config(config_file):
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

# ===================== input json =====================

def read_input_json():
    try:
        input_json_data = sys.stdin.read()
        return json.loads(input_json_data)
    except Exception as e:
        print("JSON ERROR:", e)
        return None

# ===================== DB COMMANDS =====================

def db_create(db_name):
    """Создание базы данных"""
    if not db_name:
        print("ERROR: не указано имя базы")
        return

    os.makedirs(DB_PATH, exist_ok=True)

    db_file = os.path.join(DB_PATH, f"{db_name}.db")

    # если уже существует — пересоздаём
    if os.path.exists(db_file):
        print(f"DB_CREATE: база {db_name}.db уже существует, пересоздаём...")
        os.remove(db_file)

    conn = sqlite3.connect(db_file)
    cursor = conn.cursor()

    tables = parse_config(BaseDataBaseConfig_PATH)

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

    db_file = os.path.join(DB_PATH, f"{db_name}.db")

    if not os.path.exists(db_file):
        print(f"DB_DELETE: база {db_name}.db не найдена")
        return

    os.remove(db_file)
    print(f"DB_DELETE: база {db_name}.db удалена")


# ===================== COMMAND HANDLER =====================

def main():
    global BaseDataBaseConfig_PATH
    global DB_PATH 

    input_json_data = read_input_json()
    if not input_json_data:
        return
    
    BaseDataBaseConfig_PATH = input_json_data.get("configPath") + "\BaseDataBaseConfig.txt"
    DB_PATH = input_json_data.get("dbPath") 

    command = input_json_data.get("command")
    db_name = input_json_data.get("db_name")

    print("DEBUG JSON:", input_json_data)

    if command == "dbCreate":
        db_create(db_name)

    elif command == "dbDelete":
        db_delete(db_name)

    else:
        print(f"ERROR: неизвестная команда '{command}'")


# ===================== ENTRY =====================

if __name__ == "__main__":
    main()