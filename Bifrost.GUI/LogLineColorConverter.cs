using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Bifrost.GUI;

public class LogLineColorConverter : IValueConverter
{
    public static readonly LogLineColorConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var line = value as string ?? "";
        if (line.Contains("[FAIL]")) return new SolidColorBrush(Color.Parse("#f38ba8"));
        if (line.Contains("[OK]"))   return new SolidColorBrush(Color.Parse("#a6e3a1"));
        if (line.Contains("[WARN]")) return new SolidColorBrush(Color.Parse("#f9e2af"));
        if (line.Contains("[DB]"))   return new SolidColorBrush(Color.Parse("#89b4fa"));
        if (line.Contains("[TIME]")) return new SolidColorBrush(Color.Parse("#cba6f7"));
        if (line.Contains("[DIR]"))  return new SolidColorBrush(Color.Parse("#89dceb"));
        if (line.Contains("==="))    return new SolidColorBrush(Color.Parse("#45475a"));
        return new SolidColorBrush(Color.Parse("#cdd6f4"));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
