using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using AutoScope.WpfClient.Models;

namespace AutoScope.WpfClient.Converters;

public class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string resourceKey = value switch
        {
            DashboardStateKind.Running => "AccentBrush",
            DashboardStateKind.Success => "SuccessBrush",
            DashboardStateKind.Error => "ErrorBrush",
            DashboardStateKind.Warning => "WarningBrush",
            _ => "NeutralBrush"
        };

        return Application.Current.TryFindResource(resourceKey) as Brush ?? Brushes.White;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
