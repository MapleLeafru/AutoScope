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


## Update v2: Java test modules and dictionaries

### Test Java parser

File to copy into `Parsers`:

- `TestJavaParser.jar`

Optional source file:

- `TestJavaParser.java`

The parser reads the standard AutoScope request from `stdin` and prints one JSON Lines batch to `stdout`.
It writes diagnostics to `stderr`.

### Test Java analyzer

File to copy into `Analyzers`:

- `TestJavaAnalyzer.jar`

Optional source file:

- `TestJavaAnalyzer.java`

The analyzer reads the data object produced by `OutputApi`, counts objects inside the `data` array and prints one JSON result to `stdout`.

### Dictionaries directory

Dictionary files should be stored in:

```text
Configs/Dictionaries/
```

Current dictionary:

```text
Configs/Dictionaries/BrandCountryMap.json
```

For backward compatibility, `ConfigLoader.load_brand_country_map()` still falls back to the old location:

```text
Configs/BrandCountryMap.json
```
