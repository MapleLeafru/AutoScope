using System;
using System.IO;
using System.Text.Json;

// Отвечает за запуск InputPipeline и OutputPipeline из консольного клиента.
public class PipelineService
{
    private readonly AppPaths _paths;
    private readonly ConsoleInputService _input;
    private readonly DatabaseDiscoveryService _databaseDiscovery;
    private readonly ModuleDiscoveryService _moduleDiscovery;
    private readonly SettingsService _settingsService;
    private readonly PythonProcessService _pythonProcess;

    public PipelineService(
        AppPaths paths,
        ConsoleInputService input,
        DatabaseDiscoveryService databaseDiscovery,
        ModuleDiscoveryService moduleDiscovery,
        SettingsService settingsService,
        PythonProcessService pythonProcess
    )
    {
        _paths = paths;
        _input = input;
        _databaseDiscovery = databaseDiscovery;
        _moduleDiscovery = moduleDiscovery;
        _settingsService = settingsService;
        _pythonProcess = pythonProcess;
    }

    // Запускает цепочку парсинга: выбор базы, выбор парсера, ввод параметров, запуск Python-менеджера.
    public void StartInputPipeline()
    {
        Console.WriteLine("=== Запуск InputPipeline ===");

        string selectedDatabase = _databaseDiscovery.SelectDatabase(
            message: "Выберите базу данных для продолжения работы",
            cancelText: "Отменить запуск парсера"
        );
        if (string.IsNullOrWhiteSpace(selectedDatabase)) { return; }

        Console.WriteLine($"Выбрана база: {Path.GetFileName(selectedDatabase)}");
        Console.WriteLine();

        string selectedParser = _moduleDiscovery.SelectParser(
            message: "Выберите парсер для продолжения работы",
            cancelText: "Отменить запуск парсера"
        );
        if (string.IsNullOrWhiteSpace(selectedParser)) { return; }

        Console.WriteLine($"Выбран парсер: {Path.GetFileName(selectedParser)}");
        Console.WriteLine();

        ParserRunSettings parserSettings = _settingsService.ReadParserRunSettings();
        Console.WriteLine();

        ApiSettings apiSettings = _settingsService.ReadApiSettings();
        RuntimeSettings runtimeSettings = _settingsService.GetRuntimeSettings();

        bool confirmed = PrintInputRunSummaryAndConfirm(
            selectedDatabase,
            selectedParser,
            parserSettings,
            apiSettings,
            runtimeSettings
        );
        if (!confirmed) { return; }

        RunInputPipeline(selectedDatabase, selectedParser, parserSettings, apiSettings, runtimeSettings);
    }

    // Запускает цепочку анализа: выбор базы, анализатора, фильтров выборки и запуск Python-менеджера.
    public void StartOutputPipeline()
    {
        Console.WriteLine("=== Запуск OutputPipeline ===");

        string selectedDatabase = _databaseDiscovery.SelectDatabase(
            message: "Выберите базу данных для анализа",
            cancelText: "Отменить запуск анализатора"
        );
        if (string.IsNullOrWhiteSpace(selectedDatabase)) { return; }

        Console.WriteLine($"Выбрана база: {Path.GetFileName(selectedDatabase)}");
        Console.WriteLine();

        string selectedAnalyzer = _moduleDiscovery.SelectAnalyzer(
            message: "Выберите анализатор для продолжения работы",
            cancelText: "Отменить запуск анализатора"
        );
        if (string.IsNullOrWhiteSpace(selectedAnalyzer)) { return; }

        Console.WriteLine($"Выбран анализатор: {Path.GetFileName(selectedAnalyzer)}");
        Console.WriteLine();

        OutputFilterSettings outputSettings = _settingsService.ReadOutputFilterSettings();
        RuntimeSettings runtimeSettings = _settingsService.GetRuntimeSettings();

        RunOutputPipeline(selectedDatabase, selectedAnalyzer, outputSettings, runtimeSettings);
    }

    // Показывает итоговые параметры перед запуском парсера и просит подтверждение пользователя.
    private bool PrintInputRunSummaryAndConfirm(
        string databasePath,
        string parserPath,
        ParserRunSettings parserSettings,
        ApiSettings apiSettings,
        RuntimeSettings runtimeSettings
    )
    {
        Console.WriteLine("=== Параметры запуска парсера ===");
        Console.WriteLine($"База данных: {Path.GetFileName(databasePath)}");
        Console.WriteLine($"Парсер: {Path.GetFileName(parserPath)}");
        Console.WriteLine($"START_URL: {parserSettings.StartUrl}");
        Console.WriteLine($"MAX_CARS: {FormatMaxCars(parserSettings.MaxCars)}");
        Console.WriteLine($"STREAM_BATCH_SIZE: {parserSettings.StreamBatchSize}");
        Console.WriteLine($"Задержка между HTTP-запросами: {parserSettings.RequestDelaySeconds:0.##} сек.");
        Console.WriteLine($"Повторов при ошибке запроса: {parserSettings.RetryCount}");
        Console.WriteLine($"Пауза при HTTP 429: {parserSettings.RateLimitDelaySeconds:0.##} сек.");
        Console.WriteLine($"Python: {runtimeSettings.PythonPath}");
        Console.WriteLine();

        Console.WriteLine("Настройки API:");
        Console.WriteLine($"- определение страны бренда: {FormatEnabled(apiSettings.BrandCountryEnrichment)}");
        Console.WriteLine($"- нормализация коробки передач: {FormatEnabled(apiSettings.TransmissionNormalization)}");
        Console.WriteLine($"- нормализация типа привода: {FormatEnabled(apiSettings.DriveTypeNormalization)}");
        Console.WriteLine($"- нормализация типа топлива: {FormatEnabled(apiSettings.FuelTypeNormalization)}");
        Console.WriteLine();

        Console.WriteLine($"Примерное время: {EstimateParserRunTime(parserSettings)}");
        return _input.AskYesNo("Парсер готов к запуску. Запуск: y/n: ");
    }

    // Форматирует ограничение количества объявлений.
    private string FormatMaxCars(int maxCars)
    {
        return maxCars == 0 ? "все доступные объявления" : maxCars.ToString();
    }

    // Форматирует включённую или выключенную настройку.
    private string FormatEnabled(bool value)
    {
        return value ? "включено" : "выключено";
    }

    // Считает примерное время парсинга по количеству объявлений и задержке между запросами.
    private string EstimateParserRunTime(ParserRunSettings parserSettings)
    {
        if (parserSettings.MaxCars == 0)
            return "неизвестно, выбран сбор всех доступных объявлений";

        double secondsPerAd = parserSettings.RequestDelaySeconds + 0.6;
        int totalMinutes = (int)Math.Ceiling((parserSettings.MaxCars * secondsPerAd) / 60.0);

        if (totalMinutes < 1)
            totalMinutes = 1;

        int hours = totalMinutes / 60;
        int minutes = totalMinutes % 60;

        if (hours > 0 && minutes > 0)
            return $"примерно {hours}ч {minutes}м";

        if (hours > 0)
            return $"примерно {hours}ч";

        return $"примерно {minutes}м";
    }

    // Запускает InputPipeline по уже готовым параметрам. Используется обычным запуском и менеджером сценариев.
    public ProcessRunResult RunInputPipeline(
        string databasePath,
        string parserPath,
        ParserRunSettings parserSettings,
        ApiSettings apiSettings,
        RuntimeSettings runtimeSettings
    )
    {
        var request = new
        {
            parser = new
            {
                modulePath = parserPath,
                parserPath = parserPath,
                runtime = _moduleDiscovery.GetRuntimeNameByPath(parserPath),
                python = runtimeSettings.PythonPath,
                java = runtimeSettings.JavaPath
            },
            parserSettings = new
            {
                startUrl = parserSettings.StartUrl,
                maxCars = parserSettings.MaxCars,
                streamBatchSize = parserSettings.StreamBatchSize,
                requestDelaySeconds = parserSettings.RequestDelaySeconds,
                retryCount = parserSettings.RetryCount,
                rateLimitDelaySeconds = parserSettings.RateLimitDelaySeconds
            },
            apiSettings = new
            {
                brandCountryEnrichment = apiSettings.BrandCountryEnrichment,
                transmissionNormalization = apiSettings.TransmissionNormalization,
                driveTypeNormalization = apiSettings.DriveTypeNormalization,
                fuelTypeNormalization = apiSettings.FuelTypeNormalization
            },
            runtimeSettings = new
            {
                pythonPath = runtimeSettings.PythonPath,
                javaPath = runtimeSettings.JavaPath
            },
            dbPath = databasePath,
            configPath = _paths.ConfigsPath
        };

        return RunPipelineManager(_paths.GetInputPipelineManagerPath(), request);
    }

    // Запускает OutputPipeline по уже готовым параметрам. Используется обычным запуском и менеджером сценариев.
    public ProcessRunResult RunOutputPipeline(
        string databasePath,
        string analyzerPath,
        OutputFilterSettings outputSettings,
        RuntimeSettings runtimeSettings
    )
    {
        PrintOutputFilterSummary(outputSettings);

        var request = new
        {
            analyzer = new
            {
                modulePath = analyzerPath,
                analyzerPath = analyzerPath,
                runtime = _moduleDiscovery.GetRuntimeNameByPath(analyzerPath),
                python = runtimeSettings.PythonPath,
                java = runtimeSettings.JavaPath
            },
            outputSettings = new
            {
                latestOnly = outputSettings.LatestOnly,
                onlyChanged = outputSettings.OnlyChanged,
                brand = outputSettings.Brand,
                model = outputSettings.Model,
                saleRegion = outputSettings.SaleRegion,
                yearFrom = outputSettings.YearFrom,
                yearTo = outputSettings.YearTo
            },
            runtimeSettings = new
            {
                pythonPath = runtimeSettings.PythonPath,
                javaPath = runtimeSettings.JavaPath
            },
            dbPath = databasePath,
            configPath = _paths.ConfigsPath
        };

        ProcessRunResult result = RunPipelineManager(_paths.GetOutputPipelineManagerPath(), request);
        PrintOutputResultSummary(result);

        return result;
    }

    // Выводит настройки выборки, которые будут использованы перед запуском анализатора.
    private void PrintOutputFilterSummary(OutputFilterSettings outputSettings)
    {
        Console.WriteLine("=== Настройки выборки анализа ===");
        Console.WriteLine($"Только последние снимки: {FormatBool(outputSettings.LatestOnly)}");
        Console.WriteLine($"Только записи с изменениями: {FormatBool(outputSettings.OnlyChanged)}");
        Console.WriteLine($"Бренд: {FormatStringFilter(outputSettings.Brand)}");
        Console.WriteLine($"Модель: {FormatStringFilter(outputSettings.Model)}");
        Console.WriteLine($"Регион продажи: {FormatStringFilter(outputSettings.SaleRegion)}");
        Console.WriteLine($"Год выпуска от: {FormatNullableInt(outputSettings.YearFrom)}");
        Console.WriteLine($"Год выпуска до: {FormatNullableInt(outputSettings.YearTo)}");
        Console.WriteLine();
    }

    // Пытается вывести краткую информацию о количестве записей, которые получил анализатор.
    private void PrintOutputResultSummary(ProcessRunResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Output))
            return;

        try
        {
            using JsonDocument document = JsonDocument.Parse(result.Output);
            JsonElement root = document.RootElement;

            if (!root.TryGetProperty("meta", out JsonElement meta))
                return;

            if (!meta.TryGetProperty("count", out JsonElement countElement))
                return;

            int count = countElement.GetInt32();

            Console.WriteLine("=== Сводка выборки ===");
            Console.WriteLine($"Найдено записей для анализа: {count}");
            Console.WriteLine();
        }
        catch
        {
            // Если результат не является JSON или имеет другую структуру, просто не выводим сводку.
        }
    }

    // Преобразует bool в короткий текст для консоли.
    private string FormatBool(bool value)
    {
        return value ? "да" : "нет";
    }

    // Форматирует строковый фильтр для вывода в консоль.
    private string FormatStringFilter(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "не задано" : value;
    }

    // Форматирует необязательное число для вывода в консоль.
    private string FormatNullableInt(int? value)
    {
        return value.HasValue ? value.Value.ToString() : "не задано";
    }

    // Сериализует запрос, запускает нужный Python PipelineManager и выводит результат в консоль.
    private ProcessRunResult RunPipelineManager(string pipelineManagerPath, object request)
    {
        string json = JsonSerializer.Serialize(request);
        ProcessRunResult result = _pythonProcess.RunScript(pipelineManagerPath, json);

        Console.WriteLine("=== RESULT ===");
        Console.WriteLine(result.Output);

        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            Console.WriteLine("=== ERRORS ===");
            Console.WriteLine(result.Error);
        }

        return result;
    }
}
