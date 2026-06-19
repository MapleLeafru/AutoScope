using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AutoScope.WpfClient.Models;
using AutoScope.WpfClient.Services;

namespace AutoScope.WpfClient;

public partial class ProcessManagementWindow : Window, INotifyPropertyChanged
{
    private readonly DashboardDataService _dataService;
    private readonly DispatcherTimer _refreshTimer = new();
    private readonly HashSet<string> _sessionProcessIds = new(StringComparer.OrdinalIgnoreCase);
    private string _statusMessage = "";
    private string _summaryText = "";
    private string _filterText = "";
    private string _historyText = "";
    private string _activeFilter = "all";
    private int _historyVisibleCount = 30;
    private bool _showAllHistory;

    public ObservableCollection<ProcessDashboardItem> Processes { get; } = new();

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public string SummaryText
    {
        get => _summaryText;
        set { _summaryText = value; OnPropertyChanged(); }
    }

    public string FilterText
    {
        get => _filterText;
        set { _filterText = value; OnPropertyChanged(); }
    }

    public string HistoryText
    {
        get => _historyText;
        set { _historyText = value; OnPropertyChanged(); }
    }

    public ProcessManagementWindow(string rootPath)
    {
        InitializeComponent();
        DataContext = this;

        _dataService = new DashboardDataService(rootPath);

        ReloadProcesses();

        _refreshTimer.Interval = TimeSpan.FromSeconds(1.5);
        _refreshTimer.Tick += (_, _) => ReloadProcesses(keepStatus: true);
        _refreshTimer.Start();
        Closed += (_, _) => _refreshTimer.Stop();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        ReloadProcesses();
        StatusMessage = $"Процессы обновлены: {DateTime.Now:HH:mm:ss}.";
    }

    private void SetFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not string filter)
            return;

        _activeFilter = filter;
        ReloadProcesses();
    }

    private void LoadMoreHistory_Click(object sender, RoutedEventArgs e)
    {
        if (_showAllHistory)
        {
            StatusMessage = "Вся история уже отображается.";
            return;
        }

        _historyVisibleCount += 30;
        ReloadProcesses();
        StatusMessage = $"История расширена до {_historyVisibleCount} записей.";
    }

    private void LoadAllHistory_Click(object sender, RoutedEventArgs e)
    {
        _showAllHistory = true;
        ReloadProcesses();
        StatusMessage = "Отображается вся найденная история процессов.";
    }

    private void ClearCompleted_Click(object sender, RoutedEventArgs e)
    {
        MessageBoxResult answer = MessageBox.Show(
            "Убрать завершённые процессы текущей WPF-сессии из списка?\n\nЛоги и история запусков не будут удалены.",
            "Очистка завершённых процессов",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
            return;

        int removed = WpfRunManagerService.Instance.ClearCompletedRecords();
        ReloadProcesses();
        StatusMessage = removed == 0
            ? "Завершённых процессов текущей сессии для очистки нет."
            : $"Убрано завершённых процессов текущей сессии: {removed}.";
    }

    private void OpenLogsFolder_Click(object sender, RoutedEventArgs e)
    {
        _dataService.OpenFolder(_dataService.GetLogsFolderPath());
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

        MessageBox.Show(BuildProcessInfo(process), "Процесс AutoScope", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void PauseProcess_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not ProcessDashboardItem process)
            return;

        if (!process.CanPause)
            return;

        bool paused = WpfRunManagerService.Instance.PauseProcess(process.Id, out string message);
        ReloadProcesses();
        StatusMessage = message;

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
        ReloadProcesses();
        StatusMessage = message;

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
        ReloadProcesses();
        StatusMessage = message;

        if (!stopped)
            MessageBox.Show(message, "AutoScope", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ReloadProcesses(bool keepStatus = false)
    {
        List<ProcessDashboardItem> allItems = LoadAllProcessItems();
        List<ProcessDashboardItem> filtered = ApplyFilter(allItems);

        Processes.Clear();
        foreach (ProcessDashboardItem item in filtered)
            Processes.Add(item);

        if (Processes.Count == 0)
            Processes.Add(CreateEmptyFilterItem());

        int activeCount = allItems.Count(item => item.IsRunningLike && !item.IsHistoryItem && !item.IsActionRow);
        int finishedCount = allItems.Count(item => !item.IsRunningLike && !item.IsActionRow);
        int successCount = allItems.Count(item => item.StateKind == DashboardStateKind.Success && !item.IsActionRow);
        int stoppedCount = allItems.Count(item => item.IsStopped && !item.IsActionRow);
        int errorCount = allItems.Count(item => item.StateKind == DashboardStateKind.Error && !item.IsActionRow);
        int historyCount = allItems.Count(item => item.IsHistoryItem);

        SummaryText = $"Активных: {activeCount} · успешных: {successCount} · остановленных: {stoppedCount} · ошибок: {errorCount}";
        FilterText = FormatFilterText(_activeFilter, filtered.Count);
        HistoryText = _showAllHistory
            ? $"История: отображаются все найденные процессы ({historyCount})."
            : $"История: показано до {_historyVisibleCount} последних завершённых процессов. Можно подгрузить больше или открыть всю историю.";

        if (!keepStatus)
            StatusMessage = $"Отображено процессов: {Math.Max(0, Processes.Count)}.";
    }

    private List<ProcessDashboardItem> LoadAllProcessItems()
    {
        List<ProcessDashboardItem> sessionProcesses = WpfRunManagerService.Instance.GetProcessItems();
        foreach (ProcessDashboardItem item in sessionProcesses)
            _sessionProcessIds.Add(item.Id);

        HashSet<string> excluded = BuildExcludedHistoryProcessIds(sessionProcesses);

        List<ProcessDashboardItem> observedExternalProcesses = _dataService.LoadObservedProcesses(excluded)
            .Where(item => item.IsRunningLike)
            .Where(item => !sessionProcesses.Any(session => SameLogPath(session.LogPath, item.LogPath)))
            .ToList();

        List<ProcessDashboardItem> historyProcesses = _dataService.LoadHistoryProcesses(_historyVisibleCount, _showAllHistory, excluded);

        return sessionProcesses
            .Concat(observedExternalProcesses)
            .Concat(historyProcesses)
            .OrderByDescending(item => item.IsRunningLike)
            .ThenByDescending(item => item.LastUpdatedAt)
            .ToList();
    }

    private List<ProcessDashboardItem> ApplyFilter(List<ProcessDashboardItem> items)
    {
        IEnumerable<ProcessDashboardItem> query = items;

        query = _activeFilter switch
        {
            "active" => query.Where(item => item.IsRunningLike),
            "finished" => query.Where(item => !item.IsRunningLike),
            "success" => query.Where(item => item.StateKind == DashboardStateKind.Success),
            "errors" => query.Where(item => item.StateKind == DashboardStateKind.Error),
            "stopped" => query.Where(item => item.IsStopped),
            "history" => query.Where(item => item.IsHistoryItem),
            _ => query
        };

        return query.ToList();
    }

    private HashSet<string> BuildExcludedHistoryProcessIds(IEnumerable<ProcessDashboardItem> sessionProcesses)
    {
        HashSet<string> excluded = new(_sessionProcessIds, StringComparer.OrdinalIgnoreCase);

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

    private ProcessDashboardItem CreateEmptyFilterItem()
    {
        return new ProcessDashboardItem
        {
            Name = "Процессы не найдены",
            StatusText = "пусто",
            TypeText = "фильтр",
            TimeText = "",
            Details = "Для выбранного фильтра нет процессов. Можно запустить парсинг, анализ или сценарий, либо сменить фильтр.",
            CanOpenDetails = false,
            CanOpenLog = false,
            StateKind = DashboardStateKind.Neutral
        };
    }

    private string FormatFilterText(string filter, int visibleCount)
    {
        string name = filter switch
        {
            "active" => "Активные процессы",
            "finished" => "Завершённые процессы",
            "success" => "Успешные процессы",
            "errors" => "Ошибки",
            "stopped" => "Остановленные процессы",
            "history" => "История по логам",
            _ => "Все процессы"
        };

        return $"Фильтр: {name} · показано: {visibleCount}";
    }

    private string BuildProcessInfo(ProcessDashboardItem process)
    {
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

        return string.Join(Environment.NewLine, lines);
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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
