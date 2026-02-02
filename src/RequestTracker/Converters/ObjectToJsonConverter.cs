using Avalonia.Data.Converters;
using System;
using System.Globalization;
using System.Text.Json;

namespace RequestTracker.Converters;

public class ObjectToJsonConverter : IValueConverter
{
    private static readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null) return "null";
        try
        {
            return JsonSerializer.Serialize(value, _options);
        }
        catch (Exception ex)
        {
            return $"Error serializing JSON: {ex.Message}";
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}