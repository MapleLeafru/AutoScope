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
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Path { get; set; } = "";
    public string StatusText { get; set; } = "";
    public string Details { get; set; } = "";
    public string PipelineType { get; set; } = "";
    public string PipelineText { get; set; } = "";
    public string ModuleName { get; set; } = "";
    public string DatabaseName { get; set; } = "";
    public string ScheduleText { get; set; } = "";
    public string LastRunText { get; set; } = "";
    public string NextRunText { get; set; } = "";
    public string NextRunAtRaw { get; set; } = "";
    public string CreatedText { get; set; } = "";
    public string ToggleActionText { get; set; } = "";
    public bool Enabled { get; set; }
    public bool CanOpenFile { get; set; }
    public bool CanShowHistory { get; set; }
    public bool CanRun { get; set; }
    public bool CanEdit { get; set; }
    public int ScheduleEveryHours { get; set; }
    public bool IsManualOnly { get; set; }
    public DashboardStateKind StateKind { get; set; } = DashboardStateKind.Neutral;
}

public class DatabaseDashboardItem
{
    public string Name { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ConfigName { get; set; } = "";
    public string Path { get; set; } = "";
    public string Details { get; set; } = "";
    public string RecordsText { get; set; } = "";
    public string SizeText { get; set; } = "";
    public DashboardStateKind StateKind { get; set; } = DashboardStateKind.Neutral;

    public string ConfigText => string.IsNullOrWhiteSpace(ConfigName)
        ? "Конфиг: не указан"
        : $"Конфиг: {ConfigName}";

    public string FileText => string.IsNullOrWhiteSpace(FileName)
        ? "Файл не определён"
        : $"Файл: {FileName}";

    public string LaunchDisplayName => string.IsNullOrWhiteSpace(ConfigName)
        ? Name
        : $"{Name} ({ConfigName})";

    public override string ToString()
    {
        return LaunchDisplayName;
    }
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
    public bool CanStop { get; set; }
    public bool CanPause { get; set; }
    public bool CanResume { get; set; }
    public bool IsStopped { get; set; }
    public int ProgressPercent { get; set; }
    public DateTime LastUpdatedAt { get; set; } = DateTime.MinValue;
    public DashboardStateKind StateKind { get; set; } = DashboardStateKind.Neutral;
}
