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
    public int MaxCars { get; set; } = 200;
    public int StreamBatchSize { get; set; } = 100;
    public double RequestDelaySeconds { get; set; } = 1.2;
    public int RetryCount { get; set; } = 3;
    public double RateLimitDelaySeconds { get; set; } = 5;
    public InputApiLaunchSettings ApiSettings { get; set; } = new();
    public Dictionary<string, object?> ExtraSettings { get; set; } = new();
}

public class InputApiLaunchSettings
{
    public bool BrandCountryEnrichment { get; set; } = true;
    public bool TransmissionNormalization { get; set; } = false;
    public bool DriveTypeNormalization { get; set; } = false;
    public bool FuelTypeNormalization { get; set; } = false;
}

public class BooleanChoiceOption
{
    public string Key { get; set; } = "default";
    public string DisplayName { get; set; } = "По умолчанию";
    public bool? Value { get; set; }

    public BooleanChoiceOption()
    {
    }

    public BooleanChoiceOption(string key, string displayName, bool? value)
    {
        Key = key;
        DisplayName = displayName;
        Value = value;
    }

    public override string ToString()
    {
        return DisplayName;
    }
}

public class InputPipelineLaunchRequest
{
    public string DatabasePath { get; set; } = "";
    public ParserLaunchItem Parser { get; set; } = new();
    public string StartUrl { get; set; } = "";
    public int? MaxCars { get; set; }
    public int? StreamBatchSize { get; set; }
    public double? RequestDelaySeconds { get; set; }
    public int? RetryCount { get; set; }
    public double? RateLimitDelaySeconds { get; set; }
    public bool? BrandCountryEnrichment { get; set; }
    public bool? TransmissionNormalization { get; set; }
    public bool? DriveTypeNormalization { get; set; }
    public bool? FuelTypeNormalization { get; set; }
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
