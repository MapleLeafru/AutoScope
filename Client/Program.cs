using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Xml.Linq;

// Ошибка NETSDK1004 файл ресурсов "...\obj\project.assets.json" не найден. Восстановите пакет NuGet, чтобы создать его.
// dotnet restore

Console.WriteLine("C# Клиент запущен");

Console.OutputEncoding = System.Text.Encoding.UTF8;

string ROOT_PATH = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\")); // Корень проекта
string DB_PATH = Path.Combine(ROOT_PATH, "Databases"); // Папка с базами
string CONFIGS_PATH = Path.Combine(ROOT_PATH, "Configs"); // Папка с конфигами
string PYTHON_PATH = Path.Combine(ROOT_PATH, @"Python\python.exe"); // python.exe

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

    int mode = 0;
    int min = 1;
    int max = 2;
    while (true)
    {
        Console.Write("Номер выбранного режима: ");
        if (int.TryParse(Console.ReadLine(), out mode) && mode >= min && mode <= max) { Console.WriteLine(); break; }

        Console.WriteLine("Некорректный ввод.");
    }

    if (mode == 1) { selectDbForPythonCore(); }
    else if (mode == 2) { menuPythonUtils(); }
}

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

void menuPythonUtils()
{
    // Выбор инструмента
    Console.WriteLine("Выберите инструмент:");
    Console.WriteLine("0 - Вернуться назад");
    Console.WriteLine("1 - Создать новую базу данных");
    Console.WriteLine("2 - Удалить базу данных");

    int mode = 0;
    int min = 0;
    int max = 2;
    while (true)
    {
        Console.Write("Номер выбранного интсрумента: ");
        if (int.TryParse(Console.ReadLine(), out mode) && mode >= min && mode <= max) { Console.WriteLine(); break; }
        Console.WriteLine("Некорректный ввод.");
    }

    if (mode == 0) { return; }
    else if (mode == 1) { preparationPythonUtils_dbCreate(); }
    else if (mode == 2) { preparationPythonUtils_dbDelete(); }
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
    var pythonUtils_dbCreate_request = new
    {
        command = commandToRun,
        dbFileName = dataBaseName,
        configPath = CONFIGS_PATH,
        dbPath = DB_PATH
    };
    string pythonUtils_dbCreate_request_json = JsonSerializer.Serialize(pythonUtils_dbCreate_request);

    using (var pythonUtilsProcess = Process.Start(startUtils))
    {
        pythonUtilsProcess.StandardInput.WriteLine(pythonUtils_dbCreate_request_json);
        pythonUtilsProcess.StandardInput.Close();

        string output = pythonUtilsProcess.StandardOutput.ReadToEnd();
        string error = pythonUtilsProcess.StandardError.ReadToEnd();

        pythonUtilsProcess.WaitForExit();
        Console.WriteLine("Exit code: " + pythonUtilsProcess.ExitCode);                                     // debag

        Console.WriteLine(output);
        if (!string.IsNullOrEmpty(error))
            Console.WriteLine("Errors:\n" + error);
    }
}

string dataBaseScanningAndSelection(string message = "debag: Сообщение не передано в функцию dataBaseScanningAndSelection")
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
