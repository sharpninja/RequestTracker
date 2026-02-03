using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System;
using System.Linq;
using Avalonia.Markup.Xaml;
using RequestTracker.ViewModels;
using RequestTracker.Views;

namespace RequestTracker;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void RegisterServices()
    {
        base.RegisterServices();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DisableAvaloniaDataAnnotationValidation();

            Avalonia.Controls.Window? window = null;
            try
            {
                window = new MainWindow();
                try
                {
                    window.DataContext = new MainWindowViewModel();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"ViewModel init failed (window will still show): {ex}");
                    System.Diagnostics.Debug.WriteLine(ex.ToString());
                }

                desktop.MainWindow = window;
                window.Show();
                window.Activate();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"MainWindow creation failed: {ex}");
                System.Diagnostics.Debug.WriteLine(ex.ToString());
                System.IO.File.WriteAllText("crash.log", ex.ToString());
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}
