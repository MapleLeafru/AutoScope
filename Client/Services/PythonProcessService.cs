using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

// Запускает Python-скрипты проекта и передаёт им JSON через stdin.
public class PythonProcessService
{
    private readonly AppPaths _paths;
    private readonly object _consoleLock = new object();

    public PythonProcessService(AppPaths paths)
    {
        _paths = paths;
    }

    // Запускает Python-файл, передаёт входной JSON и возвращает stdout/stderr.
    public ProcessRunResult RunScript(string scriptPath, string inputJson)
    {
        return RunScript(scriptPath, inputJson, enableProgressInput: true);
    }

    // Запускает Python-файл с возможностью отключить чтение Enter для фоновых запусков.
    public ProcessRunResult RunScript(string scriptPath, string inputJson, bool enableProgressInput)
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
            ProgressState progressState = new ProgressState();
            using CancellationTokenSource progressInputCancel = new CancellationTokenSource();

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task errorTask = Task.Run(() => ReadErrorStream(process, errorBuilder, progressState));
            Task? progressInputTask = enableProgressInput
                ? Task.Run(() => WatchProgressRequests(progressState, progressInputCancel.Token))
                : null;

            process.StandardInput.WriteLine(inputJson);
            process.StandardInput.Flush();
            process.StandardInput.Close();

            process.WaitForExit();
            progressInputCancel.Cancel();

            Task.WaitAll(outputTask, errorTask);

            if (progressInputTask != null)
            {
                try
                {
                    progressInputTask.Wait(300);
                }
                catch
                {
                    // Поток чтения Enter не должен мешать завершению процесса.
                }
            }

            return new ProcessRunResult
            {
                Output = outputTask.Result,
                Error = errorBuilder.ToString(),
                ExitCode = process.ExitCode
            };
        }
    }

    // Читает stderr Python-процесса. Progress-события показывает по правилам консольного режима.
    private void ReadErrorStream(Process process, StringBuilder errorBuilder, ProgressState progressState)
    {
        string? line;

        while ((line = process.StandardError.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (line.TrimStart().StartsWith("[PROGRESS]"))
                HandleProgressLine(line, progressState);
            else
                errorBuilder.AppendLine(line);
        }
    }

    // Обрабатывает progress-событие: сохраняет последнее состояние и печатает только ключевые этапы.
    private void HandleProgressLine(string line, ProgressState progressState)
    {
        ProgressSnapshot? snapshot = ParseProgressLine(line);

        if (snapshot == null)
        {
            WriteConsoleLine(line);
            return;
        }

        bool shouldPrintStartMessage = progressState.Update(snapshot);
        if (shouldPrintStartMessage)
            WriteConsoleLine("Парсер запущен, для вывода актуального состояния нажмите Enter");

        if (progressState.ShouldPrintAutomatically(snapshot))
            WriteConsoleLine(FormatProgressSnapshot(snapshot));
    }

    // Следит за нажатием Enter во время работы Python-процесса и выводит актуальное состояние.
    private void WatchProgressRequests(ProgressState progressState, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (Console.KeyAvailable)
                {
                    ConsoleKeyInfo key = Console.ReadKey(intercept: true);

                    if (key.Key == ConsoleKey.Enter)
                        PrintCurrentProgress(progressState);
                }
            }
            catch
            {
                // В некоторых режимах консоли KeyAvailable может быть недоступен.
                return;
            }

            Thread.Sleep(100);
        }
    }

    // Печатает последнее известное состояние парсера по запросу пользователя.
    private void PrintCurrentProgress(ProgressState progressState)
    {
        ProgressSnapshot? snapshot = progressState.GetLatest();

        if (snapshot == null)
            return;

        WriteConsoleLine(FormatProgressSnapshot(snapshot));
    }

    // Разбирает progress-событие из строки stderr.
    private ProgressSnapshot? ParseProgressLine(string line)
    {
        try
        {
            string json = line.Substring(line.IndexOf(']') + 1).Trim();
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            return new ProgressSnapshot
            {
                Stage = GetString(root, "stage", "stage"),
                StageTitle = GetString(root, "stageTitle", GetString(root, "stage_title", "")),
                StageIndex = GetInt(root, "stageIndex", GetInt(root, "stage_index", 0)),
                StageTotal = GetInt(root, "stageTotal", GetInt(root, "stage_total", 0)),
                Message = GetString(root, "message", ""),
                Percent = GetInt(root, "percent", 0),
                Current = GetInt(root, "current", 0),
                Total = GetInt(root, "total", 0)
            };
        }
        catch
        {
            return null;
        }
    }

    // Форматирует progress-событие в понятную строку консоли.
    private string FormatProgressSnapshot(ProgressSnapshot snapshot)
    {
        string counter = snapshot.Total > 0 ? $" ({snapshot.Current}/{snapshot.Total})" : "";
        string message = string.IsNullOrWhiteSpace(snapshot.Message)
            ? FormatStage(snapshot)
            : snapshot.Message;

        string stageNumber = FormatStageNumber(snapshot);
        return $"[{snapshot.Percent}%] [{DateTime.Now:HH:mm:ss}] {stageNumber}{FormatStage(snapshot)}{counter}: {message}";
    }

    // Потокобезопасно пишет строку в консоль, чтобы progress и Enter не перемешивались.
    private void WriteConsoleLine(string message)
    {
        lock (_consoleLock)
        {
            Console.WriteLine(message);
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

    // Форматирует номер текущего этапа, если парсер передал его в progress-событии.
    private string FormatStageNumber(ProgressSnapshot snapshot)
    {
        if (snapshot.StageIndex <= 0 || snapshot.StageTotal <= 0)
            return "";

        return $"[Этап {snapshot.StageIndex} из {snapshot.StageTotal}] ";
    }

    // Переводит технический stage в короткий русский текст для консоли.
    private string FormatStage(ProgressSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.StageTitle))
            return snapshot.StageTitle;

        return snapshot.Stage switch
        {
            "collect_links" => "Сбор ссылок",
            "parse_ads" => "Обработка объявлений",
            "listing_pages" => "Сбор объявлений из выдачи",
            "single_ad" => "Обработка карточки объявления",
            "blocked" => "Сбор остановлен защитой Auto.ru",
            "warning" => "Предупреждение",
            "done" => "Завершение",
            "rate_limit" => "Ограничение запросов",
            "error" => "Ошибка",
            _ => snapshot.Stage
        };
    }

    // Хранит последнее известное progress-состояние.
    private class ProgressState
    {
        private readonly object _lock = new object();
        private readonly HashSet<string> _printedCompletedStages = new HashSet<string>();
        private ProgressSnapshot? _latest;
        private bool _started;

        // Обновляет состояние. Возвращает true только при первом progress-событии.
        public bool Update(ProgressSnapshot snapshot)
        {
            lock (_lock)
            {
                _latest = snapshot;

                if (_started)
                    return false;

                _started = true;
                return true;
            }
        }

        // Проверяет, нужно ли показать событие без запроса пользователя.
        public bool ShouldPrintAutomatically(ProgressSnapshot snapshot)
        {
            lock (_lock)
            {
                if (snapshot.Stage == "rate_limit" || snapshot.Stage == "error" || snapshot.Stage == "blocked" || snapshot.Stage == "warning")
                    return true;

                if (snapshot.Stage == "done")
                    return MarkStageAsPrinted(snapshot.Stage);

                if (IsStageCompleted(snapshot))
                    return MarkStageAsPrinted(snapshot.Stage);

                return false;
            }
        }

        // Возвращает последнее известное состояние.
        public ProgressSnapshot? GetLatest()
        {
            lock (_lock)
            {
                return _latest;
            }
        }

        // Проверяет, что этап реально завершён, а не просто округлился до 100%.
        private bool IsStageCompleted(ProgressSnapshot snapshot)
        {
            return snapshot.Total > 0 && snapshot.Current >= snapshot.Total;
        }

        // Защищает завершение этапа от повторного автоматического вывода.
        private bool MarkStageAsPrinted(string stage)
        {
            if (_printedCompletedStages.Contains(stage))
                return false;

            _printedCompletedStages.Add(stage);
            return true;
        }
    }

    // Описывает одно progress-событие.
    private class ProgressSnapshot
    {
        public string Stage { get; set; } = "stage";
        public string StageTitle { get; set; } = "";
        public int StageIndex { get; set; }
        public int StageTotal { get; set; }
        public string Message { get; set; } = "";
        public int Percent { get; set; }
        public int Current { get; set; }
        public int Total { get; set; }
    }
}
