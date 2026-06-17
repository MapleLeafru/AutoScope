using System.Collections.Generic;
using System.IO;
using System.Text.Json;

// Описывает внешний модуль AutoScope: парсер или анализатор.
// Минимальный модуль может существовать без конфига, metadata и settingsSchema.
public class ModuleInfo
{
    public string ModulePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string FileNameWithoutExtension { get; set; } = "";

    public string ModuleType { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public string Source { get; set; } = "";
    public string Mode { get; set; } = "";
    public string Category { get; set; } = "";
    public bool Recommended { get; set; }
    public bool HasConfig { get; set; }
    public bool HasMetadata { get; set; }

    // Описание пользовательских настроек модуля для будущего UI.
    // Это необязательный слой: отсутствие схемы не мешает запуску модуля.
    public Dictionary<string, ModuleSettingSchema> SettingsSchema { get; set; } = new Dictionary<string, ModuleSettingSchema>();

    public bool HasSettingsSchema => SettingsSchema.Count > 0;

    // Возвращает название, которое удобно показывать пользователю.
    public string GetDisplayName()
    {
        if (!string.IsNullOrWhiteSpace(DisplayName))
            return DisplayName;

        if (!string.IsNullOrWhiteSpace(FileNameWithoutExtension))
            return FileNameWithoutExtension;

        if (!string.IsNullOrWhiteSpace(FileName))
            return Path.GetFileNameWithoutExtension(FileName);

        return "Неизвестный модуль";
    }
}

// Описывает одно поле настройки модуля.
// Используется интерфейсом для построения формы, но не обязателен для выполнения модуля.
public class ModuleSettingSchema
{
    public string Key { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public string Type { get; set; } = "string";
    public string Group { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Placeholder { get; set; } = "";
    public bool Required { get; set; }
    public bool Advanced { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public string[] Options { get; set; } = new string[0];
    public JsonElement? DefaultValue { get; set; }

    // Возвращает название поля для интерфейса. Если название не задано, используется ключ.
    public string GetDisplayName()
    {
        if (!string.IsNullOrWhiteSpace(DisplayName))
            return DisplayName;

        return string.IsNullOrWhiteSpace(Key) ? "Настройка" : Key;
    }
}
