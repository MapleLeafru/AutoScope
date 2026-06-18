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

public class InputPipelineLaunchService
{
    private readonly string _rootPath;
    private readonly DashboardDataService _dashboardDataService;

    public InputPipelineLaunchService(string rootPath)
    {
        _rootPath = rootPath;
        _dashboardDataService = new DashboardDataService(rootPath);
    }

    public List<DatabaseDashboardItem> LoadDatabases()
    {
        return _dashboardDataService.LoadDatabases();
    }

    public List<ParserLaunchItem> LoadParsers()
    {
        string parsersPath = Path.Combine(_rootPath, "Parsers");
        if (!Directory.Exists(parsersPath))
            return new List<ParserLaunchItem>();

        string[] allowedExtensions = { ".py", ".jar", ".exe" };

        return Directory.GetFiles(parsersPath)
            .Where(path => allowedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .Select(ReadParserInfo)
            .ToList();
    }

    public PipelineLaunchResult StartInputPipeline(InputPipelineLaunchRequest launchRequest)
    {
        try
        {
            string managerPath = Path.Combine(_rootPath, "PipelineManagers", "InputPipelineManager.py");
            if (!File.Exists(managerPath))
            {
                return new PipelineLaunchResult
                {
                    Started = false,
                    Message = $"InputPipelineManager.py не найден: {managerPath}"
                };
            }

            string pythonPath = ResolvePythonPath();
            ParserLaunchSettings settings = CloneSettings(launchRequest.Parser.Settings);
            settings.StartUrl = launchRequest.StartUrl.Trim();
            settings.MaxCars = launchRequest.MaxCars;
            settings.StreamBatchSize = launchRequest.StreamBatchSize;

            object payload = BuildInputPayload(launchRequest.DatabasePath, launchRequest.Parser, settings, pythonPath);
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
                Message = $"Парсинг запущен: {launchRequest.Parser.DisplayName} → {Path.GetFileName(launchRequest.DatabasePath)}. Обнови хаб через общую кнопку, чтобы увидеть карточку процесса."
            };
        }
        catch (Exception ex)
        {
            return new PipelineLaunchResult
            {
                Started = false,
                Message = $"Запуск парсинга не удался: {ex.Message}"
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

    private ParserLaunchItem ReadParserInfo(string path)
    {
        FileInfo file = new FileInfo(path);
        ParserLaunchItem item = new ParserLaunchItem
        {
            FileName = file.Name,
            DisplayName = Path.GetFileNameWithoutExtension(file.Name),
            Path = file.FullName,
            Runtime = DetectRuntime(file.FullName),
            Description = "Описание модуля не задано.",
            Settings = ReadDefaultSettings(file.FullName)
        };

        string configPath = Path.Combine(_rootPath, "Configs", "ParserConfigs", Path.GetFileNameWithoutExtension(file.Name) + ".json");
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

            ApplySettings(item.Settings, settingsElement);
        }
        catch
        {
            item.Description = "Конфиг парсера найден, но его не удалось разобрать.";
        }

        return item;
    }

    private ParserLaunchSettings ReadDefaultSettings(string parserPath)
    {
        ParserLaunchSettings settings = new ParserLaunchSettings();
        string defaultSettingsPath = Path.Combine(_rootPath, "Configs", "ParserDefaultSettings.json");
        if (!File.Exists(defaultSettingsPath))
            return settings;

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(defaultSettingsPath));
            ApplySettings(settings, document.RootElement);
        }
        catch
        {
            // Если общий конфиг временно повреждён, используем безопасные значения по умолчанию.
        }

        return settings;
    }

    private void ApplySettings(ParserLaunchSettings settings, JsonElement source)
    {
        foreach (JsonProperty property in source.EnumerateObject())
        {
            if (property.NameEquals("metadata") || property.NameEquals("settingsSchema") || property.NameEquals("settings"))
                continue;

            string name = property.Name;
            JsonElement value = property.Value;

            if (string.Equals(name, "startUrl", StringComparison.OrdinalIgnoreCase))
            {
                settings.StartUrl = ReadElementAsString(value, settings.StartUrl);
                continue;
            }

            if (string.Equals(name, "maxCars", StringComparison.OrdinalIgnoreCase))
            {
                settings.MaxCars = ReadElementAsInt(value, settings.MaxCars);
                continue;
            }

            if (string.Equals(name, "streamBatchSize", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "batchSize", StringComparison.OrdinalIgnoreCase))
            {
                settings.StreamBatchSize = Math.Max(1, ReadElementAsInt(value, settings.StreamBatchSize));
                continue;
            }

            if (string.Equals(name, "requestDelaySeconds", StringComparison.OrdinalIgnoreCase))
            {
                settings.RequestDelaySeconds = Math.Max(0, ReadElementAsDouble(value, settings.RequestDelaySeconds));
                continue;
            }

            if (string.Equals(name, "retryCount", StringComparison.OrdinalIgnoreCase))
            {
                settings.RetryCount = Math.Max(0, ReadElementAsInt(value, settings.RetryCount));
                continue;
            }

            if (string.Equals(name, "rateLimitDelaySeconds", StringComparison.OrdinalIgnoreCase))
            {
                settings.RateLimitDelaySeconds = Math.Max(0, ReadElementAsDouble(value, settings.RateLimitDelaySeconds));
                continue;
            }

            settings.ExtraSettings[name] = ConvertJsonElement(value);
        }
    }

    private object BuildInputPayload(string databasePath, ParserLaunchItem parser, ParserLaunchSettings settings, string pythonPath)
    {
        Dictionary<string, object?> parserSettings = new Dictionary<string, object?>
        {
            ["startUrl"] = settings.StartUrl,
            ["maxCars"] = settings.MaxCars,
            ["streamBatchSize"] = settings.StreamBatchSize,
            ["requestDelaySeconds"] = settings.RequestDelaySeconds,
            ["retryCount"] = settings.RetryCount,
            ["rateLimitDelaySeconds"] = settings.RateLimitDelaySeconds
        };

        foreach (KeyValuePair<string, object?> item in settings.ExtraSettings)
        {
            if (!parserSettings.ContainsKey(item.Key))
                parserSettings[item.Key] = item.Value;
        }

        string javaPath = ReadJavaPath();

        return new
        {
            parser = new
            {
                modulePath = parser.Path,
                parserPath = parser.Path,
                runtime = parser.Runtime,
                python = pythonPath,
                java = javaPath
            },
            parserSettings,
            apiSettings = new
            {
                brandCountryEnrichment = true,
                transmissionNormalization = false,
                driveTypeNormalization = false,
                fuelTypeNormalization = false
            },
            runtimeSettings = new
            {
                pythonPath,
                javaPath
            },
            dbPath = databasePath,
            configPath = Path.Combine(_rootPath, "Configs")
        };
    }

    private ParserLaunchSettings CloneSettings(ParserLaunchSettings source)
    {
        return new ParserLaunchSettings
        {
            StartUrl = source.StartUrl,
            MaxCars = source.MaxCars,
            StreamBatchSize = source.StreamBatchSize,
            RequestDelaySeconds = source.RequestDelaySeconds,
            RetryCount = source.RetryCount,
            RateLimitDelaySeconds = source.RateLimitDelaySeconds,
            ExtraSettings = new Dictionary<string, object?>(source.ExtraSettings, StringComparer.OrdinalIgnoreCase)
        };
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

        return value.ToString();
    }

    private int ReadElementAsInt(JsonElement value, int fallback)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result))
            return result;

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out result))
            return result;

        return fallback;
    }

    private double ReadElementAsDouble(JsonElement value, double fallback)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double result))
            return result;

        if (value.ValueKind == JsonValueKind.String)
        {
            string raw = (value.GetString() ?? "").Trim().Replace(",", ".");
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
                return result;
        }

        return fallback;
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
