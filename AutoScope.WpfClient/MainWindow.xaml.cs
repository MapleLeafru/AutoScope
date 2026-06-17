using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using AutoScope.WpfClient.Models;
using AutoScope.WpfClient.Services;

namespace AutoScope.WpfClient;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly DashboardDataService _dataService;
    private string _rootPathText = "";
    private string _statusMessage = "";

    public ObservableCollection<ScenarioDashboardItem> Scenarios { get; } = new();
    public ObservableCollection<DatabaseDashboardItem> Databases { get; } = new();
    public ObservableCollection<ProcessDashboardItem> Processes { get; } = new();

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

    private void OpenRootFolder_Click(object sender, RoutedEventArgs e)
    {
        _dataService.OpenFolder(_dataService.RootPath);
    }

    private void OpenReportsFolder_Click(object sender, RoutedEventArgs e)
    {
        _dataService.OpenFolder(_dataService.GetReportsFolderPath());
    }

    private void OpenLogsFolder_Click(object sender, RoutedEventArgs e)
    {
        _dataService.OpenFolder(_dataService.GetLogsFolderPath());
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

    private void Stub_Click(object sender, RoutedEventArgs e)
    {
        string message = "Этот экран будет добавлен следующим шагом.";
        if (sender is FrameworkElement element && element.Tag is string tag && !string.IsNullOrWhiteSpace(tag))
            message = tag;

        MessageBox.Show(message, "AutoScope UI", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ReloadDashboard()
    {
        Scenarios.Clear();
        foreach (ScenarioDashboardItem item in _dataService.LoadScenarios())
            Scenarios.Add(item);

        Databases.Clear();
        foreach (DatabaseDashboardItem item in _dataService.LoadDatabases())
            Databases.Add(item);

        Processes.Clear();
        foreach (ProcessDashboardItem item in _dataService.LoadInitialProcesses())
            Processes.Add(item);

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

        StatusMessage = $"Обновлено: {DateTime.Now:HH:mm:ss}. Сценариев: {Scenarios.Count}, баз: {Databases.Count}.";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
