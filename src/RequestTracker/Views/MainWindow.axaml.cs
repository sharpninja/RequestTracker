using Avalonia.Controls;
using AvaloniaWebView;
using WebViewCore.Events;
using RequestTracker.ViewModels;
using System;

namespace RequestTracker.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnNavigationStarting(object? sender, WebViewUrlLoadingEventArg e)
    {
        if (e.Url == null) return;
        
        // Console.WriteLine($"NavigationStarting: {e.Url}");

        if (e.Url.Scheme == "file")
        {
            string path = e.Url.LocalPath;
            if (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) return;

            if (path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.HandleNavigation(path);
                }
            }
        }
    }

    private async void OnJsonNodeDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (e.Source is Avalonia.Controls.TextBlock textBlock && !string.IsNullOrEmpty(textBlock.Text))
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(textBlock.Text);
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.SetStatus($"Copied: {textBlock.Text}");
                }
                e.Handled = true;
            }
        }
    }
}
