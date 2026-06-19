using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using AutoScope.WpfClient.Models;
using AutoScope.WpfClient.Services;

namespace AutoScope.WpfClient;

public partial class ScenarioCreateWindow : Window, INotifyPropertyChanged
{
    private readonly ScenarioManagementService _scenarioService;
    private bool _isLoading;
    private string _scenarioName = "Новый сценарий";
    private bool _enabled = true;
    private bool _isManualOnly;
    private string _everyHoursText = "24";
    private ScenarioPipelineOption? _selectedPipelineOption;
    private DatabaseDashboardItem? _selectedDatabase;
    private ParserLaunchItem? _selectedParser;
    private AnalyzerLaunchItem? _selectedAnalyzer;
    private string _startUrl = "";
    private string _maxCarsText = "20";
    private string _streamBatchSizeText = "5";
    private bool _latestOnly = true;
    private bool _onlyChanged;
    private string _brand = "";
    private string _model = "";
    private string _saleRegion = "";
    private string _yearFromText = "";
    private string _yearToText = "";
    private string _statusMessage = "Готово к созданию сценария.";

    public ObservableCollection<ScenarioPipelineOption> PipelineOptions { get; } = new();
    public ObservableCollection<DatabaseDashboardItem> Databases { get; } = new();
    public ObservableCollection<ParserLaunchItem> Parsers { get; } = new();
    public ObservableCollection<AnalyzerLaunchItem> Analyzers { get; } = new();

    public string ResultMessage { get; private set; } = "";

    public string ScenarioName
    {
        get => _scenarioName;
        set { _scenarioName = value; OnPropertyChanged(); NotifySummaryChanged(); }
    }

    public bool Enabled
    {
        get => _enabled;
        set { _enabled = value; OnPropertyChanged(); NotifySummaryChanged(); }
    }

    public bool IsManualOnly
    {
        get => _isManualOnly;
        set
        {
            _isManualOnly = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsIntervalEnabled));
            NotifySummaryChanged();
        }
    }

    public bool IsIntervalEnabled => !IsManualOnly;

    public string EveryHoursText
    {
        get => _everyHoursText;
        set { _everyHoursText = value; OnPropertyChanged(); NotifySummaryChanged(); }
    }

    public ScenarioPipelineOption? SelectedPipelineOption
    {
        get => _selectedPipelineOption;
        set
        {
            _selectedPipelineOption = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(InputSettingsVisibility));
            OnPropertyChanged(nameof(OutputSettingsVisibility));
            NotifySummaryChanged();
        }
    }

    public DatabaseDashboardItem? SelectedDatabase
    {
        get => _selectedDatabase;
        set { _selectedDatabase = value; OnPropertyChanged(); NotifySummaryChanged(); }
    }

    public ParserLaunchItem? SelectedParser
    {
        get => _selectedParser;
        set
        {
            _selectedParser = value;
            ApplyParserDefaults(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedParserDescription));
            NotifySummaryChanged();
        }
    }

    public AnalyzerLaunchItem? SelectedAnalyzer
    {
        get => _selectedAnalyzer;
        set
        {
            _selectedAnalyzer = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedAnalyzerDescription));
            NotifySummaryChanged();
        }
    }

    public string StartUrl
    {
        get => _startUrl;
        set { _startUrl = value; OnPropertyChanged(); NotifySummaryChanged(); }
    }

    public string MaxCarsText
    {
        get => _maxCarsText;
        set { _maxCarsText = value; OnPropertyChanged(); NotifySummaryChanged(); }
    }

    public string StreamBatchSizeText
    {
        get => _streamBatchSizeText;
        set { _streamBatchSizeText = value; OnPropertyChanged(); NotifySummaryChanged(); }
    }

    public bool LatestOnly
    {
        get => _latestOnly;
        set { _latestOnly = value; OnPropertyChanged(); NotifySummaryChanged(); }
    }

    public bool OnlyChanged
    {
        get => _onlyChanged;
        set { _onlyChanged = value; OnPropertyChanged(); NotifySummaryChanged(); }
    }

    public string Brand
    {
        get => _brand;
        set { _brand = value; OnPropertyChanged(); NotifySummaryChanged(); }
    }

    public string Model
    {
        get => _model;
        set { _model = value; OnPropertyChanged(); NotifySummaryChanged(); }
    }

    public string SaleRegion
    {
        get => _saleRegion;
        set { _saleRegion = value; OnPropertyChanged(); NotifySummaryChanged(); }
    }

    public string YearFromText
    {
        get => _yearFromText;
        set { _yearFromText = value; OnPropertyChanged(); NotifySummaryChanged(); }
    }

    public string YearToText
    {
        get => _yearToText;
        set { _yearToText = value; OnPropertyChanged(); NotifySummaryChanged(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public string SelectedParserDescription => string.IsNullOrWhiteSpace(SelectedParser?.Description)
        ? "Описание парсера не задано."
        : SelectedParser.Description;

    public string SelectedAnalyzerDescription => string.IsNullOrWhiteSpace(SelectedAnalyzer?.Description)
        ? "Описание анализатора не задано."
        : SelectedAnalyzer.Description;

    public Visibility InputSettingsVisibility => IsInputSelected ? Visibility.Visible : Visibility.Collapsed;
    public Visibility OutputSettingsVisibility => IsInputSelected ? Visibility.Collapsed : Visibility.Visible;

    public string SummaryText
    {
        get
        {
            string type = SelectedPipelineOption?.DisplayName ?? "тип не выбран";
            string database = SelectedDatabase?.LaunchDisplayName ?? "база не выбрана";
            string module = IsInputSelected
                ? SelectedParser?.DisplayName ?? "парсер не выбран"
                : SelectedAnalyzer?.DisplayName ?? "анализатор не выбран";
            string schedule = IsManualOnly ? "только ручной запуск" : $"каждые {EveryHoursText} ч";
            string state = Enabled ? "включён" : "выключен";

            if (IsInputSelected)
                return $"{ScenarioName} · {type} · {module} · {database} · {schedule} · {state} · максимум {MaxCarsText}, пакет {StreamBatchSizeText}";

            string filters = BuildOutputFiltersSummary();
            return $"{ScenarioName} · {type} · {module} · {database} · {schedule} · {state}{filters}";
        }
    }

    private bool IsInputSelected => string.Equals(SelectedPipelineOption?.Key, "input", StringComparison.OrdinalIgnoreCase);

    public ScenarioCreateWindow(ScenarioManagementService scenarioService)
    {
        InitializeComponent();
        _scenarioService = scenarioService;

        DataContext = this;
        LoadInitialData();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void LoadInitialData()
    {
        _isLoading = true;

        PipelineOptions.Add(new ScenarioPipelineOption("input", "Парсинг"));
        PipelineOptions.Add(new ScenarioPipelineOption("output", "Анализ"));
        SelectedPipelineOption = PipelineOptions[0];

        foreach (DatabaseDashboardItem item in _scenarioService.LoadDatabasesForCreation())
            Databases.Add(item);
        SelectedDatabase = Databases.Count > 0 ? Databases[0] : null;

        foreach (ParserLaunchItem item in _scenarioService.LoadParsersForCreation())
            Parsers.Add(item);
        SelectedParser = Parsers.Count > 0 ? Parsers[0] : null;

        foreach (AnalyzerLaunchItem item in _scenarioService.LoadAnalyzersForCreation())
            Analyzers.Add(item);
        SelectedAnalyzer = Analyzers.Count > 0 ? Analyzers[0] : null;

        OutputPipelineLaunchSettings outputDefaults = _scenarioService.LoadOutputDefaultsForCreation();
        LatestOnly = outputDefaults.LatestOnly;
        OnlyChanged = outputDefaults.OnlyChanged;
        Brand = outputDefaults.Brand;
        Model = outputDefaults.Model;
        SaleRegion = outputDefaults.SaleRegion;
        YearFromText = outputDefaults.YearFrom?.ToString() ?? "";
        YearToText = outputDefaults.YearTo?.ToString() ?? "";

        _isLoading = false;
        ApplyParserDefaults(SelectedParser);
        NotifySummaryChanged();
    }

    private void ApplyParserDefaults(ParserLaunchItem? parser)
    {
        if (_isLoading || parser == null)
            return;

        if (string.IsNullOrWhiteSpace(StartUrl))
            StartUrl = parser.Settings.StartUrl;

        MaxCarsText = Math.Max(1, parser.Settings.MaxCars).ToString();
        StreamBatchSizeText = Math.Max(1, parser.Settings.StreamBatchSize).ToString();
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadDraft(out ScenarioCreateDraft? draft))
            return;

        ScenarioOperationResult result = _scenarioService.CreateScenario(draft!);
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

    private bool TryReadDraft(out ScenarioCreateDraft? draft)
    {
        draft = null;

        int everyHours = 0;
        if (!IsManualOnly && !int.TryParse((EveryHoursText ?? "").Trim(), out everyHours))
        {
            StatusMessage = "Интервал должен быть целым числом.";
            return false;
        }

        if (!int.TryParse((MaxCarsText ?? "").Trim(), out int maxCars))
        {
            StatusMessage = "Максимум объявлений должен быть целым числом.";
            return false;
        }

        if (!int.TryParse((StreamBatchSizeText ?? "").Trim(), out int streamBatchSize))
        {
            StatusMessage = "Размер пакета должен быть целым числом.";
            return false;
        }

        if (!TryParseNullableInt(YearFromText, out int? yearFrom))
        {
            StatusMessage = "Год от должен быть целым числом или пустым.";
            return false;
        }

        if (!TryParseNullableInt(YearToText, out int? yearTo))
        {
            StatusMessage = "Год до должен быть целым числом или пустым.";
            return false;
        }

        draft = new ScenarioCreateDraft
        {
            Name = ScenarioName,
            Enabled = Enabled,
            IsManualOnly = IsManualOnly,
            EveryHours = everyHours,
            PipelineType = SelectedPipelineOption?.Key ?? "input",
            Database = SelectedDatabase,
            Parser = SelectedParser,
            Analyzer = SelectedAnalyzer,
            StartUrl = StartUrl,
            MaxCars = maxCars,
            StreamBatchSize = streamBatchSize,
            LatestOnly = LatestOnly,
            OnlyChanged = OnlyChanged,
            Brand = Brand,
            Model = Model,
            SaleRegion = SaleRegion,
            YearFrom = yearFrom,
            YearTo = yearTo
        };

        return true;
    }

    private bool TryParseNullableInt(string value, out int? result)
    {
        result = null;
        value = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value))
            return true;

        if (int.TryParse(value, out int parsed))
        {
            result = parsed;
            return true;
        }

        return false;
    }

    private string BuildOutputFiltersSummary()
    {
        string filters = "";
        if (LatestOnly)
            filters += " · последние снимки";
        if (OnlyChanged)
            filters += " · только изменённые";
        if (!string.IsNullOrWhiteSpace(Brand))
            filters += $" · марка: {Brand.Trim()}";
        if (!string.IsNullOrWhiteSpace(Model))
            filters += $" · модель: {Model.Trim()}";
        if (!string.IsNullOrWhiteSpace(SaleRegion))
            filters += $" · регион: {SaleRegion.Trim()}";
        if (!string.IsNullOrWhiteSpace(YearFromText) || !string.IsNullOrWhiteSpace(YearToText))
            filters += $" · годы: {ValueOrAny(YearFromText)}–{ValueOrAny(YearToText)}";
        return filters;
    }

    private string ValueOrAny(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "любой" : value.Trim();
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

    private void NotifySummaryChanged()
    {
        if (!_isLoading)
            OnPropertyChanged(nameof(SummaryText));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class ScenarioPipelineOption
{
    public string Key { get; }
    public string DisplayName { get; }

    public ScenarioPipelineOption(string key, string displayName)
    {
        Key = key;
        DisplayName = displayName;
    }

    public override string ToString()
    {
        return DisplayName;
    }
}
