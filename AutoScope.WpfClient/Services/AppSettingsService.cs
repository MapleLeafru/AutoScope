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

public static class AppSettingsService
{
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
                return new UiSettings();

            UiSettings? settings = JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(settingsPath));
            return settings ?? new UiSettings();
        }
        catch
        {
            return new UiSettings();
        }
    }

    public static void Save(string rootPath, UiSettings settings)
    {
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

    public static int NormalizeUiScalePercent(int value)
    {
        int[] allowed = new[] { 90, 100, 110, 125, 150 };
        return allowed.Contains(value) ? value : 100;
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
}
