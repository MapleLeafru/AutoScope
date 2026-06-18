using System.Collections.Generic;

namespace AutoScope.WpfClient.Models;

public class ParserLaunchItem
{
    public string DisplayName { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Path { get; set; } = "";
    public string Description { get; set; } = "";
    public string Runtime { get; set; } = "python";
    public ParserLaunchSettings Settings { get; set; } = new();

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(DisplayName) ? FileName : DisplayName;
    }
}

public class ParserLaunchSettings
{
    public string StartUrl { get; set; } = "";
    public int MaxCars { get; set; } = 20;
    public int StreamBatchSize { get; set; } = 5;
    public double RequestDelaySeconds { get; set; } = 1.2;
    public int RetryCount { get; set; } = 2;
    public double RateLimitDelaySeconds { get; set; } = 20;
    public Dictionary<string, object?> ExtraSettings { get; set; } = new();
}

public class InputPipelineLaunchRequest
{
    public string DatabasePath { get; set; } = "";
    public ParserLaunchItem Parser { get; set; } = new();
    public string StartUrl { get; set; } = "";
    public int MaxCars { get; set; }
    public int StreamBatchSize { get; set; }
}

public class AnalyzerLaunchItem
{
    public string DisplayName { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Path { get; set; } = "";
    public string Description { get; set; } = "";
    public string Runtime { get; set; } = "python";
    public Dictionary<string, object?> Settings { get; set; } = new();

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(DisplayName) ? FileName : DisplayName;
    }
}

public class OutputPipelineLaunchSettings
{
    public bool LatestOnly { get; set; } = true;
    public bool OnlyChanged { get; set; }
    public string Brand { get; set; } = "";
    public string Model { get; set; } = "";
    public string SaleRegion { get; set; } = "";
    public int? YearFrom { get; set; }
    public int? YearTo { get; set; }
    public Dictionary<string, object?> ExtraSettings { get; set; } = new();
}

public class OutputPipelineLaunchRequest
{
    public string DatabasePath { get; set; } = "";
    public AnalyzerLaunchItem Analyzer { get; set; } = new();
    public OutputPipelineLaunchSettings Settings { get; set; } = new();
}

public class PipelineLaunchResult
{
    public bool Started { get; set; }
    public string Message { get; set; } = "";
}
