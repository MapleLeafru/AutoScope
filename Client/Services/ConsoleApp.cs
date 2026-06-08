using System;

// Основной консольный интерфейс AutoScope.
public class ConsoleApp
{
    private readonly ConsoleInputService _input;
    private readonly PipelineService _pipelineService;
    private readonly DatabaseToolService _databaseToolService;
    private readonly JobManagerService _jobManagerService;

    public ConsoleApp(AppPaths paths)
    {
        _input = new ConsoleInputService();

        DatabaseDiscoveryService databaseDiscovery = new DatabaseDiscoveryService(paths, _input);
        ModuleDiscoveryService moduleDiscovery = new ModuleDiscoveryService(paths, _input);
        SettingsService settingsService = new SettingsService(paths, _input);
        PythonProcessService pythonProcess = new PythonProcessService(paths);

        _pipelineService = new PipelineService(
            paths,
            databaseDiscovery,
            moduleDiscovery,
            settingsService,
            pythonProcess
        );

        _databaseToolService = new DatabaseToolService(
            paths,
            _input,
            databaseDiscovery,
            pythonProcess
        );

        _jobManagerService = new JobManagerService(
            paths,
            _input,
            databaseDiscovery,
            moduleDiscovery,
            settingsService,
            _pipelineService
        );
    }

    // Запускает главный цикл консольного клиента.
    public void Run()
    {
        Console.WriteLine("C# клиент AutoScope запущен");
        Console.WriteLine();

        _jobManagerService.CheckDueJobsOnStartup();

        while (true)
        {
            ShowMainMenu();
        }
    }

    // Показывает главное меню и вызывает выбранный сценарий.
    private void ShowMainMenu()
    {
        Console.WriteLine("Выберите режим:");
        Console.WriteLine("0 - Закрыть программу");
        Console.WriteLine("1 - Запустить InputPipeline (парсинг)");
        Console.WriteLine("2 - Запустить OutputPipeline (анализ)");
        Console.WriteLine("3 - Менеджер заданий");
        Console.WriteLine("4 - Открыть инструменты");

        int modeNumber = _input.ReadMenuNumber(min: 0, max: 4, "Номер выбранного режима: ");

        if (modeNumber == 0) { Environment.Exit(0); }
        if (modeNumber == 1) { _pipelineService.StartInputPipeline(); }
        if (modeNumber == 2) { _pipelineService.StartOutputPipeline(); }
        if (modeNumber == 3) { _jobManagerService.ShowMenu(); }
        if (modeNumber == 4) { _databaseToolService.ShowMenu(); }
    }
}
