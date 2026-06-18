using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AutoScope.WpfClient.Services;

namespace AutoScope.WpfClient;

public partial class SettingsWindow : Window
{
    private readonly string _rootPath;

    public SettingsWindow(string rootPath)
    {
        InitializeComponent();
        _rootPath = rootPath;

        var themes = ThemeService.GetAvailableThemes(_rootPath);
        ThemeComboBox.ItemsSource = themes;
        string savedThemeKey = ThemeService.LoadSavedThemeKey(_rootPath);
        ThemeComboBox.SelectedItem = themes.FirstOrDefault(theme => theme.Key == savedThemeKey) ?? themes.FirstOrDefault();
        UpdateThemeDescription();
    }


    private void RootWindowBorder_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RootWindowBorder.Clip = null;
    }

    private void ThemeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateThemeDescription();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (ThemeComboBox.SelectedItem is not ThemeOption selectedTheme)
            return;

        ThemeService.ApplyTheme(selectedTheme.Key, save: true, rootPath: _rootPath);
        DialogResult = true;
        Close();
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

    private void UpdateThemeDescription()
    {
        if (ThemeComboBox.SelectedItem is ThemeOption selectedTheme)
            ThemeDescriptionText.Text = selectedTheme.Description;
        else
            ThemeDescriptionText.Text = "";
    }
}
