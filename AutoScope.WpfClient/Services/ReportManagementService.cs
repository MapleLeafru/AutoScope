using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using AutoScope.WpfClient.Models;

namespace AutoScope.WpfClient.Services;

public sealed class ReportManagementService
{
    private readonly string _rootPath;
    private readonly string _reportsPath;

    public ReportManagementService(string rootPath)
    {
        _rootPath = rootPath;
        _reportsPath = Path.Combine(_rootPath, "Reports");
    }

    public string GetReportsFolderPath()
    {
        Directory.CreateDirectory(_reportsPath);
        return _reportsPath;
    }

    public List<ReportDashboardItem> LoadReports()
    {
        if (!Directory.Exists(_reportsPath))
            return new List<ReportDashboardItem>();

        return Directory.EnumerateFiles(_reportsPath, "*.html", SearchOption.AllDirectories)
            .Select(CreateReportItem)
            .OrderByDescending(item => item.ModifiedAt)
            .ToList();
    }

    public void OpenReport(ReportDashboardItem report)
    {
        if (string.IsNullOrWhiteSpace(report.Path) || !File.Exists(report.Path))
            throw new FileNotFoundException("Файл отчёта не найден.", report.Path);

        Process.Start(new ProcessStartInfo(report.Path)
        {
            UseShellExecute = true
        });
    }

    public void OpenReportsFolder()
    {
        OpenFolder(GetReportsFolderPath());
    }

    public void OpenReportFolder(ReportDashboardItem report)
    {
        string? folder = Path.GetDirectoryName(report.Path);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            folder = GetReportsFolderPath();

        OpenFolder(folder);
    }

    public ReportOperationResult DeleteReport(ReportDashboardItem report)
    {
        if (string.IsNullOrWhiteSpace(report.Path) || !File.Exists(report.Path))
            return new ReportOperationResult(false, "Файл отчёта уже отсутствует.");

        try
        {
            File.Delete(report.Path);
            return new ReportOperationResult(true, $"Отчёт удалён: {report.FileName}");
        }
        catch (Exception ex)
        {
            return new ReportOperationResult(false, $"Не удалось удалить отчёт: {ex.Message}");
        }
    }

    private static ReportDashboardItem CreateReportItem(string filePath)
    {
        FileInfo fileInfo = new(filePath);
        string fileName = fileInfo.Name;
        string rawName = Path.GetFileNameWithoutExtension(fileName);
        string analyzerName = InferAnalyzerName(rawName);
        DateTime modifiedAt = fileInfo.LastWriteTime;

        return new ReportDashboardItem
        {
            Name = BuildDisplayName(rawName, analyzerName),
            FileName = fileName,
            Path = filePath,
            AnalyzerName = analyzerName,
            AnalyzerText = string.IsNullOrWhiteSpace(analyzerName) ? "Анализатор: не определён" : $"Анализатор: {analyzerName}",
            DateText = $"Создан/изменён: {modifiedAt:dd.MM.yyyy HH:mm}",
            SizeText = $"Размер: {FormatFileSize(fileInfo.Length)}",
            Details = fileInfo.DirectoryName ?? "",
            ModifiedAt = modifiedAt
        };
    }

    private static string InferAnalyzerName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
            return "";

        int dateSeparatorIndex = rawName.IndexOf("_20", StringComparison.OrdinalIgnoreCase);
        if (dateSeparatorIndex > 0)
            return rawName[..dateSeparatorIndex];

        int firstSeparator = rawName.IndexOf('_');
        if (firstSeparator > 0)
            return rawName[..firstSeparator];

        return rawName;
    }

    private static string BuildDisplayName(string rawName, string analyzerName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
            return "Отчёт AutoScope";

        if (!string.IsNullOrWhiteSpace(analyzerName) && rawName.Length > analyzerName.Length)
        {
            string tail = rawName[analyzerName.Length..].TrimStart('_', '-', ' ');
            if (!string.IsNullOrWhiteSpace(tail))
                return $"{analyzerName} · {tail}";
        }

        return rawName;
    }

    private static string FormatFileSize(long bytes)
    {
        string[] units = { "Б", "КБ", "МБ", "ГБ" };
        double value = bytes;
        int unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        string format = value >= 10 || unitIndex == 0 ? "0" : "0.0";
        return value.ToString(format, CultureInfo.InvariantCulture) + " " + units[unitIndex];
    }

    private static void OpenFolder(string folderPath)
    {
        Directory.CreateDirectory(folderPath);
        Process.Start(new ProcessStartInfo(folderPath)
        {
            UseShellExecute = true
        });
    }
}

public sealed record ReportOperationResult(bool Success, string Message);
