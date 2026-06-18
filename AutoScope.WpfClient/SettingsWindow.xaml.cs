using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using AutoScope.WpfClient.Services;
using Microsoft.Win32;

namespace AutoScope.WpfClient;

public partial class SettingsWindow : Window
{
    private readonly string _rootPath;

    public SettingsWindow(string rootPath)
    {
        InitializeComponent();
        _rootPath = rootPath;

        LoadThemes();
        LoadAppSettings();
    }

    private void LoadThemes()
    {
        var themes = ThemeService.GetAvailableThemes(_rootPath);
        ThemeComboBox.ItemsSource = themes;
        string savedThemeKey = ThemeService.LoadSavedThemeKey(_rootPath);
        ThemeComboBox.SelectedItem = themes.FirstOrDefault(theme => theme.Key == savedThemeKey) ?? themes.FirstOrDefault();
        UpdateThemeDescription();
    }

    private void LoadAppSettings()
    {
        UiSettings settings = AppSettingsService.Load(_rootPath);
        DbBrowserPathTextBox.Text = settings.DbBrowserPath ?? "";

        var scaleOptions = AppSettingsService.GetUiScaleOptions();
        UiScaleComboBox.ItemsSource = scaleOptions;
        int savedScale = AppSettingsService.NormalizeUiScalePercent(settings.UiScalePercent);
        UiScaleComboBox.SelectedItem = scaleOptions.FirstOrDefault(option => option.Percent == savedScale)
            ?? scaleOptions.FirstOrDefault(option => option.Percent == 100);

        UpdateDbBrowserHint();
    }

    private void RootWindowBorder_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RootWindowBorder.Clip = null;
    }

    private void ThemeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateThemeDescription();
    }

    private void BrowseDbBrowser_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Title = "Выберите DB Browser for SQLite",
            Filter = "Исполняемые файлы (*.exe)|*.exe|Все файлы (*.*)|*.*",
            CheckFileExists = true
        };

        string currentPath = DbBrowserPathTextBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(currentPath) && File.Exists(currentPath))
            dialog.InitialDirectory = Path.GetDirectoryName(currentPath);
        else if (Directory.Exists(_rootPath))
            dialog.InitialDirectory = _rootPath;

        if (dialog.ShowDialog(this) == true)
        {
            DbBrowserPathTextBox.Text = dialog.FileName;
            UpdateDbBrowserHint();
        }
    }

    private void AutoDetectDbBrowser_Click(object sender, RoutedEventArgs e)
    {
        string? detectedPath = FindDbBrowserExecutable();
        if (string.IsNullOrWhiteSpace(detectedPath))
        {
            DbBrowserHintText.Text = "DB Browser for SQLite не найден автоматически. Укажите путь вручную через кнопку «Выбрать».";
            return;
        }

        DbBrowserPathTextBox.Text = detectedPath;
        UpdateDbBrowserHint();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        string dbBrowserPath = DbBrowserPathTextBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(dbBrowserPath) && !File.Exists(dbBrowserPath))
        {
            MessageBox.Show(
                "Указанный путь к DB Browser не существует. Проверьте путь или очистите поле.",
                "Настройки",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (ThemeComboBox.SelectedItem is ThemeOption selectedTheme)
            ThemeService.ApplyTheme(selectedTheme.Key, save: true, rootPath: _rootPath);

        AppSettingsService.Update(_rootPath, settings =>
        {
            if (ThemeComboBox.SelectedItem is ThemeOption selectedTheme)
                settings.ThemeKey = selectedTheme.Key;

            settings.DbBrowserPath = dbBrowserPath;
            if (UiScaleComboBox.SelectedItem is UiScaleOption selectedScale)
                settings.UiScalePercent = selectedScale.Percent;
        });

        AppSettingsService.ApplyUiScaleToOpenWindows(_rootPath);

        DialogResult = true;
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            return;

        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // DragMove может упасть, если мышь уже отпущена. Для UI это не критично.
        }
    }

    private void UpdateThemeDescription()
    {
        if (ThemeComboBox.SelectedItem is ThemeOption selectedTheme)
            ThemeDescriptionText.Text = selectedTheme.Description;
        else
            ThemeDescriptionText.Text = "";
    }

    private void UpdateDbBrowserHint()
    {
        string path = DbBrowserPathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            DbBrowserHintText.Text = "Путь не задан. AutoScope будет искать DB Browser автоматически в папке проекта.";
            return;
        }

        DbBrowserHintText.Text = File.Exists(path)
            ? "Путь найден. Базы будут открываться через выбранный DB Browser."
            : "Файл по указанному пути не найден.";
    }

    private string? FindDbBrowserExecutable()
    {
        string[] names = new[]
        {
            "DB Browser for SQLite.exe",
            "DB Browser.exe",
            "sqlitebrowser.exe"
        };

        try
        {
            return Directory.EnumerateFiles(_rootPath, "*.exe", SearchOption.AllDirectories)
                .FirstOrDefault(path => names.Any(name => Path.GetFileName(path).Equals(name, StringComparison.OrdinalIgnoreCase)));
        }
        catch
        {
            return null;
        }
    }
}
