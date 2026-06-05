# AutoScope Module Contract v1

Этот контракт нужен для подключаемых парсеров и анализаторов AutoScope.

## Поддерживаемые типы модулей

- Python: `.py`
- Java: `.jar`
- Windows executable: `.exe`

Java-модули должны быть заранее скомпилированы в `.jar`. AutoScope не компилирует `.java` файлы и не устанавливает Java автоматически.

## Настройки сред выполнения

Файл: `Configs/ParserDefaultSettings.json`

```json
{
  "startUrl": "https://auto.drom.ru/subaru/levorg/",
  "maxCars": 22,
  "streamBatchSize": 4,
  "pythonPath": "",
  "javaPath": "java"
}
```

- `pythonPath`: пустая строка означает использовать встроенный `AutoScope/Python/python.exe`, который передаёт C#-клиент.
- `javaPath`: значение `java` означает использовать Java из `PATH`.
- если нужна конкретная Java, нужно указать полный путь, например `C:\Program Files\Java\jdk-21\bin\java.exe`.

Если Java не установлена или путь указан неверно, AutoScope не должен падать без объяснения. `ModuleRunner` вернёт понятную ошибку в результат пайплайна и запишет её в лог.

## Общий входной контракт

Любой модуль получает один JSON-объект через `stdin`.

Парсер получает примерно такой объект:

```json
{
  "parser": {
    "modulePath": "C:\\...\\Parsers\\BaseDromParser.py",
    "parserPath": "C:\\...\\Parsers\\BaseDromParser.py",
    "runtime": "python",
    "python": "C:\\...\\Python\\python.exe",
    "java": "java"
  },
  "parserSettings": {
    "startUrl": "https://auto.drom.ru/subaru/levorg/",
    "maxCars": 22,
    "streamBatchSize": 4
  },
  "runtimeSettings": {
    "pythonPath": "C:\\...\\Python\\python.exe",
    "javaPath": "java"
  },
  "dbPath": "C:\\...\\Databases\\base.BaseDataBaseConfig.db",
  "configPath": "C:\\...\\Configs"
}
```

Анализатор получает похожий объект, но в нём будет блок `analyzer` вместо `parser`.

## Контракт парсера

Парсер должен:

1. прочитать JSON из `stdin`;
2. брать параметры из `parserSettings`;
3. писать в `stdout` только JSON Lines;
4. каждая строка `stdout` должна быть валидным JSON;
5. технические сообщения, debug и ошибки писать в `stderr`.

Пример строки stdout:

```json
[{"source":"drom","url":"https://...","brand":"Subaru","model":"Levorg","price":1800000}]
```

То есть один batch = одна строка.

## Контракт анализатора

Анализатор должен:

1. прочитать JSON из `stdin`;
2. обработать поле `data`;
3. вернуть один JSON-объект через `stdout`;
4. технические сообщения писать в `stderr`.

Пример stdout:

```json
{"status":"success","reportPath":"C:\\...\\Reports\\report.html"}
```

## Главное правило stdout/stderr

`stdout` — только машинный JSON.

`stderr` — любые сообщения для логов.

Это правило важно для Python, Java и `.exe` одинаково.
