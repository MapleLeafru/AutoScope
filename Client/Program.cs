using System;
using System.Diagnostics;
using System.IO;

Console.WriteLine("C# Клиент запущен");

// Корень проекта
string baseDir = AppDomain.CurrentDomain.BaseDirectory;
string root = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\"));

// ####################################################################################################################
// Папка с базами
string dbFolder = Path.Combine(root, "Databases");

// Получаем все .db файлы
string[] dbFiles = Directory.Exists(dbFolder) // ?
    ? Directory.GetFiles(dbFolder, "*.db") // ?
    : new string[0]; // ?

if (dbFiles.Length == 0)
{
    Console.WriteLine("Нет доступных баз данных в папке Databases.");
    return;
}

// Показываем список пользователю
Console.WriteLine("Выберите базу данных:");
for (int i = 0; i < dbFiles.Length; i++)
{
    Console.WriteLine($"{i}: {Path.GetFileName(dbFiles[i])}");
}

// Читаем выбор
int selectedIndex = -1;
while (true)
{
    Console.Write("Введите номер базы: ");
    string input = Console.ReadLine();

    if (int.TryParse(input, out selectedIndex) && selectedIndex >= 0 && selectedIndex < dbFiles.Length)
        break;

    Console.WriteLine("Некорректный ввод. Попробуйте снова.");
}

string selectedDb = dbFiles[selectedIndex];
Console.WriteLine($"Выбрана база: {Path.GetFileName(selectedDb)}");
// ####################################################################################################################

// Запуск питона
string pythonPath = Path.Combine(root, @"AutoScopeVenv\Scripts\python.exe");
string corePath = Path.Combine(root, @"Core\Core.py");

ProcessStartInfo start = new ProcessStartInfo(); // ХЗ но догадываюсь
start.FileName = pythonPath;
start.Arguments = $"\"{corePath}\" \"{selectedDb}\"";
start.UseShellExecute = false; // ХЗ

Process.Start(start); // ХЗ но догадываюсь