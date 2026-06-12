using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using AudioShare.Models;

namespace AudioShare.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        var invert = parameter as string == "Invert";
        var b = value is bool x && x;
        if (invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class BytesToMbConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is long bytes) return $"{bytes / 1024.0 / 1024.0:0.0} MB";
        return "—";
    }
    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class DeviceStateToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DeviceState state)
            return Application.Current.Resources["TextMutedBrush"];
        return state switch
        {
            DeviceState.Active => Application.Current.Resources["SuccessBrush"],
            DeviceState.Disabled => Application.Current.Resources["TextMutedBrush"],
            DeviceState.Unplugged => Application.Current.Resources["DangerBrush"],
            _ => Application.Current.Resources["TextMutedBrush"]
        };
    }
    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
