using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

// Загружает настройки из ParserDefaultSettings.json и помогает вводить параметры запуска.
public class SettingsService
{
    private readonly AppPaths _paths;
    private readonly ConsoleInputService _input;
    private readonly Dictionary<string, JsonElement> _settings;

    public SettingsService(AppPaths paths, ConsoleInputService input)
    {
        _paths = paths;
        _input = input;
        _settings = LoadDefaultSettings();
    }

    // Возвращает настройки сред выполнения Python и Java.
    public RuntimeSettings GetRuntimeSettings()
    {
        string configuredPythonPath = GetStringSetting("pythonPath", "");
        string pythonPath = string.IsNullOrWhiteSpace(configuredPythonPath)
            ? _paths.PythonPath
            : configuredPythonPath;

        return new RuntimeSettings
        {
            PythonPath = pythonPath,
            JavaPath = GetStringSetting("javaPath", "java")
        };
    }

    // Возвращает параметры парсера: либо из конфига, либо после ручного ввода.
    public ParserRunSettings ReadParserRunSettings()
    {
        string defaultStartUrl = GetStringSetting("startUrl", "");
        int defaultMaxCars = GetIntSetting("maxCars", 10);
        int defaultStreamBatchSize = GetIntSetting("streamBatchSize", 5);

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
        bool defaultBrandCountryEnrichment = GetNestedBoolSetting(
            "apiSettings",
            "brandCountryEnrichment",
            true
        );

        bool defaultTransmissionNormalization = GetNestedBoolSetting(
            "apiSettings",
            "transmissionNormalization",
            true
        );

        bool defaultDriveTypeNormalization = GetNestedBoolSetting(
            "apiSettings",
            "driveTypeNormalization",
            true
        );

        bool defaultFuelTypeNormalization = GetNestedBoolSetting(
            "apiSettings",
            "fuelTypeNormalization",
            true
        );

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

    // Загружает JSON-настройки парсинга. Если файла нет, возвращает пустой словарь.
    private Dictionary<string, JsonElement> LoadDefaultSettings()
    {
        string configFile = Path.Combine(_paths.ConfigsPath, "ParserDefaultSettings.json");

        if (!File.Exists(configFile))
            return new Dictionary<string, JsonElement>();

        string configJson = File.ReadAllText(configFile);
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(configJson)
            ?? new Dictionary<string, JsonElement>();
    }

    // Безопасно получает строковое значение из JSON-настроек.
    private string GetStringSetting(string key, string fallback = "")
    {
        if (_settings.TryGetValue(key, out JsonElement value))
        {
            if (value.ValueKind == JsonValueKind.String)
                return value.GetString() ?? fallback;

            return value.ToString();
        }

        return fallback;
    }

    // Безопасно получает целочисленное значение из JSON-настроек.
    private int GetIntSetting(string key, int fallback = 0)
    {
        if (_settings.TryGetValue(key, out JsonElement value))
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result))
                return result;

            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out result))
                return result;
        }

        return fallback;
    }

    // Безопасно получает bool-значение из вложенного JSON-объекта.
    private bool GetNestedBoolSetting(string objectKey, string valueKey, bool fallback)
    {
        if (!_settings.TryGetValue(objectKey, out JsonElement section))
            return fallback;

        if (section.ValueKind != JsonValueKind.Object)
            return fallback;

        if (!section.TryGetProperty(valueKey, out JsonElement value))
            return fallback;

        if (value.ValueKind == JsonValueKind.True)
            return true;

        if (value.ValueKind == JsonValueKind.False)
            return false;

        if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out bool result))
            return result;

        return fallback;
    }

    // Преобразует bool в понятный текст для консоли.
    private string FormatBool(bool value)
    {
        return value ? "включено" : "выключено";
    }

}
