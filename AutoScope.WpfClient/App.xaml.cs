using System;
using System.Linq;
using System.Windows;
using AutoScope.WpfClient.Services;
using Forms = System.Windows.Forms;

namespace AutoScope.WpfClient;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private TrayService? _trayService;

    public bool IsExplicitExitRequested { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        string rootPath = AutoScopeRootLocator.FindRoot();
        UiSettings settings = AppSettingsService.Load(rootPath);

        ThemeService.ApplySavedTheme(rootPath);
        AppSettingsService.RegisterWindowScaleHandler(rootPath);
        AppSettingsService.ApplyUiScaleToOpenWindows(rootPath);

        _mainWindow = new MainWindow();
        MainWindow = _mainWindow;

        if (settings.RunInBackground)
            EnsureTrayService(rootPath);

        bool startInTray = settings.RunInBackground && e.Args.Any(arg => string.Equals(arg, "--tray", StringComparison.OrdinalIgnoreCase));
        if (startInTray)
        {
            if (settings.ShowBackgroundHintOnClose)
            {
                ShowTrayNotification(
                    "AutoScope запущен в фоне",
                    "Приложение находится в трее и продолжает проверять сценарии, если автопроверка включена.",
                    Forms.ToolTipIcon.Info);
            }

            return;
        }

        _mainWindow.Show();
    }

    public void ShowMainWindow()
    {
        if (_mainWindow == null)
            return;

        _mainWindow.ShowFromTray();
    }

    public void RefreshTrayFromSettings(string rootPath)
    {
        UiSettings settings = AppSettingsService.Load(rootPath);
        if (settings.RunInBackground)
        {
            EnsureTrayService(rootPath);
            return;
        }

        _trayService?.Dispose();
        _trayService = null;
    }

    public void ShowTrayNotification(string title, string message, Forms.ToolTipIcon icon)
    {
        _trayService?.ShowNotification(title, message, icon);
    }

    private void EnsureTrayService(string rootPath)
    {
        if (_mainWindow == null || _trayService != null)
            return;

        _trayService = new TrayService(rootPath, _mainWindow);
        _trayService.Initialize();
    }

    public void RequestExplicitExit()
    {
        IsExplicitExitRequested = true;
        _trayService?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayService?.Dispose();
        base.OnExit(e);
    }
}
