using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace RequestTracker.Converters;

/// <summary>Converts chat message role ("user" / "assistant") to a background brush.</summary>
public class RoleToBrushConverter : IValueConverter
{
    public static readonly RoleToBrushConverter Instance = new();
    private static readonly IBrush UserBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
    private static readonly IBrush AssistantBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xF0, 0xFF));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string role && string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase))
            return AssistantBrush;
        return UserBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}
