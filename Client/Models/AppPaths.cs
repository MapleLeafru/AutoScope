using System;
using System.IO;

// Хранит основные пути проекта, которые нужны консольному клиенту.
public class AppPaths
{
    public string RootPath { get; private set; }
    public string DbPath { get; private set; }
    public string ConfigsPath { get; private set; }
    public string ParsersPath { get; private set; }
    public string AnalyzersPath { get; private set; }
    public string PythonPath { get; private set; }
    public string JobsPath { get; private set; }
    public string JobRunsPath { get; private set; }

    private AppPaths(string rootPath)
    {
        RootPath = rootPath;
        DbPath = Path.Combine(rootPath, "Databases");
        ConfigsPath = Path.Combine(rootPath, "Configs");
        ParsersPath = Path.Combine(rootPath, "Parsers");
        AnalyzersPath = Path.Combine(rootPath, "Analyzers");
        PythonPath = Path.Combine(rootPath, @"Python\python.exe");
        JobsPath = Path.Combine(rootPath, "Jobs");
        JobRunsPath = Path.Combine(rootPath, "Logs", "JobRuns");
    }

    // Определяет корень проекта относительно папки запуска Client/bin/Debug/...
    public static AppPaths FromCurrentDirectory()
    {
        string rootPath = Path.GetFullPath(
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\")
        );

        return new AppPaths(rootPath);
    }

    // Возвращает путь до Python-менеджера InputPipeline.
    public string GetInputPipelineManagerPath()
    {
        return Path.Combine(RootPath, @"PipelineManagers\InputPipelineManager.py");
    }

    // Возвращает путь до Python-менеджера OutputPipeline.
    public string GetOutputPipelineManagerPath()
    {
        return Path.Combine(RootPath, @"PipelineManagers\OutputPipelineManager.py");
    }

    // Возвращает путь до Python-утилиты управления базами данных.
    public string GetDatabaseManagerPath()
    {
        return Path.Combine(RootPath, @"Utils\DatabaseManager.py");
    }
}
