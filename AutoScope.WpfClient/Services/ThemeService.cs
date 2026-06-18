using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;

namespace AutoScope.WpfClient.Services;

public sealed class ThemeOption
{
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public required string ResourcePath { get; init; }
    public required string Description { get; init; }
}

public static class ThemeService
{
    private const string DefaultThemeKey = "graphite-blue";

    public static IReadOnlyList<ThemeOption> AvailableThemes { get; } = new List<ThemeOption>
    {
        new()
        {
            Key = "graphite-blue",
            DisplayName = "Графитовая синяя",
            ResourcePath = "Styles/Colors.xaml",
            Description = "Текущая основная тёмная схема AutoScope с синим акцентом."
        },
        new()
        {
            Key = "deep-emerald",
            DisplayName = "Тёмная зелёная",
            ResourcePath = "Styles/Themes/DeepEmerald.xaml",
            Description = "Альтернативная зелёная схема для экспериментов с палитрой."
        }
    };

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

            bool exists = AvailableThemes.Any(theme => theme.Key == settings.ThemeKey);
            return exists ? settings.ThemeKey : DefaultThemeKey;
        }
        catch
        {
            return DefaultThemeKey;
        }
    }

    public static ThemeOption GetTheme(string themeKey)
    {
        return AvailableThemes.FirstOrDefault(theme => theme.Key == themeKey)
            ?? AvailableThemes.First(theme => theme.Key == DefaultThemeKey);
    }

    public static void ApplySavedTheme(string rootPath)
    {
        ApplyTheme(LoadSavedThemeKey(rootPath), save: false, rootPath: rootPath);
    }

    public static void ApplyTheme(string themeKey, bool save, string rootPath)
    {
        ThemeOption theme = GetTheme(themeKey);
        ReplaceThemeDictionary(theme.ResourcePath);

        if (save)
            SaveTheme(theme.Key, rootPath);
    }

    private static void ReplaceThemeDictionary(string resourcePath)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        ResourceDictionary newDictionary = new()
        {
            Source = new Uri(resourcePath, UriKind.Relative)
        };

        int themeIndex = -1;
        for (int i = 0; i < dictionaries.Count; i++)
        {
            string? source = dictionaries[i].Source?.OriginalString.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(source))
                continue;

            if (source.EndsWith("Styles/Colors.xaml", StringComparison.OrdinalIgnoreCase) ||
                source.Contains("Styles/Themes/", StringComparison.OrdinalIgnoreCase))
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

    private static void SaveTheme(string themeKey, string rootPath)
    {
        string settingsPath = GetSettingsPath(rootPath);
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);

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
        return Path.Combine(rootPath, "Configs", "UiSettings.json");
    }

    private sealed class UiSettings
    {
        public string ThemeKey { get; set; } = DefaultThemeKey;
    }
}
