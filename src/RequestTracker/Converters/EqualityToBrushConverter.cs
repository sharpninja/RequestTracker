using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace RequestTracker.Converters;

/// <summary>
/// Multi-value converter: returns selected brush when values[0] equals values[1], otherwise transparent.
/// Used for selected row background (current entry, selected entry).
/// </summary>
public class EqualityToBrushConverter : IMultiValueConverter
{
    public static readonly EqualityToBrushConverter Instance = new();
    private static readonly IBrush SelectedBrush = new SolidColorBrush(Color.FromRgb(0xAD, 0xD8, 0xE6)); // light blue
    private static readonly IBrush UnselectedBrush = Brushes.Transparent;

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values == null || values.Count < 2) return UnselectedBrush;
        if (values[0] == null || values[1] == null) return UnselectedBrush;
        bool equal = Equals(values[0], values[1]);
        return equal ? SelectedBrush : UnselectedBrush;
    }
}
