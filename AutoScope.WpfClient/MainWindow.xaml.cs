using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
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
    private readonly DispatcherTimer _processRefreshTimer = new();
    private readonly DispatcherTimer _scenarioAutoCheckTimer = new();
    private readonly HashSet<string> _sessionProcessIds = new(StringComparer.OrdinalIgnoreCase);
    private bool _isScenarioAutoCheckEnabled;
    private bool _isScenarioAutoCheckRunning;
    private string _scenarioAutoCheckStatus = "Автопроверка: выключена";
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

    public string ScenarioAutoCheckStatus
    {
        get => _scenarioAutoCheckStatus;
        set { _scenarioAutoCheckStatus = value; OnPropertyChanged(); }
    }

    public string ScenarioAutoCheckButtonText => _isScenarioAutoCheckEnabled
        ? "Выключить автопроверку"
        : "Включить автопроверку";

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        string rootPath = AutoScopeRootLocator.FindRoot();
        _dataService = new DashboardDataService(rootPath);
        RootPathText = $"Корень проекта: {rootPath}";

        ReloadDashboard();

        _processRefreshTimer.Interval = TimeSpan.FromSeconds(1.5);
        _processRefreshTimer.Tick += ProcessRefreshTimer_Tick;
        _processRefreshTimer.Start();

        _scenarioAutoCheckTimer.Interval = TimeSpan.FromMinutes(1);
        _scenarioAutoCheckTimer.Tick += ScenarioAutoCheckTimer_Tick;

        Closed += (_, _) =>
        {
            _processRefreshTimer.Stop();
            _scenarioAutoCheckTimer.Stop();
        };
    }

    private void ProcessRefreshTimer_Tick(object? sender, EventArgs e)
    {
        ReloadProcesses();
    }

    private void ScenarioAutoCheckTimer_Tick(object? sender, EventArgs e)
    {
        RunScenarioAutoCheck(showNoDueMessage: false);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        ReloadDashboard();
    }

    private void ToggleScenarioAutoCheck_Click(object sender, RoutedEventArgs e)
    {
        _isScenarioAutoCheckEnabled = !_isScenarioAutoCheckEnabled;
        OnPropertyChanged(nameof(ScenarioAutoCheckButtonText));

        if (_isScenarioAutoCheckEnabled)
        {
            _scenarioAutoCheckTimer.Start();
            ScenarioAutoCheckStatus = "Автопроверка: включена · каждые 60 сек.";
            RunScenarioAutoCheck(showNoDueMessage: true);
        }
        else
        {
            _scenarioAutoCheckTimer.Stop();
            ScenarioAutoCheckStatus = "Автопроверка: выключена";
            StatusMessage = "Автопроверка сценариев выключена.";
        }
    }

    private void CheckScenariosNow_Click(object sender, RoutedEventArgs e)
    {
        RunScenarioAutoCheck(showNoDueMessage: true);
    }

    private void RefreshProcesses_Click(object sender, RoutedEventArgs e)
    {
        ReloadProcesses();
        StatusMessage = $"Процессы обновлены: {DateTime.Now:HH:mm:ss}.";
    }

    private void LoadProcessHistory_Click(object sender, RoutedEventArgs e)
    {
        int totalHistoryCount = _dataService.CountHistoryProcesses(BuildExcludedHistoryProcessIds(WpfRunManagerService.Instance.GetProcessItems()));
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

    private void RunScenarioAutoCheck(bool showNoDueMessage)
    {
        if (_isScenarioAutoCheckRunning)
        {
            if (showNoDueMessage)
                StatusMessage = "Автопроверка уже выполняется.";
            return;
        }

        _isScenarioAutoCheckRunning = true;

        try
        {
            ScenarioManagementService service = new ScenarioManagementService(_dataService.RootPath);
            ScenarioAutoCheckResult result = service.RunDueScenariosFromWpf(DateTime.Now);
            string message = result.BuildStatusMessage(showNoDueMessage);

            if (!string.IsNullOrWhiteSpace(message))
                StatusMessage = message;

            string stateText = _isScenarioAutoCheckEnabled
                ? "включена · каждые 60 сек."
                : "ручная проверка";

            ScenarioAutoCheckStatus = $"Автопроверка: {stateText} · последняя: {DateTime.Now:HH:mm:ss}";

            if (result.StartedCount > 0 || result.SkippedActiveCount > 0 || result.FailedCount > 0 || showNoDueMessage)
            {
                ReloadScenarioCards();
                ReloadProcesses();
            }

            if (result.FailedCount > 0 && showNoDueMessage)
            {
                MessageBox.Show(
                    string.Join(Environment.NewLine, result.FailedMessages),
                    "Автопроверка сценариев",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        finally
        {
            _isScenarioAutoCheckRunning = false;
        }
    }

    private void OpenRootFolder_Click(object sender, RoutedEventArgs e)
    {
        _dataService.OpenFolder(_dataService.RootPath);
    }

    private void OpenReportsFolder_Click(object sender, RoutedEventArgs e)
    {
        _dataService.OpenFolder(_dataService.GetReportsFolderPath());
    }

    private void OpenReportsManager_Click(object sender, RoutedEventArgs e)
    {
        ReportManagementWindow window = new ReportManagementWindow(_dataService.RootPath)
        {
            Owner = this
        };

        window.ShowDialog();
        StatusMessage = $"Отчёты обновлены после просмотра: {DateTime.Now:HH:mm:ss}.";
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


    private void OpenProcessManager_Click(object sender, RoutedEventArgs e)
    {
        ProcessManagementWindow window = new ProcessManagementWindow(_dataService.RootPath)
        {
            Owner = this
        };

        window.ShowDialog();
        ReloadProcesses();
        StatusMessage = $"Процессы обновлены после управления: {DateTime.Now:HH:mm:ss}.";
    }

    private void OpenDatabaseManager_Click(object sender, RoutedEventArgs e)
    {
        DatabaseManagementWindow window = new DatabaseManagementWindow(_dataService.RootPath)
        {
            Owner = this
        };

        window.ShowDialog();
        ReloadDashboard();
        StatusMessage = $"Базы данных обновлены после управления: {DateTime.Now:HH:mm:ss}.";
    }

    private void OpenScenarioManager_Click(object sender, RoutedEventArgs e)
    {
        ScenarioManagementWindow window = new ScenarioManagementWindow(_dataService.RootPath)
        {
            Owner = this
        };

        window.ShowDialog();
        ReloadDashboard();
        StatusMessage = $"Сценарии обновлены после управления: {DateTime.Now:HH:mm:ss}.";
    }

    private void StartScenarioFromHub_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not ScenarioDashboardItem scenario)
            return;

        if (!scenario.CanRun)
        {
            MessageBox.Show(
                "Этот сценарий нельзя запустить: проверь файл сценария, базу данных и выбранный модуль.",
                "Запуск сценария",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!scenario.Enabled)
        {
            MessageBoxResult confirmation = MessageBox.Show(
                $"Сценарий «{scenario.Name}» сейчас выключен. Запустить его вручную всё равно?",
                "Запуск выключенного сценария",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmation != MessageBoxResult.Yes)
                return;
        }

        ScenarioManagementService service = new ScenarioManagementService(_dataService.RootPath);
        ScenarioOperationResult result = service.StartScenarioNow(scenario);

        StatusMessage = result.Message;

        if (!result.Success)
        {
            MessageBox.Show(result.Message, "Запуск сценария", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ReloadProcesses();
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

    private void PauseProcess_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not ProcessDashboardItem process)
            return;

        if (!process.CanPause)
            return;

        bool paused = WpfRunManagerService.Instance.PauseProcess(process.Id, out string message);
        StatusMessage = message;
        ReloadProcesses();

        if (!paused)
            MessageBox.Show(message, "AutoScope", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ResumeProcess_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not ProcessDashboardItem process)
            return;

        if (!process.CanResume)
            return;

        bool resumed = WpfRunManagerService.Instance.ResumeProcess(process.Id, out string message);
        StatusMessage = message;
        ReloadProcesses();

        if (!resumed)
            MessageBox.Show(message, "AutoScope", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void StopProcess_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not ProcessDashboardItem process)
            return;

        if (!process.CanStop)
            return;

        MessageBoxResult confirmation = MessageBox.Show(
            $"Остановить процесс \"{process.Name}\"?\n\nЕсли модуль сейчас записывает данные, он будет завершён принудительно.",
            "Остановка процесса",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
            return;

        bool stopped = WpfRunManagerService.Instance.StopProcess(process.Id, out string message);
        StatusMessage = message;
        ReloadProcesses();

        if (!stopped)
            MessageBox.Show(message, "AutoScope", MessageBoxButton.OK, MessageBoxImage.Information);
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

    private void LaunchOutputPipeline_Click(object sender, RoutedEventArgs e)
    {
        OutputPipelineLaunchWindow window = new OutputPipelineLaunchWindow(_dataService.RootPath)
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

        List<ProcessDashboardItem> sessionProcesses = WpfRunManagerService.Instance.GetProcessItems();
        foreach (ProcessDashboardItem item in sessionProcesses)
            _sessionProcessIds.Add(item.Id);

        HashSet<string> excludedHistoryIds = BuildExcludedHistoryProcessIds(sessionProcesses);

        List<ProcessDashboardItem> observedExternalProcesses = _dataService.LoadObservedProcesses(excludedHistoryIds)
            .Where(item => item.IsRunningLike)
            .Where(item => !sessionProcesses.Any(session => SameLogPath(session.LogPath, item.LogPath)))
            .ToList();

        foreach (ProcessDashboardItem item in sessionProcesses)
            Processes.Add(item);

        foreach (ProcessDashboardItem item in observedExternalProcesses)
            Processes.Add(item);

        if (sessionProcesses.Count == 0 && observedExternalProcesses.Count == 0)
            Processes.Add(CreateNoActiveProcessesItem());

        if (_historyVisibleCount > 0 || _showAllHistory)
        {
            foreach (ProcessDashboardItem historyItem in _dataService.LoadHistoryProcesses(_historyVisibleCount, _showAllHistory, excludedHistoryIds))
                Processes.Add(historyItem);
        }

        Processes.Add(CreateHistoryActionItem(excludedHistoryIds));
    }

    private HashSet<string> BuildExcludedHistoryProcessIds(IEnumerable<ProcessDashboardItem> sessionProcesses)
    {
        HashSet<string> excluded = new HashSet<string>(_sessionProcessIds, StringComparer.OrdinalIgnoreCase);

        foreach (ProcessDashboardItem process in sessionProcesses)
        {
            if (!string.IsNullOrWhiteSpace(process.LogPath))
                excluded.Add(process.LogPath);
        }

        return excluded;
    }

    private bool SameLogPath(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    }

    private ProcessDashboardItem CreateNoActiveProcessesItem()
    {
        return new ProcessDashboardItem
        {
            Name = "Активных процессов нет",
            StatusText = "ожидание",
            TypeText = "текущая сессия",
            TimeText = "",
            Details = "После запуска парсинга, анализа или сценария здесь сразу появится живая карточка процесса. Завершённые процессы этой же сессии останутся в списке.",
            CanOpenLog = false,
            CanOpenDetails = false,
            StateKind = DashboardStateKind.Neutral
        };
    }

    private ProcessDashboardItem CreateHistoryActionItem(IReadOnlyCollection<string> excludedHistoryIds)
    {
        int totalHistoryCount = _dataService.CountHistoryProcesses(excludedHistoryIds);
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


    private void ReloadScenarioCards()
    {
        Scenarios.Clear();
        foreach (ScenarioDashboardItem item in _dataService.LoadScenarios())
            Scenarios.Add(item);

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
    }

    private void ReloadDashboard()
    {
        ReloadScenarioCards();

        Databases.Clear();
        foreach (DatabaseDashboardItem item in _dataService.LoadDatabases())
            Databases.Add(item);

        ReloadProcesses();

        if (Databases.Count == 0)
        {
            Databases.Add(new DatabaseDashboardItem
            {
                Name = "Базы не найдены",
                RecordsText = "нет данных",
                SizeText = "размер: —",
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
