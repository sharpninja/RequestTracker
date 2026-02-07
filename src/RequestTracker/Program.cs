using Avalonia;
using System;
using System.IO;

namespace RequestTracker;

sealed class Program
{
    private static readonly string HtmlCacheDir = AppSettings.ResolveHtmlCacheDirectory();

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            RemoveGeneratedHtmlFiles();
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FATAL CRASH: {ex}");
            System.IO.File.WriteAllText("crash.log", ex.ToString());
            throw; // Re-throw to let the OS handle it (or not)
        }
    }

    /// <summary>Deletes the RequestTracker_Cache folder and any generated HTML files from previous runs.</summary>
    private static void RemoveGeneratedHtmlFiles()
    {
        try
        {
            if (!Directory.Exists(HtmlCacheDir))
                return;
            Directory.Delete(HtmlCacheDir, recursive: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Could not remove HTML cache: {ex.Message}");
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
