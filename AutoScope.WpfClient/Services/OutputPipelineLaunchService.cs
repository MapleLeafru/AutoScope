using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AutoScope.WpfClient.Models;

namespace AutoScope.WpfClient.Services;

public class OutputPipelineLaunchService
{
    private readonly string _rootPath;
    private readonly DashboardDataService _dashboardDataService;

    public OutputPipelineLaunchService(string rootPath)
    {
        _rootPath = rootPath;
        _dashboardDataService = new DashboardDataService(rootPath);
    }

    public List<DatabaseDashboardItem> LoadDatabases()
    {
        return _dashboardDataService.LoadDatabases();
    }

    public List<AnalyzerLaunchItem> LoadAnalyzers()
    {
        string analyzersPath = Path.Combine(_rootPath, "Analyzers");
        if (!Directory.Exists(analyzersPath))
            return new List<AnalyzerLaunchItem>();

        string[] allowedExtensions = { ".py", ".jar", ".exe" };
        HashSet<string> recommended = ReadRecommendedAnalyzers();

        return Directory.GetFiles(analyzersPath)
            .Where(path => allowedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(path => recommended.Contains(Path.GetFileName(path)))
            .ThenBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .Select(ReadAnalyzerInfo)
            .ToList();
    }

    public OutputPipelineLaunchSettings LoadDefaultSettings()
    {
        OutputPipelineLaunchSettings settings = new OutputPipelineLaunchSettings();
        string defaultSettingsPath = Path.Combine(_rootPath, "Configs", "AnalyzerDefaultSettings.json");
        if (!File.Exists(defaultSettingsPath))
            return settings;

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(defaultSettingsPath));
            JsonElement root = document.RootElement;

            JsonElement settingsElement = root.TryGetProperty("outputSettings", out JsonElement nestedSettings)
                ? nestedSettings
                : root;

            ApplyOutputSettings(settings, settingsElement);
        }
        catch
        {
            // Если общий конфиг временно повреждён, используем безопасные значения по умолчанию.
        }

        return settings;
    }

    public PipelineLaunchResult StartOutputPipeline(OutputPipelineLaunchRequest launchRequest)
    {
        try
        {
            string managerPath = Path.Combine(_rootPath, "PipelineManagers", "OutputPipelineManager.py");
            if (!File.Exists(managerPath))
            {
                return new PipelineLaunchResult
                {
                    Started = false,
                    Message = $"OutputPipelineManager.py не найден: {managerPath}"
                };
            }

            string pythonPath = ResolvePythonPath();
            string javaPath = ReadJavaPath();

            object payload = BuildOutputPayload(launchRequest.DatabasePath, launchRequest.Analyzer, launchRequest.Settings, pythonPath, javaPath);
            string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = false
            });

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
            {
                return new PipelineLaunchResult
                {
                    Started = false,
                    Message = "Python-процесс не удалось создать."
                };
            }

            _ = Task.Run(async () => await FeedAndDrainProcessAsync(process, json));

            return new PipelineLaunchResult
            {
                Started = true,
                Message = $"Анализ запущен: {launchRequest.Analyzer.DisplayName} → {Path.GetFileName(launchRequest.DatabasePath)}. После завершения отчёт появится в папке Reports, а процесс — в блоке процессов после обновления хаба."
            };
        }
        catch (Exception ex)
        {
            return new PipelineLaunchResult
            {
                Started = false,
                Message = $"Запуск анализа не удался: {ex.Message}"
            };
        }
    }

    private async Task FeedAndDrainProcessAsync(Process process, string inputJson)
    {
        try
        {
            await process.StandardInput.WriteAsync(inputJson);
            process.StandardInput.Close();

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();
            await Task.WhenAll(stdoutTask, stderrTask);
        }
        catch
        {
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
    }

    private AnalyzerLaunchItem ReadAnalyzerInfo(string path)
    {
        FileInfo file = new FileInfo(path);
        AnalyzerLaunchItem item = new AnalyzerLaunchItem
        {
            FileName = file.Name,
            DisplayName = Path.GetFileNameWithoutExtension(file.Name),
            Path = file.FullName,
            Runtime = DetectRuntime(file.FullName),
            Description = "Описание анализатора не задано."
        };

        string configPath = Path.Combine(_rootPath, "Configs", "AnalyzerConfigs", Path.GetFileNameWithoutExtension(file.Name) + ".json");
        if (!File.Exists(configPath))
            return item;

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(configPath));
            JsonElement root = document.RootElement;

            if (root.TryGetProperty("metadata", out JsonElement metadata))
            {
                item.DisplayName = ReadString(metadata, "displayName", item.DisplayName);
                item.Description = ReadString(metadata, "description", item.Description);
            }

            JsonElement settingsElement = root.TryGetProperty("settings", out JsonElement nestedSettings)
                ? nestedSettings
                : root;

            foreach (JsonProperty property in settingsElement.EnumerateObject())
            {
                if (property.NameEquals("metadata") || property.NameEquals("settingsSchema") || property.NameEquals("settings"))
                    continue;

                item.Settings[property.Name] = ConvertJsonElement(property.Value);
            }
        }
        catch
        {
            item.Description = "Конфиг анализатора найден, но его не удалось разобрать.";
        }

        return item;
    }

    private void ApplyOutputSettings(OutputPipelineLaunchSettings settings, JsonElement source)
    {
        foreach (JsonProperty property in source.EnumerateObject())
        {
            if (property.NameEquals("metadata") || property.NameEquals("settingsSchema") || property.NameEquals("settings"))
                continue;

            string name = property.Name;
            JsonElement value = property.Value;

            if (string.Equals(name, "latestOnly", StringComparison.OrdinalIgnoreCase))
            {
                settings.LatestOnly = ReadElementAsBool(value, settings.LatestOnly);
                continue;
            }

            if (string.Equals(name, "onlyChanged", StringComparison.OrdinalIgnoreCase))
            {
                settings.OnlyChanged = ReadElementAsBool(value, settings.OnlyChanged);
                continue;
            }

            if (string.Equals(name, "brand", StringComparison.OrdinalIgnoreCase))
            {
                settings.Brand = ReadElementAsString(value, settings.Brand);
                continue;
            }

            if (string.Equals(name, "model", StringComparison.OrdinalIgnoreCase))
            {
                settings.Model = ReadElementAsString(value, settings.Model);
                continue;
            }

            if (string.Equals(name, "saleRegion", StringComparison.OrdinalIgnoreCase))
            {
                settings.SaleRegion = ReadElementAsString(value, settings.SaleRegion);
                continue;
            }

            if (string.Equals(name, "yearFrom", StringComparison.OrdinalIgnoreCase))
            {
                settings.YearFrom = ReadElementAsNullableInt(value);
                continue;
            }

            if (string.Equals(name, "yearTo", StringComparison.OrdinalIgnoreCase))
            {
                settings.YearTo = ReadElementAsNullableInt(value);
                continue;
            }

            settings.ExtraSettings[name] = ConvertJsonElement(value);
        }
    }

    private object BuildOutputPayload(string databasePath, AnalyzerLaunchItem analyzer, OutputPipelineLaunchSettings settings, string pythonPath, string javaPath)
    {
        Dictionary<string, object?> outputSettings = new Dictionary<string, object?>
        {
            ["latestOnly"] = settings.LatestOnly,
            ["onlyChanged"] = settings.OnlyChanged,
            ["brand"] = CleanText(settings.Brand),
            ["model"] = CleanText(settings.Model),
            ["saleRegion"] = CleanText(settings.SaleRegion),
            ["yearFrom"] = settings.YearFrom,
            ["yearTo"] = settings.YearTo
        };

        foreach (KeyValuePair<string, object?> item in settings.ExtraSettings)
        {
            if (!outputSettings.ContainsKey(item.Key))
                outputSettings[item.Key] = item.Value;
        }

        Dictionary<string, object?> analyzerSettings = new Dictionary<string, object?>(analyzer.Settings, StringComparer.OrdinalIgnoreCase);

        return new
        {
            analyzer = new
            {
                modulePath = analyzer.Path,
                analyzerPath = analyzer.Path,
                runtime = analyzer.Runtime,
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
            dbPath = databasePath,
            configPath = Path.Combine(_rootPath, "Configs")
        };
    }

    private HashSet<string> ReadRecommendedAnalyzers()
    {
        HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string defaultSettingsPath = Path.Combine(_rootPath, "Configs", "AnalyzerDefaultSettings.json");
        if (!File.Exists(defaultSettingsPath))
            return result;

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(defaultSettingsPath));
            if (!document.RootElement.TryGetProperty("recommendedAnalyzers", out JsonElement recommended)
                || recommended.ValueKind != JsonValueKind.Array)
                return result;

            foreach (JsonElement item in recommended.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    string? value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        result.Add(value);
                }
            }
        }
        catch
        {
            // Рекомендации влияют только на порядок списка.
        }

        return result;
    }

    private string ResolvePythonPath()
    {
        string bundled = Path.Combine(_rootPath, "Python", "python.exe");
        if (File.Exists(bundled))
            return bundled;

        string? configured = ReadStringFromJsonFile(Path.Combine(_rootPath, "Configs", "ParserDefaultSettings.json"), "pythonPath");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        return "python";
    }

    private string ReadJavaPath()
    {
        string? configured = ReadStringFromJsonFile(Path.Combine(_rootPath, "Configs", "ParserDefaultSettings.json"), "javaPath");
        return string.IsNullOrWhiteSpace(configured) ? "java" : configured;
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

    private string? CleanText(string value)
    {
        value = value.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private string ReadString(JsonElement root, string propertyName, string fallback)
    {
        if (root.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            return value.GetString() ?? fallback;

        return fallback;
    }

    private string ReadElementAsString(JsonElement value, string fallback)
    {
        if (value.ValueKind == JsonValueKind.String)
            return value.GetString() ?? fallback;

        if (value.ValueKind == JsonValueKind.Null)
            return "";

        return value.ToString();
    }

    private bool ReadElementAsBool(JsonElement value, bool fallback)
    {
        if (value.ValueKind == JsonValueKind.True)
            return true;

        if (value.ValueKind == JsonValueKind.False)
            return false;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int intValue))
            return intValue != 0;

        if (value.ValueKind == JsonValueKind.String)
        {
            string raw = (value.GetString() ?? "").Trim().ToLowerInvariant();
            if (raw is "true" or "1" or "yes" or "y" or "да")
                return true;
            if (raw is "false" or "0" or "no" or "n" or "нет")
                return false;
        }

        return fallback;
    }

    private int? ReadElementAsNullableInt(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null)
            return null;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result))
            return result;

        if (value.ValueKind == JsonValueKind.String)
        {
            string raw = (value.GetString() ?? "").Trim();
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            if (int.TryParse(raw, out result))
                return result;
        }

        return null;
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
}
