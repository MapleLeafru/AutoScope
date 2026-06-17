using System;
using System.Collections.Generic;
using System.Globalization;
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
        _parserSettings = LoadSettingsFile(Path.Combine(_paths.ConfigsPath, "ParserDefaultSettings.json"));
        _analyzerSettings = LoadSettingsFile(Path.Combine(_paths.ConfigsPath, "AnalyzerDefaultSettings.json"));
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

    // Добавляет в сохранённые сценарии новые дополнительные параметры из личного конфига парсера.
    public ParserRunSettings AddMissingExtraParserSettings(string parserPath, ParserRunSettings parserSettings)
    {
        if (parserSettings.ExtraSettings == null)
            parserSettings.ExtraSettings = new Dictionary<string, JsonElement>();

        Dictionary<string, JsonElement> mergedSettings = BuildParserSettings(parserPath);
        Dictionary<string, JsonElement> extraSettings = ExtractExtraParserSettings(mergedSettings);

        foreach (KeyValuePair<string, JsonElement> item in extraSettings)
        {
            if (!parserSettings.ExtraSettings.ContainsKey(item.Key))
                parserSettings.ExtraSettings[item.Key] = item.Value;
        }

        return parserSettings;
    }

    // Возвращает параметры парсера. START_URL пользователь всегда указывает явно.
    public ParserRunSettings ReadParserRunSettings(string parserPath)
    {
        Dictionary<string, JsonElement> mergedSettings = BuildParserSettings(parserPath);

        int defaultMaxCars = GetIntSetting(mergedSettings, "maxCars", 10);
        int defaultStreamBatchSize = GetIntSetting(mergedSettings, "streamBatchSize", 5);
        double defaultRequestDelaySeconds = GetDoubleSetting(mergedSettings, "requestDelaySeconds", 1.2);
        int defaultRetryCount = GetIntSetting(mergedSettings, "retryCount", 3);
        double defaultRateLimitDelaySeconds = GetDoubleSetting(mergedSettings, "rateLimitDelaySeconds", 5.0);

        ParserRunSettings parserSettings = new ParserRunSettings
        {
            StartUrl = _input.ReadRequiredString("Введите START_URL: "),
            MaxCars = defaultMaxCars,
            StreamBatchSize = defaultStreamBatchSize,
            RequestDelaySeconds = defaultRequestDelaySeconds,
            RetryCount = defaultRetryCount,
            RateLimitDelaySeconds = defaultRateLimitDelaySeconds,
            ExtraSettings = ExtractExtraParserSettings(mergedSettings)
        };

        bool useDefaultParserSettings = _input.AskYesNo("Использовать остальные параметры парсера по умолчанию? y/n: ");

        if (useDefaultParserSettings)
            return parserSettings;

        parserSettings.MaxCars = _input.ReadIntWithDefault(
            $"Введите MAX_CARS (0 = собрать все доступные объявления, пустое поле = значение из конфига: {defaultMaxCars}): ",
            defaultMaxCars
        );

        parserSettings.StreamBatchSize = _input.ReadIntWithDefault(
            $"Введите STREAM_BATCH_SIZE (Пустое поле = значение из конфига: {defaultStreamBatchSize}): ",
            defaultStreamBatchSize
        );

        parserSettings.RequestDelaySeconds = _input.ReadDoubleWithDefault(
            $"Введите задержку между HTTP-запросами в секундах (Пустое поле = значение из конфига: {FormatDouble(defaultRequestDelaySeconds)}): ",
            defaultRequestDelaySeconds
        );

        parserSettings.RetryCount = _input.ReadIntWithDefault(
            $"Введите количество повторов при ошибке запроса (Пустое поле = значение из конфига: {defaultRetryCount}): ",
            defaultRetryCount
        );

        parserSettings.RateLimitDelaySeconds = _input.ReadDoubleWithDefault(
            $"Введите базовую паузу при HTTP 429 в секундах (Пустое поле = значение из конфига: {FormatDouble(defaultRateLimitDelaySeconds)}): ",
            defaultRateLimitDelaySeconds
        );

        return parserSettings;
    }

    // Возвращает настройки InputApi: либо из общего конфига, либо из личного конфига парсера, либо после ручного ввода.
    public ApiSettings ReadApiSettings(string parserPath)
    {
        Dictionary<string, JsonElement> mergedSettings = BuildParserSettings(parserPath);

        bool defaultBrandCountryEnrichment = GetNestedBoolSetting(mergedSettings, "apiSettings", "brandCountryEnrichment", true);
        bool defaultTransmissionNormalization = GetNestedBoolSetting(mergedSettings, "apiSettings", "transmissionNormalization", false);
        bool defaultDriveTypeNormalization = GetNestedBoolSetting(mergedSettings, "apiSettings", "driveTypeNormalization", false);
        bool defaultFuelTypeNormalization = GetNestedBoolSetting(mergedSettings, "apiSettings", "fuelTypeNormalization", false);

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
    public OutputFilterSettings ReadOutputFilterSettings(string analyzerPath = "")
    {
        Dictionary<string, JsonElement> analyzerSettings = BuildAnalyzerSettings(analyzerPath);

        bool defaultLatestOnly = GetNestedBoolSetting(analyzerSettings, "outputSettings", "latestOnly", true);
        bool defaultOnlyChanged = GetNestedBoolSetting(analyzerSettings, "outputSettings", "onlyChanged", false);
        string defaultBrand = GetNestedStringSetting(analyzerSettings, "outputSettings", "brand", "");
        string defaultModel = GetNestedStringSetting(analyzerSettings, "outputSettings", "model", "");
        string defaultSaleRegion = GetNestedStringSetting(analyzerSettings, "outputSettings", "saleRegion", "");
        int? defaultYearFrom = GetNestedNullableIntSetting(analyzerSettings, "outputSettings", "yearFrom", null);
        int? defaultYearTo = GetNestedNullableIntSetting(analyzerSettings, "outputSettings", "yearTo", null);

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

    // Строит итоговый набор настроек: общий ParserDefaultSettings + settings из личного конфига выбранного парсера.
    private Dictionary<string, JsonElement> BuildParserSettings(string parserPath)
    {
        Dictionary<string, JsonElement> result = new Dictionary<string, JsonElement>(_parserSettings);

        string parserConfigPath = GetParserConfigPath(parserPath);
        if (string.IsNullOrWhiteSpace(parserConfigPath) || !File.Exists(parserConfigPath))
            return result;

        Dictionary<string, JsonElement> parserSpecificSettings = LoadModuleSettingsFile(parserConfigPath);
        foreach (KeyValuePair<string, JsonElement> item in parserSpecificSettings)
            result[item.Key] = item.Value;

        return result;
    }

    // Строит итоговый набор настроек: общий AnalyzerDefaultSettings + settings из личного конфига выбранного анализатора.
    private Dictionary<string, JsonElement> BuildAnalyzerSettings(string analyzerPath)
    {
        Dictionary<string, JsonElement> result = new Dictionary<string, JsonElement>(_analyzerSettings);

        string analyzerConfigPath = GetAnalyzerConfigPath(analyzerPath);
        if (string.IsNullOrWhiteSpace(analyzerConfigPath) || !File.Exists(analyzerConfigPath))
            return result;

        Dictionary<string, JsonElement> analyzerSpecificSettings = LoadModuleSettingsFile(analyzerConfigPath);
        foreach (KeyValuePair<string, JsonElement> item in analyzerSpecificSettings)
            result[item.Key] = item.Value;

        return result;
    }

    // Возвращает путь к личному конфигу выбранного парсера.
    private string GetParserConfigPath(string parserPath)
    {
        if (string.IsNullOrWhiteSpace(parserPath))
            return "";

        string parserName = Path.GetFileNameWithoutExtension(parserPath);
        if (string.IsNullOrWhiteSpace(parserName))
            return "";

        return Path.Combine(_paths.ParserConfigsPath, parserName + ".json");
    }

    // Возвращает путь к личному конфигу выбранного анализатора.
    private string GetAnalyzerConfigPath(string analyzerPath)
    {
        if (string.IsNullOrWhiteSpace(analyzerPath))
            return "";

        string analyzerName = Path.GetFileNameWithoutExtension(analyzerPath);
        if (string.IsNullOrWhiteSpace(analyzerName))
            return "";

        return Path.Combine(_paths.AnalyzerConfigsPath, analyzerName + ".json");
    }

    // Отбирает дополнительные parserSettings, которые не входят в базовую модель C#.
    private Dictionary<string, JsonElement> ExtractExtraParserSettings(Dictionary<string, JsonElement> settings)
    {
        HashSet<string> knownKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "startUrl",
            "maxCars",
            "streamBatchSize",
            "requestDelaySeconds",
            "retryCount",
            "rateLimitDelaySeconds",
            "pythonPath",
            "javaPath",
            "dictionariesPath",
            "apiSettings",
            "metadata",
            "settings"
        };

        Dictionary<string, JsonElement> extra = new Dictionary<string, JsonElement>();
        foreach (KeyValuePair<string, JsonElement> item in settings)
        {
            if (!knownKeys.Contains(item.Key))
                extra[item.Key] = item.Value;
        }

        return extra;
    }

    // Загружает JSON-настройки по полному пути.
    private Dictionary<string, JsonElement> LoadSettingsFile(string configFile)
    {
        if (!File.Exists(configFile))
            return new Dictionary<string, JsonElement>();

        string configJson = File.ReadAllText(configFile);
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(configJson)
            ?? new Dictionary<string, JsonElement>();
    }

    // Загружает рабочие settings из личного конфига модуля.
    // Новый формат: { "metadata": {...}, "settings": {...} }.
    // Старый плоский формат поддерживается, но metadata не попадает в parserSettings/analyzer settings.
    private Dictionary<string, JsonElement> LoadModuleSettingsFile(string configFile)
    {
        Dictionary<string, JsonElement> rawSettings = LoadSettingsFile(configFile);

        if (rawSettings.TryGetValue("settings", out JsonElement settingsSection) &&
            settingsSection.ValueKind == JsonValueKind.Object)
        {
            return JsonObjectToDictionary(settingsSection);
        }

        Dictionary<string, JsonElement> result = new Dictionary<string, JsonElement>();
        foreach (KeyValuePair<string, JsonElement> item in rawSettings)
        {
            if (!item.Key.Equals("metadata", StringComparison.OrdinalIgnoreCase))
                result[item.Key] = item.Value;
        }

        return result;
    }

    // Преобразует JSON-объект в словарь и клонирует значения, чтобы они безопасно жили после выхода из метода.
    private Dictionary<string, JsonElement> JsonObjectToDictionary(JsonElement section)
    {
        Dictionary<string, JsonElement> result = new Dictionary<string, JsonElement>();

        foreach (JsonProperty property in section.EnumerateObject())
            result[property.Name] = property.Value.Clone();

        return result;
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

    // Безопасно получает дробное число из JSON-настроек.
    private double GetDoubleSetting(Dictionary<string, JsonElement> settings, string key, double fallback = 0)
    {
        if (settings.TryGetValue(key, out JsonElement value))
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double result))
                return result;

            if (value.ValueKind == JsonValueKind.String)
            {
                string raw = (value.GetString() ?? "").Trim().Replace(",", ".");
                if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
                    return result;
            }
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

    // Форматирует дробное число для вывода в консоль.
    private string FormatDouble(double value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
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
