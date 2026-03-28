using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

Console.WriteLine("C# Клиент запущен");

Console.OutputEncoding = System.Text.Encoding.UTF8;

string ROOT_PATH = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\")); // Корень проекта
string DB_PATH = Path.Combine(ROOT_PATH, "Databases"); // Папка с базами
string CONFIGS_PATH = Path.Combine(ROOT_PATH, "Configs"); // Папка с конфигами
string PYTHON_PATH = Path.Combine(ROOT_PATH, @"AutoScopeVenv\Scripts\python.exe"); // python.exe

while (true)
{
    modeSelection();
}

// Дальше функции ###################################################################
void modeSelection()
{
    // Выбор режима
    Console.WriteLine("Выберите режим:");
    Console.WriteLine("1 - Выбрать базу данных");
    Console.WriteLine("2 - Открыть инструменты");

    int mode = 0;
    while (true)
    {
        Console.Write("Ваш выбор: ");
        if (int.TryParse(Console.ReadLine(), out mode) && (mode == 1 || mode == 2))
            break;
        Console.WriteLine("Некорректный ввод.");
    }

    if (mode == 1) { selectDbForPythonCore(); }
    else if (mode == 2) { startPythonUtils(); }
}

void selectDbForPythonCore()
{
    // Режим выбора существующей базы
    string selectedDb = dataBaseScanningAndSelection();

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

void startPythonUtils()
{
    // Выбор инструмента
    Console.WriteLine("Выберите инструмент:");
    Console.WriteLine("1 - Создать новую базу данных");
    Console.WriteLine("2 - Удалить базу данных");

    int mode = 0;
    while (true)
    {
        Console.Write("Ваш выбор: ");
        if (int.TryParse(Console.ReadLine(), out mode) && (mode == 1 || mode == 2))
            break;
        Console.WriteLine("Некорректный ввод.");
    }

    string dbName = "";
    if (mode == 1) {
        while (true)
        {
            Console.WriteLine("Введите название новой базы данных:");
            dbName = Console.ReadLine();
            if (dbName != "")
            {
                while (true)
                {
                    string[] existingDatabases = dataBaseScanning(consoleOutput: false, returnOnlyFileNames: true);
                    if (existingDatabases.Contains(dbName + ".db")) Console.WriteLine($"База данных с названием <{dbName}> уже существует, пересоздать? y/n:");
                    else Console.WriteLine($"Подтвердите создание базы данных с названием <{dbName}> y/n:");
                    string ansver = Console.ReadLine().ToUpper();
                    if (ansver == "Y") { pythonUtils_dbCreate(dbName); return; }
                    else if (ansver == "N") { dbName = ""; return; }
                    Console.WriteLine("Некорректный ввод. Попробуйте снова.");
                }
            }
            else
            {
                Console.WriteLine("Введено пустое поле. Желаете продолжить создание базы данных? y/n:");
                if (Console.ReadLine().ToUpper() != "Y") return;
            }
        }
    }
    else if (mode == 2) { pythonUtils_dbDelete(dbName); }
}

void pythonUtils_dbCreate(string dbName)
{
    string utilsPath = Path.Combine(ROOT_PATH, @"Utils\Utils.py");

    ProcessStartInfo startUtils_dbCreate = new ProcessStartInfo();
    startUtils_dbCreate.FileName = PYTHON_PATH;

    // Создаём json с параметрами перед запуском
    var pythonUtils_dbCreate_request = new
    {
        command = "dbCreate",
        db_name = dbName,
        configPath = CONFIGS_PATH,
        dbPath = DB_PATH
    }; 
    string pythonUtils_dbCreate_request_json = JsonSerializer.Serialize(pythonUtils_dbCreate_request);
    startUtils_dbCreate.RedirectStandardInput = true;

    //startUtils_dbCreate.Arguments = $"\"{utilsPath}\" dbCreate {dbName} \"{CONFIGS_PATH}\" \"{DB_PATH}\"";
    startUtils_dbCreate.Arguments = $"\"{utilsPath}\"";
    startUtils_dbCreate.UseShellExecute = false;
    startUtils_dbCreate.RedirectStandardOutput = true;
    startUtils_dbCreate.RedirectStandardError = true;

    using (var process = Process.Start(startUtils_dbCreate))
    {
        process.StandardInput.WriteLine(pythonUtils_dbCreate_request_json);
        process.StandardInput.Close();

        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Console.WriteLine(output);
        if (!string.IsNullOrEmpty(error))
            Console.WriteLine("Errors:\n" + error);
    }
}

void pythonUtils_dbDelete(string dbName)
{
    dataBaseScanning();
    Console.WriteLine("Тут пока ничего нет");
}

string dataBaseScanningAndSelection()
{
    string[] dbFiles = dataBaseScanning();

    int selectedIndex = -1;
    while (true)
    {
        Console.Write("Введите номер выбранной базы: ");
        string input = Console.ReadLine();
        if (int.TryParse(input, out selectedIndex) && selectedIndex >= 0 && selectedIndex < dbFiles.Length)
            break;
        Console.WriteLine("Некорректный ввод. Попробуйте снова.");
    }
    return dbFiles[selectedIndex];
}

string[] dataBaseScanning(bool consoleOutput = true, bool returnOnlyFileNames = false) // Возвращает массив путей до бд
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
