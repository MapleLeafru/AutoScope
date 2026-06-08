// Параметры запуска парсера, которые передаются в parserSettings.
public class ParserRunSettings
{
    public string StartUrl { get; set; } = "";
    public int MaxCars { get; set; }
    public int StreamBatchSize { get; set; }
}
