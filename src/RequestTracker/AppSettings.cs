using System;
using System.IO;
using System.Text.Json;
using System.Runtime.InteropServices;
using RequestTracker.Converters;

namespace RequestTracker;

public sealed class AppSettings
{
    private const string ConfigFileName = "appsettings.config";

    public PathSettings Paths { get; init; } = new();

    private static readonly Lazy<AppSettings> CurrentValue = new(Load);

    public static AppSettings Current => CurrentValue.Value;

    public static string ResolveSessionsRootPath()
        => ResolveRequiredPath(Current.Paths.SessionsRootPath, "Paths.SessionsRootPath");

    public static string ResolveHtmlCacheDirectory()
        => ResolveRequiredPath(Current.Paths.HtmlCacheDirectory, "Paths.HtmlCacheDirectory");

    public static string? ResolveCssFallbackPath()
        => ResolveOptionalPath(Current.Paths.CssFallbackPath);

    private static AppSettings Load()
    {
        string configPath = Path.Combine(AppContext.BaseDirectory, ConfigFileName);
        if (!File.Exists(configPath))
            throw new FileNotFoundException("Missing appsettings.config. Ensure it is copied next to the executable.", configPath);

        string json = File.ReadAllText(configPath);
        var settings = JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        if (settings == null)
            throw new InvalidOperationException("Failed to parse appsettings.config.");

        return settings;
    }

    private static string ResolveRequiredPath(string? value, string settingName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Setting '{settingName}' is required.");
        return NormalizePath(value);
    }

    private static string? ResolveOptionalPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return NormalizePath(value);
    }

    private static string NormalizePath(string value)
    {
        string expanded = Environment.ExpandEnvironmentVariables(value.Trim());
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && PathConverter.IsWindowsPath(expanded))
            return Path.GetFullPath(PathConverter.ToWslPath(expanded));
        if (!Path.IsPathRooted(expanded))
            expanded = Path.Combine(AppContext.BaseDirectory, expanded);
        return Path.GetFullPath(expanded);
    }

    public sealed class PathSettings
    {
        public string? SessionsRootPath { get; init; }
        public string? HtmlCacheDirectory { get; init; }
        public string? CssFallbackPath { get; init; }
    }
}
