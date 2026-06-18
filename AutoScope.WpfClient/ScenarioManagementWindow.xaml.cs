using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using AutoScope.WpfClient.Models;
using AutoScope.WpfClient.Services;

namespace AutoScope.WpfClient;

public partial class ScenarioManagementWindow : Window, INotifyPropertyChanged
{
    private readonly ScenarioManagementService _scenarioService;
    private string _statusMessage = "";
    private string _scenarioCountText = "";

    public ObservableCollection<ScenarioDashboardItem> Scenarios { get; } = new();

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public string ScenarioCountText
    {
        get => _scenarioCountText;
        set { _scenarioCountText = value; OnPropertyChanged(); }
    }

    public ScenarioManagementWindow(string rootPath)
    {
        InitializeComponent();
        DataContext = this;

        _scenarioService = new ScenarioManagementService(rootPath);
        ReloadScenarios();
    }

    public event PropertyChangedEventHandler? PropertyChanged;


    private void CreateScenario_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ScenarioCreateWindow window = new ScenarioCreateWindow(_scenarioService)
            {
                Owner = this
            };

            bool? result = window.ShowDialog();
            if (result == true)
            {
                StatusMessage = window.ResultMessage;
                ReloadScenarios();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось открыть создание сценария: {ex.Message}", "AutoScope", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        ReloadScenarios();
        StatusMessage = $"Список сценариев обновлён: {DateTime.Now:HH:mm:ss}.";
    }

    private void OpenJobsFolder_Click(object sender, RoutedEventArgs e)
    {
        _scenarioService.OpenJobsFolder();
    }

    private void Details_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetScenario(sender, out ScenarioDashboardItem scenario))
            return;

        MessageBox.Show(
            _scenarioService.BuildScenarioDetailsText(scenario),
            "Сценарий",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void ToggleScenario_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetScenario(sender, out ScenarioDashboardItem scenario))
            return;

        ScenarioOperationResult result = _scenarioService.ToggleScenario(scenario);
        StatusMessage = result.Message;

        if (!result.Success)
        {
            MessageBox.Show(result.Message, "AutoScope", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ReloadScenarios();
    }

    private void RunScenario_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetScenario(sender, out ScenarioDashboardItem scenario))
            return;

        if (!scenario.Enabled)
        {
            MessageBoxResult answer = MessageBox.Show(
                $"Сценарий «{scenario.Name}» выключен. Запустить его вручную всё равно?",
                "Ручной запуск сценария",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes)
                return;
        }

        ScenarioOperationResult result = _scenarioService.StartScenarioNow(scenario);
        StatusMessage = result.Message;

        if (!result.Success)
        {
            MessageBox.Show(result.Message, "AutoScope", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MessageBox.Show(
            result.Message + "\n\nПроцесс появится в логах и блоке процессов после обновления хаба.",
            "Сценарий запущен",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void EditScenario_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetScenario(sender, out ScenarioDashboardItem scenario))
            return;

        try
        {
            ScenarioEditDraft draft = _scenarioService.LoadEditDraft(scenario);
            ScenarioEditWindow window = new ScenarioEditWindow(_scenarioService, draft)
            {
                Owner = this
            };

            bool? result = window.ShowDialog();
            if (result == true)
            {
                StatusMessage = window.ResultMessage;
                ReloadScenarios();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось открыть редактирование сценария: {ex.Message}", "AutoScope", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void History_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetScenario(sender, out ScenarioDashboardItem scenario))
            return;

        MessageBox.Show(
            _scenarioService.BuildScenarioHistoryText(scenario),
            "История сценария",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void OpenScenarioFile_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetScenario(sender, out ScenarioDashboardItem scenario))
            return;

        ScenarioOperationResult result = _scenarioService.OpenScenarioFile(scenario);
        StatusMessage = result.Message;

        if (!result.Success)
            MessageBox.Show(result.Message, "AutoScope", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void DeleteScenario_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetScenario(sender, out ScenarioDashboardItem scenario))
            return;

        MessageBoxResult answer = MessageBox.Show(
            $"Удалить сценарий «{scenario.Name}»? Это действие нельзя отменить.",
            "Удаление сценария",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
            return;

        ScenarioOperationResult result = _scenarioService.DeleteScenario(scenario);
        StatusMessage = result.Message;

        if (!result.Success)
        {
            MessageBox.Show(result.Message, "AutoScope", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ReloadScenarios();
    }

    private bool TryGetScenario(object sender, out ScenarioDashboardItem scenario)
    {
        scenario = new ScenarioDashboardItem();
        if (sender is not FrameworkElement element || element.Tag is not ScenarioDashboardItem selected)
            return false;

        scenario = selected;
        return true;
    }

    private void ReloadScenarios()
    {
        Scenarios.Clear();
        foreach (ScenarioDashboardItem item in _scenarioService.LoadScenarios())
            Scenarios.Add(item);

        if (Scenarios.Count == 0)
        {
            Scenarios.Add(new ScenarioDashboardItem
            {
                Name = "Сценариев пока нет",
                StatusText = "пусто",
                Details = "Нажми «Создать сценарий» в верхней панели или открой папку Jobs.",
                ScheduleText = "—",
                NextRunText = "—",
                FileName = "Папка Jobs пуста",
                ToggleActionText = "—",
                CanOpenFile = false,
                CanShowHistory = false,
                StateKind = DashboardStateKind.Neutral
            });
        }

        int realCount = 0;
        foreach (ScenarioDashboardItem item in Scenarios)
        {
            if (item.CanOpenFile)
                realCount++;
        }

        ScenarioCountText = realCount == 0 ? "нет сценариев" : $"{realCount} сценариев";
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
