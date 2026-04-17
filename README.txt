Таски:

ready 1. Отпалировать базовый парсер, что бы выводил данные как надо бд
2. Допилить api, ну и что бы мог переваривать калл
2.1 Тестовый парсер на другом языке
2.2 Микро момент, подтягивать тип парсера из расширения
ready 3. Модернизировать базовый конфиг (тип топлива -> тип топлива + октановое число + чек на гибрид; prise из real в int и в api округлять в случае чего)
ready 3.1 Сделать конфиг соотвтествий марки и страны (Если в конфиге есть, то подтягиваем, если нет то null) и интегрировать в api !!!!!!!!!!!!
4. Реализовать логгирование
5. Установить связть между входными параметрами и настройками пайплайна (ну что бы то что ты вводишь реально на что то влияло размер пачки, кол-во объявлений (и возврат кол-во найденых))
6. Разобраться в коде


Main (Работа над базовым прототипом):
Реализовать C# -> startPythonPipeLine (parser -> json(грязные или уже чистые данные) -> api+стандартизация -> json(чистые) -> core -> sqlite)

Минимальный Api
В core добавить функции вставки и считывания записи
Связь между питон скриптами (json)
- Сканирование парсеров
Добавить тестовый парсер для отладки
- Сканирование анализаторов
Добавить тестовый анализатор

Второстепенные:
Сделать так что бы базы данных выводились с 1, а не с 0 (Например при удалении баз данных нет возможности выйти)
Запустить парсер
Переписать Utils под class

===============================================================================================================================

Ошибка NETSDK1004 файл ресурсов "...\obj\project.assets.json" не найден. Восстановите пакет NuGet, чтобы создать его.
dotnet restore
python -m compileall AutoScope\Utils - компилирует файлы питона, после того как скачал на новую машину

import sys
!{sys.executable} -m pip install -r "../requirements.txt"
!{sys.executable} -m pip freeze > "../requirements.lock.txt"

На проде удалить ненужные пакеты: pip, setuptools wheel (если ты не будем ставить пакеты на проде) для того что бы уменьшить размер приложения, подробнее у gpt





json:
ads
- id (type: INTEGER, primary_key: true, autoincrement: true)
- source (type: TEXT, default: null)
- url (type: TEXT, default: null)

ads_checks
- id (type: INTEGER, primary_key: true, autoincrement: true)
- ads_id (type: INTEGER, default: null)
- checked_at (type: TEXT, default: CURRENT_TIMESTAMP)
- has_changes (type: BOOLEAN, default: FALSE, check: has_changes IN (0,1))

ads_snapshots
- id (type: INTEGER, primary_key: true, autoincrement: true)
- check_id (type: INTEGER, default: null)
- brand (type: TEXT, default: null)
- model (type: TEXT, default: null)
- price (type: INTEGER, default: null)
- year (type: INTEGER, default: null)
- brand_origin_country (type: TEXT, default: null)
- sale_region (type: TEXT, default: null)
- license_plate (type: TEXT, default: null)
- mileage (type: INTEGER, default: null)
- transmission (type: TEXT, default: null)
- drive_type (type: TEXT, default: null)
- color (type: TEXT, default: null)
- body_type (type: TEXT, default: null)
- steering_wheel (type: TEXT, default: null)
- engine_power (type: INTEGER, default: null)
- engine_volume (type: REAL, default: null)
- engine_model (type: TEXT, default: null)
- fuel_type (type: TEXT, default: null)
- octane (type: INTEGER, default: null)
- powertrain_type (type: TEXT, default: null)
- description (type: TEXT, default: null)