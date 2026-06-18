using System;
using System.Collections.Generic;
using System.IO;
using IOPath = System.IO.Path;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Xml.Linq;

namespace AutoScope.WpfClient.Services;

public sealed class ThemeOption
{
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public required string ResourcePath { get; init; }
    public required string Description { get; init; }
    public bool IsExternalFile { get; init; }

    public override string ToString() => DisplayName;
}

public static class ThemeService
{
    private const string DefaultThemeKey = "graphite-blue";

    public static IReadOnlyList<ThemeOption> GetAvailableThemes(string rootPath)
    {
        List<ThemeOption> themes = LoadThemesFromFolder(rootPath);

        if (themes.Count == 0)
            themes.AddRange(GetFallbackThemes());

        return themes
            .OrderBy(theme => theme.Key == DefaultThemeKey ? 0 : 1)
            .ThenBy(theme => theme.DisplayName)
            .ToList();
    }

    public static string LoadSavedThemeKey(string rootPath)
    {
        try
        {
            string settingsPath = GetSettingsPath(rootPath);
            if (!File.Exists(settingsPath))
                return DefaultThemeKey;

            string json = File.ReadAllText(settingsPath);
            UiSettings? settings = JsonSerializer.Deserialize<UiSettings>(json);
            if (settings == null || string.IsNullOrWhiteSpace(settings.ThemeKey))
                return DefaultThemeKey;

            bool exists = GetAvailableThemes(rootPath).Any(theme => theme.Key == settings.ThemeKey);
            return exists ? settings.ThemeKey : DefaultThemeKey;
        }
        catch
        {
            return DefaultThemeKey;
        }
    }

    public static ThemeOption GetTheme(string themeKey, string rootPath)
    {
        IReadOnlyList<ThemeOption> themes = GetAvailableThemes(rootPath);
        return themes.FirstOrDefault(theme => theme.Key == themeKey)
            ?? themes.FirstOrDefault(theme => theme.Key == DefaultThemeKey)
            ?? themes.First();
    }

    public static void ApplySavedTheme(string rootPath)
    {
        ApplyTheme(LoadSavedThemeKey(rootPath), save: false, rootPath: rootPath);
    }

    public static void ApplyTheme(string themeKey, bool save, string rootPath)
    {
        ThemeOption theme = GetTheme(themeKey, rootPath);
        ReplaceThemeDictionary(theme.ResourcePath, theme.IsExternalFile);
        RefreshOpenWindowsAfterThemeChange();

        if (save)
            SaveTheme(theme.Key, rootPath);
    }

    private static List<ThemeOption> LoadThemesFromFolder(string rootPath)
    {
        List<ThemeOption> themes = new();
        string themesDirectory = IOPath.Combine(rootPath, "AutoScope.WpfClient", "Styles", "Themes");

        if (!Directory.Exists(themesDirectory))
            return themes;

        foreach (string themeFile in Directory.GetFiles(themesDirectory, "*.xaml"))
        {
            ThemeOption option = LoadThemeOptionFromFile(themeFile);
            if (themes.Any(theme => theme.Key.Equals(option.Key, StringComparison.OrdinalIgnoreCase)))
                continue;

            themes.Add(option);
        }

        return themes;
    }

    private static ThemeOption LoadThemeOptionFromFile(string themeFile)
    {
        string fileName = IOPath.GetFileNameWithoutExtension(themeFile);
        string fallbackKey = NormalizeKey(fileName);

        try
        {
            XDocument document = XDocument.Load(themeFile);
            string key = ReadThemeString(document, "ThemeKey") ?? fallbackKey;
            string displayName = ReadThemeString(document, "ThemeDisplayName") ?? SplitPascalCase(fileName);
            string description = ReadThemeString(document, "ThemeDescription") ?? "Пользовательская цветовая схема из папки Styles/Themes.";

            return new ThemeOption
            {
                Key = key,
                DisplayName = displayName,
                Description = description,
                ResourcePath = themeFile,
                IsExternalFile = true
            };
        }
        catch
        {
            return new ThemeOption
            {
                Key = fallbackKey,
                DisplayName = SplitPascalCase(fileName),
                Description = "Тема найдена в папке Styles/Themes, но её описание не удалось прочитать.",
                ResourcePath = themeFile,
                IsExternalFile = true
            };
        }
    }

    private static string? ReadThemeString(XDocument document, string key)
    {
        XNamespace xNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

        return document
            .Descendants()
            .FirstOrDefault(element =>
                string.Equals((string?)element.Attribute(xNamespace + "Key"), key, StringComparison.OrdinalIgnoreCase))
            ?.Value
            .Trim();
    }

    private static IReadOnlyList<ThemeOption> GetFallbackThemes()
    {
        return new List<ThemeOption>
        {
            new()
            {
                Key = "graphite-blue",
                DisplayName = "Графитовая синяя",
                ResourcePath = "Styles/Themes/GraphiteBlue.xaml",
                Description = "Основная тёмная графитовая схема AutoScope с мягким синим акцентом.",
                IsExternalFile = false
            },
            new()
            {
                Key = "deep-emerald",
                DisplayName = "Тёмная зелёная",
                ResourcePath = "Styles/Themes/DeepEmerald.xaml",
                Description = "Альтернативная тёмная схема с зелёным акцентом для экспериментов с палитрой.",
                IsExternalFile = false
            }
        };
    }

    private static void ReplaceThemeDictionary(string resourcePath, bool isExternalFile)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        Uri source = isExternalFile
            ? new Uri(resourcePath, UriKind.Absolute)
            : new Uri(resourcePath, UriKind.Relative);

        ResourceDictionary newDictionary = new()
        {
            Source = source
        };

        int themeIndex = -1;
        for (int i = 0; i < dictionaries.Count; i++)
        {
            string? dictionarySource = dictionaries[i].Source?.OriginalString.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(dictionarySource))
                continue;

            if (dictionarySource.EndsWith("Styles/Colors.xaml", StringComparison.OrdinalIgnoreCase) ||
                dictionarySource.Contains("Styles/Themes/", StringComparison.OrdinalIgnoreCase))
            {
                themeIndex = i;
                break;
            }
        }

        if (themeIndex >= 0)
            dictionaries[themeIndex] = newDictionary;
        else
            dictionaries.Insert(0, newDictionary);
    }

    private static void RefreshOpenWindowsAfterThemeChange()
    {
        if (Application.Current == null)
            return;

        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            foreach (Window window in Application.Current.Windows)
            {
                RefreshThemeBindings(window, new HashSet<DependencyObject>());
                window.InvalidateVisual();
                window.UpdateLayout();
            }
        }, DispatcherPriority.ApplicationIdle);
    }

    private static void RefreshThemeBindings(DependencyObject root, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(root))
            return;

        UpdateBinding(root, Control.ForegroundProperty);
        UpdateBinding(root, Control.BackgroundProperty);
        UpdateBinding(root, Control.BorderBrushProperty);
        UpdateBinding(root, TextBlock.ForegroundProperty);
        UpdateBinding(root, Panel.BackgroundProperty);
        UpdateBinding(root, Border.BackgroundProperty);
        UpdateBinding(root, Border.BorderBrushProperty);
        UpdateBinding(root, Shape.FillProperty);
        UpdateBinding(root, Shape.StrokeProperty);
        UpdateBinding(root, Window.BackgroundProperty);
        UpdateBinding(root, Window.ForegroundProperty);

        int visualChildrenCount = 0;
        try
        {
            visualChildrenCount = VisualTreeHelper.GetChildrenCount(root);
        }
        catch
        {
            visualChildrenCount = 0;
        }

        for (int i = 0; i < visualChildrenCount; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            RefreshThemeBindings(child, visited);
        }

        foreach (object logicalChild in LogicalTreeHelper.GetChildren(root))
        {
            if (logicalChild is DependencyObject dependencyChild)
                RefreshThemeBindings(dependencyChild, visited);
        }
    }

    private static void UpdateBinding(DependencyObject target, DependencyProperty property)
    {
        try
        {
            BindingOperations.GetBindingExpressionBase(target, property)?.UpdateTarget();
        }
        catch
        {
            // Некоторые свойства не применимы к конкретному элементу. Это не ошибка смены темы.
        }
    }

    private static void SaveTheme(string themeKey, string rootPath)
    {
        string settingsPath = GetSettingsPath(rootPath);
        Directory.CreateDirectory(IOPath.GetDirectoryName(settingsPath)!);

        UiSettings settings = new()
        {
            ThemeKey = themeKey
        };

        JsonSerializerOptions options = new()
        {
            WriteIndented = true
        };

        File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings, options));
    }

    private static string GetSettingsPath(string rootPath)
    {
        return IOPath.Combine(rootPath, "Configs", "UiSettings.json");
    }

    private static string NormalizeKey(string value)
    {
        return value.Trim().Replace('_', '-').Replace(' ', '-').ToLowerInvariant();
    }

    private static string SplitPascalCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Без названия";

        List<char> result = new();
        for (int i = 0; i < value.Length; i++)
        {
            char current = value[i];
            if (i > 0 && char.IsUpper(current) && !char.IsWhiteSpace(value[i - 1]))
                result.Add(' ');
            result.Add(current);
        }

        return new string(result.ToArray());
    }

    private sealed class UiSettings
    {
        public string ThemeKey { get; set; } = DefaultThemeKey;
    }
}
