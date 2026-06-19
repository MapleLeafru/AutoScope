using System;
using System.IO;
using System.Linq;
using System.Text.Json;

// Управляет консольными инструментами создания и удаления баз данных.
public class DatabaseToolService
{
    private readonly AppPaths _paths;
    private readonly ConsoleInputService _input;
    private readonly DatabaseDiscoveryService _databaseDiscovery;
    private readonly PythonProcessService _pythonProcess;

    public DatabaseToolService(
        AppPaths paths,
        ConsoleInputService input,
        DatabaseDiscoveryService databaseDiscovery,
        PythonProcessService pythonProcess
    )
    {
        _paths = paths;
        _input = input;
        _databaseDiscovery = databaseDiscovery;
        _pythonProcess = pythonProcess;
    }

    // Показывает меню инструментов для работы с базами данных.
    public void ShowMenu()
    {
        Console.WriteLine("Выберите инструмент:");
        Console.WriteLine("0 - Вернуться назад");
        Console.WriteLine("1 - Создать новую базу данных");
        Console.WriteLine("2 - Удалить базу данных");

        int toolNumber = _input.ReadMenuNumber(min: 0, max: 2, "Номер выбранного инструмента: ");

        if (toolNumber == 0) { return; }
        if (toolNumber == 1) { CreateDatabase(); }
        if (toolNumber == 2) { DeleteDatabase(); }
    }

    // Запрашивает имя новой базы и запускает DatabaseManager.py в режиме создания.
    private void CreateDatabase()
    {
        while (true)
        {
            Console.Write("Введите название новой базы данных: ");
            string databaseName = Console.ReadLine() ?? "";

            if (string.IsNullOrWhiteSpace(databaseName))
            {
                bool continueCreation = _input.AskYesNo("Введено пустое поле. Желаете продолжить создание базы данных? y/n: ");
                if (!continueCreation) { return; }
                continue;
            }

            string[] existingDatabases = _databaseDiscovery.ScanDatabases(consoleOutput: false, returnOnlyFileNames: true);
            string plainFileName = databaseName + ".db";
            string configuredFileName = databaseName + ".BaseDataBaseConfig.db";
            bool databaseAlreadyExists = existingDatabases.Contains(plainFileName)
                || existingDatabases.Contains(configuredFileName);

            string question = databaseAlreadyExists
                ? $"База данных с названием <{databaseName}> уже существует, пересоздать? y/n: "
                : $"Подтвердите создание базы данных <{databaseName}> (конфиг будет добавлен автоматически) y/n: ";

            if (_input.AskYesNo(question))
            {
                RunDatabaseManager(databaseName + ".db", "dbCreate");
                return;
            }

            return;
        }
    }

    // Запрашивает базу данных и запускает DatabaseManager.py в режиме удаления.
    private void DeleteDatabase()
    {
        string databasePath = _databaseDiscovery.SelectDatabase(
            message: "Выберите базу данных для удаления",
            cancelText: "Отменить удаление базы данных"
        );
        if (string.IsNullOrWhiteSpace(databasePath)) { return; }

        string databaseFileName = Path.GetFileName(databasePath);
        bool confirmed = _input.AskYesNo(
            $"Вы уверены, что хотите удалить базу данных {databaseFileName}? Это действие необратимо. y/n: "
        );

        if (!confirmed) { return; }

        RunDatabaseManager(databaseFileName, "dbDelete");
    }

    // Формирует JSON-запрос для DatabaseManager.py и выводит результат в консоль.
    private void RunDatabaseManager(string databaseFileName, string command)
    {
        var request = new
        {
            command = command,
            dbFileName = databaseFileName,
            configPath = _paths.ConfigsPath,
            dbPath = _paths.DbPath
        };

        string json = JsonSerializer.Serialize(request);
        ProcessRunResult result = _pythonProcess.RunScript(_paths.GetDatabaseManagerPath(), json);

        Console.WriteLine(result.Output);
        if (!string.IsNullOrWhiteSpace(result.Error))
            Console.WriteLine("Errors:\n" + result.Error);
    }
}
