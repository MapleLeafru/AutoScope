// Описывает одно сохранённое задание автоповтора.
public class JobConfig
{
    public string JobId { get; set; } = "";
    public string JobName { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string PipelineType { get; set; } = "input";

    public string DbPath { get; set; } = "";
    public string ParserPath { get; set; } = "";
    public string AnalyzerPath { get; set; } = "";

    public ParserRunSettings ParserSettings { get; set; } = new ParserRunSettings();
    public RuntimeSettings RuntimeSettings { get; set; } = new RuntimeSettings();
    public JobScheduleSettings Schedule { get; set; } = new JobScheduleSettings();

    public string CreatedAt { get; set; } = "";
    public string LastRunAt { get; set; } = "";
    public string NextRunAt { get; set; } = "";
}

// Хранит простое расписание задания. Пока поддерживается интервал в часах.
public class JobScheduleSettings
{
    public string Type { get; set; } = "interval";
    public int EveryHours { get; set; } = 24;
}
