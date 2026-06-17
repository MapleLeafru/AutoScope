﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

// Ищет доступные внешние модули: парсеры и анализаторы.
// Личный конфиг модуля необязателен. Если он есть, из него читаются metadata/settings.
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
        return ScanModules(
            _paths.ParsersPath,
            _paths.ParserConfigsPath,
            "parser",
            "парсеров",
            "Найденные парсеры:",
            consoleOutput,
            returnOnlyFileNames,
            cancelText
        );
    }

    // Возвращает список доступных анализаторов.
    public string[] ScanAnalyzers(bool consoleOutput = true, bool returnOnlyFileNames = false, string cancelText = "Вернуться назад")
    {
        return ScanModules(
            _paths.AnalyzersPath,
            _paths.AnalyzerConfigsPath,
            "analyzer",
            "анализаторов",
            "Найденные анализаторы:",
            consoleOutput,
            returnOnlyFileNames,
            cancelText
        );
    }

    // Возвращает расширенную информацию о парсерах.
    public ModuleInfo[] GetParsers()
    {
        return BuildModuleInfos(_paths.ParsersPath, _paths.ParserConfigsPath, "parser");
    }

    // Возвращает расширенную информацию об анализаторах.
    public ModuleInfo[] GetAnalyzers()
    {
        return BuildModuleInfos(_paths.AnalyzersPath, _paths.AnalyzerConfigsPath, "analyzer");
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
        string configFolderPath,
        string defaultModuleType,
        string emptyLabel,
        string listTitle,
        bool consoleOutput,
        bool returnOnlyFileNames,
        string cancelText
    )
    {
        ModuleInfo[] modules = BuildModuleInfos(folderPath, configFolderPath, defaultModuleType);

        if (modules.Length == 0)
        {
            if (consoleOutput) Console.WriteLine($"Нет доступных {emptyLabel}.");
            return new string[0];
        }

        if (consoleOutput)
        {
            Console.WriteLine(listTitle);
            Console.WriteLine($"0: {cancelText}");
            for (int i = 0; i < modules.Length; i++)
                PrintModuleListItem(i + 1, modules[i]);
        }

        if (returnOnlyFileNames)
            return modules.Select(module => module.FileName).ToArray();

        return modules.Select(module => module.ModulePath).ToArray();
    }

    // Собирает список модулей и дополняет его необязательными метаданными из личного конфига.
    private ModuleInfo[] BuildModuleInfos(string folderPath, string configFolderPath, string defaultModuleType)
    {
        string[] files = Directory.Exists(folderPath)
            ? Directory.GetFiles(folderPath).Where(IsSupportedModuleFile).OrderBy(Path.GetFileName).ToArray()
            : new string[0];

        return files
            .Select(path => BuildModuleInfo(path, configFolderPath, defaultModuleType))
            .ToArray();
    }

    // Формирует описание одного модуля.
    private ModuleInfo BuildModuleInfo(string modulePath, string configFolderPath, string defaultModuleType)
    {
        string fileName = Path.GetFileName(modulePath);
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(modulePath);
        string configPath = GetModuleConfigPath(configFolderPath, modulePath);

        ModuleInfo info = new ModuleInfo
        {
            ModulePath = modulePath,
            FileName = fileName,
            FileNameWithoutExtension = fileNameWithoutExtension,
            ModuleType = defaultModuleType,
            DisplayName = fileNameWithoutExtension,
            HasConfig = File.Exists(configPath)
        };

        if (info.HasConfig)
            ApplyMetadataFromConfig(info, configPath);

        return info;
    }

    // Выводит один пункт списка модулей.
    private void PrintModuleListItem(int number, ModuleInfo module)
    {
        string displayName = module.GetDisplayName();
        bool hasReadableMetadata = module.HasMetadata && !displayName.Equals(module.FileNameWithoutExtension, StringComparison.OrdinalIgnoreCase);

        List<string> details = new List<string>();
        if (!string.IsNullOrWhiteSpace(module.Source)) details.Add($"источник: {module.Source}");
        if (!string.IsNullOrWhiteSpace(module.Mode)) details.Add($"режим: {module.Mode}");
        if (!string.IsNullOrWhiteSpace(module.Category)) details.Add($"категория: {module.Category}");
        if (module.Recommended) details.Add("рекомендовано");

        WriteColored($"{number}: ", ConsoleColor.White);

        if (hasReadableMetadata)
        {
            WriteColored(displayName, ConsoleColor.White);
            WriteColored(" (", ConsoleColor.White);
            WriteColored(module.FileName, ConsoleColor.DarkCyan);
            WriteColored(")", ConsoleColor.White);
        }
        else
        {
            WriteColored(displayName, ConsoleColor.DarkCyan);
        }

        foreach (string detail in details)
        {
            WriteColored(" | ", ConsoleColor.White);
            WriteColored(detail, ConsoleColor.Gray);
        }

        Console.WriteLine();

        if (!string.IsNullOrWhiteSpace(module.Description))
        {
            WriteColored("   ", ConsoleColor.White);
            WriteColoredLine(module.Description, ConsoleColor.DarkYellow);
        }
    }

    // Пишет фрагмент строки выбранным цветом и возвращает предыдущий цвет консоли.
    private void WriteColored(string text, ConsoleColor color)
    {
        ConsoleColor previousColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ForegroundColor = previousColor;
    }

    // Пишет строку выбранным цветом и возвращает предыдущий цвет консоли.
    private void WriteColoredLine(string text, ConsoleColor color)
    {
        ConsoleColor previousColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ForegroundColor = previousColor;
    }

    // Применяет metadata из личного конфига. Поддерживает новый формат metadata/settings и старый плоский формат.
    private void ApplyMetadataFromConfig(ModuleInfo info, string configPath)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(configPath));
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return;

            JsonElement metadata = root;
            if (root.TryGetProperty("metadata", out JsonElement nestedMetadata) && nestedMetadata.ValueKind == JsonValueKind.Object)
                metadata = nestedMetadata;

            string displayName = GetStringProperty(metadata, "displayName", "");
            if (!string.IsNullOrWhiteSpace(displayName))
                info.DisplayName = displayName;

            info.Description = GetStringProperty(metadata, "description", info.Description);
            info.ModuleType = GetStringProperty(metadata, "moduleType", info.ModuleType);
            info.Source = GetStringProperty(metadata, "source", info.Source);
            info.Mode = GetStringProperty(metadata, "mode", info.Mode);
            info.Category = GetStringProperty(metadata, "category", info.Category);
            info.Recommended = GetBoolProperty(metadata, "recommended", info.Recommended);

            info.HasMetadata = HasAnyMetadata(metadata);
        }
        catch
        {
            // Невалидный личный конфиг не должен ломать обнаружение модулей.
            info.HasMetadata = false;
        }
    }

    // Проверяет, есть ли в блоке metadata хотя бы одно пользовательское поле.
    private bool HasAnyMetadata(JsonElement metadata)
    {
        string[] keys = new[] { "displayName", "description", "moduleType", "source", "mode", "category", "recommended" };
        foreach (string key in keys)
        {
            if (metadata.TryGetProperty(key, out _))
                return true;
        }

        return false;
    }

    // Безопасно получает строковое поле из JSON.
    private string GetStringProperty(JsonElement element, string key, string fallback)
    {
        if (!element.TryGetProperty(key, out JsonElement value))
            return fallback;

        if (value.ValueKind == JsonValueKind.String)
            return value.GetString() ?? fallback;

        if (value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.Undefined)
            return fallback;

        return value.ToString();
    }

    // Безопасно получает bool-поле из JSON.
    private bool GetBoolProperty(JsonElement element, string key, bool fallback)
    {
        if (!element.TryGetProperty(key, out JsonElement value))
            return fallback;

        if (value.ValueKind == JsonValueKind.True)
            return true;

        if (value.ValueKind == JsonValueKind.False)
            return false;

        if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out bool result))
            return result;

        return fallback;
    }

    // Возвращает путь к личному конфигу модуля.
    private string GetModuleConfigPath(string configFolderPath, string modulePath)
    {
        string moduleName = Path.GetFileNameWithoutExtension(modulePath);
        if (string.IsNullOrWhiteSpace(moduleName))
            return "";

        return Path.Combine(configFolderPath, moduleName + ".json");
    }

    // Проверяет, является ли файл поддерживаемым внешним модулем.
    private bool IsSupportedModuleFile(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return _moduleExtensions.Contains(extension);
    }
}
