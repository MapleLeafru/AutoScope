using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using AutoScope.WpfClient.Models;
using AutoScope.WpfClient.Services;

namespace AutoScope.WpfClient;

public partial class ScenarioEditWindow : Window, INotifyPropertyChanged
{
    private readonly ScenarioManagementService _scenarioService;
    private readonly ScenarioEditDraft _sourceDraft;
    private bool _isLoading;
    private string _name = "";
    private bool _enabled = true;
    private bool _isManualOnly;
    private string _everyHoursText = "24";
    private ScenarioPipelineOption? _selectedPipelineOption;
    private DatabaseDashboardItem? _selectedDatabase;
    private ParserLaunchItem? _selectedParser;
    private AnalyzerLaunchItem? _selectedAnalyzer;
    private string _startUrl = "";
    private string _maxCarsText = "20";
    private string _streamBatchSizeText = "";
    private string _requestDelaySecondsText = "";
    private string _retryCountText = "";
    private string _rateLimitDelaySecondsText = "";
    private BooleanChoiceOption? _selectedBrandCountryEnrichment;
    private BooleanChoiceOption? _selectedTransmissionNormalization;
    private BooleanChoiceOption? _selectedDriveTypeNormalization;
    private BooleanChoiceOption? _selectedFuelTypeNormalization;
    private bool _latestOnly = true;
    private bool _onlyChanged;
    private string _brand = "";
    private string _model = "";
    private string _saleRegion = "";
    private string _yearFromText = "";
    private string _yearToText = "";
    private string _statusMessage = "Готово к сохранению сценария.";

    public ObservableCollection<ScenarioPipelineOption> PipelineOptions { get; } = new();
    public ObservableCollection<DatabaseDashboardItem> Databases { get; } = new();
    public ObservableCollection<ParserLaunchItem> Parsers { get; } = new();
    public ObservableCollection<AnalyzerLaunchItem> Analyzers { get; } = new();
    public ObservableCollection<BooleanChoiceOption> BrandCountryOptions { get; } = new();
    public ObservableCollection<BooleanChoiceOption> TransmissionOptions { get; } = new();
    public ObservableCollection<BooleanChoiceOption> DriveTypeOptions { get; } = new();
    public ObservableCollection<BooleanChoiceOption> FuelTypeOptions { get; } = new();

    public string ResultMessage { get; private set; } = "";

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); NotifySummaryChanged(); }
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

    public string RequestDelaySecondsText
    {
        get => _requestDelaySecondsText;
        set { _requestDelaySecondsText = value; OnPropertyChanged(); NotifySummaryChanged(); }
    }

    public string RetryCountText
    {
        get => _retryCountText;
        set { _retryCountText = value; OnPropertyChanged(); NotifySummaryChanged(); }
    }

    public string RateLimitDelaySecondsText
    {
        get => _rateLimitDelaySecondsText;
        set { _rateLimitDelaySecondsText = value; OnPropertyChanged(); NotifySummaryChanged(); }
    }

    public BooleanChoiceOption? SelectedBrandCountryEnrichment
    {
        get => _selectedBrandCountryEnrichment;
        set { _selectedBrandCountryEnrichment = value; OnPropertyChanged(); NotifySummaryChanged(); }
    }

    public BooleanChoiceOption? SelectedTransmissionNormalization
    {
        get => _selectedTransmissionNormalization;
        set { _selectedTransmissionNormalization = value; OnPropertyChanged(); NotifySummaryChanged(); }
    }

    public BooleanChoiceOption? SelectedDriveTypeNormalization
    {
        get => _selectedDriveTypeNormalization;
        set { _selectedDriveTypeNormalization = value; OnPropertyChanged(); NotifySummaryChanged(); }
    }

    public BooleanChoiceOption? SelectedFuelTypeNormalization
    {
        get => _selectedFuelTypeNormalization;
        set { _selectedFuelTypeNormalization = value; OnPropertyChanged(); NotifySummaryChanged(); }
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

    public string FileName => _sourceDraft.FileName;

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
                return $"{Name} · {type} · {module} · {database} · {schedule} · {state} · максимум {ValueOrConfig(MaxCarsText)}, пакет {ValueOrConfig(StreamBatchSizeText)}, HTTP {ValueOrConfig(RequestDelaySecondsText)} сек., повторы {ValueOrConfig(RetryCountText)}, 429 {ValueOrConfig(RateLimitDelaySecondsText)} сек.";

            string filters = BuildOutputFiltersSummary();
            return $"{Name} · {type} · {module} · {database} · {schedule} · {state}{filters}";
        }
    }

    private bool IsInputSelected => string.Equals(SelectedPipelineOption?.Key, "input", StringComparison.OrdinalIgnoreCase);

    public ScenarioEditWindow(ScenarioManagementService scenarioService, ScenarioEditDraft draft)
    {
        InitializeComponent();
        _scenarioService = scenarioService;
        _sourceDraft = draft;

        DataContext = this;
        LoadInitialData();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void LoadInitialData()
    {
        _isLoading = true;

        PipelineOptions.Add(new ScenarioPipelineOption("input", "Парсинг"));
        PipelineOptions.Add(new ScenarioPipelineOption("output", "Анализ"));

        foreach (DatabaseDashboardItem item in _scenarioService.LoadDatabasesForCreation())
            Databases.Add(item);

        foreach (ParserLaunchItem item in _scenarioService.LoadParsersForCreation())
            Parsers.Add(item);

        foreach (AnalyzerLaunchItem item in _scenarioService.LoadAnalyzersForCreation())
            Analyzers.Add(item);

        Name = _sourceDraft.Name;
        Enabled = _sourceDraft.Enabled;
        IsManualOnly = _sourceDraft.IsManualOnly;
        EveryHoursText = Math.Max(1, _sourceDraft.EveryHours).ToString();
        SelectedPipelineOption = PipelineOptions.FirstOrDefault(item => string.Equals(item.Key, _sourceDraft.PipelineType, StringComparison.OrdinalIgnoreCase)) ?? PipelineOptions[0];
        SelectedDatabase = FindDatabase(_sourceDraft.DatabasePath) ?? Databases.FirstOrDefault();
        SelectedParser = FindParser(_sourceDraft.ParserPath) ?? Parsers.FirstOrDefault();
        SelectedAnalyzer = FindAnalyzer(_sourceDraft.AnalyzerPath) ?? Analyzers.FirstOrDefault();

        StartUrl = _sourceDraft.StartUrl;
        MaxCarsText = _sourceDraft.MaxCars.ToString(CultureInfo.InvariantCulture);
        StreamBatchSizeText = Math.Max(1, _sourceDraft.StreamBatchSize).ToString(CultureInfo.InvariantCulture);
        RequestDelaySecondsText = _sourceDraft.RequestDelaySeconds.ToString("0.###", CultureInfo.InvariantCulture);
        RetryCountText = _sourceDraft.RetryCount.ToString(CultureInfo.InvariantCulture);
        RateLimitDelaySecondsText = _sourceDraft.RateLimitDelaySeconds.ToString("0.###", CultureInfo.InvariantCulture);
        ResetApiChoices(new InputApiLaunchSettings
        {
            BrandCountryEnrichment = _sourceDraft.BrandCountryEnrichment,
            TransmissionNormalization = _sourceDraft.TransmissionNormalization,
            DriveTypeNormalization = _sourceDraft.DriveTypeNormalization,
            FuelTypeNormalization = _sourceDraft.FuelTypeNormalization
        });
        SelectExplicitApiChoices(_sourceDraft);
        LatestOnly = _sourceDraft.LatestOnly;
        OnlyChanged = _sourceDraft.OnlyChanged;
        Brand = _sourceDraft.Brand;
        Model = _sourceDraft.Model;
        SaleRegion = _sourceDraft.SaleRegion;
        YearFromText = _sourceDraft.YearFrom?.ToString() ?? "";
        YearToText = _sourceDraft.YearTo?.ToString() ?? "";

        _isLoading = false;
        NotifySummaryChanged();
    }

    private DatabaseDashboardItem? FindDatabase(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        return Databases.FirstOrDefault(item => SamePath(item.Path, path));
    }

    private ParserLaunchItem? FindParser(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        return Parsers.FirstOrDefault(item => SamePath(item.Path, path));
    }

    private AnalyzerLaunchItem? FindAnalyzer(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        return Analyzers.FirstOrDefault(item => SamePath(item.Path, path));
    }

    private bool SamePath(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void ApplyParserDefaults(ParserLaunchItem? parser)
    {
        if (_isLoading || parser == null)
            return;

        if (string.IsNullOrWhiteSpace(StartUrl))
            StartUrl = parser.Settings.StartUrl;

        MaxCarsText = parser.Settings.MaxCars.ToString(CultureInfo.InvariantCulture);
        StreamBatchSizeText = Math.Max(1, parser.Settings.StreamBatchSize).ToString(CultureInfo.InvariantCulture);
        RequestDelaySecondsText = parser.Settings.RequestDelaySeconds.ToString("0.###", CultureInfo.InvariantCulture);
        RetryCountText = parser.Settings.RetryCount.ToString(CultureInfo.InvariantCulture);
        RateLimitDelaySecondsText = parser.Settings.RateLimitDelaySeconds.ToString("0.###", CultureInfo.InvariantCulture);
        ResetApiChoices(parser.Settings.ApiSettings);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadDraft(out ScenarioEditDraft? draft))
            return;

        ScenarioOperationResult result = _scenarioService.SaveEditDraft(draft!);
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

    private bool TryReadDraft(out ScenarioEditDraft? draft)
    {
        draft = null;

        int everyHours = 0;
        if (!IsManualOnly && !int.TryParse((EveryHoursText ?? "").Trim(), out everyHours))
        {
            StatusMessage = "Интервал должен быть целым числом.";
            return false;
        }

        int maxCars = ParseIntWithDefault(MaxCarsText, SelectedParser?.Settings.MaxCars ?? 200);
        if (maxCars < 0)
        {
            StatusMessage = "MAX_CARS должен быть целым числом 0 или больше.";
            return false;
        }

        int streamBatchSize = ParseIntWithDefault(StreamBatchSizeText, SelectedParser?.Settings.StreamBatchSize ?? 100);
        if (streamBatchSize <= 0)
        {
            StatusMessage = "STREAM_BATCH_SIZE должен быть целым числом больше 0.";
            return false;
        }

        double requestDelaySeconds = ParseDoubleWithDefault(RequestDelaySecondsText, SelectedParser?.Settings.RequestDelaySeconds ?? 1.2);
        if (requestDelaySeconds < 0)
        {
            StatusMessage = "Задержка HTTP должна быть числом 0 или больше.";
            return false;
        }

        int retryCount = ParseIntWithDefault(RetryCountText, SelectedParser?.Settings.RetryCount ?? 3);
        if (retryCount < 0)
        {
            StatusMessage = "Количество повторов должно быть целым числом 0 или больше.";
            return false;
        }

        double rateLimitDelaySeconds = ParseDoubleWithDefault(RateLimitDelaySecondsText, SelectedParser?.Settings.RateLimitDelaySeconds ?? 5);
        if (rateLimitDelaySeconds < 0)
        {
            StatusMessage = "Пауза HTTP 429 должна быть числом 0 или больше.";
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

        draft = new ScenarioEditDraft
        {
            FilePath = _sourceDraft.FilePath,
            FileName = _sourceDraft.FileName,
            Name = Name,
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
            RequestDelaySeconds = requestDelaySeconds,
            RetryCount = retryCount,
            RateLimitDelaySeconds = rateLimitDelaySeconds,
            BrandCountryEnrichment = SelectedBrandCountryEnrichment?.Value ?? SelectedParser?.Settings.ApiSettings.BrandCountryEnrichment ?? true,
            TransmissionNormalization = SelectedTransmissionNormalization?.Value ?? SelectedParser?.Settings.ApiSettings.TransmissionNormalization ?? false,
            DriveTypeNormalization = SelectedDriveTypeNormalization?.Value ?? SelectedParser?.Settings.ApiSettings.DriveTypeNormalization ?? false,
            FuelTypeNormalization = SelectedFuelTypeNormalization?.Value ?? SelectedParser?.Settings.ApiSettings.FuelTypeNormalization ?? false,
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

    private int ParseIntWithDefault(string value, int fallback)
    {
        value = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : -1;
    }

    private double ParseDoubleWithDefault(string value, double fallback)
    {
        value = (value ?? "").Trim().Replace(",", ".");
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : -1;
    }

    private void ResetApiChoices(InputApiLaunchSettings settings)
    {
        FillChoiceOptions(BrandCountryOptions, settings.BrandCountryEnrichment);
        FillChoiceOptions(TransmissionOptions, settings.TransmissionNormalization);
        FillChoiceOptions(DriveTypeOptions, settings.DriveTypeNormalization);
        FillChoiceOptions(FuelTypeOptions, settings.FuelTypeNormalization);

        SelectedBrandCountryEnrichment = BrandCountryOptions.FirstOrDefault();
        SelectedTransmissionNormalization = TransmissionOptions.FirstOrDefault();
        SelectedDriveTypeNormalization = DriveTypeOptions.FirstOrDefault();
        SelectedFuelTypeNormalization = FuelTypeOptions.FirstOrDefault();
    }

    private void SelectExplicitApiChoices(ScenarioEditDraft draft)
    {
        SelectedBrandCountryEnrichment = BrandCountryOptions.FirstOrDefault(item => item.Value == draft.BrandCountryEnrichment) ?? BrandCountryOptions.FirstOrDefault();
        SelectedTransmissionNormalization = TransmissionOptions.FirstOrDefault(item => item.Value == draft.TransmissionNormalization) ?? TransmissionOptions.FirstOrDefault();
        SelectedDriveTypeNormalization = DriveTypeOptions.FirstOrDefault(item => item.Value == draft.DriveTypeNormalization) ?? DriveTypeOptions.FirstOrDefault();
        SelectedFuelTypeNormalization = FuelTypeOptions.FirstOrDefault(item => item.Value == draft.FuelTypeNormalization) ?? FuelTypeOptions.FirstOrDefault();
    }

    private void FillChoiceOptions(ObservableCollection<BooleanChoiceOption> target, bool defaultValue)
    {
        target.Clear();
        target.Add(new BooleanChoiceOption("default", $"По умолчанию ({FormatBool(defaultValue)})", null));
        target.Add(new BooleanChoiceOption("yes", "Включено", true));
        target.Add(new BooleanChoiceOption("no", "Выключено", false));
    }

    private string FormatBool(bool value)
    {
        return value ? "включено" : "выключено";
    }

    private string ValueOrConfig(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "из конфига" : value.Trim();
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
