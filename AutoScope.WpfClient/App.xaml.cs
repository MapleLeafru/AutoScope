using System.Windows;
using AutoScope.WpfClient.Services;

namespace AutoScope.WpfClient;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string rootPath = AutoScopeRootLocator.FindRoot();
        ThemeService.ApplySavedTheme(rootPath);
    }
}
