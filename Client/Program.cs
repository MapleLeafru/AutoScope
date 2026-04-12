using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using static System.Runtime.InteropServices.JavaScript.JSType;

/*========================================================ProgramParams========================================================*/

Console.OutputEncoding = System.Text.Encoding.UTF8;

string ROOT_PATH = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\")); // Корень проекта
string DB_PATH = Path.Combine(ROOT_PATH, "Databases"); // Папка с базами
string CONFIGS_PATH = Path.Combine(ROOT_PATH, "Configs"); // Папка с конфигами
string PARSERS_PATH = Path.Combine(ROOT_PATH, "Parsers"); // Папка с парсерами
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
    Console.WriteLine("1 - Выбрать базу данных");
    Console.WriteLine("2 - Открыть инструменты");

    int modeNumber = selectingMenuNumber(min: 1, max: 2, "Номер выбранного режима: ");
    
    if (modeNumber == 1) { startPythonPipelineManager(); }
    else if (modeNumber == 2) { menuPythonUtils(); }
}

/*========================================================PythonPipelineManager========================================================*/

void startPythonPipelineManager()
{
    string pipelineManagerPath = Path.Combine(ROOT_PATH, @"PipelineManager\PipelineManager.py");

    Console.WriteLine("=== Запуск Pipeline ===");

    // Выбор базы данных
    string selectedDataBase = dataBaseScanningAndSelection(message: "Выберите базу данных для продолжениея работы");
    Console.WriteLine($"Выбрана база: {Path.GetFileName(selectedDataBase)}");
    Console.WriteLine();

    // Выбор парсера
    string selectedParser = parserScanningAndSelection(message: "Выберите парсер для продолжениея работы");
    Console.WriteLine($"Выбран парсер: {Path.GetFileName(selectedParser)}");
    Console.WriteLine();

    // Пример параметров (потом вынесешь в UI) !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
    Console.Write("Введите источник (url/путь): ");
    string source = Console.ReadLine();

    var request = new
    {
        source = source, // Это 100% будет работать не так, но пока в качестве примера пускай будет
        parser = selectedParser,
//        parser = new
//        {
//            type = "python",
//            path = selectedParser
//        },
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
    else if (toolNumber == 1) { preparationPythonUtils_dbCreate(); }
    else if (toolNumber == 2) { preparationPythonUtils_dbDelete(); }
}

void preparationPythonUtils_dbCreate()
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
                else { Console.Write($"Подтвердите создание базы данных с названием <{dataBaseNameToCreate}> y/n: "); }
                string ansver = Console.ReadLine().ToUpper();
                if (ansver == "Y") { startPythonUtils(dataBaseName: dataBaseNameToCreate, isThereExtension_db:false, commandToRun: "dbCreate"); return; }
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

void preparationPythonUtils_dbDelete()
{
    string dataBasePathToDelete = dataBaseScanningAndSelection(message: "Выберите номер базы данных для удаления: ");
    // Добавить удаление на 0
    string dataBaseNameToDelete = Path.GetFileName(dataBasePathToDelete);
    while (true)
    {
        Console.Write($"Вы уверены что хотите удалить базу данных {dataBaseNameToDelete}? Это действие не обратимо y/n: ");
        string ansver = Console.ReadLine().ToUpper();
        if (ansver == "Y") { startPythonUtils(dataBaseName: dataBaseNameToDelete, isThereExtension_db: true , commandToRun: "dbDelete"); return; }
        else if (ansver == "N") { return; }
        Console.WriteLine("Некорректный ввод.");
    }
}

void startPythonUtils(string dataBaseName, bool isThereExtension_db, string commandToRun)
{
    string utilsPath = Path.Combine(ROOT_PATH, @"Utils\Utils.py");

    ProcessStartInfo startUtils = new ProcessStartInfo // Параметры запуска
    {
        FileName = PYTHON_PATH,
        Arguments = $"\"{utilsPath}\"",
        UseShellExecute = false,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };
    
    // Создаём json с переменными для передачи перед запуском
    if (!isThereExtension_db) { dataBaseName += ".db"; }
    var pythonUtils_request = new
    {
        command = commandToRun,
        dbFileName = dataBaseName,
        configPath = CONFIGS_PATH,
        dbPath = DB_PATH
    };
    string pythonUtils_request_json = JsonSerializer.Serialize(pythonUtils_request);

    using (var pythonUtilsProcess = Process.Start(startUtils))
    {
        pythonUtilsProcess.StandardInput.WriteLine(pythonUtils_request_json);
        pythonUtilsProcess.StandardInput.Close();

        string output = pythonUtilsProcess.StandardOutput.ReadToEnd();
        string error = pythonUtilsProcess.StandardError.ReadToEnd();

        pythonUtilsProcess.WaitForExit();
        //Console.WriteLine("Exit code: " + pythonUtilsProcess.ExitCode);                                     // debag

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
    string[] parserFiles = Directory.Exists(PARSERS_PATH) ? Directory.GetFiles(PARSERS_PATH) : new string[0]; // Если  Directory.Exists(DB_PATH) Существует, то Directory.GetFiles(DB_PATH), а если нет, то new string[0]
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


