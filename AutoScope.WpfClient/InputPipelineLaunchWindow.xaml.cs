using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutoScope.WpfClient.Models;
using AutoScope.WpfClient.Services;

namespace AutoScope.WpfClient;

public partial class InputPipelineLaunchWindow : Window, INotifyPropertyChanged
{
    private readonly InputPipelineLaunchService _launchService;
    private DatabaseDashboardItem? _selectedDatabase;
    private ParserLaunchItem? _selectedParser;
    private string _startUrl = "";
    private string _maxCarsText = "";
    private string _batchSizeText = "";
    private string _requestDelaySecondsText = "";
    private string _retryCountText = "";
    private string _rateLimitDelaySecondsText = "";
    private string _statusMessage = "";
    private BooleanChoiceOption? _selectedBrandCountryEnrichment;
    private BooleanChoiceOption? _selectedTransmissionNormalization;
    private BooleanChoiceOption? _selectedDriveTypeNormalization;
    private BooleanChoiceOption? _selectedFuelTypeNormalization;

    public ObservableCollection<DatabaseDashboardItem> Databases { get; } = new();
    public ObservableCollection<ParserLaunchItem> Parsers { get; } = new();
    public ObservableCollection<BooleanChoiceOption> BrandCountryOptions { get; } = new();
    public ObservableCollection<BooleanChoiceOption> TransmissionOptions { get; } = new();
    public ObservableCollection<BooleanChoiceOption> DriveTypeOptions { get; } = new();
    public ObservableCollection<BooleanChoiceOption> FuelTypeOptions { get; } = new();

    public bool RunStarted { get; private set; }
    public string ResultMessage { get; private set; } = "";

    public DatabaseDashboardItem? SelectedDatabase
    {
        get => _selectedDatabase;
        set { _selectedDatabase = value; OnPropertyChanged(); OnPropertyChanged(nameof(SummaryText)); }
    }

    public ParserLaunchItem? SelectedParser
    {
        get => _selectedParser;
        set
        {
            _selectedParser = value;
            ApplyParserDefaults(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(SummaryText));
        }
    }

    public string StartUrl
    {
        get => _startUrl;
        set { _startUrl = value; OnPropertyChanged(); OnPropertyChanged(nameof(SummaryText)); }
    }

    public string MaxCarsText
    {
        get => _maxCarsText;
        set { _maxCarsText = value; OnPropertyChanged(); OnPropertyChanged(nameof(SummaryText)); }
    }

    public string BatchSizeText
    {
        get => _batchSizeText;
        set { _batchSizeText = value; OnPropertyChanged(); OnPropertyChanged(nameof(SummaryText)); }
    }

    public string RequestDelaySecondsText
    {
        get => _requestDelaySecondsText;
        set { _requestDelaySecondsText = value; OnPropertyChanged(); OnPropertyChanged(nameof(SummaryText)); }
    }

    public string RetryCountText
    {
        get => _retryCountText;
        set { _retryCountText = value; OnPropertyChanged(); OnPropertyChanged(nameof(SummaryText)); }
    }

    public string RateLimitDelaySecondsText
    {
        get => _rateLimitDelaySecondsText;
        set { _rateLimitDelaySecondsText = value; OnPropertyChanged(); OnPropertyChanged(nameof(SummaryText)); }
    }

    public BooleanChoiceOption? SelectedBrandCountryEnrichment
    {
        get => _selectedBrandCountryEnrichment;
        set { _selectedBrandCountryEnrichment = value; OnPropertyChanged(); OnPropertyChanged(nameof(SummaryText)); }
    }

    public BooleanChoiceOption? SelectedTransmissionNormalization
    {
        get => _selectedTransmissionNormalization;
        set { _selectedTransmissionNormalization = value; OnPropertyChanged(); OnPropertyChanged(nameof(SummaryText)); }
    }

    public BooleanChoiceOption? SelectedDriveTypeNormalization
    {
        get => _selectedDriveTypeNormalization;
        set { _selectedDriveTypeNormalization = value; OnPropertyChanged(); OnPropertyChanged(nameof(SummaryText)); }
    }

    public BooleanChoiceOption? SelectedFuelTypeNormalization
    {
        get => _selectedFuelTypeNormalization;
        set { _selectedFuelTypeNormalization = value; OnPropertyChanged(); OnPropertyChanged(nameof(SummaryText)); }
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
            string parser = SelectedParser?.DisplayName ?? "парсер не выбран";
            string maxCars = FormatOptionalNumber(MaxCarsText, "из конфига");
            string batchSize = FormatOptionalNumber(BatchSizeText, "из конфига");
            string delay = FormatOptionalNumber(RequestDelaySecondsText, "из конфига");
            string retries = FormatOptionalNumber(RetryCountText, "из конфига");
            string rateLimit = FormatOptionalNumber(RateLimitDelaySecondsText, "из конфига");
            return $"База: {database}\nПарсер: {parser}\nМаксимум объявлений: {maxCars}\nРазмер пакета: {batchSize}\nЗадержка HTTP: {delay} сек. · Повторы: {retries} · Пауза 429: {rateLimit} сек.\nAPI: страна бренда — {FormatChoice(SelectedBrandCountryEnrichment)}, коробка — {FormatChoice(SelectedTransmissionNormalization)}, привод — {FormatChoice(SelectedDriveTypeNormalization)}, топливо — {FormatChoice(SelectedFuelTypeNormalization)}";
        }
    }

    public InputPipelineLaunchWindow(string rootPath)
    {
        InitializeComponent();
        DataContext = this;

        _launchService = new InputPipelineLaunchService(rootPath);
        LoadData();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void LoadData()
    {
        Databases.Clear();
        foreach (DatabaseDashboardItem item in _launchService.LoadDatabases())
            Databases.Add(item);

        Parsers.Clear();
        foreach (ParserLaunchItem item in _launchService.LoadParsers())
            Parsers.Add(item);

        SelectedDatabase = Databases.Count > 0 ? Databases[0] : null;
        SelectedParser = Parsers.Count > 0 ? Parsers[0] : null;

        if (Databases.Count == 0)
            StatusMessage = "Базы AutoScope не найдены. Создай или выбери базу перед запуском парсинга.";
        else if (Parsers.Count == 0)
            StatusMessage = "Парсеры не найдены в папке Parsers.";
        else
            StatusMessage = "Готово к настройке запуска.";
    }

    private void ParserComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(SummaryText));
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (!Validate(out int? maxCars, out int? batchSize, out double? requestDelaySeconds, out int? retryCount, out double? rateLimitDelaySeconds))
            return;

        InputPipelineLaunchRequest request = new InputPipelineLaunchRequest
        {
            DatabasePath = SelectedDatabase!.Path,
            Parser = SelectedParser!,
            StartUrl = StartUrl,
            MaxCars = maxCars,
            StreamBatchSize = batchSize,
            RequestDelaySeconds = requestDelaySeconds,
            RetryCount = retryCount,
            RateLimitDelaySeconds = rateLimitDelaySeconds,
            BrandCountryEnrichment = SelectedBrandCountryEnrichment?.Value,
            TransmissionNormalization = SelectedTransmissionNormalization?.Value,
            DriveTypeNormalization = SelectedDriveTypeNormalization?.Value,
            FuelTypeNormalization = SelectedFuelTypeNormalization?.Value
        };

        PipelineLaunchResult result = _launchService.StartInputPipeline(request);
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

    private bool Validate(out int? maxCars, out int? batchSize, out double? requestDelaySeconds, out int? retryCount, out double? rateLimitDelaySeconds)
    {
        maxCars = null;
        batchSize = null;
        requestDelaySeconds = null;
        retryCount = null;
        rateLimitDelaySeconds = null;

        if (SelectedDatabase == null)
        {
            StatusMessage = "Выбери базу данных.";
            return false;
        }

        if (SelectedParser == null)
        {
            StatusMessage = "Выбери парсер.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(StartUrl))
        {
            StatusMessage = "Укажи стартовый URL.";
            return false;
        }

        if (!TryParseOptionalInt(MaxCarsText, 0, "Максимум объявлений должен быть целым числом 0 или больше.", out maxCars))
            return false;

        if (!TryParseOptionalInt(BatchSizeText, 1, "Размер пакета должен быть целым числом больше 0.", out batchSize))
            return false;

        if (!TryParseOptionalDouble(RequestDelaySecondsText, 0, "Задержка между HTTP-запросами должна быть числом 0 или больше.", out requestDelaySeconds))
            return false;

        if (!TryParseOptionalInt(RetryCountText, 0, "Количество повторов должно быть целым числом 0 или больше.", out retryCount))
            return false;

        if (!TryParseOptionalDouble(RateLimitDelaySecondsText, 0, "Пауза при HTTP 429 должна быть числом 0 или больше.", out rateLimitDelaySeconds))
            return false;

        return true;
    }

    private bool TryParseOptionalInt(string text, int minimum, string errorMessage, out int? result)
    {
        result = null;
        text = (text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text))
            return true;

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed >= minimum)
        {
            result = parsed;
            return true;
        }

        StatusMessage = errorMessage;
        return false;
    }

    private bool TryParseOptionalDouble(string text, double minimum, string errorMessage, out double? result)
    {
        result = null;
        text = (text ?? "").Trim().Replace(",", ".");
        if (string.IsNullOrWhiteSpace(text))
            return true;

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) && parsed >= minimum)
        {
            result = parsed;
            return true;
        }

        StatusMessage = errorMessage;
        return false;
    }

    private void ApplyParserDefaults(ParserLaunchItem? parser)
    {
        if (parser == null)
            return;

        StartUrl = parser.Settings.StartUrl;
        MaxCarsText = parser.Settings.MaxCars.ToString(CultureInfo.InvariantCulture);
        BatchSizeText = parser.Settings.StreamBatchSize.ToString(CultureInfo.InvariantCulture);
        RequestDelaySecondsText = parser.Settings.RequestDelaySeconds.ToString("0.###", CultureInfo.InvariantCulture);
        RetryCountText = parser.Settings.RetryCount.ToString(CultureInfo.InvariantCulture);
        RateLimitDelaySecondsText = parser.Settings.RateLimitDelaySeconds.ToString("0.###", CultureInfo.InvariantCulture);

        ResetApiChoices(parser.Settings.ApiSettings);
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

    private void FillChoiceOptions(ObservableCollection<BooleanChoiceOption> target, bool defaultValue)
    {
        target.Clear();
        target.Add(new BooleanChoiceOption("default", $"По умолчанию ({FormatBool(defaultValue)})", null));
        target.Add(new BooleanChoiceOption("yes", "Включено", true));
        target.Add(new BooleanChoiceOption("no", "Выключено", false));
    }

    private string FormatOptionalNumber(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private string FormatChoice(BooleanChoiceOption? option)
    {
        return option?.DisplayName ?? "по умолчанию";
    }

    private string FormatBool(bool value)
    {
        return value ? "включено" : "выключено";
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
