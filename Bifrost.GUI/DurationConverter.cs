using Avalonia.Data.Converters;
using System.Globalization;

namespace Bifrost.GUI;

public class DurationConverter : IValueConverter
{
    public static string Format(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        if (!TimeSpan.TryParse(raw, out var ts)) return raw;

        if (ts.TotalSeconds < 1)  return "< 1s";
        if (ts.TotalMinutes < 1)  return $"{ts.Seconds}s";
        if (ts.TotalHours < 1)    return ts.Seconds > 0
            ? $"{ts.Minutes}m {ts.Seconds}s"
            : $"{ts.Minutes}m";

        var h = (int)ts.TotalHours;
        return ts.Minutes > 0
            ? $"{h}h {ts.Minutes}m"
            : $"{h}h";
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Format(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
