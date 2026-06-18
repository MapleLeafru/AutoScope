using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using AutoScope.WpfClient.Models;

namespace AutoScope.WpfClient.Services;

public class ScenarioManagementService
{
    private readonly string _rootPath;
    private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public ScenarioManagementService(string rootPath)
    {
        _rootPath = rootPath;
    }

    public string GetJobsFolderPath()
    {
        return Path.Combine(_rootPath, "Jobs");
    }

    public List<ScenarioDashboardItem> LoadScenarios()
    {
        string jobsPath = GetJobsFolderPath();
        if (!Directory.Exists(jobsPath))
            return new List<ScenarioDashboardItem>();

        return Directory.GetFiles(jobsPath, "*.json")
            .Select(ReadScenario)
            .Where(item => item != null)
            .Select(item => item!)
            .OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }


    public List<DatabaseDashboardItem> LoadDatabasesForCreation()
    {
        return new DashboardDataService(_rootPath).LoadDatabases();
    }

    public List<ParserLaunchItem> LoadParsersForCreation()
    {
        return new InputPipelineLaunchService(_rootPath).LoadParsers();
    }

    public List<AnalyzerLaunchItem> LoadAnalyzersForCreation()
    {
        return new OutputPipelineLaunchService(_rootPath).LoadAnalyzers();
    }

    public OutputPipelineLaunchSettings LoadOutputDefaultsForCreation()
    {
        return new OutputPipelineLaunchService(_rootPath).LoadDefaultSettings();
    }

    public ScenarioOperationResult CreateScenario(ScenarioCreateDraft draft)
    {
        if (draft == null)
            return ScenarioOperationResult.Fail("Данные нового сценария не заданы.");

        string scenarioName = (draft.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(scenarioName))
            return ScenarioOperationResult.Fail("Название сценария не может быть пустым.");

        if (draft.Database == null || string.IsNullOrWhiteSpace(draft.Database.Path))
            return ScenarioOperationResult.Fail("Выбери базу данных.");

        bool isInput = string.Equals(draft.PipelineType, "input", StringComparison.OrdinalIgnoreCase);
        bool isOutput = string.Equals(draft.PipelineType, "output", StringComparison.OrdinalIgnoreCase);

        if (!isInput && !isOutput)
            return ScenarioOperationResult.Fail("Выбери тип сценария: парсинг или анализ.");

        if (isInput && (draft.Parser == null || string.IsNullOrWhiteSpace(draft.Parser.Path)))
            return ScenarioOperationResult.Fail("Выбери парсер.");

        if (isOutput && (draft.Analyzer == null || string.IsNullOrWhiteSpace(draft.Analyzer.Path)))
            return ScenarioOperationResult.Fail("Выбери анализатор.");

        int everyHours = Math.Max(0, draft.EveryHours);
        if (!draft.IsManualOnly && everyHours <= 0)
            return ScenarioOperationResult.Fail("Интервал автоповтора должен быть больше нуля.");

        if (isInput)
        {
            if (string.IsNullOrWhiteSpace(draft.StartUrl))
                return ScenarioOperationResult.Fail("Для сценария парсинга нужен стартовый URL.");
            if (draft.MaxCars < 0)
                return ScenarioOperationResult.Fail("Максимум объявлений должен быть 0 или больше.");
            if (draft.StreamBatchSize <= 0)
                return ScenarioOperationResult.Fail("Размер пакета должен быть больше нуля.");
        }

        try
        {
            Directory.CreateDirectory(GetJobsFolderPath());

            string jobId = BuildNewJobId(scenarioName);
            string filePath = BuildNewScenarioPath(jobId);
            DateTime now = DateTime.Now;
            string scheduleType = draft.IsManualOnly ? "manual" : "interval";

            JsonObject obj = new JsonObject
            {
                ["jobId"] = jobId,
                ["jobName"] = scenarioName,
                ["enabled"] = draft.Enabled,
                ["pipelineType"] = isInput ? "input" : "output",
                ["dbPath"] = draft.Database.Path,
                ["createdAt"] = FormatJobTime(now),
                ["lastRunAt"] = "",
                ["nextRunAt"] = draft.IsManualOnly || !draft.Enabled ? "" : FormatJobTime(now.AddHours(everyHours)),
                ["schedule"] = new JsonObject
                {
                    ["type"] = scheduleType,
                    ["everyHours"] = draft.IsManualOnly ? 0 : everyHours
                },
                ["runtimeSettings"] = new JsonObject
                {
                    ["pythonPath"] = ResolveDefaultPythonPath(),
                    ["javaPath"] = ResolveDefaultJavaPath()
                }
            };

            if (isInput)
            {
                ParserLaunchItem parser = draft.Parser!;
                Dictionary<string, object?> parserSettings = BuildParserSettings(draft);

                obj["parserPath"] = parser.Path;
                obj["parserSettings"] = JsonSerializer.SerializeToNode(parserSettings) ?? new JsonObject();
                obj["apiSettings"] = JsonSerializer.SerializeToNode(BuildApiSettings(draft)) ?? new JsonObject();
            }
            else
            {
                AnalyzerLaunchItem analyzer = draft.Analyzer!;
                Dictionary<string, object?> outputSettings = BuildOutputSettings(draft);
                Dictionary<string, object?> analyzerSettings = new Dictionary<string, object?>(analyzer.Settings, StringComparer.OrdinalIgnoreCase);

                obj["analyzerPath"] = analyzer.Path;
                obj["outputSettings"] = JsonSerializer.SerializeToNode(outputSettings) ?? new JsonObject();
                obj["analyzerSettings"] = JsonSerializer.SerializeToNode(analyzerSettings) ?? new JsonObject();
            }

            File.WriteAllText(filePath, obj.ToJsonString(_jsonOptions));

            return ScenarioOperationResult.Ok($"Сценарий «{scenarioName}» создан.");
        }
        catch (Exception ex)
        {
            return ScenarioOperationResult.Fail($"Не удалось создать сценарий: {ex.Message}");
        }
    }

    public ScenarioOperationResult StartScenarioNow(ScenarioDashboardItem scenario)
    {
        if (scenario == null || string.IsNullOrWhiteSpace(scenario.Path) || !File.Exists(scenario.Path))
            return ScenarioOperationResult.Fail("Файл сценария не найден.");

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(scenario.Path));
            JsonElement root = document.RootElement;

            string pipelineType = ReadString(root, "pipelineType") ?? "";
            string managerPath = ResolveManagerPath(pipelineType);
            if (string.IsNullOrWhiteSpace(managerPath) || !File.Exists(managerPath))
                return ScenarioOperationResult.Fail($"Менеджер pipeline не найден для типа сценария: {pipelineType}");

            object payload = BuildScenarioPayload(root, pipelineType);
            string inputJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false });
            string pythonPath = ResolvePythonPath(root);
            string jobName = ReadString(root, "jobName", "scenarioName", "name") ?? scenario.Name;

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = $"\"{managerPath}\"",
                WorkingDirectory = _rootPath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            startInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            startInfo.EnvironmentVariables["PYTHONUTF8"] = "1";

            Process? process = Process.Start(startInfo);
            if (process == null)
                return ScenarioOperationResult.Fail("Python-процесс не удалось создать.");

            string modulePath = string.Equals(pipelineType, "output", StringComparison.OrdinalIgnoreCase)
                ? ReadString(root, "analyzerPath") ?? ""
                : ReadString(root, "parserPath") ?? "";
            string databasePath = ReadString(root, "dbPath") ?? "";
            string typeText = string.Equals(pipelineType, "output", StringComparison.OrdinalIgnoreCase) ? "анализ" : "парсинг";
            string logPrefix = string.Equals(pipelineType, "output", StringComparison.OrdinalIgnoreCase) ? "output_pipeline_" : "input_pipeline_";

            WpfRunManagerService.Instance.RegisterProcess(
                process,
                inputJson,
                _rootPath,
                $"Сценарий: {jobName}",
                typeText,
                Path.GetFileName(modulePath),
                Path.GetFileName(databasePath),
                logPrefix,
                result => SaveScenarioRunResult(scenario.Path, result.StartedAt, result.FinishedAt, result.ExitCode, result.StandardOutput, result.StandardError));

            return ScenarioOperationResult.Ok($"Сценарий «{jobName}» запущен вручную. Карточка процесса появится в хабе автоматически, а после завершения запись появится в истории сценария.");
        }
        catch (Exception ex)
        {
            return ScenarioOperationResult.Fail($"Не удалось запустить сценарий: {ex.Message}");
        }
    }

    public ScenarioEditDraft LoadEditDraft(ScenarioDashboardItem scenario)
    {
        if (scenario == null || string.IsNullOrWhiteSpace(scenario.Path) || !File.Exists(scenario.Path))
            throw new FileNotFoundException("Файл сценария не найден.");

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(scenario.Path));
        JsonElement root = document.RootElement;

        string scheduleType = ReadNestedString(root, "schedule", "type") ?? "manual";
        int everyHours = ReadNestedInt(root, "schedule", "everyHours") ?? 0;
        string pipelineType = ReadString(root, "pipelineType") ?? "input";
        string dbPath = ResolveProjectPath(ReadString(root, "dbPath") ?? "");
        string parserPath = ResolveProjectPath(ReadString(root, "parserPath") ?? "");
        string analyzerPath = ResolveProjectPath(ReadString(root, "analyzerPath") ?? "");
        string modulePath = string.IsNullOrWhiteSpace(parserPath) ? analyzerPath : parserPath;

        Dictionary<string, object?> parserSettings = ReadObjectAsDictionary(root, "parserSettings");
        Dictionary<string, object?> apiSettings = ReadObjectAsDictionary(root, "apiSettings");
        Dictionary<string, object?> outputSettings = ReadObjectAsDictionary(root, "outputSettings");

        return new ScenarioEditDraft
        {
            FilePath = scenario.Path,
            FileName = Path.GetFileName(scenario.Path),
            Name = ReadString(root, "jobName", "scenarioName", "name") ?? scenario.Name,
            Enabled = ReadBool(root, "enabled") ?? true,
            IsManualOnly = string.Equals(scheduleType, "manual", StringComparison.OrdinalIgnoreCase) || everyHours <= 0,
            EveryHours = everyHours > 0 ? everyHours : 24,
            PipelineType = string.Equals(pipelineType, "output", StringComparison.OrdinalIgnoreCase) ? "output" : "input",
            DatabasePath = dbPath,
            ParserPath = parserPath,
            AnalyzerPath = analyzerPath,
            PipelineText = FormatPipelineType(pipelineType),
            DatabaseName = Path.GetFileName(dbPath),
            ModuleName = Path.GetFileName(modulePath),
            StartUrl = ReadStringValue(parserSettings, "startUrl") ?? "",
            MaxCars = ReadIntValue(parserSettings, "maxCars") ?? 20,
            StreamBatchSize = ReadIntValue(parserSettings, "streamBatchSize") ?? ReadIntValue(parserSettings, "batchSize") ?? 5,
            RequestDelaySeconds = ReadDoubleValue(parserSettings, "requestDelaySeconds") ?? 1.2,
            RetryCount = ReadIntValue(parserSettings, "retryCount") ?? 3,
            RateLimitDelaySeconds = ReadDoubleValue(parserSettings, "rateLimitDelaySeconds") ?? 5,
            BrandCountryEnrichment = ReadBoolValue(apiSettings, "brandCountryEnrichment") ?? true,
            TransmissionNormalization = ReadBoolValue(apiSettings, "transmissionNormalization") ?? false,
            DriveTypeNormalization = ReadBoolValue(apiSettings, "driveTypeNormalization") ?? false,
            FuelTypeNormalization = ReadBoolValue(apiSettings, "fuelTypeNormalization") ?? false,
            LatestOnly = ReadBoolValue(outputSettings, "latestOnly") ?? true,
            OnlyChanged = ReadBoolValue(outputSettings, "onlyChanged") ?? false,
            Brand = ReadStringValue(outputSettings, "brand") ?? "",
            Model = ReadStringValue(outputSettings, "model") ?? "",
            SaleRegion = ReadStringValue(outputSettings, "saleRegion") ?? "",
            YearFrom = ReadIntValue(outputSettings, "yearFrom"),
            YearTo = ReadIntValue(outputSettings, "yearTo")
        };
    }

    public ScenarioOperationResult SaveEditDraft(ScenarioEditDraft draft)
    {
        if (draft == null || string.IsNullOrWhiteSpace(draft.FilePath) || !File.Exists(draft.FilePath))
            return ScenarioOperationResult.Fail("Файл сценария не найден.");

        string newName = (draft.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(newName))
            return ScenarioOperationResult.Fail("Название сценария не может быть пустым.");

        if (draft.Database == null || string.IsNullOrWhiteSpace(draft.Database.Path))
            return ScenarioOperationResult.Fail("Выбери базу данных.");

        bool isInput = string.Equals(draft.PipelineType, "input", StringComparison.OrdinalIgnoreCase);
        bool isOutput = string.Equals(draft.PipelineType, "output", StringComparison.OrdinalIgnoreCase);

        if (!isInput && !isOutput)
            return ScenarioOperationResult.Fail("Выбери тип сценария: парсинг или анализ.");

        if (isInput && (draft.Parser == null || string.IsNullOrWhiteSpace(draft.Parser.Path)))
            return ScenarioOperationResult.Fail("Выбери парсер.");

        if (isOutput && (draft.Analyzer == null || string.IsNullOrWhiteSpace(draft.Analyzer.Path)))
            return ScenarioOperationResult.Fail("Выбери анализатор.");

        int everyHours = Math.Max(0, draft.EveryHours);
        if (!draft.IsManualOnly && everyHours <= 0)
            return ScenarioOperationResult.Fail("Интервал автоповтора должен быть больше нуля.");

        if (isInput)
        {
            if (string.IsNullOrWhiteSpace(draft.StartUrl))
                return ScenarioOperationResult.Fail("Для сценария парсинга нужен стартовый URL.");
            if (draft.MaxCars < 0)
                return ScenarioOperationResult.Fail("Максимум объявлений должен быть 0 или больше.");
            if (draft.StreamBatchSize <= 0)
                return ScenarioOperationResult.Fail("Размер пакета должен быть больше нуля.");
        }

        try
        {
            JsonNode? node = JsonNode.Parse(File.ReadAllText(draft.FilePath));
            if (node is not JsonObject obj)
                return ScenarioOperationResult.Fail("Файл сценария не удалось разобрать как JSON-объект.");

            if (obj.ContainsKey("jobName"))
                obj["jobName"] = newName;
            else if (obj.ContainsKey("scenarioName"))
                obj["scenarioName"] = newName;
            else
                obj["name"] = newName;

            obj["enabled"] = draft.Enabled;
            obj["pipelineType"] = isInput ? "input" : "output";
            obj["dbPath"] = draft.Database.Path;

            if (obj["runtimeSettings"] is not JsonObject)
            {
                obj["runtimeSettings"] = new JsonObject
                {
                    ["pythonPath"] = ResolveDefaultPythonPath(),
                    ["javaPath"] = ResolveDefaultJavaPath()
                };
            }

            JsonObject schedule;
            if (obj["schedule"] is JsonObject existingSchedule)
                schedule = existingSchedule;
            else
            {
                schedule = new JsonObject();
                obj["schedule"] = schedule;
            }

            if (draft.IsManualOnly)
            {
                schedule["type"] = "manual";
                schedule["everyHours"] = 0;
                obj["nextRunAt"] = "";
            }
            else
            {
                schedule["type"] = "interval";
                schedule["everyHours"] = everyHours;

                string currentNextRun = ReadStringFromNode(obj, "nextRunAt");
                if (string.IsNullOrWhiteSpace(currentNextRun))
                    obj["nextRunAt"] = FormatJobTime(DateTime.Now.AddHours(everyHours));
            }

            if (isInput)
            {
                ParserLaunchItem parser = draft.Parser!;
                obj["parserPath"] = parser.Path;
                obj["parserSettings"] = JsonSerializer.SerializeToNode(BuildParserSettings(draft)) ?? new JsonObject();
                obj["apiSettings"] = JsonSerializer.SerializeToNode(BuildApiSettings(draft)) ?? new JsonObject();

                obj.Remove("analyzerPath");
                obj.Remove("outputSettings");
                obj.Remove("analyzerSettings");
            }
            else
            {
                AnalyzerLaunchItem analyzer = draft.Analyzer!;
                obj["analyzerPath"] = analyzer.Path;
                obj["outputSettings"] = JsonSerializer.SerializeToNode(BuildOutputSettings(draft)) ?? new JsonObject();
                obj["analyzerSettings"] = JsonSerializer.SerializeToNode(new Dictionary<string, object?>(analyzer.Settings, StringComparer.OrdinalIgnoreCase)) ?? new JsonObject();

                obj.Remove("parserPath");
                obj.Remove("parserSettings");
                obj.Remove("apiSettings");
            }

            File.WriteAllText(draft.FilePath, obj.ToJsonString(_jsonOptions));

            return ScenarioOperationResult.Ok($"Сценарий «{newName}» сохранён.");
        }
        catch (Exception ex)
        {
            return ScenarioOperationResult.Fail($"Не удалось сохранить сценарий: {ex.Message}");
        }
    }

    public ScenarioOperationResult ToggleScenario(ScenarioDashboardItem scenario)
    {
        if (scenario == null || string.IsNullOrWhiteSpace(scenario.Path) || !File.Exists(scenario.Path))
            return ScenarioOperationResult.Fail("Файл сценария не найден.");

        try
        {
            JsonNode? node = JsonNode.Parse(File.ReadAllText(scenario.Path));
            if (node is not JsonObject obj)
                return ScenarioOperationResult.Fail("Файл сценария не удалось разобрать как JSON-объект.");

            bool newValue = !scenario.Enabled;
            obj["enabled"] = newValue;

            File.WriteAllText(scenario.Path, obj.ToJsonString(_jsonOptions));

            string status = newValue ? "включён" : "выключен";
            return ScenarioOperationResult.Ok($"Сценарий «{scenario.Name}» теперь {status}.");
        }
        catch (Exception ex)
        {
            return ScenarioOperationResult.Fail($"Не удалось изменить сценарий: {ex.Message}");
        }
    }

    public ScenarioOperationResult DeleteScenario(ScenarioDashboardItem scenario)
    {
        if (scenario == null || string.IsNullOrWhiteSpace(scenario.Path) || !File.Exists(scenario.Path))
            return ScenarioOperationResult.Fail("Файл сценария не найден.");

        try
        {
            File.Delete(scenario.Path);
            return ScenarioOperationResult.Ok($"Сценарий «{scenario.Name}» удалён.");
        }
        catch (Exception ex)
        {
            return ScenarioOperationResult.Fail($"Не удалось удалить сценарий: {ex.Message}");
        }
    }

    public void OpenJobsFolder()
    {
        Directory.CreateDirectory(GetJobsFolderPath());
        OpenPath(GetJobsFolderPath());
    }

    public ScenarioOperationResult OpenScenarioFile(ScenarioDashboardItem scenario)
    {
        if (scenario == null || string.IsNullOrWhiteSpace(scenario.Path) || !File.Exists(scenario.Path))
            return ScenarioOperationResult.Fail("Файл сценария не найден.");

        try
        {
            OpenPath(scenario.Path);
            return ScenarioOperationResult.Ok($"Открыт файл сценария: {scenario.FileName}");
        }
        catch (Exception ex)
        {
            return ScenarioOperationResult.Fail($"Не удалось открыть файл сценария: {ex.Message}");
        }
    }

    public string BuildScenarioDetailsText(ScenarioDashboardItem scenario)
    {
        if (scenario == null)
            return "Сценарий не выбран.";

        StringBuilder builder = new StringBuilder();
        builder.AppendLine(scenario.Name);
        builder.AppendLine();
        builder.AppendLine($"Статус: {scenario.StatusText}");
        builder.AppendLine($"Тип: {scenario.PipelineText}");
        builder.AppendLine($"Модуль: {ValueOrDash(scenario.ModuleName)}");
        builder.AppendLine($"База: {ValueOrDash(scenario.DatabaseName)}");
        builder.AppendLine($"Расписание: {ValueOrDash(scenario.ScheduleText)}");
        builder.AppendLine($"Последний запуск: {ValueOrDash(scenario.LastRunText)}");
        builder.AppendLine($"Следующий запуск: {ValueOrDash(scenario.NextRunText)}");
        builder.AppendLine($"Создан: {ValueOrDash(scenario.CreatedText)}");
        builder.AppendLine($"Файл: {scenario.FileName}");
        builder.AppendLine(scenario.Path);

        return builder.ToString();
    }

    public string BuildScenarioHistoryText(ScenarioDashboardItem scenario)
    {
        if (scenario == null)
            return "Сценарий не выбран.";

        string historyPath = GetHistoryPath(scenario);
        if (string.IsNullOrWhiteSpace(historyPath) || !File.Exists(historyPath))
            return "У сценария пока нет истории запусков.";

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(historyPath));
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return "Файл истории найден, но его структура не похожа на список запусков.";

            List<JsonElement> records = document.RootElement.EnumerateArray().ToList();
            if (records.Count == 0)
                return "У сценария пока нет истории запусков.";

            StringBuilder builder = new StringBuilder();
            builder.AppendLine($"История запусков: {scenario.Name}");
            builder.AppendLine($"Всего записей: {records.Count}. Показаны последние 10.");
            builder.AppendLine();

            foreach (JsonElement record in records.Skip(Math.Max(0, records.Count - 10)).Reverse())
            {
                string startedAt = FormatDateTime(ReadString(record, "startedAt") ?? "");
                string status = ReadString(record, "status") ?? "unknown";
                string message = ReadString(record, "message") ?? "";
                string module = ReadString(record, "moduleName") ?? "";
                string duration = ReadDouble(record, "durationSeconds") is double seconds
                    ? $"{seconds:0.##} сек."
                    : "—";

                builder.AppendLine($"[{FormatStatus(status)}] {startedAt} · {duration}");
                if (!string.IsNullOrWhiteSpace(module))
                    builder.AppendLine($"Модуль: {module}");
                if (!string.IsNullOrWhiteSpace(message))
                    builder.AppendLine(message);
                builder.AppendLine();
            }

            return builder.ToString();
        }
        catch (Exception ex)
        {
            return $"Историю не удалось прочитать: {ex.Message}";
        }
    }

    private ScenarioDashboardItem? ReadScenario(string path)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = document.RootElement;

            string name = ReadString(root, "jobName", "scenarioName", "name") ?? Path.GetFileNameWithoutExtension(path);
            string id = ReadString(root, "jobId", "scenarioId", "id") ?? "";
            bool enabled = ReadBool(root, "enabled") ?? true;
            string pipelineType = ReadString(root, "pipelineType") ?? "";
            string dbPath = ReadString(root, "dbPath") ?? "";
            string parserPath = ReadString(root, "parserPath") ?? "";
            string analyzerPath = ReadString(root, "analyzerPath") ?? "";
            string modulePath = string.IsNullOrWhiteSpace(parserPath) ? analyzerPath : parserPath;
            string moduleName = string.IsNullOrWhiteSpace(modulePath) ? "" : Path.GetFileName(modulePath);
            string databaseName = string.IsNullOrWhiteSpace(dbPath) ? "" : Path.GetFileName(dbPath);
            string scheduleType = ReadNestedString(root, "schedule", "type") ?? "";
            int everyHours = ReadNestedInt(root, "schedule", "everyHours") ?? 0;
            string lastRunAt = ReadString(root, "lastRunAt") ?? "";
            string nextRunAt = ReadString(root, "nextRunAt") ?? "";
            string createdAt = ReadString(root, "createdAt") ?? "";

            DashboardStateKind kind = GetStateKind(enabled, scheduleType, everyHours);
            string statusText = GetStatusText(enabled, scheduleType, everyHours);
            string scheduleText = FormatSchedule(scheduleType, everyHours);

            return new ScenarioDashboardItem
            {
                Id = id,
                Name = name,
                FileName = Path.GetFileName(path),
                Path = path,
                Enabled = enabled,
                StatusText = statusText,
                Details = BuildCompactDetails(pipelineType, moduleName, databaseName, scheduleText, nextRunAt),
                PipelineType = pipelineType,
                PipelineText = FormatPipelineType(pipelineType),
                ModuleName = moduleName,
                DatabaseName = databaseName,
                ScheduleText = scheduleText,
                LastRunText = string.IsNullOrWhiteSpace(lastRunAt) ? "ещё не запускался" : FormatDateTime(lastRunAt),
                NextRunText = FormatNextRun(enabled, scheduleType, everyHours, nextRunAt),
                CreatedText = FormatDateTime(createdAt),
                ToggleActionText = enabled ? "Выключить" : "Включить",
                CanOpenFile = true,
                CanShowHistory = true,
                CanRun = IsRunnableScenario(pipelineType, parserPath, analyzerPath, dbPath),
                CanEdit = true,
                ScheduleEveryHours = everyHours,
                IsManualOnly = string.Equals(scheduleType, "manual", StringComparison.OrdinalIgnoreCase) || everyHours <= 0,
                StateKind = kind
            };
        }
        catch
        {
            return new ScenarioDashboardItem
            {
                Name = Path.GetFileNameWithoutExtension(path),
                FileName = Path.GetFileName(path),
                Path = path,
                Enabled = false,
                StatusText = "ошибка чтения",
                Details = "Файл сценария не удалось разобрать как JSON.",
                ToggleActionText = "—",
                CanOpenFile = true,
                CanRun = false,
                CanEdit = false,
                StateKind = DashboardStateKind.Error
            };
        }
    }

    private async Task RunScenarioProcessAsync(Process process, string inputJson, string scenarioPath)
    {
        DateTime startedAt = DateTime.Now;
        string output = "";
        string error = "";
        int exitCode = -1;

        try
        {
            await process.StandardInput.WriteAsync(inputJson);
            process.StandardInput.Close();

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();
            await Task.WhenAll(stdoutTask, stderrTask);

            output = stdoutTask.Result;
            error = stderrTask.Result;
            exitCode = process.ExitCode;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Фоновый запуск не должен ронять UI.
            }
        }
        finally
        {
            process.Dispose();
        }

        DateTime finishedAt = DateTime.Now;
        SaveScenarioRunResult(scenarioPath, startedAt, finishedAt, exitCode, output, error);
    }

    private void SaveScenarioRunResult(string scenarioPath, DateTime startedAt, DateTime finishedAt, int exitCode, string output, string error)
    {
        try
        {
            JsonNode? node = JsonNode.Parse(File.ReadAllText(scenarioPath));
            if (node is not JsonObject obj)
                return;

            string jobId = ReadStringFromNode(obj, "jobId", "scenarioId", "id");
            if (string.IsNullOrWhiteSpace(jobId))
            {
                jobId = Path.GetFileNameWithoutExtension(scenarioPath);
                obj["jobId"] = jobId;
            }

            string jobName = ReadStringFromNode(obj, "jobName", "scenarioName", "name");
            if (string.IsNullOrWhiteSpace(jobName))
                jobName = Path.GetFileNameWithoutExtension(scenarioPath);

            string pipelineType = ReadStringFromNode(obj, "pipelineType");
            string dbPath = ReadStringFromNode(obj, "dbPath");
            string modulePath = string.Equals(pipelineType, "output", StringComparison.OrdinalIgnoreCase)
                ? ReadStringFromNode(obj, "analyzerPath")
                : ReadStringFromNode(obj, "parserPath");

            bool success = IsPipelineRunSuccessful(exitCode, output, error);
            JsonObject record = new JsonObject
            {
                ["jobId"] = jobId,
                ["jobName"] = jobName,
                ["startedAt"] = FormatJobTime(startedAt),
                ["finishedAt"] = FormatJobTime(finishedAt),
                ["durationSeconds"] = Math.Round((finishedAt - startedAt).TotalSeconds, 2),
                ["status"] = success ? "success" : "error",
                ["pipelineType"] = pipelineType,
                ["moduleName"] = Path.GetFileName(modulePath),
                ["dbName"] = Path.GetFileName(dbPath),
                ["exitCode"] = exitCode,
                ["message"] = success ? "Сценарий выполнен успешно" : "Сценарий завершился с ошибкой",
                ["outputPreview"] = BuildPreview(output),
                ["errorPreview"] = BuildPreview(error)
            };

            AppendHistoryRecord(jobId, jobName, record);

            obj["lastRunAt"] = FormatJobTime(finishedAt);
            int everyHours = ReadEveryHoursFromNode(obj);
            if (everyHours > 0)
                obj["nextRunAt"] = FormatJobTime(finishedAt.AddHours(everyHours));

            File.WriteAllText(scenarioPath, obj.ToJsonString(_jsonOptions));
        }
        catch
        {
            // Ошибка записи истории не должна влиять на выполненный pipeline.
        }
    }

    private void AppendHistoryRecord(string jobId, string jobName, JsonObject record)
    {
        Directory.CreateDirectory(Path.Combine(_rootPath, "Logs", "JobRuns"));
        string safeId = string.IsNullOrWhiteSpace(jobId)
            ? SanitizeFileName(jobName).Replace(' ', '_')
            : SanitizeFileName(jobId);
        string historyPath = Path.Combine(_rootPath, "Logs", "JobRuns", $"{safeId}_runs.json");

        JsonArray records = new JsonArray();
        if (File.Exists(historyPath))
        {
            try
            {
                JsonNode? current = JsonNode.Parse(File.ReadAllText(historyPath));
                if (current is JsonArray array)
                {
                    foreach (JsonNode? item in array)
                        records.Add(item?.DeepClone());
                }
            }
            catch
            {
                records = new JsonArray();
            }
        }

        records.Add(record);
        File.WriteAllText(historyPath, records.ToJsonString(_jsonOptions));
    }

    private object BuildScenarioPayload(JsonElement root, string pipelineType)
    {
        string dbPath = ResolveProjectPath(ReadString(root, "dbPath") ?? "");
        string pythonPath = ResolvePythonPath(root);
        string javaPath = ResolveJavaPath(root);

        if (string.Equals(pipelineType, "input", StringComparison.OrdinalIgnoreCase))
        {
            string parserPath = ResolveProjectPath(ReadString(root, "parserPath") ?? "");
            Dictionary<string, object?> parserSettings = ReadObjectAsDictionary(root, "parserSettings");
            Dictionary<string, object?> apiSettings = ReadObjectAsDictionary(root, "apiSettings");
            EnsureInputDefaults(parserSettings, apiSettings);

            return new
            {
                parser = new
                {
                    modulePath = parserPath,
                    parserPath,
                    runtime = DetectRuntime(parserPath),
                    python = pythonPath,
                    java = javaPath
                },
                parserSettings,
                apiSettings,
                runtimeSettings = new
                {
                    pythonPath,
                    javaPath
                },
                dbPath,
                configPath = Path.Combine(_rootPath, "Configs")
            };
        }

        if (string.Equals(pipelineType, "output", StringComparison.OrdinalIgnoreCase))
        {
            string analyzerPath = ResolveProjectPath(ReadString(root, "analyzerPath") ?? "");
            Dictionary<string, object?> outputSettings = ReadObjectAsDictionary(root, "outputSettings");
            Dictionary<string, object?> analyzerSettings = ReadObjectAsDictionary(root, "analyzerSettings");
            EnsureOutputDefaults(outputSettings);

            return new
            {
                analyzer = new
                {
                    modulePath = analyzerPath,
                    analyzerPath,
                    runtime = DetectRuntime(analyzerPath),
                    python = pythonPath,
                    java = javaPath,
                    settings = analyzerSettings
                },
                outputSettings,
                runtimeSettings = new
                {
                    pythonPath,
                    javaPath
                },
                dbPath,
                configPath = Path.Combine(_rootPath, "Configs")
            };
        }

        throw new InvalidOperationException($"Неизвестный тип сценария: {pipelineType}");
    }

    private void EnsureInputDefaults(Dictionary<string, object?> parserSettings, Dictionary<string, object?> apiSettings)
    {
        if (!parserSettings.ContainsKey("startUrl"))
            parserSettings["startUrl"] = "";
        if (!parserSettings.ContainsKey("maxCars"))
            parserSettings["maxCars"] = 20;
        if (!parserSettings.ContainsKey("streamBatchSize") && !parserSettings.ContainsKey("batchSize"))
            parserSettings["streamBatchSize"] = 5;
        if (!parserSettings.ContainsKey("requestDelaySeconds"))
            parserSettings["requestDelaySeconds"] = 1.2;
        if (!parserSettings.ContainsKey("retryCount"))
            parserSettings["retryCount"] = 3;
        if (!parserSettings.ContainsKey("rateLimitDelaySeconds"))
            parserSettings["rateLimitDelaySeconds"] = 5;

        if (!apiSettings.ContainsKey("brandCountryEnrichment"))
            apiSettings["brandCountryEnrichment"] = true;
        if (!apiSettings.ContainsKey("transmissionNormalization"))
            apiSettings["transmissionNormalization"] = false;
        if (!apiSettings.ContainsKey("driveTypeNormalization"))
            apiSettings["driveTypeNormalization"] = false;
        if (!apiSettings.ContainsKey("fuelTypeNormalization"))
            apiSettings["fuelTypeNormalization"] = false;
    }

    private void EnsureOutputDefaults(Dictionary<string, object?> outputSettings)
    {
        if (!outputSettings.ContainsKey("latestOnly"))
            outputSettings["latestOnly"] = true;
        if (!outputSettings.ContainsKey("onlyChanged"))
            outputSettings["onlyChanged"] = false;
    }


    private Dictionary<string, object?> BuildParserSettings(ScenarioCreateDraft draft)
    {
        ParserLaunchSettings source = draft.Parser?.Settings ?? new ParserLaunchSettings();
        Dictionary<string, object?> parserSettings = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["startUrl"] = draft.StartUrl.Trim(),
            ["maxCars"] = draft.MaxCars,
            ["streamBatchSize"] = draft.StreamBatchSize,
            ["batchSize"] = draft.StreamBatchSize,
            ["requestDelaySeconds"] = draft.RequestDelaySeconds,
            ["retryCount"] = draft.RetryCount,
            ["rateLimitDelaySeconds"] = draft.RateLimitDelaySeconds
        };

        foreach (KeyValuePair<string, object?> item in source.ExtraSettings)
        {
            if (!parserSettings.ContainsKey(item.Key))
                parserSettings[item.Key] = item.Value;
        }

        return parserSettings;
    }

    private Dictionary<string, object?> BuildApiSettings(ScenarioCreateDraft draft)
    {
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["brandCountryEnrichment"] = draft.BrandCountryEnrichment,
            ["transmissionNormalization"] = draft.TransmissionNormalization,
            ["driveTypeNormalization"] = draft.DriveTypeNormalization,
            ["fuelTypeNormalization"] = draft.FuelTypeNormalization
        };
    }

    private Dictionary<string, object?> BuildOutputSettings(ScenarioCreateDraft draft)
    {
        Dictionary<string, object?> outputSettings = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["latestOnly"] = draft.LatestOnly,
            ["onlyChanged"] = draft.OnlyChanged,
            ["brand"] = CleanOptionalText(draft.Brand),
            ["model"] = CleanOptionalText(draft.Model),
            ["saleRegion"] = CleanOptionalText(draft.SaleRegion),
            ["yearFrom"] = draft.YearFrom,
            ["yearTo"] = draft.YearTo
        };

        foreach (KeyValuePair<string, object?> item in draft.OutputExtraSettings)
        {
            if (!outputSettings.ContainsKey(item.Key))
                outputSettings[item.Key] = item.Value;
        }

        return outputSettings;
    }


    private string? ReadStringValue(Dictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out object? value) || value == null)
            return null;

        string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private int? ReadIntValue(Dictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out object? value) || value == null)
            return null;

        if (value is int intValue)
            return intValue;

        if (value is long longValue)
            return (int)longValue;

        if (value is double doubleValue)
            return (int)doubleValue;

        if (int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out int parsed))
            return parsed;

        return null;
    }

    private double? ReadDoubleValue(Dictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out object? value) || value == null)
            return null;

        if (value is double doubleValue)
            return doubleValue;

        if (value is float floatValue)
            return floatValue;

        if (value is int intValue)
            return intValue;

        if (value is long longValue)
            return longValue;

        string text = (Convert.ToString(value, CultureInfo.InvariantCulture) ?? "").Replace(",", ".");
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            return parsed;

        return null;
    }

    private bool? ReadBoolValue(Dictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out object? value) || value == null)
            return null;

        if (value is bool boolValue)
            return boolValue;

        if (bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out bool parsed))
            return parsed;

        return null;
    }

    private string BuildNewJobId(string scenarioName)
    {
        string safeName = SanitizeFileName(scenarioName).Trim();
        safeName = string.IsNullOrWhiteSpace(safeName) ? "scenario" : safeName.Replace(' ', '_');
        return $"{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}";
    }

    private string BuildNewScenarioPath(string jobId)
    {
        string safeId = SanitizeFileName(jobId);
        string jobsPath = GetJobsFolderPath();
        string filePath = Path.Combine(jobsPath, safeId + ".json");

        if (!File.Exists(filePath))
            return filePath;

        for (int i = 2; i < 1000; i++)
        {
            string candidate = Path.Combine(jobsPath, $"{safeId}_{i}.json");
            if (!File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(jobsPath, safeId + "_copy.json");
    }

    private string ResolveDefaultPythonPath()
    {
        string bundled = Path.Combine(_rootPath, "Python", "python.exe");
        if (File.Exists(bundled))
            return bundled;

        string? configured = ReadStringFromJsonFile(Path.Combine(_rootPath, "Configs", "ParserDefaultSettings.json"), "pythonPath");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        return "python";
    }

    private string ResolveDefaultJavaPath()
    {
        string? configured = ReadStringFromJsonFile(Path.Combine(_rootPath, "Configs", "ParserDefaultSettings.json"), "javaPath");
        return string.IsNullOrWhiteSpace(configured) ? "java" : configured;
    }

    private string? CleanOptionalText(string value)
    {
        value = (value ?? "").Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private string ResolveManagerPath(string pipelineType)
    {
        if (string.Equals(pipelineType, "input", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(_rootPath, "PipelineManagers", "InputPipelineManager.py");

        if (string.Equals(pipelineType, "output", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(_rootPath, "PipelineManagers", "OutputPipelineManager.py");

        return "";
    }

    private string ResolvePythonPath(JsonElement root)
    {
        string runtimePython = ReadNestedString(root, "runtimeSettings", "pythonPath") ?? "";
        if (!string.IsNullOrWhiteSpace(runtimePython) && File.Exists(runtimePython))
            return runtimePython;

        string bundled = Path.Combine(_rootPath, "Python", "python.exe");
        if (File.Exists(bundled))
            return bundled;

        string? configured = ReadStringFromJsonFile(Path.Combine(_rootPath, "Configs", "ParserDefaultSettings.json"), "pythonPath");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        return "python";
    }

    private string ResolveJavaPath(JsonElement root)
    {
        string runtimeJava = ReadNestedString(root, "runtimeSettings", "javaPath") ?? "";
        if (!string.IsNullOrWhiteSpace(runtimeJava))
            return runtimeJava;

        string? configured = ReadStringFromJsonFile(Path.Combine(_rootPath, "Configs", "ParserDefaultSettings.json"), "javaPath");
        return string.IsNullOrWhiteSpace(configured) ? "java" : configured;
    }

    private string ResolveProjectPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";

        if (Path.IsPathRooted(path))
            return path;

        return Path.GetFullPath(Path.Combine(_rootPath, path));
    }

    private Dictionary<string, object?> ReadObjectAsDictionary(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement element) || element.ValueKind != JsonValueKind.Object)
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        Dictionary<string, object?> result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (property.NameEquals("metadata") || property.NameEquals("settingsSchema") || property.NameEquals("settings"))
                continue;

            result[property.Name] = ConvertJsonElement(property.Value);
        }

        return result;
    }

    private object? ConvertJsonElement(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt32(out int intValue) => intValue,
            JsonValueKind.Number when value.TryGetDouble(out double doubleValue) => doubleValue,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array or JsonValueKind.Object => JsonSerializer.Deserialize<object>(value.GetRawText()),
            _ => value.ToString()
        };
    }

    private string GetHistoryPath(ScenarioDashboardItem scenario)
    {
        string jobRunsPath = Path.Combine(_rootPath, "Logs", "JobRuns");
        string safeId = !string.IsNullOrWhiteSpace(scenario.Id)
            ? SanitizeFileName(scenario.Id)
            : SanitizeFileName(scenario.Name).Replace(' ', '_');

        return Path.Combine(jobRunsPath, $"{safeId}_runs.json");
    }

    private bool IsRunnableScenario(string pipelineType, string parserPath, string analyzerPath, string dbPath)
    {
        if (string.IsNullOrWhiteSpace(dbPath))
            return false;

        if (string.Equals(pipelineType, "input", StringComparison.OrdinalIgnoreCase))
            return !string.IsNullOrWhiteSpace(parserPath);

        if (string.Equals(pipelineType, "output", StringComparison.OrdinalIgnoreCase))
            return !string.IsNullOrWhiteSpace(analyzerPath);

        return false;
    }

    private bool IsPipelineRunSuccessful(int exitCode, string output, string error)
    {
        if (exitCode != 0)
            return false;

        if (!string.IsNullOrWhiteSpace(error))
            return false;

        output ??= "";
        if (output.Contains("\"status\": \"error\"") || output.Contains("\"status\":\"error\""))
            return false;

        return true;
    }

    private int ReadEveryHoursFromNode(JsonObject obj)
    {
        if (obj["schedule"] is not JsonObject schedule)
            return 0;

        if (schedule["everyHours"] is JsonValue value && value.TryGetValue(out int result))
            return result;

        return 0;
    }

    private string BuildPreview(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        string normalized = value.Replace("\r", " ").Replace("\n", " ").Trim();
        if (normalized.Length <= 500)
            return normalized;

        return normalized.Substring(0, 500) + "...";
    }

    private string BuildCompactDetails(string pipelineType, string moduleName, string databaseName, string scheduleText, string nextRunAt)
    {
        return string.Join(" · ", new[]
        {
            FormatPipelineType(pipelineType),
            string.IsNullOrWhiteSpace(moduleName) ? "модуль не задан" : moduleName,
            string.IsNullOrWhiteSpace(databaseName) ? "база не задана" : databaseName,
            string.IsNullOrWhiteSpace(scheduleText) ? "расписание не задано" : scheduleText,
            string.IsNullOrWhiteSpace(nextRunAt) ? "next: —" : $"next: {FormatDateTime(nextRunAt)}"
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private DashboardStateKind GetStateKind(bool enabled, string scheduleType, int everyHours)
    {
        if (!enabled)
            return DashboardStateKind.Error;

        if (string.Equals(scheduleType, "manual", StringComparison.OrdinalIgnoreCase) || everyHours <= 0)
            return DashboardStateKind.Neutral;

        return DashboardStateKind.Success;
    }

    private string GetStatusText(bool enabled, string scheduleType, int everyHours)
    {
        if (!enabled)
            return "выключен";

        if (string.Equals(scheduleType, "manual", StringComparison.OrdinalIgnoreCase) || everyHours <= 0)
            return "только ручной запуск";

        return "включён";
    }

    private string FormatSchedule(string scheduleType, int everyHours)
    {
        if (string.Equals(scheduleType, "manual", StringComparison.OrdinalIgnoreCase) || everyHours <= 0)
            return "ручной запуск";

        return $"каждые {everyHours} ч.";
    }

    private string FormatNextRun(bool enabled, string scheduleType, int everyHours, string nextRunAt)
    {
        if (!enabled)
            return "сценарий выключен";

        if (string.Equals(scheduleType, "manual", StringComparison.OrdinalIgnoreCase) || everyHours <= 0)
            return "только вручную";

        return string.IsNullOrWhiteSpace(nextRunAt) ? "не задан" : FormatDateTime(nextRunAt);
    }

    private string FormatPipelineType(string pipelineType)
    {
        if (string.Equals(pipelineType, "input", StringComparison.OrdinalIgnoreCase))
            return "парсинг";

        if (string.Equals(pipelineType, "output", StringComparison.OrdinalIgnoreCase))
            return "анализ";

        return string.IsNullOrWhiteSpace(pipelineType) ? "тип не задан" : pipelineType;
    }

    private string FormatStatus(string status)
    {
        if (string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
            return "успешно";

        if (string.Equals(status, "error", StringComparison.OrdinalIgnoreCase))
            return "ошибка";

        return status;
    }

    private string FormatJobTime(DateTime value)
    {
        return value.ToString("yyyy-MM-ddTHH:mm:ss");
    }

    private string FormatDateTime(string value)
    {
        if (DateTime.TryParse(value, out DateTime result))
            return result.ToString("yyyy-MM-dd HH:mm:ss");

        return string.IsNullOrWhiteSpace(value) ? "—" : value;
    }

    private string ValueOrDash(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "—" : value;
    }

    private string SanitizeFileName(string value)
    {
        string safeName = value;
        foreach (char invalidChar in Path.GetInvalidFileNameChars())
            safeName = safeName.Replace(invalidChar, '_');

        return safeName;
    }

    private string? ReadString(JsonElement element, params string[] names)
    {
        foreach (string name in names)
        {
            if (element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        }

        return null;
    }

    private bool? ReadBool(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
            return null;

        if (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
            return value.GetBoolean();

        return null;
    }

    private string? ReadNestedString(JsonElement element, string objectName, string propertyName)
    {
        if (!element.TryGetProperty(objectName, out JsonElement nested) || nested.ValueKind != JsonValueKind.Object)
            return null;

        return ReadString(nested, propertyName);
    }

    private int? ReadNestedInt(JsonElement element, string objectName, string propertyName)
    {
        if (!element.TryGetProperty(objectName, out JsonElement nested) || nested.ValueKind != JsonValueKind.Object)
            return null;

        if (!nested.TryGetProperty(propertyName, out JsonElement value))
            return null;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result))
            return result;

        return null;
    }

    private double? ReadDouble(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
            return null;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double result))
            return result;

        return null;
    }

    private string ReadStringFromNode(JsonObject obj, params string[] names)
    {
        foreach (string name in names)
        {
            if (obj[name] is JsonValue value && value.TryGetValue<string>(out string? result) && !string.IsNullOrWhiteSpace(result))
                return result;
        }

        return "";
    }

    private string? ReadStringFromJsonFile(string path, string propertyName)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            return ReadString(document.RootElement, propertyName, "");
        }
        catch
        {
            return null;
        }
    }

    private string DetectRuntime(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".jar" => "java",
            ".exe" => "exe",
            _ => "python"
        };
    }

    private void OpenPath(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }
}

public class ScenarioOperationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";

    public static ScenarioOperationResult Ok(string message)
    {
        return new ScenarioOperationResult { Success = true, Message = message };
    }

    public static ScenarioOperationResult Fail(string message)
    {
        return new ScenarioOperationResult { Success = false, Message = message };
    }
}

public class ScenarioEditDraft : ScenarioCreateDraft
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string PipelineText { get; set; } = "";
    public string DatabaseName { get; set; } = "";
    public string ModuleName { get; set; } = "";
    public string DatabasePath { get; set; } = "";
    public string ParserPath { get; set; } = "";
    public string AnalyzerPath { get; set; } = "";
}

public class ScenarioCreateDraft
{
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool IsManualOnly { get; set; } = false;
    public int EveryHours { get; set; } = 24;
    public string PipelineType { get; set; } = "input";
    public DatabaseDashboardItem? Database { get; set; }
    public ParserLaunchItem? Parser { get; set; }
    public AnalyzerLaunchItem? Analyzer { get; set; }
    public string StartUrl { get; set; } = "";
    public int MaxCars { get; set; } = 200;
    public int StreamBatchSize { get; set; } = 100;
    public double RequestDelaySeconds { get; set; } = 1.2;
    public int RetryCount { get; set; } = 3;
    public double RateLimitDelaySeconds { get; set; } = 5;
    public bool BrandCountryEnrichment { get; set; } = true;
    public bool TransmissionNormalization { get; set; } = false;
    public bool DriveTypeNormalization { get; set; } = false;
    public bool FuelTypeNormalization { get; set; } = false;
    public bool LatestOnly { get; set; } = true;
    public bool OnlyChanged { get; set; }
    public string Brand { get; set; } = "";
    public string Model { get; set; } = "";
    public string SaleRegion { get; set; } = "";
    public int? YearFrom { get; set; }
    public int? YearTo { get; set; }
    public Dictionary<string, object?> OutputExtraSettings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

