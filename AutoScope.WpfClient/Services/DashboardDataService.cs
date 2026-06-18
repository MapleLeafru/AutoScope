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

        foreach (string dbPath in EnumerateDatabaseFiles().Take(60))
        {
            if (!IsSQLiteDatabaseFile(dbPath))
                continue;

            FileInfo file = new FileInfo(dbPath);
            string? recordsText = TryCountAdsWithProjectPython(file.FullName);
            if (string.IsNullOrWhiteSpace(recordsText))
                continue;

            result.Add(new DatabaseDashboardItem
            {
                Name = file.Name,
                Path = file.FullName,
                Details = $"Размер: {FormatFileSize(file.Length)}",
                RecordsText = recordsText,
                StateKind = DashboardStateKind.Neutral
            });

            if (result.Count >= 30)
                break;
        }

        return result;
    }

    public List<ProcessDashboardItem> LoadInitialProcesses()
    {
        return new List<ProcessDashboardItem>
        {
            new ProcessDashboardItem
            {
                Name = "Процессы UI-сессии",
                StatusText = "ожидает интеграции",
                Details = "В этом первом WPF-шаге хаб уже готов визуально, но запуск Input/Output из окна будет подключён следующим этапом через RunManagerService.",
                StateKind = DashboardStateKind.Neutral
            }
        };
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

        return Directory.EnumerateFiles(RootPath, "*.db", SearchOption.AllDirectories)
            .Where(path => !excludedFolders.Any(excluded => path.Contains(excluded, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(File.GetLastWriteTime);
    }

    private bool IsSQLiteDatabaseFile(string path)
    {
        try
        {
            byte[] header = new byte[16];
            using FileStream stream = File.OpenRead(path);
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
                Arguments = $"-c \"import sqlite3,sys; con=sqlite3.connect(sys.argv[1]); print(con.execute('select count(*) from ads').fetchone()[0])\" \"{databasePath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            process.Start();
            if (!process.WaitForExit(1200))
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

    private int? GetNestedInt(JsonElement root, string objectName, string valueName)
    {
        if (!root.TryGetProperty(objectName, out JsonElement nested) || nested.ValueKind != JsonValueKind.Object)
            return null;

        if (!nested.TryGetProperty(valueName, out JsonElement value) || value.ValueKind != JsonValueKind.Number)
            return null;

        return value.TryGetInt32(out int result) ? result : null;
    }
}
