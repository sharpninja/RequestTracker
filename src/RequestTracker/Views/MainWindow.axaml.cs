using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using RequestTracker.ViewModels;
using RequestTracker.Models;
using RequestTracker.Models.Json;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace RequestTracker.Views;

public partial class MainWindow : Window
{
    private bool? _wasPortrait;
    private bool _isUpdatingLayout;
    private LayoutSettings _layoutSettings = new();
    private ChatWindow? _chatWindow;

    public MainWindow()
    {
        InitializeComponent();

        LoadSettings();
        ApplyWindowSettings();

        this.SizeChanged += OnWindowSizeChanged;
        this.PositionChanged += OnWindowPositionChanged;
        this.Closing += OnWindowClosing;
        this.Opened += OnWindowOpened;
        this.GetObservable(WindowStateProperty).Subscribe(new WindowStateObserver(this));
    }

    private sealed class WindowStateObserver : IObserver<WindowState>
    {
        private readonly MainWindow _window;
        public WindowStateObserver(MainWindow window) => _window = window;
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(WindowState value) { _window.SaveWindowStateToSettings(); _window.SaveSettings(); }
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

            // Only apply position if it looks on-screen (avoids window opening off-screen after monitor change)
            int x = (int)_layoutSettings.WindowX;
            int y = (int)_layoutSettings.WindowY;
            if (x >= -50 && x <= 10000 && y >= -50 && y <= 10000 && (x != 0 || y != 0))
            {
                this.Position = new PixelPoint(x, y);
            }

            // Never start minimized so the window is visible on launch
            this.WindowState = _layoutSettings.WindowState == WindowState.Minimized
                ? WindowState.Normal
                : _layoutSettings.WindowState;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error applying window settings: {ex.Message}");
        }
    }

    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        // Deferred ViewModel init (file tree + watcher) so window shows first; failures don't block display
        if (DataContext is MainWindowViewModel vm)
            vm.InitializeAfterWindowShown();

        ApplyJsonViewerSplitterSettings();

        // Force the saved position again after opening, as some platforms/WMs
        // ignore initial position set in constructor or override it during show
        if (_layoutSettings.WindowX != 0 || _layoutSettings.WindowY != 0)
        {
            // Small delay to ensure window manager has finished initial placement
            await Task.Delay(50);
            if (WindowState == WindowState.Normal)
            {
                this.Position = new PixelPoint((int)_layoutSettings.WindowX, (int)_layoutSettings.WindowY);
            }
        }

        // All platforms: if saved position is off-screen (e.g. disconnected monitor), put window on-screen
        var pos = Position;
        if (pos.X < -100 || pos.Y < -100 || pos.X > 10000 || pos.Y > 10000)
        {
            Console.WriteLine($"Window position ({pos.X}, {pos.Y}) off-screen; resetting to (50, 50)");
            Position = new PixelPoint(50, 50);
        }

        // Bring window to front so it's visible
        Activate();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // WSLg: window may not get focus until fully mapped. Activate again after a short delay.
            await Task.Delay(150);
            Dispatcher.UIThread.Post(() => Activate(), DispatcherPriority.Input);
        }
    }

    private void LoadSettings()
    {
        try
        {
            var settings = LayoutSettingsIo.Load();
            if (settings != null)
                _layoutSettings = settings;
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
            // Merge with existing file so we don't overwrite ChatWindow position saved by the chat window
            var toSave = LayoutSettingsIo.Load() ?? new LayoutSettings();
            toSave.LandscapeLeftColWidth = _layoutSettings.LandscapeLeftColWidth;
            toSave.LandscapeHistoryRowHeight = _layoutSettings.LandscapeHistoryRowHeight;
            toSave.PortraitTreeRowHeight = _layoutSettings.PortraitTreeRowHeight;
            toSave.PortraitViewerRowHeight = _layoutSettings.PortraitViewerRowHeight;
            toSave.PortraitHistoryRowHeight = _layoutSettings.PortraitHistoryRowHeight;
            toSave.JsonViewerSearchIndexRowHeight = _layoutSettings.JsonViewerSearchIndexRowHeight;
            toSave.JsonViewerTreeRowHeight = _layoutSettings.JsonViewerTreeRowHeight;
            toSave.WindowWidth = _layoutSettings.WindowWidth;
            toSave.WindowHeight = _layoutSettings.WindowHeight;
            toSave.WindowX = _layoutSettings.WindowX;
            toSave.WindowY = _layoutSettings.WindowY;
            toSave.WindowState = _layoutSettings.WindowState;
            LayoutSettingsIo.Save(toSave);
        }
        catch
        {
            // Ignore errors saving settings
        }
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        _chatWindow?.Close();
        _chatWindow = null;

        if (_wasPortrait.HasValue)
            SaveCurrentLayoutToSettings(_wasPortrait.Value);

        SaveJsonViewerSplitterSettings();

        // Save final state and position on closing
        if (WindowState == WindowState.Normal)
        {
            _layoutSettings.WindowWidth = Width;
            _layoutSettings.WindowHeight = Height;
            _layoutSettings.WindowX = Position.X;
            _layoutSettings.WindowY = Position.Y;
        }
        // Don't store minimized; persist Normal so next launch shows the window
        _layoutSettings.WindowState = WindowState == WindowState.Minimized ? WindowState.Normal : WindowState;

        SaveSettings();
    }

    private void ApplyJsonViewerSplitterSettings()
    {
        try
        {
            if (JsonViewerGrid?.RowDefinitions == null || JsonViewerGrid.RowDefinitions.Count < 5)
                return;
            JsonViewerGrid.RowDefinitions[2].Height = _layoutSettings.JsonViewerSearchIndexRowHeight.ToGridLength();
            JsonViewerGrid.RowDefinitions[4].Height = _layoutSettings.JsonViewerTreeRowHeight.ToGridLength();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error applying JSON viewer splitter: {ex.Message}");
        }
    }

    private void SaveJsonViewerSplitterSettings()
    {
        try
        {
            if (JsonViewerGrid?.RowDefinitions == null || JsonViewerGrid.RowDefinitions.Count < 5)
                return;
            _layoutSettings.JsonViewerSearchIndexRowHeight = GridLengthDto.FromGridLength(JsonViewerGrid.RowDefinitions[2].Height);
            _layoutSettings.JsonViewerTreeRowHeight = GridLengthDto.FromGridLength(JsonViewerGrid.RowDefinitions[4].Height);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error saving JSON viewer splitter: {ex.Message}");
        }
    }

    private void OnWindowPositionChanged(object? sender, PixelPointEventArgs e)
    {
        // Position is saved on size/state change and on close; no need to save on every move
    }

    /// <summary>
    /// Updates _layoutSettings from current window state, size and position. Call before SaveSettings().
    /// Minimized is never stored (we skip saving when minimized).
    /// </summary>
    private void SaveWindowStateToSettings()
    {
        if (WindowState == WindowState.Minimized)
            return;
        _layoutSettings.WindowState = WindowState; // Normal or Maximized only
        if (WindowState == WindowState.Normal)
        {
            _layoutSettings.WindowWidth = Width;
            _layoutSettings.WindowHeight = Height;
            _layoutSettings.WindowX = Position.X;
            _layoutSettings.WindowY = Position.Y;
        }
    }

    private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // Re-entrancy guard: layout update can trigger another SizeChanged; avoid infinite loop
        if (_isUpdatingLayout) return;
        // Don't process layout changes if minimized
        if (this.WindowState == WindowState.Minimized) return;

        bool isPortrait = e.NewSize.Height > e.NewSize.Width;
        if (_wasPortrait == isPortrait) return;

        _isUpdatingLayout = true;
        try
        {
            // If we are switching modes, save the OLD mode's state first
            if (_wasPortrait.HasValue)
            {
                SaveCurrentLayoutToSettings(_wasPortrait.Value);
            }

            _wasPortrait = isPortrait;

            UpdateLayoutForOrientation(isPortrait);

            SaveWindowStateToSettings();
            SaveSettings();
        }
        finally
        {
            _isUpdatingLayout = false;
        }
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

    private void OnJsonNodeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        // Find a JsonTreeNode with SourcePath (this node or an ancestor row) so we can navigate to request details
        for (var c = e.Source as Control; c != null; c = c.Parent as Control)
        {
            if (c.DataContext is JsonTreeNode node && !string.IsNullOrEmpty(node.SourcePath))
            {
                if (vm.TryNavigateToDetailsForSourcePath(node.SourcePath))
                {
                    e.Handled = true;
                    return;
                }
                break; // Found a node with SourcePath but no matching entry; don't keep walking
            }
        }

        // Fallback: copy clicked text to clipboard
        if (e.Source is Avalonia.Controls.TextBlock textBlock && !string.IsNullOrEmpty(textBlock.Text))
        {
            vm.CopyTextCommand.Execute(textBlock.Text);
            e.Handled = true;
        }
    }

    private void OnSearchRowTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Avalonia.Controls.Control control || control.DataContext is not SearchableEntry entry)
            return;
        if (DataContext is MainWindowViewModel vm)
            vm.SelectSearchEntryCommand.Execute(entry);
    }

    private void OnSearchRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Avalonia.Controls.Control control || control.DataContext is not SearchableEntry entry)
            return;
        if (DataContext is MainWindowViewModel vm)
            vm.ShowRequestDetailsCommand.Execute(entry);
    }

    private void OpenChatWindow(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel mainVm)
            return;
        if (_chatWindow != null)
        {
            _chatWindow.Activate();
            return;
        }
        var agentService = new Services.OllamaLogAgentService();
        var configModel = AgentConfigIo.GetModelFromConfig();
        var chatVm = new ChatWindowViewModel(agentService, mainVm.GetLogContextForAgent, configModel, model => AgentConfigIo.SetModelInConfig(model));
        mainVm.SetContextConsumer(s => chatVm.NotifyContextChanged(s));
        _chatWindow = new ChatWindow { DataContext = chatVm };
        _chatWindow.Closed += (_, _) =>
        {
            mainVm.SetContextConsumer(null);
            _chatWindow = null;
        };
        _chatWindow.Show();
        chatVm.NotifyContextChanged(mainVm.GetLogContextForAgent());
    }
}
