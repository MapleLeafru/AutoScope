using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

/*========================================================ProgramParams========================================================*/

Console.OutputEncoding = System.Text.Encoding.UTF8;

string ROOT_PATH = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\")); // Корень проекта
string DB_PATH = Path.Combine(ROOT_PATH, "Databases"); // Папка с базами
string CONFIGS_PATH = Path.Combine(ROOT_PATH, "Configs"); // Папка с конфигами
string PARSERS_PATH = Path.Combine(ROOT_PATH, "Parsers"); // Папка с парсерами
string ANALYZERS_PATH = Path.Combine(ROOT_PATH, "Analyzers"); // Папка с анализаторами
string[] MODULE_EXTENSIONS = new[] { ".py", ".jar", ".exe" };
string PYTHON_PATH = Path.Combine(ROOT_PATH, @"Python\python.exe"); // python.exe

Console.WriteLine("C# клиент AutoScope запущен");
Console.WriteLine();

/*========================================================ProgramStart========================================================*/

while (true)
{
    menuModeSelection();
}

void menuModeSelection()
{
    Console.WriteLine("Выберите режим:");
    Console.WriteLine("0 - Закрыть программу");
    Console.WriteLine("1 - Запустить InputPipeline (парсинг)");
    Console.WriteLine("2 - Запустить OutputPipeline (анализ)");
    Console.WriteLine("3 - Открыть инструменты");

    int modeNumber = selectingMenuNumber(min: 0, max: 3, "Номер выбранного режима: ");

    if (modeNumber == 0) { Environment.Exit(0); }
    else if (modeNumber == 1) { startInputPythonPipelineManager(); }
    else if (modeNumber == 2) { startOutputPythonPipelineManager(); }
    else if (modeNumber == 3) { menuPythonUtils(); }
}

/*========================================================InputPythonPipelineManager========================================================*/

void startInputPythonPipelineManager()
{
    string pipelineManagerPath = Path.Combine(ROOT_PATH, @"PipelineManagers\InputPipelineManager.py");

    Console.WriteLine("=== Запуск InputPipeline ===");

    string selectedDataBase = dataBaseScanningAndSelection(
        message: "Выберите базу данных для продолжения работы",
        cancelText: "Отменить запуск парсера"
    );
    if (string.IsNullOrWhiteSpace(selectedDataBase)) { return; }

    Console.WriteLine($"Выбрана база: {Path.GetFileName(selectedDataBase)}");
    Console.WriteLine();

    string selectedParser = parserScanningAndSelection(
        message: "Выберите парсер для продолжения работы",
        cancelText: "Отменить запуск парсера"
    );
    if (string.IsNullOrWhiteSpace(selectedParser)) { return; }

    Console.WriteLine($"Выбран парсер: {Path.GetFileName(selectedParser)}");
    Console.WriteLine();

    var defaultSettings = loadDefaultSettings();

    string defaultStartUrl = getStringSetting(defaultSettings, "startUrl", "");
    int defaultMaxCars = getIntSetting(defaultSettings, "maxCars", 10);
    int defaultStreamBatchSize = getIntSetting(defaultSettings, "streamBatchSize", 5);

    string configuredPythonPath = getStringSetting(defaultSettings, "pythonPath", "");
    string modulePythonPath = string.IsNullOrWhiteSpace(configuredPythonPath)
        ? PYTHON_PATH
        : configuredPythonPath;

    string javaPath = getStringSetting(defaultSettings, "javaPath", "java");

    string startUrl = defaultStartUrl;
    int maxCars = defaultMaxCars;
    int streamBatchSize = defaultStreamBatchSize;

    bool useDefaultParserSettings = askYesNo("Запустить парсер с параметрами по умолчанию? y/n: ");

    if (!useDefaultParserSettings)
    {
        startUrl = readStringWithDefault(
            $"Введите START_URL (Пустое поле = значение из конфига: {defaultStartUrl}): ",
            defaultStartUrl
        );

        maxCars = readIntWithDefault(
            $"Введите MAX_CARS (Пустое поле = значение из конфига: {defaultMaxCars}): ",
            defaultMaxCars
        );

        streamBatchSize = readIntWithDefault(
            $"Введите STREAM_BATCH_SIZE (Пустое поле = значение из конфига: {defaultStreamBatchSize}): ",
            defaultStreamBatchSize
        );
    }

    var request = new
    {
        parser = new
        {
            modulePath = selectedParser,
            parserPath = selectedParser,
            runtime = getRuntimeNameByPath(selectedParser),
            python = modulePythonPath,
            java = javaPath
        },
        parserSettings = new
        {
            startUrl = startUrl,
            maxCars = maxCars,
            streamBatchSize = streamBatchSize
        },
        runtimeSettings = new
        {
            pythonPath = modulePythonPath,
            javaPath = javaPath
        },
        dbPath = selectedDataBase,
        configPath = CONFIGS_PATH
    };

    string json = JsonSerializer.Serialize(request);

    ProcessStartInfo start = new ProcessStartInfo
    {
        FileName = PYTHON_PATH,
        Arguments = $"\"{pipelineManagerPath}\"",
        UseShellExecute = false,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };

    using (var process = Process.Start(start))
    {
        if (process == null)
        {
            Console.WriteLine("Не удалось запустить InputPipelineManager.");
            return;
        }

        process.StandardInput.WriteLine(json);
        process.StandardInput.Flush();
        process.StandardInput.Close();

        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();

        process.WaitForExit();

        Console.WriteLine("=== RESULT ===");
        Console.WriteLine(output);

        if (!string.IsNullOrEmpty(error))
        {
            Console.WriteLine("=== ERRORS ===");
            Console.WriteLine(error);
        }
    }
}

/*========================================================OutputPythonPipelineManager========================================================*/

void startOutputPythonPipelineManager()
{
    string pipelineManagerPath = Path.Combine(ROOT_PATH, @"PipelineManagers\OutputPipelineManager.py");

    Console.WriteLine("=== Запуск OutputPipeline ===");

    string selectedDataBase = dataBaseScanningAndSelection(
        message: "Выберите базу данных для анализа",
        cancelText: "Отменить запуск анализатора"
    );
    if (string.IsNullOrWhiteSpace(selectedDataBase)) { return; }

    Console.WriteLine($"Выбрана база: {Path.GetFileName(selectedDataBase)}");
    Console.WriteLine();

    string selectedAnalyzer = analyzerScanningAndSelection(
        message: "Выберите анализатор для продолжения работы",
        cancelText: "Отменить запуск анализатора"
    );
    if (string.IsNullOrWhiteSpace(selectedAnalyzer)) { return; }

    Console.WriteLine($"Выбран анализатор: {Path.GetFileName(selectedAnalyzer)}");
    Console.WriteLine();

    var defaultSettings = loadDefaultSettings();

    string configuredPythonPath = getStringSetting(defaultSettings, "pythonPath", "");
    string modulePythonPath = string.IsNullOrWhiteSpace(configuredPythonPath)
        ? PYTHON_PATH
        : configuredPythonPath;

    string javaPath = getStringSetting(defaultSettings, "javaPath", "java");

    var request = new
    {
        analyzer = new
        {
            modulePath = selectedAnalyzer,
            analyzerPath = selectedAnalyzer,
            runtime = getRuntimeNameByPath(selectedAnalyzer),
            python = modulePythonPath,
            java = javaPath
        },
        runtimeSettings = new
        {
            pythonPath = modulePythonPath,
            javaPath = javaPath
        },
        dbPath = selectedDataBase,
        configPath = CONFIGS_PATH
    };

    string json = JsonSerializer.Serialize(request);

    ProcessStartInfo start = new ProcessStartInfo
    {
        FileName = PYTHON_PATH,
        Arguments = $"\"{pipelineManagerPath}\"",
        UseShellExecute = false,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };

    using (var process = Process.Start(start))
    {
        if (process == null)
        {
            Console.WriteLine("Не удалось запустить OutputPipelineManager.");
            return;
        }

        process.StandardInput.WriteLine(json);
        process.StandardInput.Close();

        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();

        process.WaitForExit();

        Console.WriteLine("=== RESULT ===");
        Console.WriteLine(output);

        if (!string.IsNullOrEmpty(error))
        {
            Console.WriteLine("=== ERRORS ===");
            Console.WriteLine(error);
        }
    }
}

/*========================================================PythonCore========================================================*/

void selectDbForPythonCore()
{
    string selectedDb = dataBaseScanningAndSelection(
        message: "Выберите базу данных для продолжения работы",
        cancelText: "Вернуться назад"
    );
    if (string.IsNullOrWhiteSpace(selectedDb)) { return; }

    Console.WriteLine($"Выбрана база: {Path.GetFileName(selectedDb)}");

    string pythonCore = Path.Combine(ROOT_PATH, @"Core\Core.py");

    ProcessStartInfo coreStart = new ProcessStartInfo
    {
        FileName = PYTHON_PATH,
        Arguments = $"\"{pythonCore}\" \"{selectedDb}\"",
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };

    using (var process = Process.Start(coreStart))
    {
        if (process == null)
        {
            Console.WriteLine("Не удалось запустить Core.py.");
            return;
        }

        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Console.WriteLine(output);
        if (!string.IsNullOrEmpty(error))
            Console.WriteLine("Errors:\n" + error);
    }
}

/*========================================================PythonUtils========================================================*/

void menuPythonUtils()
{
    Console.WriteLine("Выберите инструмент:");
    Console.WriteLine("0 - Вернуться назад");
    Console.WriteLine("1 - Создать новую базу данных");
    Console.WriteLine("2 - Удалить базу данных");

    int toolNumber = selectingMenuNumber(min: 0, max: 2, "Номер выбранного инструмента: ");

    if (toolNumber == 0) { return; }
    else if (toolNumber == 1) { preparationPythonDatabaseManager_dbCreate(); }
    else if (toolNumber == 2) { preparationPythonDatabaseManager_dbDelete(); }
}

void preparationPythonDatabaseManager_dbCreate()
{
    string dataBaseNameToCreate = "";
    while (true)
    {
        Console.Write("Введите название новой базы данных: ");
        dataBaseNameToCreate = Console.ReadLine() ?? "";

        if (dataBaseNameToCreate != "")
        {
            while (true)
            {
                string[] existingDatabases = dataBaseScanning(consoleOutput: false, returnOnlyFileNames: true);
                if (existingDatabases.Contains(dataBaseNameToCreate + ".db"))
                {
                    Console.Write($"База данных с названием <{dataBaseNameToCreate}> уже существует, пересоздать? y/n: ");
                }
                else
                {
                    Console.Write($"Подтвердите создание базы данных <{dataBaseNameToCreate}> (конфиг будет добавлен автоматически) y/n: ");
                }

                string answer = (Console.ReadLine() ?? "").ToUpperInvariant();
                if (answer == "Y" || answer == "Д")
                {
                    startPythonDatabaseManager(dataBaseName: dataBaseNameToCreate, isThereExtension_db: false, commandToRun: "dbCreate");
                    return;
                }
                else if (answer == "N" || answer == "Н")
                {
                    dataBaseNameToCreate = "";
                    return;
                }

                Console.WriteLine("Некорректный ввод. Попробуйте снова.");
            }
        }
        else
        {
            Console.Write("Введено пустое поле. Желаете продолжить создание базы данных? y/n: ");
            string answer = (Console.ReadLine() ?? "").ToUpperInvariant();
            if (answer != "Y" && answer != "Д") { return; }
        }
    }
}

void preparationPythonDatabaseManager_dbDelete()
{
    string dataBasePathToDelete = dataBaseScanningAndSelection(
        message: "Выберите базу данных для удаления",
        cancelText: "Отменить удаление базы данных"
    );
    if (string.IsNullOrWhiteSpace(dataBasePathToDelete)) { return; }

    string dataBaseNameToDelete = Path.GetFileName(dataBasePathToDelete);
    while (true)
    {
        Console.Write($"Вы уверены, что хотите удалить базу данных {dataBaseNameToDelete}? Это действие необратимо. y/n: ");
        string answer = (Console.ReadLine() ?? "").ToUpperInvariant();
        if (answer == "Y" || answer == "Д")
        {
            startPythonDatabaseManager(dataBaseName: dataBaseNameToDelete, isThereExtension_db: true, commandToRun: "dbDelete");
            return;
        }
        else if (answer == "N" || answer == "Н")
        {
            return;
        }
        Console.WriteLine("Некорректный ввод.");
    }
}

void startPythonDatabaseManager(string dataBaseName, bool isThereExtension_db, string commandToRun)
{
    string databaseManagerPath = Path.Combine(ROOT_PATH, @"Utils\DatabaseManager.py");

    ProcessStartInfo startDatabaseManager = new ProcessStartInfo
    {
        FileName = PYTHON_PATH,
        Arguments = $"\"{databaseManagerPath}\"",
        UseShellExecute = false,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };

    if (!isThereExtension_db) { dataBaseName += ".db"; }
    var pythonDatabaseManagerRequest = new
    {
        command = commandToRun,
        dbFileName = dataBaseName,
        configPath = CONFIGS_PATH,
        dbPath = DB_PATH
    };
    string pythonDatabaseManagerRequestJson = JsonSerializer.Serialize(pythonDatabaseManagerRequest);

    using (var pythonDatabaseManagerProcess = Process.Start(startDatabaseManager))
    {
        if (pythonDatabaseManagerProcess == null)
        {
            Console.WriteLine("Не удалось запустить DatabaseManager.py.");
            return;
        }

        pythonDatabaseManagerProcess.StandardInput.WriteLine(pythonDatabaseManagerRequestJson);
        pythonDatabaseManagerProcess.StandardInput.Close();

        string output = pythonDatabaseManagerProcess.StandardOutput.ReadToEnd();
        string error = pythonDatabaseManagerProcess.StandardError.ReadToEnd();

        pythonDatabaseManagerProcess.WaitForExit();

        Console.WriteLine(output);
        if (!string.IsNullOrEmpty(error))
            Console.WriteLine("Errors:\n" + error);
    }
}

/*========================================================parserScanning========================================================*/

string parserScanningAndSelection(string message = "messageError_parserScanningAndSelection", string cancelText = "Вернуться назад")
{
    Console.WriteLine(message);
    string[] parserFiles = parserScanning(consoleOutput: true, returnOnlyFileNames: false, cancelText: cancelText);
    return selectPathFromList(parserFiles, "Введите номер выбранного парсера: ");
}

string[] parserScanning(bool consoleOutput = true, bool returnOnlyFileNames = false, string cancelText = "Вернуться назад")
{
    string[] parserFiles = Directory.Exists(PARSERS_PATH)
        ? Directory.GetFiles(PARSERS_PATH).Where(isSupportedModuleFile).OrderBy(Path.GetFileName).ToArray()
        : new string[0];

    if (parserFiles.Length == 0)
    {
        if (consoleOutput) Console.WriteLine("Нет доступных парсеров.");
        return new string[0];
    }

    if (consoleOutput)
    {
        Console.WriteLine("Найденные парсеры:");
        Console.WriteLine($"0: {cancelText}");
        for (int i = 0; i < parserFiles.Length; i++)
            Console.WriteLine($"{i + 1}: {Path.GetFileName(parserFiles[i])}");
    }

    if (returnOnlyFileNames)
    {
        return parserFiles.Select(Path.GetFileName).ToArray();
    }

    return parserFiles;
}

/*========================================================analyzerScanning========================================================*/

string analyzerScanningAndSelection(string message = "messageError_analyzerScanningAndSelection", string cancelText = "Вернуться назад")
{
    Console.WriteLine(message);
    string[] analyzerFiles = analyzerScanning(consoleOutput: true, returnOnlyFileNames: false, cancelText: cancelText);
    return selectPathFromList(analyzerFiles, "Введите номер выбранного анализатора: ");
}

string[] analyzerScanning(bool consoleOutput = true, bool returnOnlyFileNames = false, string cancelText = "Вернуться назад")
{
    string[] analyzerFiles = Directory.Exists(ANALYZERS_PATH)
        ? Directory.GetFiles(ANALYZERS_PATH).Where(isSupportedModuleFile).OrderBy(Path.GetFileName).ToArray()
        : new string[0];

    if (analyzerFiles.Length == 0)
    {
        if (consoleOutput) Console.WriteLine("Нет доступных анализаторов.");
        return new string[0];
    }

    if (consoleOutput)
    {
        Console.WriteLine("Найденные анализаторы:");
        Console.WriteLine($"0: {cancelText}");
        for (int i = 0; i < analyzerFiles.Length; i++)
            Console.WriteLine($"{i + 1}: {Path.GetFileName(analyzerFiles[i])}");
    }

    if (returnOnlyFileNames)
    {
        return analyzerFiles.Select(Path.GetFileName).ToArray();
    }

    return analyzerFiles;
}

/*========================================================dataBaseScanning========================================================*/

string dataBaseScanningAndSelection(string message = "messageError_dataBaseScanningAndSelection", string cancelText = "Вернуться назад")
{
    Console.WriteLine(message);
    string[] dbFiles = dataBaseScanning(consoleOutput: true, returnOnlyFileNames: false, cancelText: cancelText);
    return selectPathFromList(dbFiles, "Введите номер выбранной базы: ");
}

string[] dataBaseScanning(bool consoleOutput = true, bool returnOnlyFileNames = false, string cancelText = "Вернуться назад")
{
    string[] dbFiles = Directory.Exists(DB_PATH)
        ? Directory.GetFiles(DB_PATH, "*.db").OrderBy(Path.GetFileName).ToArray()
        : new string[0];

    if (dbFiles.Length == 0)
    {
        if (consoleOutput) Console.WriteLine("Нет доступных баз данных.");
        return new string[0];
    }

    if (consoleOutput)
    {
        Console.WriteLine("Найденные базы данных:");
        Console.WriteLine($"0: {cancelText}");
        for (int i = 0; i < dbFiles.Length; i++)
            Console.WriteLine($"{i + 1}: {Path.GetFileName(dbFiles[i])}");
    }

    if (returnOnlyFileNames)
    {
        return dbFiles.Select(Path.GetFileName).ToArray();
    }

    return dbFiles;
}

/*========================================================UniversalFunctions========================================================*/

string selectPathFromList(string[] files, string prompt)
{
    if (files.Length == 0)
    {
        Console.WriteLine();
        return "";
    }

    while (true)
    {
        Console.Write(prompt);
        string input = Console.ReadLine() ?? "";

        if (int.TryParse(input, out int selectedNumber))
        {
            if (selectedNumber == 0)
            {
                Console.WriteLine();
                return "";
            }

            if (selectedNumber >= 1 && selectedNumber <= files.Length)
            {
                Console.WriteLine();
                return files[selectedNumber - 1];
            }
        }

        Console.WriteLine("Некорректный ввод.");
    }
}

Dictionary<string, JsonElement> loadDefaultSettings()
{
    string configFile = Path.Combine(CONFIGS_PATH, "ParserDefaultSettings.json");

    if (!File.Exists(configFile))
    {
        return new Dictionary<string, JsonElement>();
    }

    string configJson = File.ReadAllText(configFile);
    return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(configJson)
        ?? new Dictionary<string, JsonElement>();
}

string getStringSetting(Dictionary<string, JsonElement> settings, string key, string fallback = "")
{
    if (settings != null && settings.TryGetValue(key, out JsonElement value))
    {
        if (value.ValueKind == JsonValueKind.String)
            return value.GetString() ?? fallback;

        return value.ToString();
    }

    return fallback;
}

int getIntSetting(Dictionary<string, JsonElement> settings, string key, int fallback = 0)
{
    if (settings != null && settings.TryGetValue(key, out JsonElement value))
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result))
            return result;

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out result))
            return result;
    }

    return fallback;
}

bool isSupportedModuleFile(string path)
{
    string extension = Path.GetExtension(path).ToLowerInvariant();
    return MODULE_EXTENSIONS.Contains(extension);
}

string getRuntimeNameByPath(string path)
{
    string extension = Path.GetExtension(path).ToLowerInvariant();

    if (extension == ".py") return "python";
    if (extension == ".jar") return "java";
    if (extension == ".exe") return "exe";

    return "unknown";
}

bool askYesNo(string message)
{
    while (true)
    {
        Console.Write(message);
        string answer = (Console.ReadLine() ?? "").Trim().ToUpperInvariant();

        if (answer == "Y" || answer == "YES" || answer == "Д" || answer == "ДА")
        {
            Console.WriteLine();
            return true;
        }

        if (answer == "N" || answer == "NO" || answer == "Н" || answer == "НЕТ")
        {
            Console.WriteLine();
            return false;
        }

        Console.WriteLine("Некорректный ввод. Введите y или n.");
    }
}

string readStringWithDefault(string message, string defaultValue)
{
    Console.Write(message);
    string input = Console.ReadLine() ?? "";
    return string.IsNullOrWhiteSpace(input) ? defaultValue : input;
}

int readIntWithDefault(string message, int defaultValue)
{
    while (true)
    {
        Console.Write(message);
        string input = Console.ReadLine() ?? "";

        if (string.IsNullOrWhiteSpace(input))
            return defaultValue;

        if (int.TryParse(input, out int result))
            return result;

        Console.WriteLine("Некорректное число. Попробуйте снова.");
    }
}

int selectingMenuNumber(int min, int max, string message = "messageError_selectingMenuNumber")
{
    while (true)
    {
        Console.Write(message);
        if (int.TryParse(Console.ReadLine(), out int selectedNumber) && selectedNumber >= min && selectedNumber <= max)
        {
            Console.WriteLine();
            return selectedNumber;
        }
        Console.WriteLine("Некорректный ввод.");
    }
}
