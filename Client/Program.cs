using System.Diagnostics;
// using System.IO;

Console.WriteLine("C# Клиент запущен");

// Корень проекта
string baseDir = AppDomain.CurrentDomain.BaseDirectory;
string root = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\"));

// Запуск питона
string pythonPath = Path.Combine(root, @"AutoScopeVenv\Scripts\python.exe");
string corePath = Path.Combine(root, @"Core\Core.py");

ProcessStartInfo start = new ProcessStartInfo(); // ХЗ но догадываюсь
start.FileName = pythonPath;
start.Arguments = corePath;
start.UseShellExecute = false; // ХЗ

Process.Start(start); // ХЗ но догадываюсь
