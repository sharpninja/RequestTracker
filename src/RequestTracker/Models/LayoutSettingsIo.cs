using System;
using System.IO;
using System.Text.Json;

namespace RequestTracker.Models;

/// <summary>Load/save LayoutSettings from the app's settings file (shared by main and chat window).</summary>
public static class LayoutSettingsIo
{
    private const string SettingsFileName = "layout_settings.json";

    public static string GetSettingsFilePath()
    {
        var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RequestTracker");
        Directory.CreateDirectory(appDataPath);
        return Path.Combine(appDataPath, SettingsFileName);
    }

    public static LayoutSettings? Load()
    {
        try
        {
            var path = GetSettingsFilePath();
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<LayoutSettings>(json);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(LayoutSettings settings)
    {
        try
        {
            var path = GetSettingsFilePath();
            var json = JsonSerializer.Serialize(settings);
            File.WriteAllText(path, json);
        }
        catch
        {
            // Ignore
        }
    }
}
