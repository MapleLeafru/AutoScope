using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AutoScope.WpfClient.Models;
using AutoScope.WpfClient.Services;

namespace AutoScope.WpfClient;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly DashboardDataService _dataService;
    private string _rootPathText = "";
    private string _statusMessage = "";
    private Rect? _restoreBoundsBeforeCustomMaximize;
    private bool _isCustomMaximized;
    private readonly HashSet<string> _sessionProcessIds = new(StringComparer.OrdinalIgnoreCase);
    private int _historyVisibleCount;
    private bool _showAllHistory;

    public ObservableCollection<ScenarioDashboardItem> Scenarios { get; } = new();
    public ObservableCollection<DatabaseDashboardItem> Databases { get; } = new();
    public ObservableCollection<ProcessDashboardItem> Processes { get; } = new();

    public string AppVersionText => $"Версия {AppVersionService.Version}";

    public string RootPathText
    {
        get => _rootPathText;
        set { _rootPathText = value; OnPropertyChanged(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        string rootPath = AutoScopeRootLocator.FindRoot();
        _dataService = new DashboardDataService(rootPath);
        RootPathText = $"Корень проекта: {rootPath}";

        ReloadDashboard();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        ReloadDashboard();
    }

    private void RefreshProcesses_Click(object sender, RoutedEventArgs e)
    {
        ReloadProcesses();
        StatusMessage = $"Процессы обновлены: {DateTime.Now:HH:mm:ss}.";
    }

    private void LoadProcessHistory_Click(object sender, RoutedEventArgs e)
    {
        int totalHistoryCount = _dataService.CountHistoryProcesses(_sessionProcessIds);
        if (_showAllHistory || _historyVisibleCount >= totalHistoryCount)
        {
            StatusMessage = "Вся доступная история процессов уже отображена.";
            return;
        }

        _historyVisibleCount = _historyVisibleCount <= 0 ? 10 : _historyVisibleCount + 10;
        ReloadProcesses();
        StatusMessage = $"История процессов подгружена: {Math.Min(_historyVisibleCount, totalHistoryCount)} из {totalHistoryCount}.";
    }

    private void LoadAllProcessHistory_Click(object sender, RoutedEventArgs e)
    {
        _showAllHistory = true;
        ReloadProcesses();
        StatusMessage = "Отображается вся найденная история процессов.";
    }

    private void OpenRootFolder_Click(object sender, RoutedEventArgs e)
    {
        _dataService.OpenFolder(_dataService.RootPath);
    }

    private void OpenReportsFolder_Click(object sender, RoutedEventArgs e)
    {
        _dataService.OpenFolder(_dataService.GetReportsFolderPath());
    }

    private void OpenDatabasesFolder_Click(object sender, RoutedEventArgs e)
    {
        _dataService.OpenFolder(_dataService.GetDatabasesFolderPath());
    }

    private void OpenLogsFolder_Click(object sender, RoutedEventArgs e)
    {
        _dataService.OpenFolder(_dataService.GetLogsFolderPath());
    }

    private void OpenJobsFolder_Click(object sender, RoutedEventArgs e)
    {
        _dataService.OpenFolder(_dataService.GetJobsFolderPath());
    }

    private void OpenDbBrowser_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not DatabaseDashboardItem database)
            return;

        bool opened = _dataService.OpenDatabaseInDbBrowser(database.Path, out string message);
        StatusMessage = message;

        if (!opened)
            MessageBox.Show(message, "AutoScope", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OpenProcessLog_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not ProcessDashboardItem process)
            return;

        try
        {
            _dataService.OpenFile(process.LogPath);
            StatusMessage = $"Открыт лог процесса: {process.Name}.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Лог процесса открыть не удалось.";
            MessageBox.Show(ex.Message, "AutoScope", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowProcessInfo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not ProcessDashboardItem process)
            return;

        List<string> lines = new()
        {
            $"Название: {process.Name}",
            $"Тип: {process.TypeText}",
            $"Статус: {process.StatusText}",
            $"Время: {process.TimeText}"
        };

        if (process.IsProgressVisible)
        {
            lines.Add($"Этап: {process.StageText}");
            lines.Add($"Прогресс: {process.ProgressText}");
            lines.Add($"Количество: {process.CountText}");
        }

        lines.Add($"Состояние: {process.Details}");
        lines.Add(string.IsNullOrWhiteSpace(process.LogPath) ? "Лог: не задан" : $"Лог: {process.LogPath}");

        string message = string.Join(Environment.NewLine, lines);

        MessageBox.Show(message, "Процесс AutoScope", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void LaunchInputPipeline_Click(object sender, RoutedEventArgs e)
    {
        InputPipelineLaunchWindow window = new InputPipelineLaunchWindow(_dataService.RootPath)
        {
            Owner = this
        };

        bool? result = window.ShowDialog();
        if (result == true && window.RunStarted)
        {
            _historyVisibleCount = 0;
            _showAllHistory = false;
            ReloadDashboard();
            StatusMessage = window.ResultMessage;
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        SettingsWindow window = new SettingsWindow(_dataService.RootPath)
        {
            Owner = this
        };

        window.ShowDialog();
    }

    private void Stub_Click(object sender, RoutedEventArgs e)
    {
        string message = "Этот экран будет добавлен следующим шагом.";
        if (sender is FrameworkElement element && element.Tag is string tag && !string.IsNullOrWhiteSpace(tag))
            message = tag;

        MessageBox.Show(message, "AutoScope UI", MessageBoxButton.OK, MessageBoxImage.Information);
    }


    private void RootWindowBorder_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RootWindowBorder.Clip = null;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleWindowState();
            return;
        }

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

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeRestore_Click(object sender, RoutedEventArgs e)
    {
        ToggleWindowState();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleWindowState()
    {
        if (_isCustomMaximized)
        {
            RestoreFromCustomMaximize();
            return;
        }

        MaximizeToCurrentMonitorWorkArea();
    }

    private void MaximizeToCurrentMonitorWorkArea()
    {
        if (WindowState != WindowState.Normal)
            WindowState = WindowState.Normal;

        _restoreBoundsBeforeCustomMaximize = new Rect(Left, Top, Width, Height);

        Rect workArea = GetCurrentMonitorWorkArea();
        Left = workArea.Left;
        Top = workArea.Top;
        Width = workArea.Width;
        Height = workArea.Height;
        _isCustomMaximized = true;
    }

    private void RestoreFromCustomMaximize()
    {
        if (_restoreBoundsBeforeCustomMaximize is Rect restoreBounds)
        {
            Left = restoreBounds.Left;
            Top = restoreBounds.Top;
            Width = restoreBounds.Width;
            Height = restoreBounds.Height;
        }

        _isCustomMaximized = false;
        _restoreBoundsBeforeCustomMaximize = null;
    }

    private Rect GetCurrentMonitorWorkArea()
    {
        IntPtr windowHandle = new WindowInteropHelper(this).Handle;
        IntPtr monitorHandle = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);

        MonitorInfo monitorInfo = new();
        monitorInfo.Size = Marshal.SizeOf<MonitorInfo>();

        if (!GetMonitorInfo(monitorHandle, ref monitorInfo))
            return SystemParameters.WorkArea;

        Point topLeft = ToDeviceIndependentPoint(monitorInfo.WorkArea.Left, monitorInfo.WorkArea.Top);
        Point bottomRight = ToDeviceIndependentPoint(monitorInfo.WorkArea.Right, monitorInfo.WorkArea.Bottom);

        return new Rect(topLeft, bottomRight);
    }

    private Point ToDeviceIndependentPoint(int x, int y)
    {
        PresentationSource? source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget == null)
            return new Point(x, y);

        return source.CompositionTarget.TransformFromDevice.Transform(new Point(x, y));
    }

    private const int MonitorDefaultToNearest = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr monitorHandle, ref MonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect MonitorArea;
        public NativeRect WorkArea;
        public int Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private void ReloadProcesses()
    {
        Processes.Clear();

        List<ProcessDashboardItem> observedProcesses = _dataService.LoadObservedProcesses(_sessionProcessIds);

        foreach (ProcessDashboardItem item in observedProcesses)
        {
            if (item.IsRunningLike)
                _sessionProcessIds.Add(item.Id);
        }

        observedProcesses = _dataService.LoadObservedProcesses(_sessionProcessIds);
        foreach (ProcessDashboardItem item in observedProcesses)
        {
            item.IsSessionItem = _sessionProcessIds.Contains(item.Id);
            Processes.Add(item);
        }

        if (observedProcesses.Count == 0)
            Processes.Add(CreateNoActiveProcessesItem());

        if (_historyVisibleCount > 0 || _showAllHistory)
        {
            foreach (ProcessDashboardItem historyItem in _dataService.LoadHistoryProcesses(_historyVisibleCount, _showAllHistory, _sessionProcessIds))
                Processes.Add(historyItem);
        }

        Processes.Add(CreateHistoryActionItem());
    }

    private ProcessDashboardItem CreateNoActiveProcessesItem()
    {
        return new ProcessDashboardItem
        {
            Name = "Активных процессов нет",
            StatusText = "ожидание",
            TypeText = "текущая сессия",
            TimeText = "",
            Details = "Когда парсинг или анализ будет запущен из UI, здесь появится карточка с этапом, процентом и текущим состоянием. Завершённые процессы этой же сессии останутся в списке.",
            CanOpenLog = false,
            CanOpenDetails = false,
            StateKind = DashboardStateKind.Neutral
        };
    }

    private ProcessDashboardItem CreateHistoryActionItem()
    {
        int totalHistoryCount = _dataService.CountHistoryProcesses(_sessionProcessIds);
        bool hasVisibleHistory = _historyVisibleCount > 0 || _showAllHistory;
        bool hasMoreHistory = !_showAllHistory && _historyVisibleCount < totalHistoryCount;

        string primaryText;
        string secondaryText = "Вся история";
        string hint;

        if (!hasVisibleHistory)
        {
            primaryText = totalHistoryCount > 0
                ? "Показать последние 10 завершённых процессов"
                : "История процессов пока не найдена";
            hint = totalHistoryCount > 0
                ? "История по логам скрыта, чтобы не мешать активным процессам. Её можно подгрузить вручную."
                : "В папке Logs пока нет завершённых pipeline-логов, которые можно показать как историю.";
        }
        else if (hasMoreHistory)
        {
            primaryText = "Отобразить ещё 10 завершённых процессов";
            int visibleCount = Math.Min(_historyVisibleCount, totalHistoryCount);
            hint = $"Показано завершённых процессов: {visibleCount} из {totalHistoryCount}.";
        }
        else
        {
            primaryText = "Вся доступная история отображена";
            hint = $"Показано завершённых процессов: {totalHistoryCount} из {totalHistoryCount}.";
        }

        return new ProcessDashboardItem
        {
            IsActionRow = true,
            PrimaryActionText = primaryText,
            SecondaryActionText = secondaryText,
            ActionHint = hint,
            IsSecondaryActionVisible = hasVisibleHistory && hasMoreHistory,
            IsPrimaryActionEnabled = totalHistoryCount > 0 && hasMoreHistory,
            CanOpenLog = false,
            CanOpenDetails = false,
            StateKind = DashboardStateKind.Neutral
        };
    }


    private void ReloadDashboard()
    {
        Scenarios.Clear();
        foreach (ScenarioDashboardItem item in _dataService.LoadScenarios())
            Scenarios.Add(item);

        Databases.Clear();
        foreach (DatabaseDashboardItem item in _dataService.LoadDatabases())
            Databases.Add(item);

        ReloadProcesses();

        if (Scenarios.Count == 0)
        {
            Scenarios.Add(new ScenarioDashboardItem
            {
                Name = "Сценарии не найдены",
                StatusText = "нет данных",
                Details = "Папка Jobs пуста или пока не найдена.",
                StateKind = DashboardStateKind.Neutral
            });
        }

        if (Databases.Count == 0)
        {
            Databases.Add(new DatabaseDashboardItem
            {
                Name = "Базы не найдены",
                RecordsText = "нет данных",
                Details = "SQLite-файлы .db в проекте пока не найдены.",
                StateKind = DashboardStateKind.Neutral
            });
        }

        StatusMessage = $"Обновлено: {DateTime.Now:HH:mm:ss}. Сценариев: {Scenarios.Count}, баз: {Databases.Count}, процессов: {Processes.Count}.";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
