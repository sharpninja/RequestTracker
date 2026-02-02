using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using AvaloniaWebView;
using WebViewCore.Events;
using RequestTracker.ViewModels;
using RequestTracker.Models;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace RequestTracker.Views;

public partial class MainWindow : Window
{
    private bool? _wasPortrait;
    private LayoutSettings _layoutSettings = new();
    private const string SettingsFileName = "layout_settings.json";

    public MainWindow()
    {
        InitializeComponent();

        LoadSettings();
        ApplyWindowSettings();

        // On Linux, WebView.Avalonia opens a separate empty GTK window and the main app window may not show.
        // Remove the WebView from the tree so it is never realized; placeholder is shown instead.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            MarkdownContentGrid.Children.Remove(MarkdownWebView);
        }

        this.SizeChanged += OnWindowSizeChanged;
        this.PositionChanged += OnWindowPositionChanged;
        this.Closed += OnWindowClosed;
        this.Opened += OnWindowOpened;
    }

    private void ApplyWindowSettings()
    {
        try 
        {
            // Apply persisted window state
            // Sanity check values
            if (_layoutSettings.WindowWidth < 100) _layoutSettings.WindowWidth = 1000;
            if (_layoutSettings.WindowHeight < 100) _layoutSettings.WindowHeight = 800;
            
            this.Width = _layoutSettings.WindowWidth;
            this.Height = _layoutSettings.WindowHeight;
            
            // Only apply position if valid (non-negative, though negative is valid for multi-monitor, 
            // usually 0,0 is safe default if not set)
            // But we'll trust the default (100,100) or saved value
            this.Position = new PixelPoint((int)_layoutSettings.WindowX, (int)_layoutSettings.WindowY);
            
            this.WindowState = _layoutSettings.WindowState;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error applying window settings: {ex.Message}");
        }
    }

    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        // Bring window to front so it's visible
        Activate();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // WSLg: force on-screen position if CenterScreen left us off-screen
            var pos = Position;
            if (pos.X < 0 || pos.Y < 0 || pos.X > 4000 || pos.Y > 4000)
            {
                Position = new PixelPoint(50, 50);
            }

            // WSLg: window may not get focus until fully mapped. Activate again after a short delay.
            await Task.Delay(150);
            Dispatcher.UIThread.Post(() => Activate(), DispatcherPriority.Input);
        }
    }

    private void LoadSettings()
    {
        try
        {
            var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RequestTracker");
            Directory.CreateDirectory(appDataPath);
            var filePath = Path.Combine(appDataPath, SettingsFileName);
            
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                var settings = JsonSerializer.Deserialize<LayoutSettings>(json);
                if (settings != null)
                {
                    _layoutSettings = settings;
                }
            }
        }
        catch
        {
            // Ignore errors loading settings, use defaults
        }
    }

    private void SaveSettings()
    {
        try
        {
            var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RequestTracker");
            Directory.CreateDirectory(appDataPath);
            var filePath = Path.Combine(appDataPath, SettingsFileName);
            
            var json = JsonSerializer.Serialize(_layoutSettings);
            File.WriteAllText(filePath, json);
        }
        catch
        {
            // Ignore errors saving settings
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        // Save current state before closing
        if (_wasPortrait.HasValue)
        {
            SaveCurrentLayoutToSettings(_wasPortrait.Value);
        }
        
        // Save window state if not minimized (to avoid saving 0 size or off-screen position)
        if (this.WindowState == WindowState.Normal || this.WindowState == WindowState.Maximized)
        {
            _layoutSettings.WindowState = this.WindowState;
            
            if (this.WindowState == WindowState.Normal)
            {
                _layoutSettings.WindowWidth = this.Width;
                _layoutSettings.WindowHeight = this.Height;
                _layoutSettings.WindowX = this.Position.X;
                _layoutSettings.WindowY = this.Position.Y;
            }
        }

        SaveSettings();
    }

    private void OnWindowPositionChanged(object? sender, PixelPointEventArgs e)
    {
        // Optional: Track live position updates if needed, but OnWindowClosed is usually sufficient
    }

    private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // Don't process layout changes if minimized
        if (this.WindowState == WindowState.Minimized) return;

        bool isPortrait = e.NewSize.Height > e.NewSize.Width;
        if (_wasPortrait == isPortrait) return;

        // If we are switching modes, save the OLD mode's state first
        if (_wasPortrait.HasValue)
        {
            SaveCurrentLayoutToSettings(_wasPortrait.Value);
        }

        _wasPortrait = isPortrait;

        UpdateLayoutForOrientation(isPortrait);
    }

    private void SaveCurrentLayoutToSettings(bool isPortrait)
    {
        if (MainGrid == null) return;

        // Validate we have the expected number of rows/cols before accessing
        if (isPortrait)
        {
            // Portrait expects Rows=5 (Tree, Split, Viewer, Split, History)
            if (MainGrid.RowDefinitions.Count >= 5)
            {
                _layoutSettings.PortraitTreeRowHeight = GridLengthDto.FromGridLength(MainGrid.RowDefinitions[0].Height);
                _layoutSettings.PortraitViewerRowHeight = GridLengthDto.FromGridLength(MainGrid.RowDefinitions[2].Height);
                _layoutSettings.PortraitHistoryRowHeight = GridLengthDto.FromGridLength(MainGrid.RowDefinitions[4].Height);
            }
        }
        else
        {
            // Landscape expects Cols=3 (Tree/Hist, Split, Viewer) and Rows=3 (Tree, Split, History)
            if (MainGrid.ColumnDefinitions.Count >= 1 && MainGrid.RowDefinitions.Count >= 3)
            {
                _layoutSettings.LandscapeLeftColWidth = GridLengthDto.FromGridLength(MainGrid.ColumnDefinitions[0].Width);
                _layoutSettings.LandscapeHistoryRowHeight = GridLengthDto.FromGridLength(MainGrid.RowDefinitions[2].Height);
            }
        }
    }

    private void UpdateLayoutForOrientation(bool isPortrait)
    {
        // Elements are: MainGrid, TreePanel, Splitter1, HistoryPanel, Splitter2, ViewerPanel
        // Note: Avalonia name generator creates references to x:Name elements

        if (MainGrid == null) return; // Not initialized yet

        MainGrid.ColumnDefinitions.Clear();
        MainGrid.RowDefinitions.Clear();

        if (isPortrait)
        {
            // Portrait: Vertical Stack
            // Tree -> Splitter -> Viewer -> Splitter -> History
            // Rows: *, 4, *, 4, 150
            // Cols: *
            
            MainGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

            MainGrid.RowDefinitions.Add(new RowDefinition(_layoutSettings.PortraitTreeRowHeight.ToGridLength()));      // Tree
            MainGrid.RowDefinitions.Add(new RowDefinition(4, GridUnitType.Pixel));     // Splitter 1
            MainGrid.RowDefinitions.Add(new RowDefinition(_layoutSettings.PortraitViewerRowHeight.ToGridLength()));    // Viewer
            MainGrid.RowDefinitions.Add(new RowDefinition(4, GridUnitType.Pixel));     // Splitter 2
            MainGrid.RowDefinitions.Add(new RowDefinition(_layoutSettings.PortraitHistoryRowHeight.ToGridLength()));   // History

            // Tree (0,0)
            Grid.SetColumn(TreePanel, 0);
            Grid.SetRow(TreePanel, 0);

            // Splitter 1 (0,1) - Horizontal
            Grid.SetColumn(Splitter1, 0);
            Grid.SetRow(Splitter1, 1);
            Splitter1.ResizeDirection = GridResizeDirection.Rows;

            // Viewer (0,2)
            Grid.SetColumn(ViewerPanel, 0);
            Grid.SetRow(ViewerPanel, 2);
            Grid.SetRowSpan(ViewerPanel, 1);

            // Splitter 2 (0,3) - Horizontal
            Grid.SetColumn(Splitter2, 0);
            Grid.SetRow(Splitter2, 3);
            Grid.SetRowSpan(Splitter2, 1);
            Splitter2.ResizeDirection = GridResizeDirection.Rows;

            // History (0,4)
            Grid.SetColumn(HistoryPanel, 0);
            Grid.SetRow(HistoryPanel, 4);
        }
        else
        {
            // Landscape: Left Pane (Tree/History) | Viewer
            // Cols: 300, 4, *
            // Rows: *, 4, 150
            
            MainGrid.ColumnDefinitions.Add(new ColumnDefinition(_layoutSettings.LandscapeLeftColWidth.ToGridLength()));
            MainGrid.ColumnDefinitions.Add(new ColumnDefinition(4, GridUnitType.Pixel));
            MainGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

            MainGrid.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));      // Tree
            MainGrid.RowDefinitions.Add(new RowDefinition(4, GridUnitType.Pixel));     // Splitter 1
            MainGrid.RowDefinitions.Add(new RowDefinition(_layoutSettings.LandscapeHistoryRowHeight.ToGridLength()));   // History

            // Tree (0,0)
            Grid.SetColumn(TreePanel, 0);
            Grid.SetRow(TreePanel, 0);

            // Splitter 1 (0,1) - Horizontal
            Grid.SetColumn(Splitter1, 0);
            Grid.SetRow(Splitter1, 1);
            Splitter1.ResizeDirection = GridResizeDirection.Rows;

            // History (0,2)
            Grid.SetColumn(HistoryPanel, 0);
            Grid.SetRow(HistoryPanel, 2);

            // Splitter 2 (1, 0-span-3) - Vertical
            Grid.SetColumn(Splitter2, 1);
            Grid.SetRow(Splitter2, 0);
            Grid.SetRowSpan(Splitter2, 3);
            Splitter2.ResizeDirection = GridResizeDirection.Columns;

            // Viewer (2, 0-span-3)
            Grid.SetColumn(ViewerPanel, 2);
            Grid.SetRow(ViewerPanel, 0);
            Grid.SetRowSpan(ViewerPanel, 3);
        }
    }

    private void OnNavigationStarting(object? sender, WebViewUrlLoadingEventArg e)
    {
        if (e.Url == null) return;
        
        if (DataContext is MainWindowViewModel vm)
        {
            if (vm.ShouldInterceptNavigation(e.Url))
            {
                e.Cancel = true;
            }
        }
    }

    private void OnJsonNodeDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (e.Source is Avalonia.Controls.TextBlock textBlock && !string.IsNullOrEmpty(textBlock.Text))
        {
             if (DataContext is MainWindowViewModel vm)
             {
                 vm.CopyTextCommand.Execute(textBlock.Text);
                 e.Handled = true;
             }
        }
    }
}
