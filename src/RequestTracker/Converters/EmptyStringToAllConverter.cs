using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace RequestTracker.Converters;

/// <summary>
/// Converts empty string to "(All)" for filter ComboBox display; other values pass through.
/// </summary>
public class EmptyStringToAllConverter : IValueConverter
{
    public static readonly EmptyStringToAllConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && string.IsNullOrEmpty(s))
            return "(All)";
        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value;
}
