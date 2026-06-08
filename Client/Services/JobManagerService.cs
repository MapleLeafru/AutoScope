using System;
using System.IO;
using System.Linq;
using System.Text.Json;

// Управляет сохранёнными заданиями автоповтора в папке Jobs.
public class JobManagerService
{
    private readonly AppPaths _paths;
    private readonly ConsoleInputService _input;
    private readonly DatabaseDiscoveryService _databaseDiscovery;
    private readonly ModuleDiscoveryService _moduleDiscovery;
    private readonly SettingsService _settingsService;
    private readonly PipelineService _pipelineService;

    private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public JobManagerService(
        AppPaths paths,
        ConsoleInputService input,
        DatabaseDiscoveryService databaseDiscovery,
        ModuleDiscoveryService moduleDiscovery,
        SettingsService settingsService,
        PipelineService pipelineService
    )
    {
        _paths = paths;
        _input = input;
        _databaseDiscovery = databaseDiscovery;
        _moduleDiscovery = moduleDiscovery;
        _settingsService = settingsService;
        _pipelineService = pipelineService;
    }

    // Показывает меню менеджера заданий.
    public void ShowMenu()
    {
        while (true)
        {
            Console.WriteLine("=== Менеджер заданий автоматического сбора данных ===");
            Console.WriteLine("0 - Вернуться назад");
            Console.WriteLine("1 - Создать задание");
            Console.WriteLine("2 - Показать задания");
            Console.WriteLine("3 - Удалить задание");
            Console.WriteLine("4 - Включить/выключить задание");
            Console.WriteLine("5 - Запустить конкретное задание сейчас");
            Console.WriteLine("6 - Запустить проверку заданий вручную");

            int selectedMode = _input.ReadMenuNumber(min: 0, max: 6, "Номер выбранного режима: ");

            if (selectedMode == 0) { return; }
            if (selectedMode == 1) { CreateJob(); }
            if (selectedMode == 2) { ShowJobs(); }
            if (selectedMode == 3) { DeleteJob(); }
            if (selectedMode == 4) { ToggleJobEnabled(); }
            if (selectedMode == 5) { RunSelectedJobNow(); }
            if (selectedMode == 6) { RunDueJobsNow(); }
        }
    }

    // Создаёт JSON-файл задания на основе выбранной базы, модуля и расписания.
    private void CreateJob()
    {
        EnsureJobsDirectory();

        Console.Write("Введите название задания: ");
        string jobName = (Console.ReadLine() ?? "").Trim();
        if (string.IsNullOrWhiteSpace(jobName))
        {
            Console.WriteLine("Название задания не может быть пустым.");
            return;
        }

        Console.WriteLine("Выберите тип задания:");
        Console.WriteLine("0 - Отменить создание задания");
        Console.WriteLine("1 - InputPipeline (парсинг)");
        Console.WriteLine("2 - OutputPipeline (анализ)");
        int pipelineTypeNumber = _input.ReadMenuNumber(min: 0, max: 2, "Номер выбранного типа: ");
        if (pipelineTypeNumber == 0) { return; }

        string selectedDatabase = _databaseDiscovery.SelectDatabase(
            message: "Выберите базу данных для задания",
            cancelText: "Отменить создание задания"
        );
        if (string.IsNullOrWhiteSpace(selectedDatabase)) { return; }

        RuntimeSettings runtimeSettings = _settingsService.GetRuntimeSettings();

        JobConfig job = new JobConfig
        {
            JobId = Guid.NewGuid().ToString("N"),
            JobName = jobName,
            Enabled = true,
            PipelineType = pipelineTypeNumber == 1 ? "input" : "output",
            DbPath = selectedDatabase,
            RuntimeSettings = runtimeSettings,
            CreatedAt = FormatJobTime(DateTime.Now)
        };

        if (job.PipelineType == "input")
            FillInputJob(job);
        else
            FillOutputJob(job);

        if (string.IsNullOrWhiteSpace(job.ParserPath) && string.IsNullOrWhiteSpace(job.AnalyzerPath))
            return;

        int everyHours = _input.ReadIntWithDefault(
            "Введите интервал автоповтора в часах (Пустое поле = 24): ",
            24
        );
        if (everyHours <= 0)
        {
            Console.WriteLine("Интервал должен быть больше нуля.");
            return;
        }

        bool runAtNextCheck = _input.AskYesNo("Выполнить задание при ближайшей проверке? y/n: ");
        DateTime now = DateTime.Now;

        job.Schedule = new JobScheduleSettings
        {
            Type = "interval",
            EveryHours = everyHours
        };
        job.LastRunAt = "";
        job.NextRunAt = runAtNextCheck
            ? FormatJobTime(now)
            : FormatJobTime(now.AddHours(everyHours));

        string jobFilePath = Path.Combine(_paths.JobsPath, BuildJobFileName(job));
        SaveJob(jobFilePath, job);

        Console.WriteLine($"Задание создано: {Path.GetFileName(jobFilePath)}");
        Console.WriteLine();
    }

    // Заполняет параметры задания InputPipeline.
    private void FillInputJob(JobConfig job)
    {
        string selectedParser = _moduleDiscovery.SelectParser(
            message: "Выберите парсер для задания",
            cancelText: "Отменить создание задания"
        );
        if (string.IsNullOrWhiteSpace(selectedParser)) { return; }

        Console.WriteLine($"Выбран парсер: {Path.GetFileName(selectedParser)}");
        Console.WriteLine();

        job.ParserPath = selectedParser;
        job.ParserSettings = _settingsService.ReadParserRunSettings();
    }

    // Заполняет параметры задания OutputPipeline.
    private void FillOutputJob(JobConfig job)
    {
        string selectedAnalyzer = _moduleDiscovery.SelectAnalyzer(
            message: "Выберите анализатор для задания",
            cancelText: "Отменить создание задания"
        );
        if (string.IsNullOrWhiteSpace(selectedAnalyzer)) { return; }

        Console.WriteLine($"Выбран анализатор: {Path.GetFileName(selectedAnalyzer)}");
        Console.WriteLine();

        job.AnalyzerPath = selectedAnalyzer;
    }

    // Показывает сохранённые задания из папки Jobs.
    private void ShowJobs()
    {
        JobFile[] jobs = LoadJobFiles();

        if (jobs.Length == 0)
        {
            Console.WriteLine("Заданий пока нет.");
            Console.WriteLine();
            return;
        }

        Console.WriteLine("Сохранённые задания:");
        for (int i = 0; i < jobs.Length; i++)
            PrintJobShortInfo(i + 1, jobs[i]);

        Console.WriteLine();
    }

    // Удаляет выбранный JSON-файл задания.
    private void DeleteJob()
    {
        JobFile? selectedJob = SelectJob("Выберите задание для удаления:", "Отменить удаление задания");
        if (selectedJob == null) { return; }

        bool confirmed = _input.AskYesNo($"Удалить задание <{selectedJob.Job.JobName}>? y/n: ");
        if (!confirmed) { return; }

        File.Delete(selectedJob.Path);
        Console.WriteLine("Задание удалено.");
        Console.WriteLine();
    }

    // Переключает состояние задания: включено или выключено.
    private void ToggleJobEnabled()
    {
        JobFile? selectedJob = SelectJob("Выберите задание для включения/выключения:", "Отменить изменение задания");
        if (selectedJob == null) { return; }

        JobConfig job = selectedJob.Job;
        job.Enabled = !job.Enabled;
        SaveJob(selectedJob.Path, job);

        string status = job.Enabled ? "включено" : "выключено";
        Console.WriteLine($"Задание <{job.JobName}> теперь {status}.");
        Console.WriteLine();
    }

    // Запускает выбранное задание сразу, без проверки nextRunAt.
    private void RunSelectedJobNow()
    {
        JobFile? selectedJob = SelectJob("Выберите задание для ручного запуска:", "Отменить запуск задания");
        if (selectedJob == null) { return; }

        JobConfig job = selectedJob.Job;
        if (!job.Enabled)
        {
            bool runDisabledJob = _input.AskYesNo("Задание выключено. Запустить его всё равно? y/n: ");
            if (!runDisabledJob) { return; }
        }

        Console.WriteLine($"=== Ручной запуск задания: {job.JobName} ===");
        RunJob(job);
        UpdateJobRunDates(selectedJob, DateTime.Now);

        Console.WriteLine("Ручной запуск задания завершён.");
        Console.WriteLine();
    }

    // Проверяет все задания и запускает только те, у которых наступило время nextRunAt.
    private void RunDueJobsNow()
    {
        JobFile[] jobs = LoadJobFiles();
        if (jobs.Length == 0)
        {
            Console.WriteLine("Заданий пока нет.");
            Console.WriteLine();
            return;
        }

        DateTime now = DateTime.Now;
        int startedCount = 0;
        int skippedCount = 0;

        foreach (JobFile jobFile in jobs)
        {
            JobConfig job = jobFile.Job;

            if (!job.Enabled)
            {
                skippedCount++;
                continue;
            }

            DateTime nextRunAt = ParseJobTime(job.NextRunAt, now);
            if (nextRunAt > now)
            {
                skippedCount++;
                continue;
            }

            Console.WriteLine($"=== Запуск задания: {job.JobName} ===");
            RunJob(job);
            UpdateJobRunDates(jobFile, now);

            startedCount++;
        }

        Console.WriteLine($"Проверка завершена. Запущено: {startedCount}, пропущено: {skippedCount}.");
        Console.WriteLine();
    }

    // Запускает сохранённое задание через PipelineService.
    private void RunJob(JobConfig job)
    {
        if (job.PipelineType == "input")
        {
            _pipelineService.RunInputPipeline(
                job.DbPath,
                job.ParserPath,
                job.ParserSettings,
                job.RuntimeSettings
            );
            return;
        }

        if (job.PipelineType == "output")
        {
            _pipelineService.RunOutputPipeline(
                job.DbPath,
                job.AnalyzerPath,
                job.RuntimeSettings
            );
        }
    }

    // Обновляет даты последнего и следующего запуска задания.
    private void UpdateJobRunDates(JobFile jobFile, DateTime runTime)
    {
        JobConfig job = jobFile.Job;
        int everyHours = job.Schedule.EveryHours > 0 ? job.Schedule.EveryHours : 24;

        job.LastRunAt = FormatJobTime(runTime);
        job.NextRunAt = FormatJobTime(runTime.AddHours(everyHours));
        SaveJob(jobFile.Path, job);
    }

    // Позволяет выбрать одно задание из списка.
    private JobFile? SelectJob(string title, string cancelText)
    {
        JobFile[] jobs = LoadJobFiles();
        if (jobs.Length == 0)
        {
            Console.WriteLine("Заданий пока нет.");
            Console.WriteLine();
            return null;
        }

        Console.WriteLine(title);
        Console.WriteLine($"0 - {cancelText}");
        for (int i = 0; i < jobs.Length; i++)
            PrintJobShortInfo(i + 1, jobs[i]);

        int selectedNumber = _input.ReadMenuNumber(min: 0, max: jobs.Length, "Номер задания: ");
        if (selectedNumber == 0) { return null; }

        return jobs[selectedNumber - 1];
    }

    // Выводит краткую информацию о задании одной строкой.
    private void PrintJobShortInfo(int number, JobFile jobFile)
    {
        JobConfig job = jobFile.Job;
        string status = job.Enabled ? "включено" : "выключено";
        string moduleName = job.PipelineType == "input"
            ? Path.GetFileName(job.ParserPath)
            : Path.GetFileName(job.AnalyzerPath);

        Console.WriteLine(
            $"{number}: [{status}] {job.JobName} | {job.PipelineType} | {moduleName} | next: {FormatDisplayTime(job.NextRunAt)}"
        );
    }

    // Загружает все задания из папки Jobs. Ошибочные JSON-файлы пропускаются.
    private JobFile[] LoadJobFiles()
    {
        EnsureJobsDirectory();

        return Directory.GetFiles(_paths.JobsPath, "*.json")
            .OrderBy(Path.GetFileName)
            .Select(TryLoadJobFile)
            .Where(jobFile => jobFile != null)
            .Select(jobFile => jobFile!)
            .ToArray();
    }

    // Пытается загрузить один JSON-файл задания.
    private JobFile? TryLoadJobFile(string filePath)
    {
        try
        {
            string json = File.ReadAllText(filePath);
            JobConfig? job = JsonSerializer.Deserialize<JobConfig>(json, _jsonOptions);
            if (job == null) { return null; }

            return new JobFile(filePath, job);
        }
        catch
        {
            Console.WriteLine($"Не удалось прочитать задание: {Path.GetFileName(filePath)}");
            return null;
        }
    }

    // Сохраняет задание в JSON-файл.
    private void SaveJob(string filePath, JobConfig job)
    {
        string json = JsonSerializer.Serialize(job, _jsonOptions);
        File.WriteAllText(filePath, json);
    }

    // Создаёт папку Jobs, если её ещё нет.
    private void EnsureJobsDirectory()
    {
        Directory.CreateDirectory(_paths.JobsPath);
    }

    // Формирует безопасное имя файла задания.
    private string BuildJobFileName(JobConfig job)
    {
        string safeName = job.JobName;
        foreach (char invalidChar in Path.GetInvalidFileNameChars())
            safeName = safeName.Replace(invalidChar, '_');

        safeName = safeName.Replace(' ', '_');
        return $"{DateTime.Now:yyyyMMdd_HHmmss}_{safeName}.json";
    }

    // Сохраняет время в формате, который удобно читать и безопасно парсить.
    private string FormatJobTime(DateTime value)
    {
        return value.ToString("yyyy-MM-ddTHH:mm:ss");
    }

    // Преобразует строку времени из задания в DateTime.
    private DateTime ParseJobTime(string value, DateTime fallback)
    {
        if (DateTime.TryParse(value, out DateTime result))
            return result;

        return fallback;
    }

    // Форматирует время для вывода в консоль.
    private string FormatDisplayTime(string value)
    {
        if (DateTime.TryParse(value, out DateTime result))
            return result.ToString("yyyy-MM-dd HH:mm:ss");

        return "не задано";
    }

    // Хранит путь к файлу и загруженное содержимое задания.
    private class JobFile
    {
        public string Path { get; }
        public JobConfig Job { get; }

        public JobFile(string path, JobConfig job)
        {
            Path = path;
            Job = job;
        }
    }
}
