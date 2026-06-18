using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using AutoScope.WpfClient.Models;

namespace AutoScope.WpfClient.Services;

public class DashboardDataService
{
    private static readonly TimeSpan RunningLogFreshness = TimeSpan.FromMinutes(3);

    public string RootPath { get; }

    public DashboardDataService(string rootPath)
    {
        RootPath = rootPath;
    }

    public List<ScenarioDashboardItem> LoadScenarios()
    {
        string jobsPath = Path.Combine(RootPath, "Jobs");
        if (!Directory.Exists(jobsPath))
            return new List<ScenarioDashboardItem>();

        return Directory.GetFiles(jobsPath, "*.json")
            .OrderBy(Path.GetFileName)
            .Select(ReadScenario)
            .Where(item => item != null)
            .Cast<ScenarioDashboardItem>()
            .ToList();
    }

    public List<DatabaseDashboardItem> LoadDatabases()
    {
        List<DatabaseDashboardItem> result = new List<DatabaseDashboardItem>();

        foreach (string dbPath in EnumerateDatabaseFiles())
        {
            bool isSQLite = IsSQLiteDatabaseFile(dbPath);
            bool looksLikeAutoScopeDatabase = LooksLikeAutoScopeDatabasePath(dbPath);

            if (!isSQLite && !looksLikeAutoScopeDatabase)
                continue;

            FileInfo file = new FileInfo(dbPath);
            string? recordsText = isSQLite ? TryCountAdsWithProjectPython(file.FullName) : null;
            if (string.IsNullOrWhiteSpace(recordsText))
                recordsText = "Записей: не считалось";

            (string displayName, string configName) = SplitDatabaseFileName(file.Name);
            string configDetails = string.IsNullOrWhiteSpace(configName)
                ? "Конфиг: не указан"
                : $"Конфиг: {configName}";

            string availabilityDetails = isSQLite
                ? "структура проверена"
                : "файл похож на базу AutoScope, но сейчас может быть занят другим приложением";

            result.Add(new DatabaseDashboardItem
            {
                Name = displayName,
                FileName = file.Name,
                ConfigName = configName,
                Path = file.FullName,
                Details = $"{configDetails} · Файл: {file.Name} · Размер: {FormatFileSize(file.Length)} · {availabilityDetails}",
                RecordsText = recordsText,
                StateKind = DashboardStateKind.Neutral
            });

            if (result.Count >= 30)
                break;
        }

        return result;
    }

    public List<ProcessDashboardItem> LoadObservedProcesses(IReadOnlyCollection<string> sessionProcessIds)
    {
        List<ProcessDashboardItem> all = LoadPipelineLogProcesses(int.MaxValue);

        return all
            .Where(item => item.IsRunningLike || sessionProcessIds.Contains(item.Id))
            .OrderByDescending(item => item.StateKind == DashboardStateKind.Running)
            .ThenByDescending(item => item.LastUpdatedAt)
            .ToList();
    }

    public List<ProcessDashboardItem> LoadHistoryProcesses(int count, bool loadAll, IReadOnlyCollection<string> excludedIds)
    {
        int take = loadAll ? int.MaxValue : Math.Max(0, count);

        return LoadPipelineLogProcesses(take == int.MaxValue ? int.MaxValue : take + excludedIds.Count + 20)
            .Where(item => !excludedIds.Contains(item.Id))
            .Where(item => !item.IsRunningLike)
            .OrderByDescending(item => item.LastUpdatedAt)
            .Take(take)
            .Select(item =>
            {
                item.IsHistoryItem = true;
                return item;
            })
            .ToList();
    }

    public int CountHistoryProcesses(IReadOnlyCollection<string> excludedIds)
    {
        return LoadPipelineLogProcesses(int.MaxValue)
            .Count(item => !excludedIds.Contains(item.Id) && !item.IsRunningLike);
    }

    public string GetReportsFolderPath()
    {
        string upper = Path.Combine(RootPath, "Reports");
        string lower = Path.Combine(RootPath, "reports");
        if (Directory.Exists(upper)) return upper;
        if (Directory.Exists(lower)) return lower;
        return upper;
    }

    public string GetLogsFolderPath()
    {
        return Path.Combine(RootPath, "Logs");
    }

    public string GetDatabasesFolderPath()
    {
        return Path.Combine(RootPath, "Databases");
    }

    public string GetJobsFolderPath()
    {
        return Path.Combine(RootPath, "Jobs");
    }

    public void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    public bool OpenDatabaseInDbBrowser(string databasePath, out string message)
    {
        string? browserPath = FindDbBrowserExecutable();
        if (string.IsNullOrWhiteSpace(browserPath))
        {
            message = "DB Browser for SQLite не найден в папке проекта. Позже путь можно будет вынести в настройки.";
            return false;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = browserPath,
            Arguments = $"\"{databasePath}\"",
            UseShellExecute = true
        });

        message = "База открыта в DB Browser.";
        return true;
    }

    public void OpenFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException("Файл не найден.", path);

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private List<ProcessDashboardItem> LoadPipelineLogProcesses(int take)
    {
        string logsPath = GetLogsFolderPath();
        if (!Directory.Exists(logsPath))
            return new List<ProcessDashboardItem>();

        try
        {
            return Directory.EnumerateFiles(logsPath, "*.log", SearchOption.AllDirectories)
                .Where(IsPipelineLogFile)
                .OrderByDescending(File.GetLastWriteTime)
                .Take(take)
                .Select(ReadPipelineLogProcess)
                .ToList();
        }
        catch
        {
            return new List<ProcessDashboardItem>();
        }
    }

    private bool IsPipelineLogFile(string path)
    {
        string name = Path.GetFileName(path);
        return name.StartsWith("input_pipeline_", StringComparison.OrdinalIgnoreCase)
               || name.StartsWith("output_pipeline_", StringComparison.OrdinalIgnoreCase);
    }

    private ProcessDashboardItem ReadPipelineLogProcess(string path)
    {
        FileInfo file = new FileInfo(path);
        List<string> lines = ReadLastLines(path, 220);
        ProgressInfo? progress = ExtractLastProgress(lines);
        DashboardStateKind state = DetectLogState(lines, file.LastWriteTime, progress);
        bool runningLike = state == DashboardStateKind.Running;
        string statusText = FormatProcessStatus(state);
        string typeText = GetLogTypeText(file.Name);
        string lastState = ExtractLastProcessState(lines, progress, state);

        ProcessDashboardItem item = new ProcessDashboardItem
        {
            Id = file.FullName,
            Name = BuildProcessName(file.Name, file.LastWriteTime, lines),
            TypeText = typeText,
            StatusText = statusText,
            TimeText = file.LastWriteTime.ToString("dd.MM.yyyy HH:mm"),
            Details = string.IsNullOrWhiteSpace(lastState)
                ? "Лог найден, но краткое состояние определить не удалось."
                : lastState,
            LogPath = file.FullName,
            CanOpenLog = true,
            CanOpenDetails = true,
            LastUpdatedAt = file.LastWriteTime,
            StateKind = state,
            IsRunningLike = runningLike
        };

        ApplyProgress(item, progress, typeText, state);
        return item;
    }

    private List<string> ReadLastLines(string path, int maxLines)
    {
        try
        {
            return File.ReadLines(path)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .TakeLast(maxLines)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    private DashboardStateKind DetectLogState(List<string> lines, DateTime lastWriteTime, ProgressInfo? progress)
    {
        string text = string.Join("\n", lines);
        bool fatalError = ContainsAny(text,
            "\"status\": \"error\"",
            "\"status\":\"error\"",
            "Traceback",
            "Fatal error",
            "Module finished with non-zero exit code",
            " ERROR | PIPELINE |",
            " ERROR | ANALYZER |",
            " ERROR | PARSER |");

        if (fatalError)
            return DashboardStateKind.Error;

        bool finished = ContainsAny(text,
            "\"status\": \"success\"",
            "\"status\":\"success\"",
            "Статус запуска: success",
            "=== Input Pipeline finished ===",
            "=== Output Pipeline finished ===",
            "Streaming pipeline finished",
            "Парсер успешно завершил работу");

        bool hasWarnings = ContainsAny(text,
            "Skipped ad because of error",
            "Ошибка при обработке объявления",
            "HTTP 403",
            "showcaptcha");

        if (finished && hasWarnings)
            return DashboardStateKind.Warning;

        if (finished)
            return DashboardStateKind.Success;

        bool recent = DateTime.Now - lastWriteTime <= RunningLogFreshness;
        if (recent && progress != null)
            return DashboardStateKind.Running;

        return DashboardStateKind.Neutral;
    }

    private bool ContainsAny(string text, params string[] fragments)
    {
        return fragments.Any(fragment => text.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private ProgressInfo? ExtractLastProgress(List<string> lines)
    {
        foreach (string line in lines.AsEnumerable().Reverse())
        {
            int index = line.IndexOf("[PROGRESS]", StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                continue;

            string json = line.Substring(index + "[PROGRESS]".Length).Trim();
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;
                return new ProgressInfo
                {
                    Stage = GetString(root, "stage") ?? "",
                    Current = GetInt(root, "current") ?? 0,
                    Total = GetInt(root, "total") ?? 0,
                    Percent = GetInt(root, "percent") ?? 0,
                    Message = GetString(root, "message") ?? ""
                };
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private string ExtractLastProcessState(List<string> lines, ProgressInfo? progress, DashboardStateKind state)
    {
        if (progress != null && !string.IsNullOrWhiteSpace(progress.Message))
            return TrimLongText(progress.Message, 170);

        foreach (string line in lines.AsEnumerable().Reverse())
        {
            string value = CleanLogLine(line);
            if (string.IsNullOrWhiteSpace(value))
                continue;

            if (value.Contains("Статус запуска", StringComparison.OrdinalIgnoreCase))
                return TrimLongText(value, 170);

            if (ContainsAny(value, "Traceback", "Exception", "ERROR", "Fatal", "HTTP 403", "showcaptcha"))
                return TrimLongText(value, 170);

            if (value.Contains("Pipeline finished", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Процесс завершён", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Парсер успешно завершил", StringComparison.OrdinalIgnoreCase))
                return state == DashboardStateKind.Warning
                    ? "Процесс завершён, но в логе есть пропущенные объявления или предупреждения."
                    : TrimLongText(value, 170);
        }

        foreach (string line in lines.AsEnumerable().Reverse())
        {
            string value = CleanLogLine(line);
            if (!string.IsNullOrWhiteSpace(value))
                return TrimLongText(value, 170);
        }

        return "";
    }

    private string CleanLogLine(string line)
    {
        string value = line.Replace("\r", " ").Replace("\n", " ").Trim();
        if (value == "=== RESULT ===")
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

        return value;
    }

    private void ApplyProgress(ProcessDashboardItem item, ProgressInfo? progress, string typeText, DashboardStateKind state)
    {
        if (progress == null || progress.Total <= 0)
            return;

        int percent = Math.Clamp(progress.Percent, 0, 100);
        item.ProgressPercent = percent;
        item.ProgressText = $"{percent}%";
        item.StageText = FormatStageText(progress.Stage, typeText, state);
        item.CountText = FormatCountText(progress.Stage, progress.Current, progress.Total);
        item.IsProgressVisible = true;
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

    private string BuildProcessName(string fileName, DateTime timestamp, List<string> lines)
    {
        string moduleName = ExtractModuleName(lines);
        if (!string.IsNullOrWhiteSpace(moduleName))
            return moduleName;

        string type = fileName.StartsWith("input_pipeline_", StringComparison.OrdinalIgnoreCase)
            ? "InputPipeline"
            : "OutputPipeline";

        return $"{type} · {timestamp:dd.MM HH:mm}";
    }

    private string ExtractModuleName(List<string> lines)
    {
        foreach (string line in lines)
        {
            int start = line.IndexOf("[", StringComparison.Ordinal);
            int end = line.IndexOf("][pipeline=", StringComparison.OrdinalIgnoreCase);
            if (start >= 0 && end > start)
                return line.Substring(start + 1, end - start - 1);
        }

        return "";
    }

    private string GetLogTypeText(string fileName)
    {
        if (fileName.StartsWith("input_pipeline_", StringComparison.OrdinalIgnoreCase))
            return "парсинг";

        if (fileName.StartsWith("output_pipeline_", StringComparison.OrdinalIgnoreCase))
            return "анализ";

        return "процесс";
    }

    private string FormatProcessStatus(DashboardStateKind state)
    {
        return state switch
        {
            DashboardStateKind.Running => "выполняется",
            DashboardStateKind.Success => "завершён успешно",
            DashboardStateKind.Error => "ошибка",
            DashboardStateKind.Warning => "завершён с предупреждениями",
            _ => "история"
        };
    }

    private string TrimLongText(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
    }

    private ScenarioDashboardItem? ReadScenario(string path)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = document.RootElement;

            string name = GetString(root, "jobName", "scenarioName", "name") ?? Path.GetFileNameWithoutExtension(path);
            bool enabled = GetBool(root, "enabled") ?? true;
            string pipelineType = GetString(root, "pipelineType") ?? "";
            string dbPath = GetString(root, "dbPath") ?? "";
            string parserPath = GetString(root, "parserPath") ?? "";
            string analyzerPath = GetString(root, "analyzerPath") ?? "";
            string moduleName = Path.GetFileName(string.IsNullOrWhiteSpace(parserPath) ? analyzerPath : parserPath);
            int everyHours = GetNestedInt(root, "schedule", "everyHours") ?? 0;
            string nextRunAt = GetString(root, "nextRunAt") ?? "";

            DashboardStateKind kind;
            string status;

            if (!enabled)
            {
                kind = DashboardStateKind.Error;
                status = "выключен";
            }
            else if (everyHours <= 0)
            {
                kind = DashboardStateKind.Neutral;
                status = "только ручной запуск";
            }
            else
            {
                kind = DashboardStateKind.Success;
                status = "активен";
            }

            string details = string.Join(" · ", new[]
            {
                FormatPipelineType(pipelineType),
                string.IsNullOrWhiteSpace(moduleName) ? "модуль не задан" : moduleName,
                string.IsNullOrWhiteSpace(dbPath) ? "база не задана" : Path.GetFileName(dbPath),
                string.IsNullOrWhiteSpace(nextRunAt) ? "next: не задан" : $"next: {nextRunAt}"
            }.Where(value => !string.IsNullOrWhiteSpace(value)));

            return new ScenarioDashboardItem
            {
                Name = name,
                StatusText = status,
                Details = details,
                StateKind = kind
            };
        }
        catch
        {
            return new ScenarioDashboardItem
            {
                Name = Path.GetFileNameWithoutExtension(path),
                StatusText = "ошибка чтения",
                Details = "Файл сценария не удалось разобрать как JSON.",
                StateKind = DashboardStateKind.Error
            };
        }
    }

    private IEnumerable<string> EnumerateDatabaseFiles()
    {
        if (!Directory.Exists(RootPath))
            return Enumerable.Empty<string>();

        string[] excludedFolders = new[]
        {
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}Python{Path.DirectorySeparatorChar}Lib{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}DB Browser{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}DB Browser for SQLite{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}sqlitebrowser{Path.DirectorySeparatorChar}"
        };

        string[] allowedExtensions = { ".db", ".bd", ".sqlite", ".sqlite3" };

        return Directory.EnumerateFiles(RootPath, "*.*", SearchOption.AllDirectories)
            .Where(path => allowedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Where(path => !excludedFolders.Any(excluded => path.Contains(excluded, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(GetDatabaseSortPriority)
            .ThenByDescending(File.GetLastWriteTime);
    }

    private int GetDatabaseSortPriority(string path)
    {
        string databasesPath = GetDatabasesFolderPath();
        string fileName = Path.GetFileName(path);

        if (path.StartsWith(databasesPath, StringComparison.OrdinalIgnoreCase)
            && fileName.Contains("BaseDataBaseConfig", StringComparison.OrdinalIgnoreCase))
            return 0;

        if (path.StartsWith(databasesPath, StringComparison.OrdinalIgnoreCase))
            return 1;

        if (fileName.Contains("BaseDataBaseConfig", StringComparison.OrdinalIgnoreCase))
            return 2;

        return 3;
    }

    private bool LooksLikeAutoScopeDatabasePath(string path)
    {
        string databasesPath = GetDatabasesFolderPath();
        string fileName = Path.GetFileName(path);

        return path.StartsWith(databasesPath, StringComparison.OrdinalIgnoreCase)
               || fileName.Contains("BaseDataBaseConfig", StringComparison.OrdinalIgnoreCase)
               || fileName.Contains("AutoScope", StringComparison.OrdinalIgnoreCase);
    }

    private (string DisplayName, string ConfigName) SplitDatabaseFileName(string fileName)
    {
        string nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrWhiteSpace(nameWithoutExtension))
            return (fileName, "");

        string[] parts = nameWithoutExtension.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            return (nameWithoutExtension, "");

        string configName = parts[^1];
        string displayName = string.Join('.', parts.Take(parts.Length - 1));

        if (!configName.Contains("config", StringComparison.OrdinalIgnoreCase))
            return (nameWithoutExtension, "");

        return (string.IsNullOrWhiteSpace(displayName) ? nameWithoutExtension : displayName, configName);
    }

    private bool IsSQLiteDatabaseFile(string path)
    {
        try
        {
            byte[] header = new byte[16];
            using FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            if (stream.Read(header, 0, header.Length) != header.Length)
                return false;

            string signature = System.Text.Encoding.ASCII.GetString(header);
            return signature == "SQLite format 3\0";
        }
        catch
        {
            return false;
        }
    }

    private string? TryCountAdsWithProjectPython(string databasePath)
    {
        string pythonPath = Path.Combine(RootPath, "Python", "python.exe");
        if (!File.Exists(pythonPath))
            return "Записей: не считалось";

        try
        {
            using Process process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = $"-c \"import sqlite3,sys; con=sqlite3.connect(sys.argv[1], timeout=1.0); con.execute('pragma query_only=ON'); print(con.execute('select count(*) from ads').fetchone()[0])\" \"{databasePath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            process.Start();
            if (!process.WaitForExit(8000))
            {
                try { process.Kill(); } catch { }
                return null;
            }

            if (process.ExitCode != 0)
                return null;

            string output = process.StandardOutput.ReadToEnd().Trim();
            return int.TryParse(output, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count)
                ? $"Записей: {count:N0}".Replace(',', ' ')
                : null;
        }
        catch
        {
            return null;
        }
    }

    private string? FindDbBrowserExecutable()
    {
        string[] names = new[]
        {
            "DB Browser for SQLite.exe",
            "DB Browser.exe",
            "sqlitebrowser.exe"
        };

        try
        {
            return Directory.EnumerateFiles(RootPath, "*.exe", SearchOption.AllDirectories)
                .FirstOrDefault(path => names.Any(name => Path.GetFileName(path).Equals(name, StringComparison.OrdinalIgnoreCase)));
        }
        catch
        {
            return null;
        }
    }

    private string FormatPipelineType(string value)
    {
        return value switch
        {
            "input" => "InputPipeline",
            "output" => "OutputPipeline",
            _ => string.IsNullOrWhiteSpace(value) ? "pipeline не задан" : value
        };
    }

    private string FormatFileSize(long bytes)
    {
        double value = bytes;
        string[] units = new[] { "Б", "КБ", "МБ", "ГБ" };
        int unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.#} {units[unitIndex]}";
    }

    private string? GetString(JsonElement root, params string[] names)
    {
        foreach (string name in names)
        {
            if (root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        }

        return null;
    }

    private int? GetInt(JsonElement root, params string[] names)
    {
        foreach (string name in names)
        {
            if (!root.TryGetProperty(name, out JsonElement value))
                continue;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int intValue))
                return intValue;

            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out int parsedValue))
                return parsedValue;
        }

        return null;
    }

    private bool? GetBool(JsonElement root, params string[] names)
    {
        foreach (string name in names)
        {
            if (root.TryGetProperty(name, out JsonElement value))
            {
                if (value.ValueKind == JsonValueKind.True) return true;
                if (value.ValueKind == JsonValueKind.False) return false;
            }
        }

        return null;
    }

    private int? GetNestedInt(JsonElement root, string objectName, string propertyName)
    {
        if (!root.TryGetProperty(objectName, out JsonElement obj) || obj.ValueKind != JsonValueKind.Object)
            return null;

        return GetInt(obj, propertyName);
    }

    private class ProgressInfo
    {
        public string Stage { get; set; } = "";
        public int Current { get; set; }
        public int Total { get; set; }
        public int Percent { get; set; }
        public string Message { get; set; } = "";
    }
}
