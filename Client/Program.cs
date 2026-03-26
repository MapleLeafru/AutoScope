using System;
using System.Diagnostics;
using System.IO;

Console.WriteLine("C# Клиент запущен");

// Корень проекта
string baseDir = AppDomain.CurrentDomain.BaseDirectory;
string root = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\"));

// Папка с базами
string dbFolder = Path.Combine(root, "Databases");

while (true)
{
    modeSelection();
}














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
        Console.WriteLine("Некорректный ввод. Введите 1 или 2.");
    }

    if (mode == 1) { selectDbForPythonCore(); }
    else if (mode == 2) { startPythonUtils(); }
}

void selectDbForPythonCore()
{
    // Режим выбора существующей базы
    string[] dbFiles = Directory.Exists(dbFolder) ? Directory.GetFiles(dbFolder, "*.db") : new string[0];
    if (dbFiles.Length == 0)
    {
        Console.WriteLine("Нет доступных баз данных.");
        throw new Exception("Нет доступных баз данных.");
    }

    Console.WriteLine("Выберите базу данных:");
    for (int i = 0; i < dbFiles.Length; i++)
        Console.WriteLine($"{i}: {Path.GetFileName(dbFiles[i])}");

    int selectedIndex = -1;
    while (true)
    {
        Console.Write("Введите номер базы: ");
        string input = Console.ReadLine();
        if (int.TryParse(input, out selectedIndex) && selectedIndex >= 0 && selectedIndex < dbFiles.Length)
            break;
        Console.WriteLine("Некорректный ввод. Попробуйте снова.");
    }

    string selectedDb = dbFiles[selectedIndex];

    Console.WriteLine($"Выбрана база: {Path.GetFileName(selectedDb)}");

    // Далее можно запускать Core.py с этой базой
    string pythonCore = Path.Combine(root, @"Core\Core.py");
    string pythonExe = Path.Combine(root, @"AutoScopeVenv\Scripts\python.exe");

    ProcessStartInfo coreStart = new ProcessStartInfo();
    coreStart.FileName = pythonExe;
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
    // Запускаем Python Utils.py для создания базы
    string pythonPath = Path.Combine(root, @"AutoScopeVenv\Scripts\python.exe");
    string utilsPath = Path.Combine(root, @"Utils\Utils.py");

    ProcessStartInfo start = new ProcessStartInfo();
    start.FileName = pythonPath;
    start.Arguments = $"\"{utilsPath}\"";
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
    return;
}
