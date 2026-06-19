using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutoScope.WpfClient.Models;
using AutoScope.WpfClient.Services;

namespace AutoScope.WpfClient;

public partial class OutputPipelineLaunchWindow : Window, INotifyPropertyChanged
{
    private readonly OutputPipelineLaunchService _launchService;
    private DatabaseDashboardItem? _selectedDatabase;
    private AnalyzerLaunchItem? _selectedAnalyzer;
    private bool _latestOnly = true;
    private bool _onlyChanged;
    private string _brand = "";
    private string _model = "";
    private string _saleRegion = "";
    private string _yearFromText = "";
    private string _yearToText = "";
    private string _statusMessage = "";

    public ObservableCollection<DatabaseDashboardItem> Databases { get; } = new();
    public ObservableCollection<AnalyzerLaunchItem> Analyzers { get; } = new();

    public bool RunStarted { get; private set; }
    public string ResultMessage { get; private set; } = "";

    public DatabaseDashboardItem? SelectedDatabase
    {
        get => _selectedDatabase;
        set { _selectedDatabase = value; OnPropertyChanged(); OnPropertyChanged(nameof(SummaryText)); }
    }

    public AnalyzerLaunchItem? SelectedAnalyzer
    {
        get => _selectedAnalyzer;
        set { _selectedAnalyzer = value; OnPropertyChanged(); OnPropertyChanged(nameof(SummaryText)); }
    }

    public bool LatestOnly
    {
        get => _latestOnly;
        set { _latestOnly = value; OnPropertyChanged(); OnPropertyChanged(nameof(SummaryText)); }
    }

    public bool OnlyChanged
    {
        get => _onlyChanged;
        set { _onlyChanged = value; OnPropertyChanged(); OnPropertyChanged(nameof(SummaryText)); }
    }

    public string Brand
    {
        get => _brand;
        set { _brand = value; OnPropertyChanged(); OnPropertyChanged(nameof(SummaryText)); }
    }

    public string Model
    {
        get => _model;
        set { _model = value; OnPropertyChanged(); OnPropertyChanged(nameof(SummaryText)); }
    }

    public string SaleRegion
    {
        get => _saleRegion;
        set { _saleRegion = value; OnPropertyChanged(); OnPropertyChanged(nameof(SummaryText)); }
    }

    public string YearFromText
    {
        get => _yearFromText;
        set { _yearFromText = value; OnPropertyChanged(); OnPropertyChanged(nameof(SummaryText)); }
    }

    public string YearToText
    {
        get => _yearToText;
        set { _yearToText = value; OnPropertyChanged(); OnPropertyChanged(nameof(SummaryText)); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public string SummaryText
    {
        get
        {
            string database = SelectedDatabase?.LaunchDisplayName ?? "база не выбрана";
            string analyzer = SelectedAnalyzer?.DisplayName ?? "анализатор не выбран";
            string latest = LatestOnly ? "только последние снимки" : "все снимки";
            string changed = OnlyChanged ? "только изменённые" : "все объявления";
            string filters = BuildFiltersSummary();

            return $"База: {database}\nАнализатор: {analyzer}\nВыборка: {latest}, {changed}\nФильтры: {filters}";
        }
    }

    public OutputPipelineLaunchWindow(string rootPath)
    {
        InitializeComponent();
        DataContext = this;

        _launchService = new OutputPipelineLaunchService(rootPath);
        LoadData();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void LoadData()
    {
        Databases.Clear();
        foreach (DatabaseDashboardItem item in _launchService.LoadDatabases())
            Databases.Add(item);

        Analyzers.Clear();
        foreach (AnalyzerLaunchItem item in _launchService.LoadAnalyzers())
            Analyzers.Add(item);

        ApplyDefaultSettings(_launchService.LoadDefaultSettings());

        SelectedDatabase = Databases.Count > 0 ? Databases[0] : null;
        SelectedAnalyzer = Analyzers.Count > 0 ? Analyzers[0] : null;

        if (Databases.Count == 0)
            StatusMessage = "Базы AutoScope не найдены. Создай или выбери базу перед формированием отчёта.";
        else if (Analyzers.Count == 0)
            StatusMessage = "Анализаторы не найдены в папке Analyzers.";
        else
            StatusMessage = "Готово к настройке отчёта.";
    }

    private void ApplyDefaultSettings(OutputPipelineLaunchSettings settings)
    {
        LatestOnly = settings.LatestOnly;
        OnlyChanged = settings.OnlyChanged;
        Brand = settings.Brand;
        Model = settings.Model;
        SaleRegion = settings.SaleRegion;
        YearFromText = settings.YearFrom?.ToString() ?? "";
        YearToText = settings.YearTo?.ToString() ?? "";
    }

    private void AnalyzerComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(SummaryText));
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (!Validate(out int? yearFrom, out int? yearTo))
            return;

        OutputPipelineLaunchSettings settings = new OutputPipelineLaunchSettings
        {
            LatestOnly = LatestOnly,
            OnlyChanged = OnlyChanged,
            Brand = Brand,
            Model = Model,
            SaleRegion = SaleRegion,
            YearFrom = yearFrom,
            YearTo = yearTo
        };

        OutputPipelineLaunchRequest request = new OutputPipelineLaunchRequest
        {
            DatabasePath = SelectedDatabase!.Path,
            Analyzer = SelectedAnalyzer!,
            Settings = settings
        };

        PipelineLaunchResult result = _launchService.StartOutputPipeline(request);
        StatusMessage = result.Message;
        ResultMessage = result.Message;
        RunStarted = result.Started;

        if (!result.Started)
        {
            MessageBox.Show(result.Message, "AutoScope", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }

    private bool Validate(out int? yearFrom, out int? yearTo)
    {
        yearFrom = null;
        yearTo = null;

        if (SelectedDatabase == null)
        {
            StatusMessage = "Выбери базу данных.";
            return false;
        }

        if (SelectedAnalyzer == null)
        {
            StatusMessage = "Выбери анализатор.";
            return false;
        }

        if (!TryReadOptionalYear(YearFromText, "Год от", out yearFrom))
            return false;

        if (!TryReadOptionalYear(YearToText, "Год до", out yearTo))
            return false;

        if (yearFrom.HasValue && yearTo.HasValue && yearFrom.Value > yearTo.Value)
        {
            StatusMessage = "Год от не может быть больше года до.";
            return false;
        }

        return true;
    }

    private bool TryReadOptionalYear(string raw, string fieldName, out int? year)
    {
        year = null;
        raw = raw.Trim();

        if (string.IsNullOrWhiteSpace(raw))
            return true;

        if (!int.TryParse(raw, out int parsed))
        {
            StatusMessage = $"{fieldName} должен быть целым числом.";
            return false;
        }

        if (parsed < 1900 || parsed > DateTime.Now.Year + 1)
        {
            StatusMessage = $"{fieldName} выглядит некорректно. Укажи год в разумном диапазоне.";
            return false;
        }

        year = parsed;
        return true;
    }

    private string BuildFiltersSummary()
    {
        ObservableCollection<string> parts = new ObservableCollection<string>();

        if (!string.IsNullOrWhiteSpace(Brand))
            parts.Add($"марка: {Brand.Trim()}");

        if (!string.IsNullOrWhiteSpace(Model))
            parts.Add($"модель: {Model.Trim()}");

        if (!string.IsNullOrWhiteSpace(SaleRegion))
            parts.Add($"регион: {SaleRegion.Trim()}");

        if (!string.IsNullOrWhiteSpace(YearFromText) || !string.IsNullOrWhiteSpace(YearToText))
        {
            string from = string.IsNullOrWhiteSpace(YearFromText) ? "…" : YearFromText.Trim();
            string to = string.IsNullOrWhiteSpace(YearToText) ? "…" : YearToText.Trim();
            parts.Add($"год: {from}–{to}");
        }

        return parts.Count == 0 ? "не применяются" : string.Join(", ", parts);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Cancel_Click(sender, e);
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
            // Окно могло уже потерять захват мыши. Для UI это не критично.
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
