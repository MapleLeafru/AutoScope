// Описывает один процесс pipeline или сценария в менеджере процессов.
public class RunTaskInfo
{
    public string RunId { get; set; } = "";
    public string ShortId { get; set; } = "";
    public string Title { get; set; } = "";
    public string RunType { get; set; } = "";
    public string Status { get; set; } = "queued";
    public string State { get; set; } = "";
    public string ModuleName { get; set; } = "";
    public string DbName { get; set; } = "";
    public string StartedAt { get; set; } = "";
    public string FinishedAt { get; set; } = "";
    public double DurationSeconds { get; set; }
    public int ExitCode { get; set; }
    public string Message { get; set; } = "";
    public string OutputPreview { get; set; } = "";
    public string ErrorPreview { get; set; } = "";
}
