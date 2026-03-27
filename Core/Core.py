import sys
import sqlite3
import os

sys.stdout.reconfigure(encoding='utf-8')

print("Python Core work")
print("Python version:", sys.version)

MAX_ROWS_DISPLAY = 10  # Максимальное количество строк для вывода

# argv[0] = core.py, argv[1] = путь к базе
if len(sys.argv) < 2:
    print("Не указан путь к базе данных!")
    sys.exit(1)

db_path = sys.argv[1]

if not os.path.exists(db_path):
    print(f"Файл базы не найден: {db_path}")
    sys.exit(1)

# подключаемся к базе
conn = sqlite3.connect(db_path)
cursor = conn.cursor()

# Получаем список таблиц
cursor.execute("SELECT name FROM sqlite_master WHERE type='table';")
tables = [row[0] for row in cursor.fetchall()]

if not tables:
    print("В базе данных нет таблиц.")
else:
    print(f"В базе найдено таблиц: {tables}")
    for table in tables:
        # Узнаем количество строк в таблице
        cursor.execute(f"SELECT COUNT(*) FROM {table}")
        count = cursor.fetchone()[0]
        print(f"\nТаблица '{table}', строк: {count}")

        # Выводим первые MAX_ROWS_DISPLAY строк
        cursor.execute(f"SELECT * FROM {table} LIMIT {MAX_ROWS_DISPLAY}")
        rows = cursor.fetchall()
        if rows:
            # Получаем имена колонок
            columns = [col[0] for col in cursor.description]
            print("\t" + "\t".join(columns))
            for row in rows:
                print("\t" + "\t".join(str(r) if r is not None else "NULL" for r in row))
        else:
            print("\tНет данных для отображения")

conn.close()
print("\nРабота Core.py завершена.")