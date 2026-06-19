using System;
using System.IO;
using System.Linq;

namespace AutoScope.WpfClient.Services;

public static class AutoScopeRootLocator
{
    // Ищет корень AutoScope, поднимаясь от папки запуска вверх.
    // Это удобно для запуска из bin/Debug/net8.0-windows.
    public static string FindRoot()
    {
        string[] startPoints = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory()
        };

        foreach (string startPoint in startPoints)
        {
            string? found = FindFrom(startPoint);
            if (!string.IsNullOrWhiteSpace(found))
                return found;
        }

        return Directory.GetCurrentDirectory();
    }

    private static string? FindFrom(string startPath)
    {
        DirectoryInfo? directory = new DirectoryInfo(startPath);

        while (directory != null)
        {
            if (LooksLikeAutoScopeRoot(directory.FullName))
                return directory.FullName;

            directory = directory.Parent;
        }

        return null;
    }

    private static bool LooksLikeAutoScopeRoot(string path)
    {
        string[] knownFolders = new[] { "Configs", "Parsers", "Analyzers", "Client", "Jobs", "Logs", "Reports" };
        int matches = knownFolders.Count(folder => Directory.Exists(Path.Combine(path, folder)));
        return matches >= 2;
    }
}
