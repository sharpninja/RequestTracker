using System;
using System.Collections;
using System.Globalization;
using Avalonia.Data.Converters;

namespace RequestTracker.Converters;

/// <summary>
/// Returns true when value is a non-null collection with Count > 0 (for IsVisible bindings).
/// Pass ConverterParameter="Invert" to return true when the collection is null or empty (for empty-state messages).
/// </summary>
public class CountToVisibilityConverter : IValueConverter
{
    public static readonly CountToVisibilityConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool hasItems;
        if (value == null) hasItems = false;
        else if (value is IList list) hasItems = list.Count > 0;
        else if (value is IEnumerable enumerable)
        {
            var e = enumerable.GetEnumerator();
            try { hasItems = e.MoveNext(); }
            finally { (e as IDisposable)?.Dispose(); }
        }
        else hasItems = false;

        bool invert = string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase);
        return invert ? !hasItems : hasItems;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
