using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using AutoScope.WpfClient.Models;
using AutoScope.WpfClient.Services;

namespace AutoScope.WpfClient;

public partial class ReportManagementWindow : Window, INotifyPropertyChanged
{
    private readonly ReportManagementService _reportService;
    private string _statusMessage = "";
    private string _reportsFolderText = "";

    public ObservableCollection<ReportDashboardItem> Reports { get; } = new();

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public string ReportsFolderText
    {
        get => _reportsFolderText;
        set { _reportsFolderText = value; OnPropertyChanged(); }
    }

    public ReportManagementWindow(string rootPath)
    {
        InitializeComponent();
        DataContext = this;

        _reportService = new ReportManagementService(rootPath);
        ReportsFolderText = $"Папка отчётов: {_reportService.GetReportsFolderPath()}";
        ReloadReports();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        ReloadReports();
        StatusMessage = $"Список отчётов обновлён: {DateTime.Now:HH:mm:ss}.";
    }

    private void OpenReportsFolder_Click(object sender, RoutedEventArgs e)
    {
        _reportService.OpenReportsFolder();
    }

    private void OpenReport_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not ReportDashboardItem report)
            return;

        try
        {
            _reportService.OpenReport(report);
            StatusMessage = $"Открыт отчёт: {report.FileName}.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Отчёт открыть не удалось.";
            MessageBox.Show(ex.Message, "Отчёт AutoScope", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenReportFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not ReportDashboardItem report)
            return;

        _reportService.OpenReportFolder(report);
    }

    private void DeleteReport_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not ReportDashboardItem report)
            return;

        MessageBoxResult answer = MessageBox.Show(
            $"Удалить отчёт {report.FileName}? Это действие нельзя отменить.",
            "Удаление отчёта",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
            return;

        ReportOperationResult result = _reportService.DeleteReport(report);
        StatusMessage = result.Message;

        if (!result.Success)
        {
            MessageBox.Show(result.Message, "Отчёт AutoScope", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ReloadReports();
    }

    private void ReloadReports()
    {
        Reports.Clear();
        foreach (ReportDashboardItem report in _reportService.LoadReports())
            Reports.Add(report);

        if (Reports.Count == 0)
        {
            Reports.Add(new ReportDashboardItem
            {
                Name = "Отчёты не найдены",
                FileName = "",
                AnalyzerText = "Сформируйте отчёт через OutputPipeline, и он появится в этом списке.",
                DateText = "Дата: —",
                SizeText = "Размер: —",
                Details = "",
                CanOpen = false
            });
        }
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
