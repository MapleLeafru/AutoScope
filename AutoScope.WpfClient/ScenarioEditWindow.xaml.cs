using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using AutoScope.WpfClient.Services;

namespace AutoScope.WpfClient;

public partial class ScenarioEditWindow : Window, INotifyPropertyChanged
{
    private readonly ScenarioManagementService _scenarioService;
    private readonly ScenarioEditDraft _draft;
    private string _name = "";
    private bool _enabled;
    private bool _isManualOnly;
    private string _everyHoursText = "24";
    private string _statusMessage = "";

    public string ResultMessage { get; private set; } = "";

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public bool Enabled
    {
        get => _enabled;
        set { _enabled = value; OnPropertyChanged(); }
    }

    public bool IsManualOnly
    {
        get => _isManualOnly;
        set { _isManualOnly = value; OnPropertyChanged(); }
    }

    public string EveryHoursText
    {
        get => _everyHoursText;
        set { _everyHoursText = value; OnPropertyChanged(); }
    }

    public string PipelineText => "Тип: " + ValueOrDash(_draft.PipelineText);
    public string DatabaseName => "База: " + ValueOrDash(_draft.DatabaseName);
    public string ModuleName => "Модуль: " + ValueOrDash(_draft.ModuleName);
    public string FileName => "Файл: " + ValueOrDash(_draft.FileName);

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public ScenarioEditWindow(ScenarioManagementService scenarioService, AutoScope.WpfClient.Models.ScenarioDashboardItem scenario)
    {
        InitializeComponent();

        _scenarioService = scenarioService;
        _draft = _scenarioService.LoadEditDraft(scenario);

        Name = _draft.Name;
        Enabled = _draft.Enabled;
        IsManualOnly = _draft.IsManualOnly;
        EveryHoursText = _draft.EveryHours.ToString();
        StatusMessage = "Готово к редактированию.";

        DataContext = this;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse((EveryHoursText ?? "").Trim(), out int everyHours))
        {
            StatusMessage = "Интервал должен быть целым числом.";
            return;
        }

        _draft.Name = Name;
        _draft.Enabled = Enabled;
        _draft.IsManualOnly = IsManualOnly;
        _draft.EveryHours = everyHours;

        ScenarioOperationResult result = _scenarioService.SaveEditDraft(_draft);
        StatusMessage = result.Message;

        if (!result.Success)
        {
            MessageBox.Show(result.Message, "AutoScope", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ResultMessage = result.Message;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
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

    private string ValueOrDash(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "—" : value;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
