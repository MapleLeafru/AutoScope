using System;
using System.Diagnostics;
using System.IO;

Console.WriteLine("C# Клиент запущен");

// Корень проекта
string baseDir = AppDomain.CurrentDomain.BaseDirectory;
string root = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\"));
string DB_FOLDER = Path.Combine(root, "Databases"); // Папка с базами
string PYTHON_PATH = Path.Combine(root, @"AutoScopeVenv\Scripts\python.exe"); // python.exe

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
    string selectedDb = databaseScanningAndSelection();

    Console.WriteLine($"Выбрана база: {Path.GetFileName(selectedDb)}");

    // Далее можно запускать Core.py с этой базой
    string pythonCore = Path.Combine(root, @"Core\Core.py");

    ProcessStartInfo coreStart = new ProcessStartInfo();
    coreStart.FileName = PYTHON_PATH;
    coreStart.Arguments = $"\"{pythonCore}\" \"{selectedDb}\"";
    coreStart.UseShellExecute = false;
    coreStart.RedirectStandardOutput = true;
    coreStart.RedirectStandardError = true;

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
                    Console.WriteLine($"Подтвердите создание базы данных с названием <{dbName}> y/n:");
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
    string utilsPath = Path.Combine(root, @"Utils\Utils.py");

    ProcessStartInfo start = new ProcessStartInfo();
    start.FileName = PYTHON_PATH;

    // команда + аргумент
    start.Arguments = $"\"{utilsPath}\" dbCreate {dbName}";
    start.UseShellExecute = false;
    start.RedirectStandardOutput = true;
    start.RedirectStandardError = true;

    using (var process = Process.Start(start))
    {
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
    databaseScanning();
    Console.WriteLine("Тут пока ничего нет");
}

string databaseScanningAndSelection()
{
    string[] dbFiles = databaseScanning();

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

string[] databaseScanning()
{
    string[] dbFiles = Directory.Exists(DB_FOLDER) ? Directory.GetFiles(DB_FOLDER, "*.db") : new string[0];
    if (dbFiles.Length == 0)
    {
        Console.WriteLine("Нет доступных баз данных.");
        throw new Exception("Нет доступных баз данных.");
    }

    Console.WriteLine("Найденые базы данных:");
    for (int i = 0; i < dbFiles.Length; i++)
        Console.WriteLine($"{i}: {Path.GetFileName(dbFiles[i])}");

    return dbFiles;
}
