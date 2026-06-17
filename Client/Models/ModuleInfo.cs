﻿using System.IO;

// Описывает внешний модуль AutoScope: парсер или анализатор.
// Метаданные необязательны: если личного конфига нет, модуль всё равно работает по имени файла.
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
