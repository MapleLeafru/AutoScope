using System;

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
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string StatusText { get; set; } = "";
    public string TypeText { get; set; } = "";
    public string TimeText { get; set; } = "";
    public string Details { get; set; } = "";
    public string LogPath { get; set; } = "";
    public string StageText { get; set; } = "";
    public string CountText { get; set; } = "";
    public string ProgressText { get; set; } = "";
    public string PrimaryActionText { get; set; } = "";
    public string SecondaryActionText { get; set; } = "";
    public string ActionHint { get; set; } = "";
    public bool CanOpenLog { get; set; }
    public bool CanOpenDetails { get; set; } = true;
    public bool IsProgressVisible { get; set; }
    public bool IsActionRow { get; set; }
    public bool IsSecondaryActionVisible { get; set; }
    public bool IsPrimaryActionEnabled { get; set; } = true;
    public bool IsHistoryItem { get; set; }
    public bool IsSessionItem { get; set; }
    public bool IsRunningLike { get; set; }
    public int ProgressPercent { get; set; }
    public DateTime LastUpdatedAt { get; set; } = DateTime.MinValue;
    public DashboardStateKind StateKind { get; set; } = DashboardStateKind.Neutral;
}
