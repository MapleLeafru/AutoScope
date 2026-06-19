// Хранит настройки обработки данных в InputApi.
public class ApiSettings
{
    public bool BrandCountryEnrichment { get; set; } = true;
    public bool TransmissionNormalization { get; set; } = false;
    public bool DriveTypeNormalization { get; set; } = false;
    public bool FuelTypeNormalization { get; set; } = false;
}
