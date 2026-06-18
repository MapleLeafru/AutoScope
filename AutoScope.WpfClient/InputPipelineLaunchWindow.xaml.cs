using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using AutoScope.WpfClient.Models;
using AutoScope.WpfClient.Services;

namespace AutoScope.WpfClient;

public partial class InputPipelineLaunchWindow : Window, INotifyPropertyChanged
{
    private readonly InputPipelineLaunchService _launchService;
    private DatabaseDashboardItem? _selectedDatabase;
    private ParserLaunchItem? _selectedParser;
    private string _startUrl = "";
    private string _maxCarsText = "20";
    private string _batchSizeText = "5";
    private string _statusMessage = "";

    public ObservableCollection<DatabaseDashboardItem> Databases { get; } = new();
    public ObservableCollection<ParserLaunchItem> Parsers { get; } = new();

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
            string maxCars = string.IsNullOrWhiteSpace(MaxCarsText) ? "не задано" : MaxCarsText;
            string batchSize = string.IsNullOrWhiteSpace(BatchSizeText) ? "не задано" : BatchSizeText;
            return $"База: {database}\nПарсер: {parser}\nМаксимум объявлений: {maxCars}\nРазмер пакета: {batchSize}";
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
        if (!Validate(out int maxCars, out int batchSize))
            return;

        InputPipelineLaunchRequest request = new InputPipelineLaunchRequest
        {
            DatabasePath = SelectedDatabase!.Path,
            Parser = SelectedParser!,
            StartUrl = StartUrl,
            MaxCars = maxCars,
            StreamBatchSize = batchSize
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

    private bool Validate(out int maxCars, out int batchSize)
    {
        maxCars = 0;
        batchSize = 0;

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

        if (!int.TryParse(MaxCarsText, out maxCars) || maxCars < 0)
        {
            StatusMessage = "Максимум объявлений должен быть целым числом 0 или больше.";
            return false;
        }

        if (!int.TryParse(BatchSizeText, out batchSize) || batchSize <= 0)
        {
            StatusMessage = "Размер пакета должен быть целым числом больше 0.";
            return false;
        }

        return true;
    }

    private void ApplyParserDefaults(ParserLaunchItem? parser)
    {
        if (parser == null)
            return;

        StartUrl = parser.Settings.StartUrl;
        MaxCarsText = parser.Settings.MaxCars.ToString();
        BatchSizeText = parser.Settings.StreamBatchSize.ToString();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
