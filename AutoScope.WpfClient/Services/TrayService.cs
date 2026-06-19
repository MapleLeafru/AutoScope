using System;
using System.Windows;
using AutoScope.WpfClient;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace AutoScope.WpfClient.Services;

public sealed class TrayService : IDisposable
{
    private readonly string _rootPath;
    private readonly MainWindow _mainWindow;
    private Forms.NotifyIcon? _notifyIcon;
    private Drawing.Icon? _appIcon;
    private bool _disposed;

    public TrayService(string rootPath, MainWindow mainWindow)
    {
        _rootPath = rootPath;
        _mainWindow = mainWindow;
    }

    public void Initialize()
    {
        if (_notifyIcon != null)
            return;

        Forms.ContextMenuStrip menu = new();
        menu.Items.Add("Открыть AutoScope", null, (_, _) => _mainWindow.ShowFromTray());
        menu.Items.Add("Управление процессами", null, (_, _) => _mainWindow.OpenProcessManagerFromTray());
        menu.Items.Add("Настройки", null, (_, _) => _mainWindow.OpenSettingsFromTray());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => RequestExit());

        _appIcon = LoadAppIcon();

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _appIcon,
            Text = "AutoScope",
            ContextMenuStrip = menu,
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => _mainWindow.ShowFromTray();
    }

    public void ShowNotification(string title, string message, Forms.ToolTipIcon icon)
    {
        if (_notifyIcon == null)
            return;

        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = icon;
        _notifyIcon.ShowBalloonTip(4000);
    }


    private Drawing.Icon LoadAppIcon()
    {
        try
        {
            Uri iconUri = new("pack://application:,,,/Resources/AppIcon.ico", UriKind.Absolute);
            var resourceInfo = Application.GetResourceStream(iconUri);

            if (resourceInfo?.Stream != null)
            {
                using Drawing.Icon resourceIcon = new(resourceInfo.Stream);
                return (Drawing.Icon)resourceIcon.Clone();
            }
        }
        catch
        {
            // Если ресурс по какой-то причине не прочитан, ниже пробуем взять иконку из exe.
        }

        try
        {
            string? processPath = Environment.ProcessPath;

            if (!string.IsNullOrWhiteSpace(processPath))
            {
                Drawing.Icon? executableIcon = Drawing.Icon.ExtractAssociatedIcon(processPath);

                if (executableIcon != null)
                    return executableIcon;
            }
        }
        catch
        {
            // Если не удалось извлечь иконку из exe, используем системный fallback.
        }

        return (Drawing.Icon)Drawing.SystemIcons.Application.Clone();
    }

    private void RequestExit()
    {
        if (Application.Current is App app)
            app.RequestExplicitExit();
        else
            Application.Current?.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        if (_appIcon != null)
        {
            _appIcon.Dispose();
            _appIcon = null;
        }
    }
}
