// Описывает один фактический запуск сохранённого задания.
public class JobRunRecord
{
    public string JobId { get; set; } = "";
    public string JobName { get; set; } = "";
    public string StartedAt { get; set; } = "";
    public string FinishedAt { get; set; } = "";
    public double DurationSeconds { get; set; }
    public string Status { get; set; } = "";
    public string PipelineType { get; set; } = "";
    public string ModuleName { get; set; } = "";
    public string DbName { get; set; } = "";
    public int ExitCode { get; set; }
    public string Message { get; set; } = "";
    public string OutputPreview { get; set; } = "";
    public string ErrorPreview { get; set; } = "";
}
