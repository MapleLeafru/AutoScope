namespace AutoScope.WpfClient.Models;

public enum DashboardStateKind
{
    Neutral,
    Running,
    Success,
    Error,
    Warning
}

public class ScenarioDashboardItem
{
    public string Name { get; set; } = "";
    public string StatusText { get; set; } = "";
    public string Details { get; set; } = "";
    public DashboardStateKind StateKind { get; set; } = DashboardStateKind.Neutral;
}

public class DatabaseDashboardItem
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Details { get; set; } = "";
    public string RecordsText { get; set; } = "";
    public DashboardStateKind StateKind { get; set; } = DashboardStateKind.Neutral;
}

public class ProcessDashboardItem
{
    public string Name { get; set; } = "";
    public string StatusText { get; set; } = "";
    public string Details { get; set; } = "";
    public DashboardStateKind StateKind { get; set; } = DashboardStateKind.Neutral;
}
