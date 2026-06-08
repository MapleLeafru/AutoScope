using System;
using System.Text;

// Точка входа консольного клиента AutoScope.
Console.OutputEncoding = Encoding.UTF8;

AppPaths paths = AppPaths.FromCurrentDirectory();
ConsoleApp app = new ConsoleApp(paths);
app.Run();
