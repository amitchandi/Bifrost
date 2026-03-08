using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace Bifrost.GUI;

public class BoolToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Color.Parse("#a6e3a1") : Color.Parse("#f38ba8");

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
