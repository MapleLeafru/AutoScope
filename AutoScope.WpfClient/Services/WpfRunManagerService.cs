using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AutoScope.WpfClient.Models;

namespace AutoScope.WpfClient.Services;

public sealed class WpfRunManagerService
{
    private readonly object _sync = new();
    private readonly List<WpfRunRecord> _records = new();

    public static WpfRunManagerService Instance { get; } = new();

    private WpfRunManagerService()
    {
    }

    public string RegisterProcess(
        Process process,
        string inputJson,
        string rootPath,
        string name,
        string typeText,
        string moduleName,
        string databaseName,
        string logFilePrefix,
        Action<WpfRunCompletionInfo>? completed = null,
        string sourceScenarioPath = "")
    {
        WpfRunRecord record = new WpfRunRecord
        {
            Id = "ui-" + Guid.NewGuid().ToString("N"),
            ProcessId = SafeProcessId(process),
            RootPath = rootPath,
            Name = string.IsNullOrWhiteSpace(name) ? "Процесс AutoScope" : name,
            TypeText = string.IsNullOrWhiteSpace(typeText) ? "процесс" : typeText,
            ModuleName = moduleName,
            DatabaseName = databaseName,
            LogFilePrefix = logFilePrefix,
            SourceScenarioPath = NormalizePath(sourceScenarioPath),
            StartedAt = DateTime.Now,
            LastUpdatedAt = DateTime.Now,
            StatusText = "выполняется",
            Details = "Процесс создан, ожидается первый вывод.",
            StateKind = DashboardStateKind.Running,
            ExitCode = null,
            Process = process
        };

        lock (_sync)
            _records.Add(record);

        _ = Task.Run(async () => await RunProcessAsync(record, process, inputJson, completed));
        return record.Id;
    }

    public List<ProcessDashboardItem> GetProcessItems()
    {
        List<WpfRunRecord> snapshot;
        lock (_sync)
            snapshot = _records.Select(record => record.Clone()).ToList();

        foreach (WpfRunRecord record in snapshot)
        {
            if (string.IsNullOrWhiteSpace(record.LogPath))
                record.LogPath = TryFindLogPath(record);
        }

        return snapshot
            .OrderByDescending(record => record.StateKind == DashboardStateKind.Running || record.IsPaused)
            .ThenByDescending(record => record.StartedAt)
            .Select(ToDashboardItem)
            .ToList();
    }

    public bool HasActiveScenario(string scenarioPath)
    {
        string normalizedPath = NormalizePath(scenarioPath);
        if (string.IsNullOrWhiteSpace(normalizedPath))
            return false;

        lock (_sync)
        {
            return _records.Any(record =>
                record.FinishedAt == null
                && !record.StopRequested
                && !string.IsNullOrWhiteSpace(record.SourceScenarioPath)
                && string.Equals(record.SourceScenarioPath, normalizedPath, StringComparison.OrdinalIgnoreCase));
        }
    }

    public bool HasActiveScenarioName(string scenarioName)
    {
        if (string.IsNullOrWhiteSpace(scenarioName))
            return false;

        string expectedName = "Сценарий: " + scenarioName.Trim();

        lock (_sync)
        {
            return _records.Any(record =>
                record.FinishedAt == null
                && !record.StopRequested
                && string.Equals(record.Name, expectedName, StringComparison.OrdinalIgnoreCase));
        }
    }

    public bool StopProcess(string recordId, out string message)
    {
        Process? processToStop;

        lock (_sync)
        {
            WpfRunRecord? record = _records.FirstOrDefault(item => item.Id == recordId);
            if (record == null)
            {
                message = "Процесс не найден.";
                return false;
            }

            if (record.FinishedAt != null)
            {
                message = "Этот процесс уже завершён.";
                return false;
            }

            if (record.StopRequested)
            {
                message = "Остановка этого процесса уже выполняется.";
                return false;
            }

            record.StopRequested = true;
            record.IsPaused = false;
            record.StatusText = "останавливается";
            record.Details = "Пользователь запросил остановку процесса.";
            record.LastUpdatedAt = DateTime.Now;
            processToStop = record.Process;
        }

        try
        {
            if (processToStop == null || processToStop.HasExited)
            {
                message = "Процесс уже завершён.";
                return false;
            }

            processToStop.Kill(entireProcessTree: true);
            message = "Отправлена команда остановки процесса.";
            return true;
        }
        catch (Exception ex)
        {
            lock (_sync)
            {
                WpfRunRecord? record = _records.FirstOrDefault(item => item.Id == recordId);
                if (record != null)
                {
                    record.StopRequested = false;
                    record.StatusText = "выполняется";
                    record.Details = "Остановить процесс не удалось: " + ex.Message;
                    record.LastUpdatedAt = DateTime.Now;
                }
            }

            message = "Остановить процесс не удалось: " + ex.Message;
            return false;
        }
    }

    public bool PauseProcess(string recordId, out string message)
    {
        int processId;

        lock (_sync)
        {
            WpfRunRecord? record = _records.FirstOrDefault(item => item.Id == recordId);
            if (record == null)
            {
                message = "Процесс не найден.";
                return false;
            }

            if (record.FinishedAt != null || record.StopRequested)
            {
                message = "Этот процесс уже завершён или останавливается.";
                return false;
            }

            if (record.IsPaused)
            {
                message = "Процесс уже находится на паузе.";
                return false;
            }

            processId = record.ProcessId;
        }

        try
        {
            if (!WindowsProcessPauseHelper.SuspendProcessTree(processId))
            {
                message = "Не удалось найти активные потоки процесса для паузы.";
                return false;
            }

            lock (_sync)
            {
                WpfRunRecord? record = _records.FirstOrDefault(item => item.Id == recordId);
                if (record != null)
                {
                    record.IsPaused = true;
                    record.StateKind = DashboardStateKind.Warning;
                    record.StatusText = "на паузе";
                    record.Details = "Процесс приостановлен пользователем.";
                    record.LastUpdatedAt = DateTime.Now;
                }
            }

            message = "Процесс поставлен на паузу.";
            return true;
        }
        catch (Exception ex)
        {
            message = "Поставить процесс на паузу не удалось: " + ex.Message;
            return false;
        }
    }

    public bool ResumeProcess(string recordId, out string message)
    {
        int processId;

        lock (_sync)
        {
            WpfRunRecord? record = _records.FirstOrDefault(item => item.Id == recordId);
            if (record == null)
            {
                message = "Процесс не найден.";
                return false;
            }

            if (record.FinishedAt != null || record.StopRequested)
            {
                message = "Этот процесс уже завершён или останавливается.";
                return false;
            }

            if (!record.IsPaused)
            {
                message = "Процесс не находится на паузе.";
                return false;
            }

            processId = record.ProcessId;
        }

        try
        {
            if (!WindowsProcessPauseHelper.ResumeProcessTree(processId))
            {
                message = "Не удалось найти активные потоки процесса для продолжения.";
                return false;
            }

            lock (_sync)
            {
                WpfRunRecord? record = _records.FirstOrDefault(item => item.Id == recordId);
                if (record != null)
                {
                    record.IsPaused = false;
                    record.StateKind = DashboardStateKind.Running;
                    record.StatusText = "выполняется";
                    record.Details = "Процесс продолжен пользователем.";
                    record.LastUpdatedAt = DateTime.Now;
                }
            }

            message = "Процесс продолжен.";
            return true;
        }
        catch (Exception ex)
        {
            message = "Продолжить процесс не удалось: " + ex.Message;
            return false;
        }
    }


    public int ClearCompletedRecords()
    {
        lock (_sync)
        {
            int removed = _records.RemoveAll(record => record.FinishedAt != null);
            return removed;
        }
    }

    private async Task RunProcessAsync(WpfRunRecord record, Process process, string inputJson, Action<WpfRunCompletionInfo>? completed)
    {
        try
        {
            await process.StandardInput.WriteAsync(inputJson);
            process.StandardInput.Close();

            Task stdoutTask = ReadStreamAsync(record.Id, process.StandardOutput, isError: false);
            Task stderrTask = ReadStreamAsync(record.Id, process.StandardError, isError: true);

            await process.WaitForExitAsync();
            await Task.WhenAll(stdoutTask, stderrTask);

            CompleteRecord(record.Id, process.ExitCode);
        }
        catch (Exception ex)
        {
            AddLine(record.Id, ex.Message, isError: true);
            CompleteRecord(record.Id, -1);

            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Фоновый процесс не должен ронять UI.
            }
        }
        finally
        {
            WpfRunCompletionInfo completionInfo = BuildCompletionInfo(record.Id);

            try
            {
                completed?.Invoke(completionInfo);
            }
            catch
            {
                // Ошибка записи истории сценария не должна ронять UI.
            }

            process.Dispose();
        }
    }

    private async Task ReadStreamAsync(string recordId, StreamReader reader, bool isError)
    {
        while (true)
        {
            string? line = await reader.ReadLineAsync();
            if (line == null)
                break;

            AddLine(recordId, line, isError);
        }
    }

    private void AddLine(string recordId, string line, bool isError)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        lock (_sync)
        {
            WpfRunRecord? record = _records.FirstOrDefault(item => item.Id == recordId);
            if (record == null)
                return;

            if (isError)
                record.StandardError.AppendLine(line);
            else
                record.StandardOutput.AppendLine(line);

            record.LastUpdatedAt = DateTime.Now;

            if (TryApplyProgress(record, line))
                return;

            string cleaned = CleanOutputLine(line);
            if (string.IsNullOrWhiteSpace(cleaned))
                return;

            if (isError || LooksLikeError(cleaned))
            {
                record.StateKind = DashboardStateKind.Running;
                record.Details = cleaned;
                return;
            }

            record.Details = cleaned;
        }
    }

    private bool TryApplyProgress(WpfRunRecord record, string line)
    {
        int index = line.IndexOf("[PROGRESS]", StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return false;

        string json = line.Substring(index + "[PROGRESS]".Length).Trim();
        if (string.IsNullOrWhiteSpace(json))
            return true;

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            string stage = ReadString(root, "stage") ?? "";
            int current = ReadInt(root, "current") ?? 0;
            int total = ReadInt(root, "total") ?? 0;
            int percent = ReadInt(root, "percent") ?? 0;
            string message = ReadString(root, "message") ?? "";

            if (!record.IsPaused)
                record.StateKind = DashboardStateKind.Running;

            record.ProgressPercent = Math.Clamp(percent, 0, 100);
            record.ProgressText = record.ProgressPercent + "%";
            record.StageText = FormatStageText(stage, record.TypeText, DashboardStateKind.Running);
            record.CountText = total > 0 ? FormatCountText(stage, current, total) : "";
            record.IsProgressVisible = total > 0;
            record.Details = string.IsNullOrWhiteSpace(message) ? record.StageText : message;
        }
        catch
        {
            record.Details = "Получен прогресс, но строку не удалось разобрать.";
        }

        return true;
    }

    private void CompleteRecord(string recordId, int exitCode)
    {
        lock (_sync)
        {
            WpfRunRecord? record = _records.FirstOrDefault(item => item.Id == recordId);
            if (record == null)
                return;

            record.ExitCode = exitCode;
            record.IsPaused = false;
            record.FinishedAt = DateTime.Now;
            record.LastUpdatedAt = DateTime.Now;

            if (record.StopRequested)
            {
                record.StateKind = DashboardStateKind.Warning;
                record.StatusText = "остановлен";
                record.Details = "Процесс остановлен пользователем.";
                TryAppendUserStoppedMarker(record);
                return;
            }

            bool explicitErrorStatus = ContainsErrorStatus(record.StandardOutput.ToString()) || LooksLikeError(record.StandardError.ToString());
            bool successStatus = ContainsSuccessStatus(record.StandardOutput.ToString());

            if (exitCode == 0 && !explicitErrorStatus)
            {
                record.StateKind = DashboardStateKind.Success;
                record.StatusText = "завершён успешно";
                record.Details = successStatus ? "Процесс завершён успешно." : "Процесс завершился без ошибок.";
                if (!record.IsProgressVisible)
                {
                    record.ProgressPercent = 100;
                    record.ProgressText = "100%";
                }
            }
            else
            {
                record.StateKind = DashboardStateKind.Error;
                record.StatusText = "ошибка";
                string lastError = ExtractLastUsefulLine(record.StandardError.ToString());
                if (string.IsNullOrWhiteSpace(lastError))
                    lastError = ExtractLastUsefulLine(record.StandardOutput.ToString());

                record.Details = string.IsNullOrWhiteSpace(lastError)
                    ? $"Процесс завершился с кодом {exitCode}."
                    : lastError;
            }
        }
    }

    private void TryAppendUserStoppedMarker(WpfRunRecord record)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(record.LogPath))
                record.LogPath = TryFindLogPath(record);

            if (string.IsNullOrWhiteSpace(record.LogPath) || !File.Exists(record.LogPath))
                return;

            string markerText = $"{Environment.NewLine}{DateTime.Now:yyyy-MM-dd HH:mm:ss} | INFO | WPF | AutoScope WPF: процесс остановлен пользователем{Environment.NewLine}";
            File.AppendAllText(record.LogPath, markerText, Encoding.UTF8);
        }
        catch
        {
            // Маркер нужен только для отображения истории. Ошибка записи не должна ломать остановку процесса.
        }
    }

    private WpfRunCompletionInfo BuildCompletionInfo(string recordId)
    {
        lock (_sync)
        {
            WpfRunRecord? record = _records.FirstOrDefault(item => item.Id == recordId);
            if (record == null)
            {
                DateTime now = DateTime.Now;
                return new WpfRunCompletionInfo
                {
                    StartedAt = now,
                    FinishedAt = now,
                    ExitCode = -1
                };
            }

            return new WpfRunCompletionInfo
            {
                StartedAt = record.StartedAt,
                FinishedAt = record.FinishedAt ?? DateTime.Now,
                ExitCode = record.ExitCode ?? -1,
                StandardOutput = record.StandardOutput.ToString(),
                StandardError = record.StandardError.ToString()
            };
        }
    }

    private ProcessDashboardItem ToDashboardItem(WpfRunRecord record)
    {
        TimeSpan duration = (record.FinishedAt ?? DateTime.Now) - record.StartedAt;
        string durationText = FormatDuration(duration);
        string timeText = record.FinishedAt == null
            ? $"идёт {durationText}"
            : $"{record.StartedAt:dd.MM HH:mm} · {durationText}";

        string resultPath = record.TypeText == "анализ"
            ? TryExtractResultPath(record.StandardOutput.ToString(), record.RootPath)
            : "";

        string details = record.Details;
        if (!string.IsNullOrWhiteSpace(record.DatabaseName) || !string.IsNullOrWhiteSpace(record.ModuleName))
        {
            string context = string.Join(" · ", new[]
            {
                string.IsNullOrWhiteSpace(record.ModuleName) ? "" : record.ModuleName,
                string.IsNullOrWhiteSpace(record.DatabaseName) ? "" : record.DatabaseName
            }.Where(value => !string.IsNullOrWhiteSpace(value)));

            if (!string.IsNullOrWhiteSpace(context))
                details = string.IsNullOrWhiteSpace(details) ? context : $"{details} · {context}";
        }

        return new ProcessDashboardItem
        {
            Id = record.Id,
            Name = record.Name,
            TypeText = record.TypeText,
            StatusText = record.StatusText,
            TimeText = timeText,
            Details = details,
            LogPath = record.LogPath,
            ResultPath = resultPath,
            CanOpenLog = !string.IsNullOrWhiteSpace(record.LogPath) && File.Exists(record.LogPath),
            CanOpenResult = !string.IsNullOrWhiteSpace(resultPath) && File.Exists(resultPath),
            CanOpenDetails = true,
            StageText = record.StageText,
            CountText = record.CountText,
            ProgressText = record.ProgressText,
            ProgressPercent = record.ProgressPercent,
            IsProgressVisible = record.IsProgressVisible,
            IsSessionItem = true,
            IsRunningLike = record.FinishedAt == null,
            CanStop = record.FinishedAt == null && !record.StopRequested,
            CanPause = record.FinishedAt == null && !record.StopRequested && !record.IsPaused,
            CanResume = record.FinishedAt == null && !record.StopRequested && record.IsPaused,
            IsStopped = record.StopRequested && record.FinishedAt != null,
            LastUpdatedAt = record.LastUpdatedAt,
            StateKind = record.IsPaused ? DashboardStateKind.Warning : record.StateKind
        };
    }

    private string TryExtractResultPath(string outputText, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(outputText))
            return "";

        foreach (string line in outputText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Reverse())
        {
            string value = line.Trim();
            if (!value.StartsWith("{", StringComparison.Ordinal) || !value.EndsWith("}", StringComparison.Ordinal))
                continue;

            try
            {
                using JsonDocument document = JsonDocument.Parse(value);
                string? rawPath = FindResultPathInJson(document.RootElement);
                string resolvedPath = ResolveExistingPath(rawPath ?? "", rootPath);
                if (!string.IsNullOrWhiteSpace(resolvedPath))
                    return resolvedPath;
            }
            catch
            {
                // В stdout могли попасть строки не из финального JSON. Просто переходим к предыдущей строке.
            }
        }

        return "";
    }

    private string? FindResultPathInJson(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String
                    && IsResultPathProperty(property.Name))
                {
                    string? value = property.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }

                string? nested = FindResultPathInJson(property.Value);
                if (!string.IsNullOrWhiteSpace(nested))
                    return nested;
            }
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                string? nested = FindResultPathInJson(item);
                if (!string.IsNullOrWhiteSpace(nested))
                    return nested;
            }
        }

        return null;
    }

    private bool IsResultPathProperty(string propertyName)
    {
        return propertyName.Equals("file", StringComparison.OrdinalIgnoreCase)
               || propertyName.Equals("report", StringComparison.OrdinalIgnoreCase)
               || propertyName.Equals("reportPath", StringComparison.OrdinalIgnoreCase)
               || propertyName.Equals("resultPath", StringComparison.OrdinalIgnoreCase)
               || propertyName.Equals("outputPath", StringComparison.OrdinalIgnoreCase);
    }

    private string ResolveExistingPath(string path, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";

        try
        {
            string trimmed = path.Trim().Trim('"');
            if (File.Exists(trimmed))
                return Path.GetFullPath(trimmed);

            if (!Path.IsPathRooted(trimmed) && !string.IsNullOrWhiteSpace(rootPath))
            {
                string combined = Path.Combine(rootPath, trimmed);
                if (File.Exists(combined))
                    return Path.GetFullPath(combined);
            }
        }
        catch
        {
            return "";
        }

        return "";
    }

    private string TryFindLogPath(WpfRunRecord record)
    {
        try
        {
            string logsPath = Path.Combine(record.RootPath, "Logs");
            if (!Directory.Exists(logsPath))
                return "";

            string prefix = string.IsNullOrWhiteSpace(record.LogFilePrefix) ? "" : record.LogFilePrefix;
            if (string.IsNullOrWhiteSpace(prefix))
                return "";

            DateTime minTime = record.StartedAt.AddSeconds(-10);

            return Directory.EnumerateFiles(logsPath, "*.log", SearchOption.AllDirectories)
                .Where(path => Path.GetFileName(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(path => new FileInfo(path))
                .Where(file => file.LastWriteTime >= minTime)
                .OrderByDescending(file => file.LastWriteTime)
                .Select(file => file.FullName)
                .FirstOrDefault() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private string CleanOutputLine(string line)
    {
        string value = line.Replace("\r", " ").Replace("\n", " ").Trim();
        if (string.IsNullOrWhiteSpace(value) || value == "=== RESULT ===")
            return "";

        if (value.StartsWith("{", StringComparison.Ordinal) && value.EndsWith("}", StringComparison.Ordinal))
        {
            if (value.Contains("\"status\":\"success\"", StringComparison.OrdinalIgnoreCase)
                || value.Contains("\"status\": \"success\"", StringComparison.OrdinalIgnoreCase))
                return "Процесс завершён успешно";

            if (value.Contains("\"status\":\"error\"", StringComparison.OrdinalIgnoreCase)
                || value.Contains("\"status\": \"error\"", StringComparison.OrdinalIgnoreCase))
                return "Процесс завершился с ошибкой";
        }

        int progressIndex = value.IndexOf("[PROGRESS]", StringComparison.OrdinalIgnoreCase);
        if (progressIndex >= 0)
            return "";

        return value.Length <= 220 ? value : value.Substring(0, 220) + "...";
    }

    private bool LooksLikeError(string text)
    {
        return text.Contains("\"status\": \"error\"", StringComparison.OrdinalIgnoreCase)
               || text.Contains("\"status\":\"error\"", StringComparison.OrdinalIgnoreCase)
               || text.Contains("Traceback", StringComparison.OrdinalIgnoreCase)
               || text.Contains("Exception", StringComparison.OrdinalIgnoreCase)
               || text.Contains("Fatal error", StringComparison.OrdinalIgnoreCase)
               || text.Contains("Module finished with non-zero exit code", StringComparison.OrdinalIgnoreCase)
               || text.Contains("HTTP 403", StringComparison.OrdinalIgnoreCase)
               || text.Contains(" ERROR ", StringComparison.OrdinalIgnoreCase);
    }


    private bool ContainsErrorStatus(string text)
    {
        return text.Contains("\"status\": \"error\"", StringComparison.OrdinalIgnoreCase)
               || text.Contains("\"status\":\"error\"", StringComparison.OrdinalIgnoreCase)
               || text.Contains("Module finished with non-zero exit code", StringComparison.OrdinalIgnoreCase)
               || text.Contains("Fatal error", StringComparison.OrdinalIgnoreCase);
    }

    private bool ContainsSuccessStatus(string text)
    {
        return text.Contains("\"status\": \"success\"", StringComparison.OrdinalIgnoreCase)
               || text.Contains("\"status\":\"success\"", StringComparison.OrdinalIgnoreCase)
               || text.Contains("=== Input Pipeline finished ===", StringComparison.OrdinalIgnoreCase)
               || text.Contains("=== Output Pipeline finished ===", StringComparison.OrdinalIgnoreCase);
    }

    private string ExtractLastUsefulLine(string text)
    {
        return text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(CleanOutputLine)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .LastOrDefault() ?? "";
    }

    private string FormatStageText(string stage, string typeText, DashboardStateKind state)
    {
        if (typeText == "анализ")
            return state == DashboardStateKind.Running ? "Этап анализа" : "Анализ завершён";

        return stage switch
        {
            "collect_links" => "Этап 1 из 2 · сбор ссылок",
            "parse_ads" => "Этап 2 из 2 · обработка объявлений",
            "done" => "Этап 2 из 2 · завершение",
            _ => string.IsNullOrWhiteSpace(stage) ? "Этап не определён" : stage
        };
    }

    private string FormatCountText(string stage, int current, int total)
    {
        string unit = stage switch
        {
            "collect_links" => "ссылок",
            "parse_ads" => "объявлений",
            "done" => "объявлений",
            _ => "элементов"
        };

        return $"{current:N0} из {total:N0} {unit}".Replace(',', ' ');
    }

    private string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}";

        return $"{duration.Minutes:00}:{duration.Seconds:00}";
    }

    private string? ReadString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
            return null;

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private int? ReadInt(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
            return null;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
            return number;

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number))
            return number;

        return null;
    }

    private int SafeProcessId(Process process)
    {
        try
        {
            return process.Id;
        }
        catch
        {
            return -1;
        }
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";

        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path.Trim();
        }
    }

    private class WpfRunRecord
    {
        public string Id { get; set; } = "";
        public int ProcessId { get; set; }
        public string RootPath { get; set; } = "";
        public string Name { get; set; } = "";
        public string TypeText { get; set; } = "";
        public string ModuleName { get; set; } = "";
        public string DatabaseName { get; set; } = "";
        public string LogFilePrefix { get; set; } = "";
        public string SourceScenarioPath { get; set; } = "";
        public string LogPath { get; set; } = "";
        public string StatusText { get; set; } = "";
        public string Details { get; set; } = "";
        public string StageText { get; set; } = "";
        public string CountText { get; set; } = "";
        public string ProgressText { get; set; } = "";
        public int ProgressPercent { get; set; }
        public bool IsProgressVisible { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
        public DateTime LastUpdatedAt { get; set; }
        public int? ExitCode { get; set; }
        public bool StopRequested { get; set; }
        public bool IsPaused { get; set; }
        public Process? Process { get; set; }
        public DashboardStateKind StateKind { get; set; } = DashboardStateKind.Running;
        public StringBuilder StandardOutput { get; set; } = new();
        public StringBuilder StandardError { get; set; } = new();

        public WpfRunRecord Clone()
        {
            return new WpfRunRecord
            {
                Id = Id,
                ProcessId = ProcessId,
                RootPath = RootPath,
                Name = Name,
                TypeText = TypeText,
                ModuleName = ModuleName,
                DatabaseName = DatabaseName,
                LogFilePrefix = LogFilePrefix,
                SourceScenarioPath = SourceScenarioPath,
                LogPath = LogPath,
                StatusText = StatusText,
                Details = Details,
                StageText = StageText,
                CountText = CountText,
                ProgressText = ProgressText,
                ProgressPercent = ProgressPercent,
                IsProgressVisible = IsProgressVisible,
                StartedAt = StartedAt,
                FinishedAt = FinishedAt,
                LastUpdatedAt = LastUpdatedAt,
                ExitCode = ExitCode,
                StopRequested = StopRequested,
                IsPaused = IsPaused,
                StateKind = StateKind,
                StandardOutput = new StringBuilder(StandardOutput.ToString()),
                StandardError = new StringBuilder(StandardError.ToString())
            };
        }
    }

    private static class WindowsProcessPauseHelper
    {
        private const uint TH32CS_SNAPPROCESS = 0x00000002;
        private const uint TH32CS_SNAPTHREAD = 0x00000004;
        private const int THREAD_SUSPEND_RESUME = 0x0002;
        private static readonly IntPtr InvalidHandleValue = new(-1);

        public static bool SuspendProcessTree(int rootProcessId)
        {
            return ApplyToProcessTreeThreads(rootProcessId, suspend: true);
        }

        public static bool ResumeProcessTree(int rootProcessId)
        {
            return ApplyToProcessTreeThreads(rootProcessId, suspend: false);
        }

        private static bool ApplyToProcessTreeThreads(int rootProcessId, bool suspend)
        {
            if (rootProcessId <= 0)
                return false;

            List<int> processIds = GetProcessTreeIds(rootProcessId);
            if (processIds.Count == 0)
                return false;

            int affectedThreads = 0;
            foreach (int processId in processIds)
                affectedThreads += ApplyToProcessThreads(processId, suspend);

            return affectedThreads > 0;
        }

        private static List<int> GetProcessTreeIds(int rootProcessId)
        {
            List<ProcessInfo> processes = EnumerateProcesses();
            HashSet<int> result = new() { rootProcessId };
            Queue<int> queue = new();
            queue.Enqueue(rootProcessId);

            while (queue.Count > 0)
            {
                int parentId = queue.Dequeue();
                foreach (ProcessInfo child in processes.Where(item => item.ParentProcessId == parentId))
                {
                    if (result.Add(child.ProcessId))
                        queue.Enqueue(child.ProcessId);
                }
            }

            return result.ToList();
        }

        private static List<ProcessInfo> EnumerateProcesses()
        {
            List<ProcessInfo> result = new();
            IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snapshot == InvalidHandleValue)
                return result;

            try
            {
                PROCESSENTRY32 entry = new();
                entry.dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>();

                if (!Process32First(snapshot, ref entry))
                    return result;

                do
                {
                    result.Add(new ProcessInfo((int)entry.th32ProcessID, (int)entry.th32ParentProcessID));
                }
                while (Process32Next(snapshot, ref entry));
            }
            finally
            {
                CloseHandle(snapshot);
            }

            return result;
        }

        private static int ApplyToProcessThreads(int processId, bool suspend)
        {
            int affectedThreads = 0;
            IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);
            if (snapshot == InvalidHandleValue)
                return affectedThreads;

            try
            {
                THREADENTRY32 entry = new();
                entry.dwSize = (uint)Marshal.SizeOf<THREADENTRY32>();

                if (!Thread32First(snapshot, ref entry))
                    return affectedThreads;

                do
                {
                    if (entry.th32OwnerProcessID != (uint)processId)
                        continue;

                    IntPtr threadHandle = OpenThread(THREAD_SUSPEND_RESUME, false, entry.th32ThreadID);
                    if (threadHandle == IntPtr.Zero)
                        continue;

                    try
                    {
                        uint result = suspend ? SuspendThread(threadHandle) : ResumeThread(threadHandle);
                        if (result != uint.MaxValue)
                            affectedThreads++;
                    }
                    finally
                    {
                        CloseHandle(threadHandle);
                    }
                }
                while (Thread32Next(snapshot, ref entry));
            }
            finally
            {
                CloseHandle(snapshot);
            }

            return affectedThreads;
        }

        private readonly record struct ProcessInfo(int ProcessId, int ParentProcessId);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct PROCESSENTRY32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct THREADENTRY32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ThreadID;
            public uint th32OwnerProcessID;
            public int tpBasePri;
            public int tpDeltaPri;
            public uint dwFlags;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool Thread32First(IntPtr hSnapshot, ref THREADENTRY32 lpte);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool Thread32Next(IntPtr hSnapshot, ref THREADENTRY32 lpte);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenThread(int dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint SuspendThread(IntPtr hThread);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint ResumeThread(IntPtr hThread);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);
    }
}

public class WpfRunCompletionInfo
{
    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }
    public int ExitCode { get; set; }
    public string StandardOutput { get; set; } = "";
    public string StandardError { get; set; } = "";
}
