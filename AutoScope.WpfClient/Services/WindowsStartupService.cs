using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace AutoScope.WpfClient.Services;

public static class WindowsStartupService
{
    private const string StartupEntryName = "AutoScope";
    private const string RunRegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const int ShowNormal = 1;

    public static bool ApplyStartupSettings(string rootPath, UiSettings settings, out string message)
    {
        try
        {
            settings.StartupMethod = AppSettingsService.NormalizeStartupMethod(settings.StartupMethod);

            if (!settings.StartWithWindows)
            {
                RemoveShortcutStartup();
                RemoveRegistryStartup();
                message = "Автозапуск Windows выключен.";
                return true;
            }

            if (settings.StartupMethod == AppSettingsService.StartupMethodRegistry)
            {
                RemoveShortcutStartup();
                SetRegistryStartup(settings);
                message = "Автозапуск включён через реестр текущего пользователя.";
                return true;
            }

            RemoveRegistryStartup();
            CreateShortcutStartup(settings);
            message = "Автозапуск включён через ярлык в папке автозагрузки.";
            return true;
        }
        catch (Exception ex)
        {
            message = "Не удалось применить автозапуск Windows: " + ex.Message;
            return false;
        }
    }

    public static string GetStartupStatusText(UiSettings settings)
    {
        bool shortcutExists = File.Exists(GetShortcutPath());
        string? registryValue = ReadRegistryStartupValue();
        bool registryExists = !string.IsNullOrWhiteSpace(registryValue);

        if (!settings.StartWithWindows && !shortcutExists && !registryExists)
            return "Автозапуск Windows выключен.";

        if (settings.StartWithWindows)
        {
            string methodText = settings.StartupMethod == AppSettingsService.StartupMethodRegistry
                ? "реестр Windows"
                : "ярлык в папке автозагрузки";

            string actualText = shortcutExists || registryExists
                ? "системная запись найдена"
                : "системная запись пока не создана";

            return $"В настройках включён автозапуск через {methodText}; {actualText}.";
        }

        if (shortcutExists && registryExists)
            return "Автозапуск выключен в настройках, но найдены ярлык и запись в реестре. Сохранение настроек удалит их.";

        if (shortcutExists)
            return "Автозапуск выключен в настройках, но найден старый ярлык. Сохранение настроек удалит его.";

        return "Автозапуск выключен в настройках, но найдена старая запись в реестре. Сохранение настроек удалит её.";
    }

    public static string GetShortcutPath()
    {
        string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        return Path.Combine(startupFolder, StartupEntryName + ".lnk");
    }

    public static string BuildStartupCommand(UiSettings settings)
    {
        string exePath = GetCurrentExecutablePath();
        string arguments = BuildStartupArguments(settings);

        if (string.IsNullOrWhiteSpace(arguments))
            return Quote(exePath);

        return Quote(exePath) + " " + arguments;
    }

    private static void CreateShortcutStartup(UiSettings settings)
    {
        string exePath = GetCurrentExecutablePath();
        string shortcutPath = GetShortcutPath();
        string? workingDirectory = Path.GetDirectoryName(exePath);

        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);

        IShellLinkW link = (IShellLinkW)new ShellLink();
        link.SetPath(exePath);
        link.SetArguments(BuildStartupArguments(settings));
        link.SetDescription("Запуск AutoScope вместе с Windows");
        link.SetWorkingDirectory(string.IsNullOrWhiteSpace(workingDirectory) ? AppContext.BaseDirectory : workingDirectory);
        link.SetIconLocation(exePath, 0);
        link.SetShowCmd(ShowNormal);

        IPersistFile file = (IPersistFile)link;
        file.Save(shortcutPath, true);
    }

    private static void RemoveShortcutStartup()
    {
        string shortcutPath = GetShortcutPath();
        if (File.Exists(shortcutPath))
            File.Delete(shortcutPath);
    }

    private static void SetRegistryStartup(UiSettings settings)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunRegistryKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunRegistryKeyPath, writable: true);

        key?.SetValue(StartupEntryName, BuildStartupCommand(settings), RegistryValueKind.String);
    }

    private static void RemoveRegistryStartup()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunRegistryKeyPath, writable: true);
        key?.DeleteValue(StartupEntryName, throwOnMissingValue: false);
    }

    private static string? ReadRegistryStartupValue()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunRegistryKeyPath, writable: false);
            return key?.GetValue(StartupEntryName)?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static string BuildStartupArguments(UiSettings settings)
    {
        return settings.StartMinimizedToTray ? "--tray" : "";
    }

    private static string GetCurrentExecutablePath()
    {
        string? path = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            return path;

        path = Process.GetCurrentProcess().MainModule?.FileName;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            return path;

        throw new InvalidOperationException("Не удалось определить путь к AutoScope.WpfClient.exe.");
    }

    private static string Quote(string value)
    {
        return "\"" + value.Trim().Trim('"') + "\"";
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        void IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }
}
