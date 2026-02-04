using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace RequestTracker.Converters;

/// <summary>Converts chat message role to visibility: Visible when Role equals ConverterParameter ("user" or "assistant"), else Collapsed.</summary>
public class RoleToVisibilityConverter : IValueConverter
{
    public static readonly RoleToVisibilityConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string? role = value as string;
        string? expected = parameter as string;
        if (string.IsNullOrEmpty(expected))
            return false;
        return string.Equals(role, expected, StringComparison.OrdinalIgnoreCase);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}
