using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

// Управляет сохранёнными сценариями автоповтора в папке Jobs.
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

    // Показывает меню сохранённых сценариев.
    public void ShowMenu()
    {
        while (true)
        {
            Console.WriteLine("=== Менеджер сценариев ===");
            Console.WriteLine("0 - Вернуться назад");
            Console.WriteLine("1 - Создать сценарий");
            Console.WriteLine("2 - Показать сценарии");
            Console.WriteLine("3 - Удалить сценарий");
            Console.WriteLine("4 - Включить/выключить сценарий");
            Console.WriteLine("5 - Запустить сценарий сейчас");
            Console.WriteLine("6 - Показать историю запусков сценария");
            Console.WriteLine("7 - Запустить проверку сценариев вручную");

            int selectedMode = _input.ReadMenuNumber(min: 0, max: 7, "Номер выбранного режима: ");

            if (selectedMode == 0) { return; }
            if (selectedMode == 1) { CreateJob(); }
            if (selectedMode == 2) { ShowJobs(); }
            if (selectedMode == 3) { DeleteJob(); }
            if (selectedMode == 4) { ToggleJobEnabled(); }
            if (selectedMode == 5) { RunSelectedJobNow(); }
            if (selectedMode == 6) { ShowJobRunHistory(); }
            if (selectedMode == 7) { RunDueJobsNow(); }
        }
    }

    // При запуске программы предлагает выполнить сценарии, у которых уже наступило время запуска.
    public void CheckDueJobsOnStartup()
    {
        JobFile[] dueJobs = GetDueJobFiles(DateTime.Now);
        if (dueJobs.Length == 0)
            return;

        Console.WriteLine($"Найдены сценарии, которые пора выполнить: {dueJobs.Length}.");
        for (int i = 0; i < dueJobs.Length; i++)
            PrintJobSelectionInfo(i + 1, dueJobs[i]);

        bool runNow = _input.AskYesNo("Запустить эти сценарии сейчас? y/n: ");
        if (!runNow)
        {
            Console.WriteLine("Автопроверка сценариев пропущена.");
            Console.WriteLine();
            return;
        }

        RunJobFiles(dueJobs);
        Console.WriteLine($"Автопроверка завершена. Запущено: {dueJobs.Length}.");
        Console.WriteLine();
    }

    // Создаёт JSON-файл сценария на основе выбранной базы, модуля и расписания.
    private void CreateJob()
    {
        EnsureJobsDirectory();
        EnsureJobRunsDirectory();

        Console.Write("Введите название сценария: ");
        string jobName = (Console.ReadLine() ?? "").Trim();
        if (string.IsNullOrWhiteSpace(jobName))
        {
            Console.WriteLine("Название сценария не может быть пустым.");
            return;
        }

        Console.WriteLine("Выберите тип сценария:");
        Console.WriteLine("0 - Отменить создание сценария");
        Console.WriteLine("1 - InputPipeline (парсинг)");
        Console.WriteLine("2 - OutputPipeline (анализ)");
        int pipelineTypeNumber = _input.ReadMenuNumber(min: 0, max: 2, "Номер выбранного типа: ");
        if (pipelineTypeNumber == 0) { return; }

        string selectedDatabase = _databaseDiscovery.SelectDatabase(
            message: "Выберите базу данных для сценария",
            cancelText: "Отменить создание сценария"
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

        Console.WriteLine();
        int everyHours = _input.ReadIntWithDefault(
            "Введите интервал автоповтора в часах (0 = только ручной запуск, пустое поле = 24): ",
            24
        );
        if (everyHours < 0)
        {
            Console.WriteLine("Интервал не может быть меньше нуля.");
            return;
        }

        DateTime now = DateTime.Now;

        job.Schedule = new JobScheduleSettings
        {
            Type = everyHours == 0 ? "manual" : "interval",
            EveryHours = everyHours
        };
        job.LastRunAt = "";

        if (everyHours == 0)
        {
            job.NextRunAt = "";
            Console.WriteLine("Сценарий будет запускаться только вручную.");
            Console.WriteLine();
        }
        else
        {
            bool runAtNextCheck = _input.AskYesNo("Выполнить сценарий при ближайшей проверке? y/n: ");
            job.NextRunAt = runAtNextCheck
                ? FormatJobTime(now)
                : FormatJobTime(now.AddHours(everyHours));
        }

        string jobFilePath = Path.Combine(_paths.JobsPath, BuildJobFileName(job));
        SaveJob(jobFilePath, job);

        Console.WriteLine($"Сценарий создан: {Path.GetFileName(jobFilePath)}");
        Console.WriteLine();
    }

    // Заполняет параметры сценария InputPipeline.
    private void FillInputJob(JobConfig job)
    {
        string selectedParser = _moduleDiscovery.SelectParser(
            message: "Выберите парсер для сценария",
            cancelText: "Отменить создание сценария"
        );
        if (string.IsNullOrWhiteSpace(selectedParser)) { return; }

        Console.WriteLine($"Выбран парсер: {Path.GetFileName(selectedParser)}");
        Console.WriteLine();

        job.ParserPath = selectedParser;
        job.ParserSettings = _settingsService.ReadParserRunSettings(selectedParser);
        job.ApiSettings = _settingsService.ReadApiSettings(selectedParser);
    }

    // Заполняет параметры сценария OutputPipeline.
    private void FillOutputJob(JobConfig job)
    {
        string selectedAnalyzer = _moduleDiscovery.SelectAnalyzer(
            message: "Выберите анализатор для сценария",
            cancelText: "Отменить создание сценария"
        );
        if (string.IsNullOrWhiteSpace(selectedAnalyzer)) { return; }

        Console.WriteLine($"Выбран анализатор: {Path.GetFileName(selectedAnalyzer)}");
        Console.WriteLine();

        job.AnalyzerPath = selectedAnalyzer;
        job.OutputSettings = _settingsService.ReadOutputFilterSettings();
    }

    // Показывает сохранённые сценарии в виде коротких карточек.
    private void ShowJobs()
    {
        JobFile[] jobs = LoadJobFiles();

        if (jobs.Length == 0)
        {
            Console.WriteLine("Сценариев пока нет.");
            Console.WriteLine();
            return;
        }

        Console.WriteLine("=== Список сценариев ===");
        Console.WriteLine();

        for (int i = 0; i < jobs.Length; i++)
            PrintJobDetailedInfo(i + 1, jobs[i]);

        Console.WriteLine();
    }

    // Удаляет выбранный JSON-файл сценария.
    private void DeleteJob()
    {
        JobFile? selectedJob = SelectJob("Выберите сценарий для удаления:", "Отменить удаление сценария");
        if (selectedJob == null) { return; }

        bool confirmed = _input.AskYesNo($"Удалить сценарий <{selectedJob.Job.JobName}>? y/n: ");
        if (!confirmed) { return; }

        File.Delete(selectedJob.Path);
        Console.WriteLine("Сценарий удалён.");
        Console.WriteLine();
    }

    // Переключает состояние сценария: включён или выключен.
    private void ToggleJobEnabled()
    {
        JobFile? selectedJob = SelectJob("Выберите сценарий для включения/выключения:", "Отменить изменение сценария");
        if (selectedJob == null) { return; }

        JobConfig job = selectedJob.Job;
        job.Enabled = !job.Enabled;
        SaveJob(selectedJob.Path, job);

        string status = job.Enabled ? "включено" : "выключено";
        Console.WriteLine($"Сценарий <{job.JobName}> теперь {status}.");
        Console.WriteLine();
    }

    // Запускает выбранный сценарий сразу, без проверки nextRunAt.
    private void RunSelectedJobNow()
    {
        JobFile? selectedJob = SelectJob("Выберите сценарий для ручного запуска:", "Отменить запуск сценария");
        if (selectedJob == null) { return; }

        JobConfig job = selectedJob.Job;
        if (!job.Enabled)
        {
            bool runDisabledJob = _input.AskYesNo("Сценарий выключен. Запустить его всё равно? y/n: ");
            if (!runDisabledJob) { return; }
        }

        Console.WriteLine($"=== Ручной запуск сценария: {job.JobName} ===");
        RunJobAndSaveHistory(selectedJob);

        Console.WriteLine("Ручной запуск сценария завершён.");
        Console.WriteLine();
    }

    // Показывает последние записи истории запусков выбранного сценария.
    private void ShowJobRunHistory()
    {
        JobFile? selectedJob = SelectJob("Выберите сценарий для просмотра истории:", "Отменить просмотр истории");
        if (selectedJob == null) { return; }

        string historyPath = GetJobRunHistoryPath(selectedJob.Job);
        List<JobRunRecord> records = LoadJobRunRecords(historyPath);

        if (records.Count == 0)
        {
            Console.WriteLine("У сценария пока нет истории запусков.");
            Console.WriteLine();
            return;
        }

        Console.WriteLine($"=== История запусков: {selectedJob.Job.JobName} ===");
        Console.WriteLine($"Всего записей: {records.Count}. Показаны последние 10.");
        Console.WriteLine();

        List<JobRunRecord> lastRecords = records
            .Skip(Math.Max(0, records.Count - 10))
            .Reverse()
            .ToList();

        for (int i = 0; i < lastRecords.Count; i++)
            PrintJobRunRecord(i + 1, lastRecords[i]);

        Console.WriteLine();
    }

    // Проверяет все сценарии и запускает только те, у которых наступило время nextRunAt.
    private void RunDueJobsNow()
    {
        JobFile[] jobs = LoadJobFiles();
        if (jobs.Length == 0)
        {
            Console.WriteLine("Сценариев пока нет.");
            Console.WriteLine();
            return;
        }

        JobFile[] dueJobs = GetDueJobFiles(DateTime.Now);
        RunJobFiles(dueJobs);

        int skippedCount = jobs.Length - dueJobs.Length;
        Console.WriteLine($"Проверка завершена. Запущено: {dueJobs.Length}, пропущено: {skippedCount}.");
        Console.WriteLine();
    }

    // Запускает набор сценариев без дополнительной проверки расписания.
    private void RunJobFiles(JobFile[] jobFiles)
    {
        foreach (JobFile jobFile in jobFiles)
        {
            Console.WriteLine($"=== Запуск сценария: {jobFile.Job.JobName} ===");
            RunJobAndSaveHistory(jobFile);
        }
    }

    // Возвращает сценарии, у которых включено расписание и уже наступило время запуска.
    private JobFile[] GetDueJobFiles(DateTime now)
    {
        return LoadJobFiles()
            .Where(jobFile => IsJobDue(jobFile.Job, now))
            .ToArray();
    }

    // Проверяет, нужно ли запускать конкретный сценарий прямо сейчас.
    private bool IsJobDue(JobConfig job, DateTime now)
    {
        if (!job.Enabled)
            return false;

        if (IsManualOnlyJob(job))
            return false;

        if (string.IsNullOrWhiteSpace(job.NextRunAt))
            return false;

        DateTime nextRunAt = ParseJobTime(job.NextRunAt, now);
        return nextRunAt <= now;
    }

    // Запускает сценарий, записывает историю запуска и обновляет даты в файле сценария.
    private void RunJobAndSaveHistory(JobFile jobFile)
    {
        JobConfig job = jobFile.Job;
        DateTime startedAt = DateTime.Now;
        ProcessRunResult result;

        try
        {
            result = RunJob(job);
        }
        catch (Exception ex)
        {
            result = new ProcessRunResult
            {
                Output = "",
                Error = ex.Message,
                ExitCode = -1
            };
        }

        DateTime finishedAt = DateTime.Now;
        JobRunRecord runRecord = BuildJobRunRecord(job, startedAt, finishedAt, result);

        AppendJobRunRecord(job, runRecord);
        UpdateJobRunDates(jobFile, finishedAt);

        Console.WriteLine($"Статус запуска: {runRecord.Status}. {runRecord.Message}");
        Console.WriteLine();
    }

    // Запускает сохранённый сценарий через PipelineService.
    private ProcessRunResult RunJob(JobConfig job)
    {
        if (job.PipelineType == "input")
        {
            return _pipelineService.RunInputPipeline(
                job.DbPath,
                job.ParserPath,
                job.ParserSettings,
                job.ApiSettings,
                job.RuntimeSettings
            );
        }

        if (job.PipelineType == "output")
        {
            return _pipelineService.RunOutputPipeline(
                job.DbPath,
                job.AnalyzerPath,
                job.OutputSettings,
                job.RuntimeSettings
            );
        }

        return new ProcessRunResult
        {
            Output = "",
            Error = $"Неизвестный тип сценария: {job.PipelineType}",
            ExitCode = -1
        };
    }

    // Создаёт запись истории на основе результата запуска pipeline.
    private JobRunRecord BuildJobRunRecord(JobConfig job, DateTime startedAt, DateTime finishedAt, ProcessRunResult result)
    {
        string status = IsPipelineRunSuccessful(result) ? "success" : "error";
        string moduleName = GetJobModuleName(job);
        double durationSeconds = Math.Round((finishedAt - startedAt).TotalSeconds, 2);

        return new JobRunRecord
        {
            JobId = job.JobId,
            JobName = job.JobName,
            StartedAt = FormatJobTime(startedAt),
            FinishedAt = FormatJobTime(finishedAt),
            DurationSeconds = durationSeconds,
            Status = status,
            PipelineType = job.PipelineType,
            ModuleName = moduleName,
            DbName = Path.GetFileName(job.DbPath),
            ExitCode = result.ExitCode,
            Message = status == "success" ? "Сценарий выполнен успешно" : "Сценарий завершился с ошибкой",
            OutputPreview = BuildPreview(result.Output),
            ErrorPreview = BuildPreview(result.Error)
        };
    }

    // Определяет, можно ли считать запуск успешным.
    private bool IsPipelineRunSuccessful(ProcessRunResult result)
    {
        if (result.ExitCode != 0)
            return false;

        if (!string.IsNullOrWhiteSpace(result.Error))
            return false;

        string output = result.Output ?? "";
        if (output.Contains("\"status\": \"error\"") || output.Contains("\"status\":\"error\""))
            return false;

        return true;
    }

    // Добавляет запись запуска в JSON-файл истории сценария.
    private void AppendJobRunRecord(JobConfig job, JobRunRecord record)
    {
        EnsureJobRunsDirectory();

        string historyPath = GetJobRunHistoryPath(job);
        List<JobRunRecord> records = LoadJobRunRecords(historyPath);
        records.Add(record);

        string json = JsonSerializer.Serialize(records, _jsonOptions);
        File.WriteAllText(historyPath, json);
    }

    // Загружает историю запусков сценария. Если файл повреждён, начинает новую историю.
    private List<JobRunRecord> LoadJobRunRecords(string historyPath)
    {
        if (!File.Exists(historyPath))
            return new List<JobRunRecord>();

        try
        {
            string json = File.ReadAllText(historyPath);
            return JsonSerializer.Deserialize<List<JobRunRecord>>(json, _jsonOptions) ?? new List<JobRunRecord>();
        }
        catch
        {
            return new List<JobRunRecord>();
        }
    }

    // Возвращает последнюю запись истории сценария, если она есть.
    private JobRunRecord? LoadLastJobRun(JobConfig job)
    {
        string historyPath = GetJobRunHistoryPath(job);
        List<JobRunRecord> records = LoadJobRunRecords(historyPath);
        return records.LastOrDefault();
    }

    // Обновляет даты последнего и следующего запуска сценария.
    private void UpdateJobRunDates(JobFile jobFile, DateTime runTime)
    {
        JobConfig job = jobFile.Job;

        job.LastRunAt = FormatJobTime(runTime);

        if (IsManualOnlyJob(job))
            job.NextRunAt = "";
        else
        {
            int everyHours = job.Schedule.EveryHours > 0 ? job.Schedule.EveryHours : 24;
            job.NextRunAt = FormatJobTime(runTime.AddHours(everyHours));
        }

        SaveJob(jobFile.Path, job);
    }

    // Позволяет выбрать один сценарий из списка.
    private JobFile? SelectJob(string title, string cancelText)
    {
        JobFile[] jobs = LoadJobFiles();
        if (jobs.Length == 0)
        {
            Console.WriteLine("Сценариев пока нет.");
            Console.WriteLine();
            return null;
        }

        Console.WriteLine(title);
        Console.WriteLine($"0 - {cancelText}");
        for (int i = 0; i < jobs.Length; i++)
            PrintJobSelectionInfo(i + 1, jobs[i]);

        int selectedNumber = _input.ReadMenuNumber(min: 0, max: jobs.Length, "Номер сценария: ");
        if (selectedNumber == 0) { return null; }

        return jobs[selectedNumber - 1];
    }

    // Выводит одну запись истории запуска сценария.
    private void PrintJobRunRecord(int number, JobRunRecord record)
    {
        Console.WriteLine($"{number}. {FormatDisplayTime(record.StartedAt)} -> {record.Status}, {record.DurationSeconds} сек.");
        Console.WriteLine($"   Тип: {FormatPipelineType(record.PipelineType)}");
        Console.WriteLine($"   Модуль: {record.ModuleName}");
        Console.WriteLine($"   База: {record.DbName}");
        Console.WriteLine($"   Код завершения: {record.ExitCode}");
        Console.WriteLine($"   Сообщение: {record.Message}");

        if (!string.IsNullOrWhiteSpace(record.ErrorPreview))
            Console.WriteLine($"   Ошибка: {record.ErrorPreview}");

        Console.WriteLine();
    }

    // Выводит подробную карточку сценария для просмотра списка.
    private void PrintJobDetailedInfo(int number, JobFile jobFile)
    {
        JobConfig job = jobFile.Job;
        JobRunRecord? lastRun = LoadLastJobRun(job);
        string status = job.Enabled ? "включено" : "выключено";
        string moduleName = GetJobModuleName(job);
        string lastRunStatus = lastRun == null ? "нет истории запусков" : lastRun.Status;

        Console.WriteLine($"{number}. {job.JobName}");
        Console.WriteLine($"   Статус: {status}");
        Console.WriteLine($"   Тип: {FormatPipelineType(job.PipelineType)}");
        Console.WriteLine($"   База: {Path.GetFileName(job.DbPath)}");
        Console.WriteLine($"   Модуль: {moduleName}");
        Console.WriteLine($"   Интервал: {FormatScheduleDisplay(job)}");
        if (job.PipelineType == "input")
            Console.WriteLine($"   Обогащение страны бренда: {FormatEnabled(job.ApiSettings.BrandCountryEnrichment)}");

        if (job.PipelineType == "output")
            Console.WriteLine($"   Выборка анализа: {FormatOutputSettings(job.OutputSettings)}");

        Console.WriteLine($"   Последний запуск: {FormatDisplayTimeOrNever(job.LastRunAt)}");
        Console.WriteLine($"   Следующий запуск: {FormatNextRunDisplay(job)}");
        Console.WriteLine($"   Последний результат: {lastRunStatus}");
        Console.WriteLine();
    }

    // Выводит краткую строку сценария для выбора в меню.
    private void PrintJobSelectionInfo(int number, JobFile jobFile)
    {
        JobConfig job = jobFile.Job;
        string status = job.Enabled ? "включено" : "выключено";
        string moduleName = GetJobModuleName(job);

        Console.WriteLine(
            $"{number}: [{status}] {job.JobName} | {FormatPipelineType(job.PipelineType)} | {moduleName} | next: {FormatNextRunDisplay(job)}"
        );
    }

    // Загружает все сценарии из папки Jobs. Ошибочные JSON-файлы пропускаются.
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

    // Пытается загрузить один JSON-файл сценария.
    private JobFile? TryLoadJobFile(string filePath)
    {
        try
        {
            string json = File.ReadAllText(filePath);
            JobConfig? job = JsonSerializer.Deserialize<JobConfig>(json, _jsonOptions);
            if (job == null) { return null; }

            if (string.IsNullOrWhiteSpace(job.JobId))
                job.JobId = Path.GetFileNameWithoutExtension(filePath);

            return new JobFile(filePath, job);
        }
        catch
        {
            Console.WriteLine($"Не удалось прочитать сценарий: {Path.GetFileName(filePath)}");
            return null;
        }
    }

    // Сохраняет сценарий в JSON-файл.
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

    // Создаёт папку Logs/JobRuns, если её ещё нет.
    private void EnsureJobRunsDirectory()
    {
        Directory.CreateDirectory(_paths.JobRunsPath);
    }

    // Формирует безопасное имя файла сценария.
    private string BuildJobFileName(JobConfig job)
    {
        string safeName = SanitizeFileName(job.JobName).Replace(' ', '_');
        return $"{DateTime.Now:yyyyMMdd_HHmmss}_{safeName}.json";
    }

    // Возвращает путь к файлу истории запусков конкретного сценария в Logs/JobRuns.
    private string GetJobRunHistoryPath(JobConfig job)
    {
        string safeId = string.IsNullOrWhiteSpace(job.JobId)
            ? SanitizeFileName(job.JobName).Replace(' ', '_')
            : SanitizeFileName(job.JobId);

        return Path.Combine(_paths.JobRunsPath, $"{safeId}_runs.json");
    }

    // Убирает из строки символы, которые нельзя использовать в имени файла.
    private string SanitizeFileName(string value)
    {
        string safeName = value;
        foreach (char invalidChar in Path.GetInvalidFileNameChars())
            safeName = safeName.Replace(invalidChar, '_');

        return safeName;
    }

    // Возвращает имя модуля сценария: парсер или анализатор.
    private string GetJobModuleName(JobConfig job)
    {
        string modulePath = job.PipelineType == "input" ? job.ParserPath : job.AnalyzerPath;
        return string.IsNullOrWhiteSpace(modulePath) ? "не указан" : Path.GetFileName(modulePath);
    }

    // Форматирует тип pipeline для вывода пользователю.

    // Форматирует состояние включаемой настройки.
    private string FormatEnabled(bool value)
    {
        return value ? "включено" : "выключено";
    }

    // Возвращает краткое описание фильтров анализа для карточки сценария.
    private string FormatOutputSettings(OutputFilterSettings settings)
    {
        List<string> parts = new List<string>();

        parts.Add(settings.LatestOnly ? "последние снимки" : "вся история");

        if (settings.OnlyChanged)
            parts.Add("только изменения");

        if (!string.IsNullOrWhiteSpace(settings.Brand))
            parts.Add($"бренд: {settings.Brand}");

        if (!string.IsNullOrWhiteSpace(settings.Model))
            parts.Add($"модель: {settings.Model}");

        if (!string.IsNullOrWhiteSpace(settings.SaleRegion))
            parts.Add($"регион: {settings.SaleRegion}");

        if (settings.YearFrom.HasValue)
            parts.Add($"год от {settings.YearFrom.Value}");

        if (settings.YearTo.HasValue)
            parts.Add($"год до {settings.YearTo.Value}");

        return string.Join(", ", parts);
    }

    private string FormatPipelineType(string pipelineType)
    {
        if (pipelineType == "input") { return "InputPipeline"; }
        if (pipelineType == "output") { return "OutputPipeline"; }
        return pipelineType;
    }

    // Делает короткую версию stdout/stderr для истории запусков.
    private string BuildPreview(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        string normalized = value.Replace("\r", " ").Replace("\n", " ").Trim();
        if (normalized.Length <= 500)
            return normalized;

        return normalized.Substring(0, 500) + "...";
    }

    // Сохраняет время в формате, который удобно читать и безопасно парсить.
    private string FormatJobTime(DateTime value)
    {
        return value.ToString("yyyy-MM-ddTHH:mm:ss");
    }

    // Преобразует строку времени из сценария в DateTime.
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

    // Форматирует время последнего запуска.
    private string FormatDisplayTimeOrNever(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "ещё не запускалось";

        return FormatDisplayTime(value);
    }

    // Форматирует время следующего запуска с учётом выключенного состояния.
    private string FormatNextRunDisplay(JobConfig job)
    {
        if (!job.Enabled)
            return "сценарий выключен";

        if (IsManualOnlyJob(job))
            return "только ручной запуск";

        return FormatDisplayTime(job.NextRunAt);
    }

    // Форматирует расписание сценария для вывода в консоль.
    private string FormatScheduleDisplay(JobConfig job)
    {
        if (IsManualOnlyJob(job))
            return "только ручной запуск";

        return $"каждые {job.Schedule.EveryHours} ч.";
    }

    // Проверяет, что сценарий не участвует в автопроверке и запускается только вручную.
    private bool IsManualOnlyJob(JobConfig job)
    {
        if (job.Schedule == null)
            return false;

        return job.Schedule.EveryHours == 0
            || string.Equals(job.Schedule.Type, "manual", StringComparison.OrdinalIgnoreCase);
    }

    // Хранит путь к файлу и загруженное содержимое сценария.
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
