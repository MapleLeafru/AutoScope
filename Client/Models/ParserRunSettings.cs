// Параметры запуска парсера, которые передаются в parserSettings.
public class ParserRunSettings
{
    public string StartUrl { get; set; } = "";
    public int MaxCars { get; set; }
    public int StreamBatchSize { get; set; }

    public double RequestDelaySeconds { get; set; } = 1.2;
    public int RetryCount { get; set; } = 3;
    public double RateLimitDelaySeconds { get; set; } = 5.0;
}
