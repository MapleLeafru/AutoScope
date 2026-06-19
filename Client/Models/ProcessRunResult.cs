// Результат запуска внешнего процесса.
public class ProcessRunResult
{
    public string Output { get; set; } = "";
    public string Error { get; set; } = "";
    public int ExitCode { get; set; }
}
