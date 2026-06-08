using System;
using System.IO;
using System.Linq;

// Ищет доступные внешние модули: парсеры и анализаторы.
public class ModuleDiscoveryService
{
    private readonly AppPaths _paths;
    private readonly ConsoleInputService _input;
    private readonly string[] _moduleExtensions = new[] { ".py", ".jar", ".exe" };

    public ModuleDiscoveryService(AppPaths paths, ConsoleInputService input)
    {
        _paths = paths;
        _input = input;
    }

    // Показывает список парсеров и возвращает выбранный путь.
    public string SelectParser(string message, string cancelText)
    {
        Console.WriteLine(message);
        string[] parserFiles = ScanParsers(consoleOutput: true, returnOnlyFileNames: false, cancelText: cancelText);
        return _input.SelectPathFromList(parserFiles, "Введите номер выбранного парсера: ");
    }

    // Показывает список анализаторов и возвращает выбранный путь.
    public string SelectAnalyzer(string message, string cancelText)
    {
        Console.WriteLine(message);
        string[] analyzerFiles = ScanAnalyzers(consoleOutput: true, returnOnlyFileNames: false, cancelText: cancelText);
        return _input.SelectPathFromList(analyzerFiles, "Введите номер выбранного анализатора: ");
    }

    // Возвращает список доступных парсеров.
    public string[] ScanParsers(bool consoleOutput = true, bool returnOnlyFileNames = false, string cancelText = "Вернуться назад")
    {
        return ScanModules(_paths.ParsersPath, "парсеров", "Найденные парсеры:", consoleOutput, returnOnlyFileNames, cancelText);
    }

    // Возвращает список доступных анализаторов.
    public string[] ScanAnalyzers(bool consoleOutput = true, bool returnOnlyFileNames = false, string cancelText = "Вернуться назад")
    {
        return ScanModules(_paths.AnalyzersPath, "анализаторов", "Найденные анализаторы:", consoleOutput, returnOnlyFileNames, cancelText);
    }

    // Определяет runtime по расширению файла модуля.
    public string GetRuntimeNameByPath(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();

        if (extension == ".py") return "python";
        if (extension == ".jar") return "java";
        if (extension == ".exe") return "exe";

        return "unknown";
    }

    // Ищет модули в папке и выводит список с нумерацией от 1.
    private string[] ScanModules(
        string folderPath,
        string emptyLabel,
        string listTitle,
        bool consoleOutput,
        bool returnOnlyFileNames,
        string cancelText
    )
    {
        string[] files = Directory.Exists(folderPath)
            ? Directory.GetFiles(folderPath).Where(IsSupportedModuleFile).OrderBy(Path.GetFileName).ToArray()
            : new string[0];

        if (files.Length == 0)
        {
            if (consoleOutput) Console.WriteLine($"Нет доступных {emptyLabel}.");
            return new string[0];
        }

        if (consoleOutput)
        {
            Console.WriteLine(listTitle);
            Console.WriteLine($"0: {cancelText}");
            for (int i = 0; i < files.Length; i++)
                Console.WriteLine($"{i + 1}: {Path.GetFileName(files[i])}");
        }

        if (returnOnlyFileNames)
            return files.Select(Path.GetFileName).ToArray();

        return files;
    }

    // Проверяет, является ли файл поддерживаемым внешним модулем.
    private bool IsSupportedModuleFile(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return _moduleExtensions.Contains(extension);
    }
}
