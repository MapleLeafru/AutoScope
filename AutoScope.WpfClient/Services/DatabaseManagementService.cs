using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using AutoScope.WpfClient.Models;

namespace AutoScope.WpfClient.Services;

public class DatabaseManagementService
{
    private readonly string _rootPath;
    private readonly DashboardDataService _dashboardDataService;

    public DatabaseManagementService(string rootPath)
    {
        _rootPath = rootPath;
        _dashboardDataService = new DashboardDataService(rootPath);
    }

    public List<DatabaseDashboardItem> LoadDatabases()
    {
        return _dashboardDataService.LoadDatabases();
    }

    public string GetDatabasesFolderPath()
    {
        return _dashboardDataService.GetDatabasesFolderPath();
    }

    public bool OpenDatabaseInDbBrowser(string databasePath, out string message)
    {
        return _dashboardDataService.OpenDatabaseInDbBrowser(databasePath, out message);
    }

    public void OpenFolder(string path)
    {
        _dashboardDataService.OpenFolder(path);
    }

    public void OpenFileFolder(string filePath)
    {
        string? folder = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(folder))
            folder = GetDatabasesFolderPath();

        _dashboardDataService.OpenFolder(folder);
    }

    public DatabaseOperationResult CreateDatabase(string rawName)
    {
        string normalizedName = NormalizeDatabaseFileName(rawName);
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return new DatabaseOperationResult
            {
                Success = false,
                Message = "Введите название базы данных."
            };
        }

        string expectedPath = Path.Combine(GetDatabasesFolderPath(), normalizedName);
        DatabaseOperationResult result = RunDatabaseManager("dbCreate", normalizedName);

        if (File.Exists(expectedPath))
        {
            result.Success = true;
            result.DatabasePath = expectedPath;
            result.Message = $"База создана: {Path.GetFileName(expectedPath)}";
            return result;
        }

        if (result.Success)
        {
            result.Success = false;
            result.Message = "DatabaseManager завершился, но созданный файл базы не найден.";
        }

        return result;
    }

    public DatabaseOperationResult DeleteDatabase(DatabaseDashboardItem database)
    {
        if (string.IsNullOrWhiteSpace(database.Path) || !File.Exists(database.Path))
        {
            return new DatabaseOperationResult
            {
                Success = false,
                Message = "Файл базы данных не найден."
            };
        }

        DatabaseOperationResult result = RunDatabaseManager("dbDelete", database.FileName);

        if (!File.Exists(database.Path))
        {
            TryDeleteSidecarFile(database.Path + "-wal");
            TryDeleteSidecarFile(database.Path + "-shm");

            result.Success = true;
            result.Message = $"База удалена: {database.FileName}";
            return result;
        }

        if (result.Success)
        {
            result.Success = false;
            result.Message = "DatabaseManager завершился, но файл базы всё ещё существует. Возможно, он открыт в DB Browser или другом приложении.";
        }

        return result;
    }

    public string BuildExpectedDatabaseFileName(string rawName)
    {
        return NormalizeDatabaseFileName(rawName);
    }

    public bool DatabaseExistsByInputName(string rawName)
    {
        string fileName = NormalizeDatabaseFileName(rawName);
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        return File.Exists(Path.Combine(GetDatabasesFolderPath(), fileName));
    }

    private DatabaseOperationResult RunDatabaseManager(string command, string dbFileName)
    {
        try
        {
            string managerPath = ResolveDatabaseManagerPath();
            if (!File.Exists(managerPath))
            {
                return new DatabaseOperationResult
                {
                    Success = false,
                    Message = $"DatabaseManager.py не найден: {managerPath}"
                };
            }

            string pythonPath = ResolvePythonPath();
            Directory.CreateDirectory(GetDatabasesFolderPath());

            object payload = new
            {
                command,
                dbFileName,
                configPath = Path.Combine(_rootPath, "Configs"),
                dbPath = GetDatabasesFolderPath()
            };

            string inputJson = JsonSerializer.Serialize(payload);

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = $"\"{managerPath}\"",
                WorkingDirectory = _rootPath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            startInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            startInfo.EnvironmentVariables["PYTHONUTF8"] = "1";

            using Process? process = Process.Start(startInfo);
            if (process == null)
            {
                return new DatabaseOperationResult
                {
                    Success = false,
                    Message = "Python-процесс DatabaseManager не удалось создать."
                };
            }

            process.StandardInput.Write(inputJson);
            process.StandardInput.Close();

            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();

            bool exited = process.WaitForExit(30000);
            if (!exited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch { }

                return new DatabaseOperationResult
                {
                    Success = false,
                    Message = "DatabaseManager не ответил за 30 секунд.",
                    Output = output,
                    Error = error
                };
            }

            bool success = process.ExitCode == 0 && string.IsNullOrWhiteSpace(error);
            string message = success
                ? FirstNonEmptyLine(output) ?? "Операция с базой данных выполнена."
                : BuildErrorMessage(output, error, process.ExitCode);

            return new DatabaseOperationResult
            {
                Success = success,
                Message = message,
                Output = output,
                Error = error
            };
        }
        catch (Exception ex)
        {
            return new DatabaseOperationResult
            {
                Success = false,
                Message = $"Операция с базой данных не выполнена: {ex.Message}"
            };
        }
    }

    private string NormalizeDatabaseFileName(string rawName)
    {
        string name = (rawName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        name = name.Replace('\\', '_').Replace('/', '_');
        foreach (char invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');

        string extension = Path.GetExtension(name);
        if (string.IsNullOrWhiteSpace(extension))
            name += ".db";

        string nameWithoutExtension = Path.GetFileNameWithoutExtension(name);
        string currentExtension = Path.GetExtension(name);

        if (!nameWithoutExtension.Contains('.', StringComparison.Ordinal))
            name = $"{nameWithoutExtension}.BaseDataBaseConfig{currentExtension}";

        return name;
    }

    private string ResolveDatabaseManagerPath()
    {
        string utilsPath = Path.Combine(_rootPath, "Utils", "DatabaseManager.py");
        if (File.Exists(utilsPath))
            return utilsPath;

        return Path.Combine(_rootPath, "DatabaseManager.py");
    }

    private string ResolvePythonPath()
    {
        string bundled = Path.Combine(_rootPath, "Python", "python.exe");
        if (File.Exists(bundled))
            return bundled;

        string? configured = ReadStringFromJsonFile(Path.Combine(_rootPath, "Configs", "ParserDefaultSettings.json"), "pythonPath");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        return "python";
    }

    private string? ReadStringFromJsonFile(string path, string propertyName)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        }
        catch
        {
            return null;
        }

        return null;
    }

    private string? FirstNonEmptyLine(string text)
    {
        foreach (string line in (text ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = line.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
                return trimmed;
        }

        return null;
    }

    private string BuildErrorMessage(string output, string error, int exitCode)
    {
        string details = !string.IsNullOrWhiteSpace(error) ? error.Trim() : output.Trim();
        if (string.IsNullOrWhiteSpace(details))
            details = "подробности не переданы";

        return $"DatabaseManager завершился с ошибкой, код {exitCode}: {details}";
    }

    private void TryDeleteSidecarFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Служебный файл может быть занят. Основная операция уже выполнена.
        }
    }
}

public class DatabaseOperationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string Output { get; set; } = "";
    public string Error { get; set; } = "";
    public string DatabasePath { get; set; } = "";
}
