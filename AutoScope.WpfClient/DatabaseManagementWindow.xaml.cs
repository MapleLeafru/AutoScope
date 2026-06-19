using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using AutoScope.WpfClient.Models;
using AutoScope.WpfClient.Services;

namespace AutoScope.WpfClient;

public partial class DatabaseManagementWindow : Window, INotifyPropertyChanged
{
    private readonly DatabaseManagementService _databaseService;
    private string _statusMessage = "";

    public ObservableCollection<DatabaseDashboardItem> Databases { get; } = new();

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public DatabaseManagementWindow(string rootPath)
    {
        InitializeComponent();
        DataContext = this;

        _databaseService = new DatabaseManagementService(rootPath);
        ReloadDatabases();
        UpdateFileNamePreview();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        ReloadDatabases();
        StatusMessage = $"Список баз обновлён: {DateTime.Now:HH:mm:ss}.";
    }

    private void OpenDatabasesFolder_Click(object sender, RoutedEventArgs e)
    {
        _databaseService.OpenFolder(_databaseService.GetDatabasesFolderPath());
    }

    private void OpenDatabaseFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not DatabaseDashboardItem database)
            return;

        _databaseService.OpenFileFolder(database.Path);
    }

    private void OpenDbBrowser_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not DatabaseDashboardItem database)
            return;

        bool opened = _databaseService.OpenDatabaseInDbBrowser(database.Path, out string message);
        StatusMessage = message;

        if (!opened)
            MessageBox.Show(message, "AutoScope", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CreateDatabase_Click(object sender, RoutedEventArgs e)
    {
        string rawName = DatabaseNameTextBox.Text.Trim();
        string expectedFileName = _databaseService.BuildExpectedDatabaseFileName(rawName);

        if (string.IsNullOrWhiteSpace(expectedFileName))
        {
            StatusMessage = "Введите название базы данных.";
            MessageBox.Show("Введите название базы данных.", "AutoScope", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_databaseService.DatabaseExistsByInputName(rawName))
        {
            MessageBoxResult answer = MessageBox.Show(
                $"База {expectedFileName} уже существует. Пересоздать её? Старый файл будет удалён.",
                "Создание базы",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes)
                return;
        }
        else
        {
            MessageBoxResult answer = MessageBox.Show(
                $"Создать базу {expectedFileName}?",
                "Создание базы",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes)
                return;
        }

        DatabaseOperationResult result = _databaseService.CreateDatabase(rawName);
        StatusMessage = result.Message;

        if (!result.Success)
        {
            MessageBox.Show(result.Message, "AutoScope", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DatabaseNameTextBox.Text = "";
        ReloadDatabases();
    }

    private void DeleteDatabase_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not DatabaseDashboardItem database)
            return;

        MessageBoxResult answer = MessageBox.Show(
            $"Удалить базу {database.FileName}? Это действие нельзя отменить.",
            "Удаление базы",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
            return;

        DatabaseOperationResult result = _databaseService.DeleteDatabase(database);
        StatusMessage = result.Message;

        if (!result.Success)
        {
            MessageBox.Show(result.Message, "AutoScope", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ReloadDatabases();
    }

    private void DatabaseNameTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        UpdateFileNamePreview();
    }

    private void UpdateFileNamePreview()
    {
        if (DatabaseNameTextBox == null || FileNamePreviewText == null)
            return;

        string expectedFileName = _databaseService.BuildExpectedDatabaseFileName(DatabaseNameTextBox.Text);
        FileNamePreviewText.Text = string.IsNullOrWhiteSpace(expectedFileName)
            ? "Файл будет создан в папке Databases. Конфиг добавится в имя автоматически."
            : $"Будет создан файл: {expectedFileName}";
    }

    private void ReloadDatabases()
    {
        Databases.Clear();
        foreach (DatabaseDashboardItem item in _databaseService.LoadDatabases())
            Databases.Add(item);

        if (Databases.Count == 0)
        {
            Databases.Add(new DatabaseDashboardItem
            {
                Name = "Базы не найдены",
                FileName = "",
                ConfigName = "",
                RecordsText = "нет данных",
                SizeText = "размер: —",
                Details = "Папка Databases пуста или базы пока не найдены.",
                StateKind = DashboardStateKind.Neutral
            });
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            return;

        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // DragMove может упасть, если мышь уже отпущена. Для UI это не критично.
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
