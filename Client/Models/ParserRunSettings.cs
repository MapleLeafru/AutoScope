using System.Collections.Generic;
using System.Text.Json;

// Параметры запуска парсера, которые передаются в parserSettings.
public class ParserRunSettings
{
    public string StartUrl { get; set; } = "";
    public int MaxCars { get; set; }
    public int StreamBatchSize { get; set; }

    public double RequestDelaySeconds { get; set; } = 1.2;
    public int RetryCount { get; set; } = 3;
    public double RateLimitDelaySeconds { get; set; } = 5.0;

    // Дополнительные параметры из личного конфига конкретного парсера.
    // Они передаются внешнему модулю как обычные поля parserSettings.
    public Dictionary<string, JsonElement> ExtraSettings { get; set; } = new Dictionary<string, JsonElement>();
}
