using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using static System.Runtime.InteropServices.JavaScript.JSType;

/*========================================================ProgramParams========================================================*/

Console.OutputEncoding = System.Text.Encoding.UTF8;

string ROOT_PATH = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\")); // Корень проекта
string DB_PATH = Path.Combine(ROOT_PATH, "Databases"); // Папка с базами
string CONFIGS_PATH = Path.Combine(ROOT_PATH, "Configs"); // Папка с конфигами
string PARSERS_PATH = Path.Combine(ROOT_PATH, "Parsers"); // Папка с парсерами
string ANALYZERS_PATH = Path.Combine(ROOT_PATH, "Analyzers"); // Папка с анализаторами
string[] MODULE_EXTENSIONS = new[] { ".py", ".jar", ".exe" };
string PYTHON_PATH = Path.Combine(ROOT_PATH, @"Python\python.exe"); // python.exe

Console.WriteLine("C# Клиент запущен");

/*========================================================ProgramStart========================================================*/

while (true)
{
    menuModeSelection();
}

void menuModeSelection()
{
    // Выбор режима
    Console.WriteLine("Выберите режим:");
    Console.WriteLine("1 - Запустить Input Pipeline (парсинг)");
    Console.WriteLine("2 - Запустить Output Pipeline (анализ)");
    Console.WriteLine("3 - Открыть инструменты");

    int modeNumber = selectingMenuNumber(min: 1, max: 3, "Номер выбранного режима: ");

    if (modeNumber == 1) { startInputPythonPipelineManager(); }
    else if (modeNumber == 2) { startOutputPythonPipelineManager(); }
    else if (modeNumber == 3) { menuPythonUtils(); }
}

/*========================================================InputPythonPipelineManager========================================================*/

void startInputPythonPipelineManager()
{
    string pipelineManagerPath = Path.Combine(ROOT_PATH, @"PipelineManagers\InputPipelineManager.py");

    Console.WriteLine("=== Запуск Pipeline ===");

    // Выбор базы данных
    string selectedDataBase = dataBaseScanningAndSelection(message: "Выберите базу данных для продолжениея работы");
    Console.WriteLine($"Выбрана база: {Path.GetFileName(selectedDataBase)}");
    Console.WriteLine();

    // Выбор парсера
    string selectedParser = parserScanningAndSelection(message: "Выберите парсер для продолжениея работы");
    Console.WriteLine($"Выбран парсер: {Path.GetFileName(selectedParser)}");
    Console.WriteLine();

    //------------------------------------------------------------------------------------------------
    // Задача параметров парсера
    string configFile = Path.Combine(CONFIGS_PATH, "ParserDefaultSettings.json");
    string configJson = File.ReadAllText(configFile);

    var defaultSettings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(configJson);

    // Задаём значения по умолчанию
    string defaultStartUrl = getStringSetting(defaultSettings, "startUrl", "");
    int defaultMaxCars = getIntSetting(defaultSettings, "maxCars", 10);
    int defaultstreamBatchSize = getIntSetting(defaultSettings, "streamBatchSize", 5);

    // Настройки сред выполнения модулей
    // pythonPath пустой = использовать встроенный Python из папки AutoScope/Python
    // javaPath = "java" = использовать Java из PATH
    string configuredPythonPath = getStringSetting(defaultSettings, "pythonPath", "");
    string modulePythonPath = string.IsNullOrWhiteSpace(configuredPythonPath)
        ? PYTHON_PATH
        : configuredPythonPath;

    string javaPath = getStringSetting(defaultSettings, "javaPath", "java");

    // Беоём у пользователя
    Console.Write("Введите START_URL (Пустое поле = значение из конфига): ");
    string startUrlInput = Console.ReadLine();

    Console.Write("Введите MAX_CARS (Пустое поле = значение из конфига): ");
    string maxCarsInput = Console.ReadLine();

    Console.Write("Введите STREAM_BATCH_SIZE (Пустое поле = значение из конфига): ");
    string streamBatchSizeInput = Console.ReadLine();

    // Подставляем
    string startUrl = string.IsNullOrWhiteSpace(startUrlInput)
        ? defaultStartUrl
        : startUrlInput;

    int maxCars = string.IsNullOrWhiteSpace(maxCarsInput)
        ? defaultMaxCars
        : int.Parse(maxCarsInput);

    int streamBatchSize = string.IsNullOrWhiteSpace(streamBatchSizeInput)
        ? defaultstreamBatchSize
        : int.Parse(streamBatchSizeInput);
    //------------------------------------------------------------------------------------------------

    // Формируем json
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

    Console.WriteLine("=== Запуск Output Pipeline ===");

    // Выбор базы данных
    string selectedDataBase = dataBaseScanningAndSelection("Выберите базу данных для анализа");
    Console.WriteLine($"Выбрана база: {Path.GetFileName(selectedDataBase)}");
    Console.WriteLine();

    // Выбор анализатора
    string selectedAnalyzer = analyzerScanningAndSelection("Выберите анализатор для продолжения работы");

    Console.WriteLine($"Выбран анализатор: {Path.GetFileName(selectedAnalyzer)}");
    Console.WriteLine();

    var defaultSettings = loadDefaultSettings();

    string configuredPythonPath = getStringSetting(defaultSettings, "pythonPath", "");
    string modulePythonPath = string.IsNullOrWhiteSpace(configuredPythonPath)
        ? PYTHON_PATH
        : configuredPythonPath;

    string javaPath = getStringSetting(defaultSettings, "javaPath", "java");

    // Формируем JSON
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
    // Режим выбора существующей базы
    string selectedDb = dataBaseScanningAndSelection(message: "Выберите базу данных для продолжениея работы");

    Console.WriteLine($"Выбрана база: {Path.GetFileName(selectedDb)}");

    // Далее можно запускать Core.py с этой базой
    string pythonCore = Path.Combine(ROOT_PATH, @"Core\Core.py");

    ProcessStartInfo coreStart = new ProcessStartInfo();
    coreStart.FileName = PYTHON_PATH;
    coreStart.Arguments = $"\"{pythonCore}\" \"{selectedDb}\"";
    coreStart.UseShellExecute = false;

    using (var process = Process.Start(coreStart))
    {
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Console.WriteLine(output);
        if (!string.IsNullOrEmpty(error))
            Console.WriteLine("Errors:\n" + error);
    }
    return;
}

/*========================================================PythonUtils========================================================*/

void menuPythonUtils()
{
    // Выбор инструмента
    Console.WriteLine("Выберите инструмент:");
    Console.WriteLine("0 - Вернуться назад");
    Console.WriteLine("1 - Создать новую базу данных");
    Console.WriteLine("2 - Удалить базу данных");

    int toolNumber = selectingMenuNumber(min: 0, max: 2, "Номер выбранного интсрумента: ");

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
        dataBaseNameToCreate = Console.ReadLine();
        if (dataBaseNameToCreate != "")
        {
            while (true)
            {
                string[] existingDatabases = dataBaseScanning(consoleOutput: false, returnOnlyFileNames: true);
                if (existingDatabases.Contains(dataBaseNameToCreate + ".db")) { Console.Write($"База данных с названием <{dataBaseNameToCreate}> уже существует, пересоздать? y/n: "); }
                else { Console.Write($"Подтвердите создание базы данных <{dataBaseNameToCreate}> (конфиг будет добавлен автоматически) y/n: "); }
                string ansver = Console.ReadLine().ToUpper();
                if (ansver == "Y") { startPythonDatabaseManager(dataBaseName: dataBaseNameToCreate, isThereExtension_db: false, commandToRun: "dbCreate"); return; }
                else if (ansver == "N") { dataBaseNameToCreate = ""; return; }

                Console.WriteLine("Некорректный ввод. Попробуйте снова.");
            }
        }
        else
        {
            Console.Write("Введено пустое поле. Желаете продолжить создание базы данных? y/n: ");
            if (Console.ReadLine().ToUpper() != "Y") { return; }
        }
    }
}

void preparationPythonDatabaseManager_dbDelete()
{
    string dataBasePathToDelete = dataBaseScanningAndSelection(message: "Выберите номер базы данных для удаления: ");
    // Добавить удаление на 0
    string dataBaseNameToDelete = Path.GetFileName(dataBasePathToDelete);
    while (true)
    {
        Console.Write($"Вы уверены что хотите удалить базу данных {dataBaseNameToDelete}? Это действие не обратимо y/n: ");
        string ansver = Console.ReadLine().ToUpper();
        if (ansver == "Y") { startPythonDatabaseManager(dataBaseName: dataBaseNameToDelete, isThereExtension_db: true, commandToRun: "dbDelete"); return; }
        else if (ansver == "N") { return; }
        Console.WriteLine("Некорректный ввод.");
    }
}

void startPythonDatabaseManager(string dataBaseName, bool isThereExtension_db, string commandToRun)
{
    string DatabaseManagerPath = Path.Combine(ROOT_PATH, @"Utils\DatabaseManager.py");

    ProcessStartInfo startDatabaseManager = new ProcessStartInfo // Параметры запуска
    {
        FileName = PYTHON_PATH,
        Arguments = $"\"{DatabaseManagerPath}\"",
        UseShellExecute = false,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };

    // Создаём json с переменными для передачи перед запуском
    if (!isThereExtension_db) { dataBaseName += ".db"; }
    var pythonDatabaseManager_request = new
    {
        command = commandToRun,
        dbFileName = dataBaseName,
        configPath = CONFIGS_PATH,
        dbPath = DB_PATH
    };
    string pythonDatabaseManager_request_json = JsonSerializer.Serialize(pythonDatabaseManager_request);

    using (var pythonDatabaseManagerProcess = Process.Start(startDatabaseManager))
    {
        pythonDatabaseManagerProcess.StandardInput.WriteLine(pythonDatabaseManager_request_json);
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
string parserScanningAndSelection(string message = "messageEror_parserScanningAndSelection")
{
    Console.WriteLine(message);

    string[] parserFiles = parserScanning();

    int selectedIndex = 0;
    while (true)
    {
        Console.Write("Введите номер выбранного парсера: ");
        string input = Console.ReadLine();
        if (int.TryParse(input, out selectedIndex) && selectedIndex >= 0 && selectedIndex < parserFiles.Length)
            break;
        Console.WriteLine("Некорректный ввод.");
    }
    return parserFiles[selectedIndex];
}

string[] parserScanning(bool consoleOutput = true, bool returnOnlyFileNames = false) // Возвращает массив путей до парсеров
{
    string[] parserFiles = Directory.Exists(PARSERS_PATH)
        ? Directory.GetFiles(PARSERS_PATH).Where(isSupportedModuleFile).ToArray()
        : new string[0]; // Если  Directory.Exists(DB_PATH) Существует, то Directory.GetFiles(DB_PATH), а если нет, то new string[0]
    if (parserFiles.Length == 0)
    {
        if (consoleOutput) Console.WriteLine("Нет доступных парсеров.");
        throw new Exception("Нет доступных парсеров.");
    }

    if (consoleOutput)
    {
        Console.WriteLine("Найденые парсеры:");
        for (int i = 0; i < parserFiles.Length; i++)
            Console.WriteLine($"{i}: {Path.GetFileName(parserFiles[i])}");
    }

    if (returnOnlyFileNames)
    {
        string[] parserFilesNames = new string[parserFiles.Length];
        for (int i = 0; i < parserFiles.Length; i++)
            parserFilesNames[i] = Path.GetFileName(parserFiles[i]);
        return parserFilesNames;
    }

    return parserFiles;
}


/*========================================================analyzerScanning========================================================*/
string analyzerScanningAndSelection(string message = "messageEror_analyzerScanningAndSelection")
{
    Console.WriteLine(message);

    string[] analyzerFiles = analyzerScanning();

    int selectedIndex = 0;
    while (true)
    {
        Console.Write("Введите номер выбранного анализатора: ");
        string input = Console.ReadLine();
        if (int.TryParse(input, out selectedIndex) && selectedIndex >= 0 && selectedIndex < analyzerFiles.Length)
            break;
        Console.WriteLine("Некорректный ввод.");
    }
    return analyzerFiles[selectedIndex];
}

string[] analyzerScanning(bool consoleOutput = true, bool returnOnlyFileNames = false)
{
    string[] analyzerFiles = Directory.Exists(ANALYZERS_PATH)
        ? Directory.GetFiles(ANALYZERS_PATH).Where(isSupportedModuleFile).ToArray()
        : new string[0];

    if (analyzerFiles.Length == 0)
    {
        if (consoleOutput) Console.WriteLine("Нет доступных анализаторов.");
        throw new Exception("Нет доступных анализаторов.");
    }

    if (consoleOutput)
    {
        Console.WriteLine("Найденые анализаторы:");
        for (int i = 0; i < analyzerFiles.Length; i++)
            Console.WriteLine($"{i}: {Path.GetFileName(analyzerFiles[i])}");
    }

    if (returnOnlyFileNames)
    {
        string[] analyzerFilesNames = new string[analyzerFiles.Length];
        for (int i = 0; i < analyzerFiles.Length; i++)
            analyzerFilesNames[i] = Path.GetFileName(analyzerFiles[i]);
        return analyzerFilesNames;
    }

    return analyzerFiles;
}

/*========================================================dataBaseScanning========================================================*/
string dataBaseScanningAndSelection(string message = "messageEror_dataBaseScanningAndSelection")
{
    Console.WriteLine(message);

    string[] dbFiles = dataBaseScanning();

    int selectedIndex = 0;
    while (true)
    {
        Console.Write("Введите номер выбранной базы: ");
        string input = Console.ReadLine();
        if (int.TryParse(input, out selectedIndex) && selectedIndex >= 0 && selectedIndex < dbFiles.Length)
            break;
        Console.WriteLine("Некорректный ввод.");
    }
    return dbFiles[selectedIndex];
}

string[] dataBaseScanning(bool consoleOutput = true, bool returnOnlyFileNames = false) // Возвращает массив путей до базы данных
{
    string[] dbFiles = Directory.Exists(DB_PATH) ? Directory.GetFiles(DB_PATH, "*.db") : new string[0]; // Если  Directory.Exists(DB_PATH) Существует, то Directory.GetFiles(DB_PATH, "*.db"), а если нет, то new string[0]
    if (dbFiles.Length == 0)
    {
        if (consoleOutput) Console.WriteLine("Нет доступных баз данных.");
        throw new Exception("Нет доступных баз данных.");
    }

    if (consoleOutput)
    {
        Console.WriteLine("Найденые базы данных:");
        for (int i = 0; i < dbFiles.Length; i++)
            Console.WriteLine($"{i}: {Path.GetFileName(dbFiles[i])}");
    }

    if (returnOnlyFileNames)
    {
        string[] dbFilesNames = new string[dbFiles.Length];
        for (int i = 0; i < dbFiles.Length; i++)
            dbFilesNames[i] = Path.GetFileName(dbFiles[i]);
        return dbFilesNames;
    }

    return dbFiles;
}

/*========================================================UniversalFunctions========================================================*/


Dictionary<string, JsonElement> loadDefaultSettings()
{
    string configFile = Path.Combine(CONFIGS_PATH, "ParserDefaultSettings.json");
    string configJson = File.ReadAllText(configFile);
    return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(configJson);
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

int selectingMenuNumber(int min, int max, string message = "messageEror_selectingMenuNumber")
{
    int selectedNumber = 0;
    while (true)
    {
        Console.Write(message);
        if (int.TryParse(Console.ReadLine(), out selectedNumber) && selectedNumber >= min && selectedNumber <= max) { Console.WriteLine(); return selectedNumber; }
        Console.WriteLine("Некорректный ввод.");
    }
}


