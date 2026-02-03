using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using RequestTracker.Models;

namespace RequestTracker.Converters;

/// <summary>
/// Returns true when the bound FileNode should show the "Archive" context menu item
/// (i.e. not the All JSON virtual node, not a directory, and not already archived).
/// </summary>
public class FileNodeToArchiveVisibleConverter : IValueConverter
{
    public static readonly FileNodeToArchiveVisibleConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not FileNode node)
            return false;
        if (node.Path == "ALL_JSON_VIRTUAL_NODE" || node.IsDirectory)
            return false;
        string name = Path.GetFileName(node.Path);
        if (string.IsNullOrEmpty(name))
            return false;
        if (name.StartsWith("archived-", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("archive-", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
