using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

// Единый менеджер запусков. Нужен для параллельного запуска ручных пайплайнов,
// сценариев и будущего графического интерфейса.
public class RunManagerService
{
    private readonly object _lock = new object();
    private readonly List<ManagedRun> _runs = new List<ManagedRun>();
    private readonly ConsoleInputService _input;

    public RunManagerService(ConsoleInputService input)
    {
        _input = input;
    }

    // Добавляет запуск в менеджер и выполняет его в фоне.
    public RunTaskInfo StartRun(
        string title,
        string runType,
        string databasePath,
        string modulePath,
        Func<ProcessRunResult> action
    )
    {
        string runId = Guid.NewGuid().ToString("N");
        RunTaskInfo info = new RunTaskInfo
        {
            RunId = runId,
            ShortId = runId.Substring(0, 8),
            Title = title,
            RunType = runType,
            Status = "queued",
            ModuleName = Path.GetFileName(modulePath),
            DbName = Path.GetFileName(databasePath),
            Message = "Ожидает запуска"
        };

        ManagedRun managedRun = new ManagedRun(info, action);

        lock (_lock)
        {
            _runs.Add(managedRun);
        }

        managedRun.Task = Task.Run(() => ExecuteRun(managedRun));
        return CloneInfo(info);
    }

    // Показывает простое меню менеджера запусков.
    public void ShowMenu()
    {
        while (true)
        {
            Console.WriteLine("=== Менеджер запусков ===");
            Console.WriteLine("0 - Вернуться назад");
            Console.WriteLine("1 - Показать активные запуски");
            Console.WriteLine("2 - Показать все запуски текущей сессии");
            Console.WriteLine("3 - Очистить завершённые запуски из списка");

            int selectedMode = _input.ReadMenuNumber(min: 0, max: 3, "Номер выбранного режима: ");

            if (selectedMode == 0) { return; }
            if (selectedMode == 1) { ShowRuns(activeOnly: true); }
            if (selectedMode == 2) { ShowRuns(activeOnly: false); }
            if (selectedMode == 3) { ClearCompletedRuns(); }
        }
    }

    // Возвращает снимок всех запусков. Это пригодится будущему UI.
    public List<RunTaskInfo> GetRunsSnapshot()
    {
        lock (_lock)
        {
            return _runs
                .Select(run => CloneInfo(run.Info))
                .ToList();
        }
    }

    // Возвращает количество активных запусков.
    public int GetActiveRunCount()
    {
        lock (_lock)
        {
            return _runs.Count(run => IsActiveStatus(run.Info.Status));
        }
    }

    // Выполняет конкретную задачу и обновляет её состояние.
    private void ExecuteRun(ManagedRun run)
    {
        DateTime startedAt = DateTime.Now;
        UpdateRun(run, info =>
        {
            info.Status = "running";
            info.StartedAt = FormatTime(startedAt);
            info.Message = "Выполняется";
        });

        ProcessRunResult result;

        try
        {
            result = run.Action();
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
        bool success = IsPipelineRunSuccessful(result);
        string status = success ? "success" : "error";

        UpdateRun(run, info =>
        {
            info.Status = status;
            info.FinishedAt = FormatTime(finishedAt);
            info.DurationSeconds = Math.Round((finishedAt - startedAt).TotalSeconds, 2);
            info.ExitCode = result.ExitCode;
            info.Message = success ? "Запуск завершён успешно" : "Запуск завершился с ошибкой";
            info.OutputPreview = BuildPreview(result.Output);
            info.ErrorPreview = BuildPreview(result.Error);
        });

        Console.WriteLine();
        Console.WriteLine($"[Менеджер запусков] {run.Info.Title}: {run.Info.Message} (ID: {run.Info.ShortId})");
        Console.WriteLine();
    }

    // Показывает список запусков в консоли.
    private void ShowRuns(bool activeOnly)
    {
        List<RunTaskInfo> runs = GetRunsSnapshot();

        if (activeOnly)
            runs = runs.Where(run => IsActiveStatus(run.Status)).ToList();

        if (runs.Count == 0)
        {
            Console.WriteLine(activeOnly ? "Активных запусков нет." : "Запусков в текущей сессии пока нет.");
            Console.WriteLine();
            return;
        }

        Console.WriteLine(activeOnly ? "=== Активные запуски ===" : "=== Запуски текущей сессии ===");
        Console.WriteLine();

        foreach (RunTaskInfo run in runs.OrderByDescending(run => run.StartedAt))
            PrintRunInfo(run);

        Console.WriteLine();
    }

    // Удаляет из списка завершённые запуски. Сами логи pipeline не удаляются.
    private void ClearCompletedRuns()
    {
        int removedCount;

        lock (_lock)
        {
            removedCount = _runs.RemoveAll(run => !IsActiveStatus(run.Info.Status));
        }

        Console.WriteLine($"Удалено завершённых запусков из списка: {removedCount}.");
        Console.WriteLine();
    }

    // Печатает одну карточку запуска.
    private void PrintRunInfo(RunTaskInfo run)
    {
        Console.WriteLine($"ID: {run.ShortId}");
        Console.WriteLine($"Название: {run.Title}");
        Console.WriteLine($"Тип: {FormatRunType(run.RunType)}");
        Console.WriteLine($"Статус: {FormatStatus(run.Status)}");
        Console.WriteLine($"База: {FormatValue(run.DbName)}");
        Console.WriteLine($"Модуль: {FormatValue(run.ModuleName)}");
        Console.WriteLine($"Начало: {FormatValue(run.StartedAt)}");

        if (!string.IsNullOrWhiteSpace(run.FinishedAt))
            Console.WriteLine($"Завершение: {run.FinishedAt}");

        if (run.DurationSeconds > 0)
            Console.WriteLine($"Длительность: {run.DurationSeconds} сек.");

        Console.WriteLine($"Сообщение: {run.Message}");

        if (!string.IsNullOrWhiteSpace(run.ErrorPreview))
            Console.WriteLine($"Ошибка: {run.ErrorPreview}");

        Console.WriteLine();
    }

    // Потокобезопасно меняет состояние запуска.
    private void UpdateRun(ManagedRun run, Action<RunTaskInfo> update)
    {
        lock (_lock)
        {
            update(run.Info);
        }
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

    // Создаёт короткое превью stdout/stderr.
    private string BuildPreview(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        string singleLine = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return singleLine.Length <= 300 ? singleLine : singleLine.Substring(0, 300) + "...";
    }

    // Проверяет, относится ли статус к активным.
    private bool IsActiveStatus(string status)
    {
        return status == "queued" || status == "running";
    }

    // Копирует объект, чтобы внешний код не менял состояние напрямую.
    private RunTaskInfo CloneInfo(RunTaskInfo source)
    {
        return new RunTaskInfo
        {
            RunId = source.RunId,
            ShortId = source.ShortId,
            Title = source.Title,
            RunType = source.RunType,
            Status = source.Status,
            ModuleName = source.ModuleName,
            DbName = source.DbName,
            StartedAt = source.StartedAt,
            FinishedAt = source.FinishedAt,
            DurationSeconds = source.DurationSeconds,
            ExitCode = source.ExitCode,
            Message = source.Message,
            OutputPreview = source.OutputPreview,
            ErrorPreview = source.ErrorPreview
        };
    }

    // Форматирует технический тип запуска для консоли.
    private string FormatRunType(string runType)
    {
        return runType switch
        {
            "input" => "InputPipeline",
            "output" => "OutputPipeline",
            "scenario" => "сценарий",
            _ => runType
        };
    }

    // Форматирует технический статус для консоли.
    private string FormatStatus(string status)
    {
        return status switch
        {
            "queued" => "ожидает запуска",
            "running" => "выполняется",
            "success" => "успешно",
            "error" => "ошибка",
            _ => status
        };
    }

    // Форматирует пустые значения.
    private string FormatValue(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "не задано" : value;
    }

    // Форматирует время запуска.
    private string FormatTime(DateTime value)
    {
        return value.ToString("yyyy-MM-dd HH:mm:ss");
    }

    // Внутреннее представление запуска вместе с делегатом выполнения.
    private class ManagedRun
    {
        public RunTaskInfo Info { get; }
        public Func<ProcessRunResult> Action { get; }
        public Task? Task { get; set; }

        public ManagedRun(RunTaskInfo info, Func<ProcessRunResult> action)
        {
            Info = info;
            Action = action;
        }
    }
}
