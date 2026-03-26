import sys
import sqlite3
import os

print("Python Core work")
print("Python version:", sys.version)

# argv[0] = core.py, argv[1] = путь к базе
if len(sys.argv) < 2:
    print("Не указан путь к базе данных!")
    sys.exit(1)

db_path = sys.argv[1]

if not os.path.exists(db_path):
    print(f"Файл базы не найден: {db_path}")
    sys.exit(1)

# подключаемся
conn = sqlite3.connect(db_path)
cursor = conn.cursor()

# пример: чтение всех автомобилей
cursor.execute("SELECT * FROM cars")
for row in cursor.fetchall():
    print(row)

conn.close()