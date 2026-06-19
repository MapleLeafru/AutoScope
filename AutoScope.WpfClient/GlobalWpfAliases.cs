// Единые WPF-псевдонимы для проекта.
//
// После подключения UseWindowsForms ради иконки в трее .NET добавляет
// WinForms/System.Drawing в неявные using-директивы проекта. Из-за этого
// обычные WPF-типы вроде MessageBox, Application, Button, Brush и Point
// становятся неоднозначными. Эти псевдонимы закрепляют, что в основном
// WPF-клиенте короткие имена относятся именно к WPF, а WinForms-типы
// используются точечно через Forms = System.Windows.Forms в TrayService.

global using Application = System.Windows.Application;
global using MessageBox = System.Windows.MessageBox;
global using MessageBoxButton = System.Windows.MessageBoxButton;
global using MessageBoxImage = System.Windows.MessageBoxImage;
global using MessageBoxResult = System.Windows.MessageBoxResult;
global using Clipboard = System.Windows.Clipboard;
global using Point = System.Windows.Point;
global using Button = System.Windows.Controls.Button;
global using Control = System.Windows.Controls.Control;
global using Panel = System.Windows.Controls.Panel;
global using Brush = System.Windows.Media.Brush;
global using Brushes = System.Windows.Media.Brushes;
global using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
