using System;
using System.IO;
using System.Text.Json;

// Отвечает за запуск InputPipeline и OutputPipeline из консольного клиента.
public class PipelineService
{
    private readonly AppPaths _paths;
    private readonly DatabaseDiscoveryService _databaseDiscovery;
    private readonly ModuleDiscoveryService _moduleDiscovery;
    private readonly SettingsService _settingsService;
    private readonly PythonProcessService _pythonProcess;

    public PipelineService(
        AppPaths paths,
        DatabaseDiscoveryService databaseDiscovery,
        ModuleDiscoveryService moduleDiscovery,
        SettingsService settingsService,
        PythonProcessService pythonProcess
    )
    {
        _paths = paths;
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
        RuntimeSettings runtimeSettings = _settingsService.GetRuntimeSettings();

        RunInputPipeline(selectedDatabase, selectedParser, parserSettings, runtimeSettings);
    }

    // Запускает цепочку анализа: выбор базы, выбор анализатора, запуск Python-менеджера.
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

        RuntimeSettings runtimeSettings = _settingsService.GetRuntimeSettings();

        RunOutputPipeline(selectedDatabase, selectedAnalyzer, runtimeSettings);
    }

    // Запускает InputPipeline по уже готовым параметрам. Используется обычным запуском и менеджером заданий.
    public ProcessRunResult RunInputPipeline(
        string databasePath,
        string parserPath,
        ParserRunSettings parserSettings,
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
                streamBatchSize = parserSettings.StreamBatchSize
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

    // Запускает OutputPipeline по уже готовым параметрам. Используется обычным запуском и менеджером заданий.
    public ProcessRunResult RunOutputPipeline(string databasePath, string analyzerPath, RuntimeSettings runtimeSettings)
    {
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
            runtimeSettings = new
            {
                pythonPath = runtimeSettings.PythonPath,
                javaPath = runtimeSettings.JavaPath
            },
            dbPath = databasePath,
            configPath = _paths.ConfigsPath
        };

        return RunPipelineManager(_paths.GetOutputPipelineManagerPath(), request);
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
