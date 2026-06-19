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
    private bool _isLoading;
    private StartupMethodOption[] _startupMethods = Array.Empty<StartupMethodOption>();

    public SettingsWindow(string rootPath)
    {
        InitializeComponent();
        _rootPath = rootPath;

        _isLoading = true;
        LoadThemes();
        LoadStartupMethods();
        LoadAppSettings();
        _isLoading = false;

        UpdateStartupControls();
        UpdateStartupStatus();
    }

    private void LoadThemes()
    {
        var themes = ThemeService.GetAvailableThemes(_rootPath);
        ThemeComboBox.ItemsSource = themes;
        string savedThemeKey = ThemeService.LoadSavedThemeKey(_rootPath);
        ThemeComboBox.SelectedItem = themes.FirstOrDefault(theme => theme.Key == savedThemeKey) ?? themes.FirstOrDefault();
        UpdateThemeDescription();
    }

    private void LoadStartupMethods()
    {
        _startupMethods = AppSettingsService.GetStartupMethodOptions();
        StartupMethodComboBox.ItemsSource = _startupMethods;
    }

    private void LoadAppSettings()
    {
        UiSettings settings = AppSettingsService.Load(_rootPath);

        RunInBackgroundCheckBox.IsChecked = settings.RunInBackground;
        MinimizeToTrayOnCloseCheckBox.IsChecked = settings.MinimizeToTrayOnClose;
        StartWithWindowsCheckBox.IsChecked = settings.StartWithWindows;
        StartMinimizedToTrayCheckBox.IsChecked = settings.StartMinimizedToTray;
        ShowBackgroundHintOnCloseCheckBox.IsChecked = settings.ShowBackgroundHintOnClose;

        string startupMethod = AppSettingsService.NormalizeStartupMethod(settings.StartupMethod);
        StartupMethodComboBox.SelectedItem = _startupMethods
            .FirstOrDefault(option => option.Key == startupMethod)
            ?? _startupMethods.FirstOrDefault();

        ScenarioAutoCheckEnabledCheckBox.IsChecked = settings.ScenarioAutoCheckEnabled;
        ShowScenarioNotificationsCheckBox.IsChecked = settings.ShowScenarioNotifications;
        ShowScenarioErrorNotificationsCheckBox.IsChecked = settings.ShowScenarioErrorNotifications;

        int intervalMinutes = AppSettingsService.NormalizeScenarioAutoCheckIntervalMinutes(settings.ScenarioAutoCheckIntervalMinutes);
        ScenarioAutoCheckIntervalTextBox.Text = intervalMinutes.ToString();

        var scaleOptions = AppSettingsService.GetUiScaleOptions();
        UiScaleComboBox.ItemsSource = scaleOptions;
        int savedScale = AppSettingsService.NormalizeUiScalePercent(settings.UiScalePercent);
        UiScaleComboBox.SelectedItem = scaleOptions.FirstOrDefault(option => option.Percent == savedScale)
            ?? scaleOptions.FirstOrDefault(option => option.Percent == 100);

        ShowEmptyStateHintsCheckBox.IsChecked = settings.ShowEmptyStateHints;
        RememberWindowPlacementCheckBox.IsChecked = settings.RememberWindowPlacement;

        DbBrowserPathTextBox.Text = settings.DbBrowserPath ?? "";

        OpenReportAfterAnalysisCheckBox.IsChecked = settings.OpenReportAfterAnalysis;
        OpenLogOnErrorCheckBox.IsChecked = settings.OpenLogOnError;
        KeepLogsDaysTextBox.Text = AppSettingsService.NormalizeKeepLogsDays(settings.KeepLogsDays).ToString();

        UpdateThemeDescription();
        UpdateStartupMethodDescription();
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

    private void StartupMethodComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateStartupMethodDescription();
        UpdateStartupStatus();
    }

    private void StartupSettings_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
            return;

        UpdateStartupControls();
        UpdateStartupStatus();
    }

    private void BackgroundSettings_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
            return;

        UpdateStartupControls();
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

        if (!int.TryParse(ScenarioAutoCheckIntervalTextBox.Text.Trim(), out int intervalMinutes))
        {
            MessageBox.Show(
                "Интервал автопроверки должен быть целым числом минут.",
                "Настройки",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(KeepLogsDaysTextBox.Text.Trim(), out int keepLogsDays))
        {
            MessageBox.Show(
                "Срок хранения логов должен быть целым числом дней.",
                "Настройки",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        intervalMinutes = AppSettingsService.NormalizeScenarioAutoCheckIntervalMinutes(intervalMinutes);
        keepLogsDays = AppSettingsService.NormalizeKeepLogsDays(keepLogsDays);
        ScenarioAutoCheckIntervalTextBox.Text = intervalMinutes.ToString();
        KeepLogsDaysTextBox.Text = keepLogsDays.ToString();

        if (ThemeComboBox.SelectedItem is ThemeOption selectedTheme)
            ThemeService.ApplyTheme(selectedTheme.Key, save: true, rootPath: _rootPath);

        UiSettings savedSettings = AppSettingsService.Load(_rootPath);
        AppSettingsService.Update(_rootPath, settings =>
        {
            if (ThemeComboBox.SelectedItem is ThemeOption selectedTheme)
                settings.ThemeKey = selectedTheme.Key;

            bool runInBackground = RunInBackgroundCheckBox.IsChecked == true;
            settings.RunInBackground = runInBackground;
            settings.MinimizeToTrayOnClose = runInBackground && MinimizeToTrayOnCloseCheckBox.IsChecked == true;
            settings.StartWithWindows = runInBackground && StartWithWindowsCheckBox.IsChecked == true;
            settings.StartupMethod = StartupMethodComboBox.SelectedItem is StartupMethodOption selectedStartupMethod
                ? selectedStartupMethod.Key
                : AppSettingsService.StartupMethodShortcut;
            settings.StartMinimizedToTray = runInBackground && StartMinimizedToTrayCheckBox.IsChecked == true;
            settings.ShowBackgroundHintOnClose = runInBackground && ShowBackgroundHintOnCloseCheckBox.IsChecked == true;

            settings.ScenarioAutoCheckEnabled = ScenarioAutoCheckEnabledCheckBox.IsChecked == true;
            settings.ScenarioAutoCheckIntervalMinutes = intervalMinutes;
            settings.ShowScenarioNotifications = ShowScenarioNotificationsCheckBox.IsChecked == true;
            settings.ShowScenarioErrorNotifications = ShowScenarioErrorNotificationsCheckBox.IsChecked == true;

            if (UiScaleComboBox.SelectedItem is UiScaleOption selectedScale)
                settings.UiScalePercent = selectedScale.Percent;

            settings.ShowEmptyStateHints = ShowEmptyStateHintsCheckBox.IsChecked == true;
            settings.RememberWindowPlacement = RememberWindowPlacementCheckBox.IsChecked == true;
            settings.DbBrowserPath = dbBrowserPath;
            settings.OpenReportAfterAnalysis = OpenReportAfterAnalysisCheckBox.IsChecked == true;
            settings.OpenLogOnError = OpenLogOnErrorCheckBox.IsChecked == true;
            settings.KeepLogsDays = keepLogsDays;

            savedSettings = settings;
        });

        AppSettingsService.ApplyUiScaleToOpenWindows(_rootPath);

        if (Application.Current is App app)
            app.RefreshTrayFromSettings(_rootPath);

        bool startupApplied = WindowsStartupService.ApplyStartupSettings(_rootPath, savedSettings, out string startupMessage);
        if (!startupApplied)
        {
            MessageBox.Show(
                startupMessage,
                "Автозапуск AutoScope",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

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

    private void UpdateStartupControls()
    {
        bool runInBackground = RunInBackgroundCheckBox.IsChecked == true;
        bool startWithWindows = StartWithWindowsCheckBox.IsChecked == true;

        MinimizeToTrayOnCloseCheckBox.IsEnabled = runInBackground;
        ShowBackgroundHintOnCloseCheckBox.IsEnabled = runInBackground;
        StartWithWindowsCheckBox.IsEnabled = runInBackground;
        StartupMethodComboBox.IsEnabled = runInBackground && startWithWindows;
        StartMinimizedToTrayCheckBox.IsEnabled = runInBackground && startWithWindows;
    }

    private void UpdateStartupMethodDescription()
    {
        if (StartupMethodComboBox.SelectedItem is StartupMethodOption option)
            StartupMethodDescriptionText.Text = option.Description;
        else
            StartupMethodDescriptionText.Text = "";
    }

    private void UpdateStartupStatus()
    {
        if (_isLoading)
            return;

        UiSettings preview = AppSettingsService.Load(_rootPath);
        preview.RunInBackground = RunInBackgroundCheckBox.IsChecked == true;
        preview.StartWithWindows = StartWithWindowsCheckBox.IsChecked == true;
        preview.StartupMethod = StartupMethodComboBox.SelectedItem is StartupMethodOption option
            ? option.Key
            : AppSettingsService.StartupMethodShortcut;
        preview.StartMinimizedToTray = StartMinimizedToTrayCheckBox.IsChecked == true;

        StartupStatusText.Text = WindowsStartupService.GetStartupStatusText(preview);
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
