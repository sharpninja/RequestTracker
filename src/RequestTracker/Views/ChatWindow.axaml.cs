using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RequestTracker.Models;

namespace RequestTracker.Views;

public partial class ChatWindow : Window
{
    public ChatWindow()
    {
        InitializeComponent();
        // Build row definitions from saved settings so row 1 height is correct before any layout.
        InitializeGridRows();
        ApplyLayoutSettings();
        Opened += OnOpened;
        Closing += OnClosing;
        Loaded += OnLoaded;
    }

    private void InitializeGridRows()
    {
        if (ChatMainGrid == null) return;
        var s = LayoutSettingsIo.Load();
        GridLength row1Length = s?.ChatTemplatePickerRowHeight != null
            ? s.ChatTemplatePickerRowHeight.ToGridLength()
            : new GridLength(1, GridUnitType.Star);
        ChatMainGrid.RowDefinitions.Clear();
        ChatMainGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));      // 0: toolbar
        ChatMainGrid.RowDefinitions.Add(new RowDefinition(row1Length));         // 1: template picker
        ChatMainGrid.RowDefinitions.Add(new RowDefinition(new GridLength(4)));   // 2: splitter
        ChatMainGrid.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star)); // 3: messages
        ChatMainGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));      // 4: input
    }

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ChatTemplateSplitter != null)
        {
            ChatTemplateSplitter.PointerCaptureLost += OnChatSplitterPointerCaptureLost;
        }
        // Sync row height with initial expander state.
        if (PromptTemplatesExpander != null)
        {
            if (PromptTemplatesExpander.IsExpanded)
                ApplyTemplatePickerSplitterOnly();
            else
                SetTemplatePickerRowToAuto();
        }
        else
        {
            Dispatcher.UIThread.Post(() => ApplyTemplatePickerSplitterOnly(), DispatcherPriority.Loaded);
        }
    }

    private void OnPromptTemplatesCollapsed(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SetTemplatePickerRowToAuto();
    }

    private void OnPromptTemplatesExpanded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ApplyTemplatePickerSplitterSettings(LayoutSettingsIo.Load());
    }

    /// <summary>Minimum height for the template-picker row when collapsed so the expander header isn't clipped by the splitter.</summary>
    private const double TemplatePickerRowMinHeightCollapsed = 40;

    private void SetTemplatePickerRowToAuto()
    {
        try
        {
            if (ChatMainGrid?.RowDefinitions == null || ChatMainGrid.RowDefinitions.Count < 5)
                return;
            var row = new RowDefinition(GridLength.Auto);
            row.MinHeight = TemplatePickerRowMinHeightCollapsed;
            ChatMainGrid.RowDefinitions[1] = row;
            ChatMainGrid.InvalidateMeasure();
            ChatMainGrid.InvalidateArrange();
        }
        catch { }
    }

    private void OnChatSplitterPointerReleased(object? sender, PointerReleasedEventArgs e) => SaveLayoutSettings();

    private void OnChatSplitterPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => SaveLayoutSettings();

    private void OnOpened(object? sender, EventArgs e)
    {
        ApplyLayoutSettings();
        if (DataContext is ViewModels.ChatWindowViewModel vm)
        {
            _ = vm.LoadModelsAsync();
            vm.LoadPrompts();
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is ViewModels.ChatWindowViewModel vm)
            vm.CancelSend();
        SaveLayoutSettings();
    }

    private void ApplyLayoutSettings()
    {
        try
        {
            var s = LayoutSettingsIo.Load();
            if (s == null) return;
            if (s.ChatWindowWidth >= 100 && s.ChatWindowHeight >= 100)
            {
                Width = s.ChatWindowWidth;
                Height = s.ChatWindowHeight;
            }
            if (s.ChatWindowX != 0 || s.ChatWindowY != 0)
                Position = new PixelPoint((int)s.ChatWindowX, (int)s.ChatWindowY);
            ApplyTemplatePickerSplitterSettings(s);
        }
        catch { }
    }

    private void ApplyTemplatePickerSplitterSettings(LayoutSettings? s)
    {
        try
        {
            if (s == null || ChatMainGrid?.RowDefinitions == null || ChatMainGrid.RowDefinitions.Count < 5)
                return;
            var length = s.ChatTemplatePickerRowHeight.ToGridLength();
            ChatMainGrid.RowDefinitions[1] = new RowDefinition(length);
            ChatMainGrid.InvalidateMeasure();
            ChatMainGrid.InvalidateArrange();
        }
        catch { }
    }

    private void ApplyTemplatePickerSplitterOnly()
    {
        try
        {
            var s = LayoutSettingsIo.Load();
            if (s != null)
                ApplyTemplatePickerSplitterSettings(s);
        }
        catch { }
    }

    private void SaveLayoutSettings()
    {
        try
        {
            var s = LayoutSettingsIo.Load() ?? new LayoutSettings();
            s.ChatWindowWidth = Width;
            s.ChatWindowHeight = Height;
            s.ChatWindowX = Position.X;
            s.ChatWindowY = Position.Y;
            SaveTemplatePickerSplitterSettings(s);
            LayoutSettingsIo.Save(s);
        }
        catch { }
    }

    private void SaveTemplatePickerSplitterSettings(LayoutSettings s)
    {
        try
        {
            if (ChatMainGrid?.RowDefinitions == null || ChatMainGrid.RowDefinitions.Count < 5)
                return;
            // Only persist row height when expander is expanded (so we don't save Auto).
            if (PromptTemplatesExpander != null && !PromptTemplatesExpander.IsExpanded)
                return;
            var current = ChatMainGrid.RowDefinitions[1].Height;
            s.ChatTemplatePickerRowHeight = GridLengthDto.FromGridLength(current);
        }
        catch { }
    }

    private static PromptTemplate? GetPromptFromSource(object? source)
    {
        if (source is not Control c) return null;
        if (c.DataContext is PromptTemplate p) return p;
        var parent = c.FindAncestorOfType<ListBoxItem>();
        return parent?.DataContext as PromptTemplate;
    }

    private void OnPromptListTapped(object? sender, TappedEventArgs e)
    {
        var prompt = GetPromptFromSource(e.Source);
        if (prompt != null && DataContext is ViewModels.ChatWindowViewModel vm)
            vm.PopulatePromptCommand.Execute(prompt);
    }

    private async void OnPromptListDoubleTapped(object? sender, TappedEventArgs e)
    {
        var prompt = GetPromptFromSource(e.Source);
        if (prompt != null && DataContext is ViewModels.ChatWindowViewModel vm)
            await vm.SubmitPromptCommand.ExecuteAsync(prompt);
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        // Send on Enter (without Shift). Shift+Enter could be used for newline if we want later.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;
        e.Handled = true;
        if (DataContext is ViewModels.ChatWindowViewModel vm && vm.SendCommand.CanExecute(null))
            vm.SendCommand.Execute(null);
    }
}
