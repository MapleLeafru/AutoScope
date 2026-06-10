using System;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

// Запускает Python-скрипты проекта и передаёт им JSON через stdin.
public class PythonProcessService
{
    private readonly AppPaths _paths;

    public PythonProcessService(AppPaths paths)
    {
        _paths = paths;
    }

    // Запускает Python-файл, передаёт входной JSON и возвращает stdout/stderr.
    public ProcessRunResult RunScript(string scriptPath, string inputJson)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = _paths.PythonPath,
            Arguments = $"\"{scriptPath}\"",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        startInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
        startInfo.EnvironmentVariables["PYTHONUTF8"] = "1";

        using (Process? process = Process.Start(startInfo))
        {
            if (process == null)
            {
                return new ProcessRunResult
                {
                    Output = "",
                    Error = $"Не удалось запустить Python-скрипт: {scriptPath}",
                    ExitCode = -1
                };
            }

            StringBuilder errorBuilder = new StringBuilder();

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task errorTask = Task.Run(() => ReadErrorStream(process, errorBuilder));

            process.StandardInput.WriteLine(inputJson);
            process.StandardInput.Flush();
            process.StandardInput.Close();

            process.WaitForExit();
            Task.WaitAll(outputTask, errorTask);

            return new ProcessRunResult
            {
                Output = outputTask.Result,
                Error = errorBuilder.ToString(),
                ExitCode = process.ExitCode
            };
        }
    }

    // Читает stderr Python-процесса. Progress-события показывает сразу, остальные строки сохраняет как ошибки/логи.
    private void ReadErrorStream(Process process, StringBuilder errorBuilder)
    {
        string? line;

        while ((line = process.StandardError.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (line.TrimStart().StartsWith("[PROGRESS]"))
                PrintProgressLine(line);
            else
                errorBuilder.AppendLine(line);
        }
    }

    // Выводит progress-событие в понятном для пользователя виде.
    private void PrintProgressLine(string line)
    {
        try
        {
            string json = line.Substring(line.IndexOf(']') + 1).Trim();
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            string stage = GetString(root, "stage", "stage");
            string message = GetString(root, "message", "");
            int percent = GetInt(root, "percent", 0);
            int current = GetInt(root, "current", 0);
            int total = GetInt(root, "total", 0);

            string counter = total > 0 ? $" ({current}/{total})" : "";
            Console.WriteLine($"[{percent}%] {FormatStage(stage)}{counter}: {message}");
        }
        catch
        {
            Console.WriteLine(line);
        }
    }

    // Безопасно получает строку из JSON-объекта.
    private string GetString(JsonElement root, string propertyName, string fallback)
    {
        if (root.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            return value.GetString() ?? fallback;

        return fallback;
    }

    // Безопасно получает число из JSON-объекта.
    private int GetInt(JsonElement root, string propertyName, int fallback)
    {
        if (root.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.Number)
        {
            if (value.TryGetInt32(out int result))
                return result;
        }

        return fallback;
    }

    // Переводит технический stage в короткий русский текст для консоли.
    private string FormatStage(string stage)
    {
        return stage switch
        {
            "collect_links" => "Сбор ссылок",
            "parse_ads" => "Обработка объявлений",
            "done" => "Завершение",
            "rate_limit" => "Ограничение запросов",
            "error" => "Ошибка",
            _ => stage
        };
    }
}
