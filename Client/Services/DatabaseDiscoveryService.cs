using System;
using System.IO;
using System.Linq;

// Ищет базы данных проекта и даёт выбрать одну из них.
public class DatabaseDiscoveryService
{
    private readonly AppPaths _paths;
    private readonly ConsoleInputService _input;

    public DatabaseDiscoveryService(AppPaths paths, ConsoleInputService input)
    {
        _paths = paths;
        _input = input;
    }

    // Показывает список баз данных и возвращает выбранный путь.
    public string SelectDatabase(string message, string cancelText)
    {
        Console.WriteLine(message);
        string[] dbFiles = ScanDatabases(consoleOutput: true, returnOnlyFileNames: false, cancelText: cancelText);
        return _input.SelectPathFromList(dbFiles, "Введите номер выбранной базы: ");
    }

    // Возвращает список баз данных из папки Databases.
    public string[] ScanDatabases(bool consoleOutput = true, bool returnOnlyFileNames = false, string cancelText = "Вернуться назад")
    {
        string[] dbFiles = Directory.Exists(_paths.DbPath)
            ? Directory.GetFiles(_paths.DbPath, "*.db").OrderBy(Path.GetFileName).ToArray()
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
            return dbFiles.Select(Path.GetFileName).ToArray();

        return dbFiles;
    }
}
