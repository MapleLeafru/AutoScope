using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

// Загружает настройки запуска из конфигов и помогает вводить параметры через консоль.
public class SettingsService
{
    private readonly AppPaths _paths;
    private readonly ConsoleInputService _input;
    private readonly Dictionary<string, JsonElement> _parserSettings;
    private readonly Dictionary<string, JsonElement> _analyzerSettings;

    public SettingsService(AppPaths paths, ConsoleInputService input)
    {
        _paths = paths;
        _input = input;
        _parserSettings = LoadDefaultSettings("ParserDefaultSettings.json");
        _analyzerSettings = LoadDefaultSettings("AnalyzerDefaultSettings.json");
    }

    // Возвращает настройки сред выполнения Python и Java.
    public RuntimeSettings GetRuntimeSettings()
    {
        string configuredPythonPath = GetStringSetting(_parserSettings, "pythonPath", "");
        string pythonPath = string.IsNullOrWhiteSpace(configuredPythonPath)
            ? _paths.PythonPath
            : configuredPythonPath;

        return new RuntimeSettings
        {
            PythonPath = pythonPath,
            JavaPath = GetStringSetting(_parserSettings, "javaPath", "java")
        };
    }

    // Возвращает параметры парсера: либо из конфига, либо после ручного ввода.
    public ParserRunSettings ReadParserRunSettings()
    {
        string defaultStartUrl = GetStringSetting(_parserSettings, "startUrl", "");
        int defaultMaxCars = GetIntSetting(_parserSettings, "maxCars", 10);
        int defaultStreamBatchSize = GetIntSetting(_parserSettings, "streamBatchSize", 5);

        ParserRunSettings parserSettings = new ParserRunSettings
        {
            StartUrl = defaultStartUrl,
            MaxCars = defaultMaxCars,
            StreamBatchSize = defaultStreamBatchSize
        };

        bool useDefaultParserSettings = _input.AskYesNo("Запустить парсер с параметрами по умолчанию? y/n: ");

        if (useDefaultParserSettings)
            return parserSettings;

        parserSettings.StartUrl = _input.ReadStringWithDefault(
            $"Введите START_URL (Пустое поле = значение из конфига: {defaultStartUrl}): ",
            defaultStartUrl
        );

        parserSettings.MaxCars = _input.ReadIntWithDefault(
            $"Введите MAX_CARS (Пустое поле = значение из конфига: {defaultMaxCars}): ",
            defaultMaxCars
        );

        parserSettings.StreamBatchSize = _input.ReadIntWithDefault(
            $"Введите STREAM_BATCH_SIZE (Пустое поле = значение из конфига: {defaultStreamBatchSize}): ",
            defaultStreamBatchSize
        );

        return parserSettings;
    }

    // Возвращает настройки InputApi: либо из конфига, либо после ручного ввода.
    public ApiSettings ReadApiSettings()
    {
        bool defaultBrandCountryEnrichment = GetNestedBoolSetting(_parserSettings, "apiSettings", "brandCountryEnrichment", true);
        bool defaultTransmissionNormalization = GetNestedBoolSetting(_parserSettings, "apiSettings", "transmissionNormalization", true);
        bool defaultDriveTypeNormalization = GetNestedBoolSetting(_parserSettings, "apiSettings", "driveTypeNormalization", true);
        bool defaultFuelTypeNormalization = GetNestedBoolSetting(_parserSettings, "apiSettings", "fuelTypeNormalization", true);

        ApiSettings apiSettings = new ApiSettings
        {
            BrandCountryEnrichment = defaultBrandCountryEnrichment,
            TransmissionNormalization = defaultTransmissionNormalization,
            DriveTypeNormalization = defaultDriveTypeNormalization,
            FuelTypeNormalization = defaultFuelTypeNormalization
        };

        bool useDefaultApiSettings = _input.AskYesNo("Использовать настройки API по умолчанию? y/n: ");
        if (useDefaultApiSettings)
            return apiSettings;

        apiSettings.BrandCountryEnrichment = _input.AskYesNoWithDefault(
            $"Включить обогащение страны бренда? (Пустое поле = значение из конфига: {FormatBool(defaultBrandCountryEnrichment)}) y/n: ",
            defaultBrandCountryEnrichment
        );

        apiSettings.TransmissionNormalization = _input.AskYesNoWithDefault(
            $"Включить нормализацию коробки передач? (Пустое поле = значение из конфига: {FormatBool(defaultTransmissionNormalization)}) y/n: ",
            defaultTransmissionNormalization
        );

        apiSettings.DriveTypeNormalization = _input.AskYesNoWithDefault(
            $"Включить нормализацию типа привода? (Пустое поле = значение из конфига: {FormatBool(defaultDriveTypeNormalization)}) y/n: ",
            defaultDriveTypeNormalization
        );

        apiSettings.FuelTypeNormalization = _input.AskYesNoWithDefault(
            $"Включить нормализацию типа топлива? (Пустое поле = значение из конфига: {FormatBool(defaultFuelTypeNormalization)}) y/n: ",
            defaultFuelTypeNormalization
        );

        return apiSettings;
    }

    // Возвращает настройки выборки OutputPipeline: либо из конфига анализатора, либо после ручного ввода.
    public OutputFilterSettings ReadOutputFilterSettings()
    {
        bool defaultLatestOnly = GetNestedBoolSetting(_analyzerSettings, "outputSettings", "latestOnly", true);
        bool defaultOnlyChanged = GetNestedBoolSetting(_analyzerSettings, "outputSettings", "onlyChanged", false);
        string defaultBrand = GetNestedStringSetting(_analyzerSettings, "outputSettings", "brand", "");
        string defaultModel = GetNestedStringSetting(_analyzerSettings, "outputSettings", "model", "");
        string defaultSaleRegion = GetNestedStringSetting(_analyzerSettings, "outputSettings", "saleRegion", "");
        int? defaultYearFrom = GetNestedNullableIntSetting(_analyzerSettings, "outputSettings", "yearFrom", null);
        int? defaultYearTo = GetNestedNullableIntSetting(_analyzerSettings, "outputSettings", "yearTo", null);

        OutputFilterSettings outputSettings = new OutputFilterSettings
        {
            LatestOnly = defaultLatestOnly,
            OnlyChanged = defaultOnlyChanged,
            Brand = defaultBrand,
            Model = defaultModel,
            SaleRegion = defaultSaleRegion,
            YearFrom = defaultYearFrom,
            YearTo = defaultYearTo
        };

        bool useDefaultOutputSettings = _input.AskYesNo("Использовать настройки выборки анализа по умолчанию? y/n: ");
        if (useDefaultOutputSettings)
            return outputSettings;

        outputSettings.LatestOnly = _input.AskYesNoWithDefault(
            $"Использовать только последние снимки объявлений? (Пустое поле = значение из конфига: {FormatBool(defaultLatestOnly)}) y/n: ",
            defaultLatestOnly
        );

        outputSettings.OnlyChanged = _input.AskYesNoWithDefault(
            $"Выбирать только записи с изменениями? (Пустое поле = значение из конфига: {FormatBool(defaultOnlyChanged)}) y/n: ",
            defaultOnlyChanged
        );

        outputSettings.Brand = _input.ReadStringWithDefault(
            $"Фильтр по бренду (Пустое поле = значение из конфига: {FormatStringDefault(defaultBrand)}): ",
            defaultBrand
        );

        outputSettings.Model = _input.ReadStringWithDefault(
            $"Фильтр по модели (Пустое поле = значение из конфига: {FormatStringDefault(defaultModel)}): ",
            defaultModel
        );

        outputSettings.SaleRegion = _input.ReadStringWithDefault(
            $"Фильтр по региону продажи (Пустое поле = значение из конфига: {FormatStringDefault(defaultSaleRegion)}): ",
            defaultSaleRegion
        );

        outputSettings.YearFrom = _input.ReadNullableIntWithDefault(
            $"Год выпуска от (Пустое поле = значение из конфига: {FormatNullableInt(defaultYearFrom)}): ",
            defaultYearFrom
        );

        outputSettings.YearTo = _input.ReadNullableIntWithDefault(
            $"Год выпуска до (Пустое поле = значение из конфига: {FormatNullableInt(defaultYearTo)}): ",
            defaultYearTo
        );

        return outputSettings;
    }

    // Загружает JSON-настройки из указанного файла в папке Configs.
    private Dictionary<string, JsonElement> LoadDefaultSettings(string fileName)
    {
        string configFile = Path.Combine(_paths.ConfigsPath, fileName);

        if (!File.Exists(configFile))
            return new Dictionary<string, JsonElement>();

        string configJson = File.ReadAllText(configFile);
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(configJson)
            ?? new Dictionary<string, JsonElement>();
    }

    // Безопасно получает строковое значение из JSON-настроек.
    private string GetStringSetting(Dictionary<string, JsonElement> settings, string key, string fallback = "")
    {
        if (settings.TryGetValue(key, out JsonElement value))
        {
            if (value.ValueKind == JsonValueKind.String)
                return value.GetString() ?? fallback;

            return value.ToString();
        }

        return fallback;
    }

    // Безопасно получает целочисленное значение из JSON-настроек.
    private int GetIntSetting(Dictionary<string, JsonElement> settings, string key, int fallback = 0)
    {
        if (settings.TryGetValue(key, out JsonElement value))
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result))
                return result;

            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out result))
                return result;
        }

        return fallback;
    }

    // Безопасно получает bool-значение из вложенного JSON-объекта.
    private bool GetNestedBoolSetting(Dictionary<string, JsonElement> settings, string objectKey, string valueKey, bool fallback)
    {
        if (!TryGetNestedValue(settings, objectKey, valueKey, out JsonElement value))
            return fallback;

        if (value.ValueKind == JsonValueKind.True)
            return true;

        if (value.ValueKind == JsonValueKind.False)
            return false;

        if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out bool result))
            return result;

        return fallback;
    }

    // Безопасно получает строковое значение из вложенного JSON-объекта.
    private string GetNestedStringSetting(Dictionary<string, JsonElement> settings, string objectKey, string valueKey, string fallback)
    {
        if (!TryGetNestedValue(settings, objectKey, valueKey, out JsonElement value))
            return fallback;

        if (value.ValueKind == JsonValueKind.String)
            return value.GetString() ?? fallback;

        if (value.ValueKind == JsonValueKind.Null)
            return fallback;

        return value.ToString();
    }

    // Безопасно получает nullable int-значение из вложенного JSON-объекта.
    private int? GetNestedNullableIntSetting(Dictionary<string, JsonElement> settings, string objectKey, string valueKey, int? fallback)
    {
        if (!TryGetNestedValue(settings, objectKey, valueKey, out JsonElement value))
            return fallback;

        if (value.ValueKind == JsonValueKind.Null)
            return null;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result))
            return result;

        if (value.ValueKind == JsonValueKind.String)
        {
            string raw = value.GetString() ?? "";
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            if (int.TryParse(raw, out result))
                return result;
        }

        return fallback;
    }

    // Пытается получить значение из вложенного JSON-объекта.
    private bool TryGetNestedValue(Dictionary<string, JsonElement> settings, string objectKey, string valueKey, out JsonElement value)
    {
        value = default;

        if (!settings.TryGetValue(objectKey, out JsonElement section))
            return false;

        if (section.ValueKind != JsonValueKind.Object)
            return false;

        return section.TryGetProperty(valueKey, out value);
    }

    // Преобразует bool в понятный текст для консоли.
    private string FormatBool(bool value)
    {
        return value ? "включено" : "выключено";
    }

    // Показывает текстовое значение по умолчанию для строкового фильтра.
    private string FormatStringDefault(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "не задано" : value;
    }

    // Показывает текстовое значение по умолчанию для необязательного числа.
    private string FormatNullableInt(int? value)
    {
        return value.HasValue ? value.Value.ToString() : "не задано";
    }
}
