using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

// Единый менеджер процессов. Нужен для параллельного запуска ручных пайплайнов,
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

    // Добавляет процесс в менеджер и выполняет его в фоне.
    public RunTaskInfo StartRun(
        string title,
        string runType,
        string databasePath,
        string modulePath,
        Func<Action<string>, ProcessRunResult> action
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
            State = "Ожидает запуска",
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

    // Удобная перегрузка для процессов, которым не нужно обновлять progress-состояние.
    public RunTaskInfo StartRun(
        string title,
        string runType,
        string databasePath,
        string modulePath,
        Func<ProcessRunResult> action
    )
    {
        return StartRun(
            title,
            runType,
            databasePath,
            modulePath,
            _ => action()
        );
    }

    // Показывает экран конкретного процесса после запуска.
    // Enter показывает только короткое состояние, 1 — полную карточку, 0 — возврат в меню.
    public void ShowProcessScreen(string runId)
    {
        RunTaskInfo? initialRun = GetRunSnapshot(runId);
        if (initialRun == null)
        {
            Console.WriteLine("Процесс не найден.");
            Console.WriteLine();
            return;
        }

        Console.WriteLine($"Процесс запущен. ID процесса: {initialRun.ShortId}.");
        Console.WriteLine("Enter - показать текущее состояние процесса");
        Console.WriteLine("1 - показать полную информацию о процессе");
        Console.WriteLine("0 - вернуться в меню и скрыть вывод этого процесса");
        Console.WriteLine();

        string lastKnownStatus = initialRun.Status;

        while (true)
        {
            RunTaskInfo? run = GetRunSnapshot(runId);
            if (run == null)
            {
                Console.WriteLine("Процесс не найден.");
                Console.WriteLine();
                return;
            }

            if (IsActiveStatus(lastKnownStatus) && !IsActiveStatus(run.Status))
            {
                Console.WriteLine();
                Console.WriteLine("Процесс завершён.");
                PrintRunInfo(run);
                return;
            }

            lastKnownStatus = run.Status;

            try
            {
                if (Console.KeyAvailable)
                {
                    ConsoleKeyInfo key = Console.ReadKey(intercept: true);

                    if (key.Key == ConsoleKey.Enter)
                    {
                        PrintShortRunState(run);
                    }
                    else if (key.Key == ConsoleKey.D1 || key.Key == ConsoleKey.NumPad1)
                    {
                        Console.WriteLine();
                        PrintRunInfo(run);
                    }
                    else if (key.Key == ConsoleKey.D0 || key.Key == ConsoleKey.NumPad0)
                    {
                        Console.WriteLine();
                        return;
                    }
                }
            }
            catch
            {
                // Если консоль не поддерживает KeyAvailable, оставляем безопасный ручной выход.
                Console.WriteLine("Нажмите Enter, чтобы вернуться в меню.");
                Console.ReadLine();
                Console.WriteLine();
                return;
            }

            System.Threading.Thread.Sleep(100);
        }
    }

    // Показывает простое меню менеджера процессов.
    public void ShowMenu()
    {
        while (true)
        {
            Console.WriteLine("=== Менеджер процессов ===");
            Console.WriteLine("0 - Вернуться назад");
            Console.WriteLine("1 - Показать активные процессы");
            Console.WriteLine("2 - Показать все процессы текущей сессии");
            Console.WriteLine("3 - Очистить завершённые процессы из списка");

            int selectedMode = _input.ReadMenuNumber(min: 0, max: 3, "Номер выбранного режима: ");

            if (selectedMode == 0) { return; }
            if (selectedMode == 1) { ShowRuns(activeOnly: true); }
            if (selectedMode == 2) { ShowRuns(activeOnly: false); }
            if (selectedMode == 3) { ClearCompletedRuns(); }
        }
    }

    // Возвращает снимок всех процессов. Это пригодится будущему UI.
    public List<RunTaskInfo> GetRunsSnapshot()
    {
        lock (_lock)
        {
            return _runs
                .Select(run => CloneInfo(run.Info))
                .ToList();
        }
    }

    // Возвращает количество активных процессов.
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
            info.State = "Процесс выполняется. Подробное состояние появится после первого progress-события.";
        });

        ProcessRunResult result;

        try
        {
            Action<string> progressStateChanged = state => UpdateRun(run, info =>
            {
                info.State = string.IsNullOrWhiteSpace(state) ? info.State : state;
            });

            result = run.Action(progressStateChanged);
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
            info.Message = success ? "Процесс завершён успешно" : "Процесс завершился с ошибкой";
            info.OutputPreview = BuildPreview(result.Output);
            info.ErrorPreview = BuildPreview(result.Error);
            info.State = info.Message;
        });
    }

    // Показывает список процессов в консоли.
    private void ShowRuns(bool activeOnly)
    {
        List<RunTaskInfo> runs = GetRunsSnapshot();

        if (activeOnly)
            runs = runs.Where(run => IsActiveStatus(run.Status)).ToList();

        if (runs.Count == 0)
        {
            Console.WriteLine(activeOnly ? "Активных процессов нет." : "Процессов в текущей сессии пока нет.");
            Console.WriteLine();
            return;
        }

        Console.WriteLine(activeOnly ? "=== Активные процессы ===" : "=== Процессы текущей сессии ===");
        Console.WriteLine();

        foreach (RunTaskInfo run in runs.OrderByDescending(run => run.StartedAt))
            PrintRunInfo(run);

        Console.WriteLine();
    }

    // Удаляет из списка завершённые процессы. Сами логи pipeline не удаляются.
    private void ClearCompletedRuns()
    {
        int removedCount;

        lock (_lock)
        {
            removedCount = _runs.RemoveAll(run => !IsActiveStatus(run.Info.Status));
        }

        Console.WriteLine($"Удалено завершённых процессов из списка: {removedCount}.");
        Console.WriteLine();
    }

    // Печатает короткое состояние процесса в одну строку.
    private void PrintShortRunState(RunTaskInfo run)
    {
        string state = FormatValue(run.State);
        Console.WriteLine(state);
    }

    // Печатает одну карточку процесса.
    private void PrintRunInfo(RunTaskInfo run)
    {
        Console.WriteLine($"ID: {run.ShortId}");
        Console.WriteLine($"Название: {run.Title}");
        Console.WriteLine($"Тип: {FormatRunType(run.RunType)}");
        Console.WriteLine($"Статус: {FormatStatus(run.Status)}");
        Console.WriteLine($"Состояние: {FormatValue(run.State)}");
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

    // Возвращает снимок одного процесса.
    private RunTaskInfo? GetRunSnapshot(string runId)
    {
        lock (_lock)
        {
            ManagedRun? run = _runs.FirstOrDefault(item => item.Info.RunId == runId);
            return run == null ? null : CloneInfo(run.Info);
        }
    }

    // Потокобезопасно меняет состояние процесса.
    private void UpdateRun(ManagedRun run, Action<RunTaskInfo> update)
    {
        lock (_lock)
        {
            update(run.Info);
        }
    }

    // Определяет, можно ли считать процесс успешным.
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
            State = source.State,
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

    // Форматирует технический тип процесса для консоли.
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

    // Внутреннее представление процесса вместе с делегатом выполнения.
    private class ManagedRun
    {
        public RunTaskInfo Info { get; }
        public Func<Action<string>, ProcessRunResult> Action { get; }
        public Task? Task { get; set; }

        public ManagedRun(RunTaskInfo info, Func<Action<string>, ProcessRunResult> action)
        {
            Info = info;
            Action = action;
        }
    }
}
