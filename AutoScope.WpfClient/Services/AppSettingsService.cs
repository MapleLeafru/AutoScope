using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace AutoScope.WpfClient.Services;

public sealed class UiSettings
{
    public string ThemeKey { get; set; } = "graphite-blue";
    public string DbBrowserPath { get; set; } = "";
    public int UiScalePercent { get; set; } = 100;

    public bool ScenarioAutoCheckEnabled { get; set; } = false;
    public int ScenarioAutoCheckIntervalMinutes { get; set; } = 1;
    public bool ShowScenarioNotifications { get; set; } = true;
    public bool ShowScenarioErrorNotifications { get; set; } = true;

    public bool RunInBackground { get; set; } = true;
    public bool MinimizeToTrayOnClose { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;
    public string StartupMethod { get; set; } = "Shortcut";
    public bool StartMinimizedToTray { get; set; } = true;
    public bool ShowBackgroundHintOnClose { get; set; } = true;

    public bool RememberWindowPlacement { get; set; } = true;
    public bool ShowEmptyStateHints { get; set; } = true;
    public bool OpenReportAfterAnalysis { get; set; } = true;
    public bool OpenLogOnError { get; set; } = false;
    public string LogLevel { get; set; } = "Normal";
    public int KeepLogsDays { get; set; } = 30;
}

public sealed class UiScaleOption
{
    public UiScaleOption(int percent, string displayName)
    {
        Percent = percent;
        DisplayName = displayName;
    }

    public int Percent { get; }
    public string DisplayName { get; }

    public override string ToString() => DisplayName;
}

public sealed class StartupMethodOption
{
    public StartupMethodOption(string key, string displayName, string description)
    {
        Key = key;
        DisplayName = displayName;
        Description = description;
    }

    public string Key { get; }
    public string DisplayName { get; }
    public string Description { get; }

    public override string ToString() => DisplayName;
}

public static class AppSettingsService
{
    public const string StartupMethodShortcut = "Shortcut";
    public const string StartupMethodRegistry = "Registry";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static UiSettings Load(string rootPath)
    {
        try
        {
            string settingsPath = GetSettingsPath(rootPath);
            if (!File.Exists(settingsPath))
                return Normalize(new UiSettings());

            UiSettings? settings = JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(settingsPath));
            return Normalize(settings ?? new UiSettings());
        }
        catch
        {
            return Normalize(new UiSettings());
        }
    }

    public static void Save(string rootPath, UiSettings settings)
    {
        settings = Normalize(settings);
        string settingsPath = GetSettingsPath(rootPath);
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    public static void Update(string rootPath, Action<UiSettings> update)
    {
        UiSettings settings = Load(rootPath);
        update(settings);
        Save(rootPath, settings);
    }

    public static UiScaleOption[] GetUiScaleOptions()
    {
        return new[]
        {
            new UiScaleOption(90, "90% — компактно"),
            new UiScaleOption(100, "100% — стандартно"),
            new UiScaleOption(110, "110% — немного крупнее"),
            new UiScaleOption(125, "125% — крупно"),
            new UiScaleOption(150, "150% — очень крупно")
        };
    }

    public static StartupMethodOption[] GetStartupMethodOptions()
    {
        return new[]
        {
            new StartupMethodOption(
                StartupMethodShortcut,
                "Ярлык в папке автозагрузки",
                "Рекомендуемый portable-вариант: AutoScope создаёт ярлык в автозагрузке текущего пользователя."),
            new StartupMethodOption(
                StartupMethodRegistry,
                "Реестр Windows",
                "Альтернативный вариант: AutoScope добавляет запись в HKCU Run без прав администратора.")
        };
    }

    public static int NormalizeUiScalePercent(int value)
    {
        int[] allowed = new[] { 90, 100, 110, 125, 150 };
        return allowed.Contains(value) ? value : 100;
    }

    public static int NormalizeScenarioAutoCheckIntervalMinutes(int value)
    {
        if (value < 1)
            return 1;

        if (value > 1440)
            return 1440;

        return value;
    }

    public static int NormalizeKeepLogsDays(int value)
    {
        if (value < 1)
            return 1;

        if (value > 3650)
            return 3650;

        return value;
    }

    public static string NormalizeStartupMethod(string value)
    {
        if (string.Equals(value, StartupMethodRegistry, StringComparison.OrdinalIgnoreCase))
            return StartupMethodRegistry;

        return StartupMethodShortcut;
    }

    public static string NormalizeLogLevel(string value)
    {
        if (string.Equals(value, "Debug", StringComparison.OrdinalIgnoreCase))
            return "Debug";

        if (string.Equals(value, "Verbose", StringComparison.OrdinalIgnoreCase))
            return "Verbose";

        return "Normal";
    }

    public static void RegisterWindowScaleHandler(string rootPath)
    {
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is Window window)
                    ApplyUiScale(window, Load(rootPath).UiScalePercent);
            }));
    }

    public static void ApplyUiScaleToOpenWindows(string rootPath)
    {
        int scalePercent = Load(rootPath).UiScalePercent;
        foreach (Window window in Application.Current.Windows)
            ApplyUiScale(window, scalePercent);
    }

    private static void ApplyUiScale(Window window, int scalePercent)
    {
        int normalizedScale = NormalizeUiScalePercent(scalePercent);
        double scale = normalizedScale / 100.0;

        if (window.Content is not FrameworkElement rootElement)
            return;

        rootElement.LayoutTransform = Math.Abs(scale - 1.0) < 0.001
            ? Transform.Identity
            : new ScaleTransform(scale, scale);

        window.InvalidateMeasure();
        window.InvalidateArrange();
    }

    public static string GetSettingsPath(string rootPath)
    {
        return Path.Combine(rootPath, "Configs", "UiSettings.json");
    }

    private static UiSettings Normalize(UiSettings settings)
    {
        settings.ThemeKey = string.IsNullOrWhiteSpace(settings.ThemeKey) ? "graphite-blue" : settings.ThemeKey.Trim();
        settings.DbBrowserPath = settings.DbBrowserPath?.Trim() ?? "";
        settings.UiScalePercent = NormalizeUiScalePercent(settings.UiScalePercent);
        settings.ScenarioAutoCheckIntervalMinutes = NormalizeScenarioAutoCheckIntervalMinutes(settings.ScenarioAutoCheckIntervalMinutes);
        settings.StartupMethod = NormalizeStartupMethod(settings.StartupMethod);
        settings.LogLevel = NormalizeLogLevel(settings.LogLevel);
        settings.KeepLogsDays = NormalizeKeepLogsDays(settings.KeepLogsDays);
        return settings;
    }
}
