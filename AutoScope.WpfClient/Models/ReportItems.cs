using System;

namespace AutoScope.WpfClient.Models;

public sealed class ReportDashboardItem
{
    public string Name { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Path { get; set; } = "";
    public string AnalyzerName { get; set; } = "";
    public string AnalyzerText { get; set; } = "";
    public string DateText { get; set; } = "";
    public string SizeText { get; set; } = "";
    public string Details { get; set; } = "";
    public DateTime ModifiedAt { get; set; }
    public bool CanOpen { get; set; } = true;
}
