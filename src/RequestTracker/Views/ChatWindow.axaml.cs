using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using RequestTracker.Models;

namespace RequestTracker.Views;

public partial class ChatWindow : Window
{
    public ChatWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closing += OnClosing;
    }

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
        catch { /* ignore */ }
    }

    private void ApplyTemplatePickerSplitterSettings(LayoutSettings? s)
    {
        try
        {
            if (s == null || ChatMainGrid?.RowDefinitions == null || ChatMainGrid.RowDefinitions.Count < 5)
                return;
            ChatMainGrid.RowDefinitions[1].Height = s.ChatTemplatePickerRowHeight.ToGridLength();
        }
        catch { /* ignore */ }
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
        catch { /* ignore */ }
    }

    private void SaveTemplatePickerSplitterSettings(LayoutSettings s)
    {
        try
        {
            if (ChatMainGrid?.RowDefinitions == null || ChatMainGrid.RowDefinitions.Count < 5)
                return;
            s.ChatTemplatePickerRowHeight = GridLengthDto.FromGridLength(ChatMainGrid.RowDefinitions[1].Height);
        }
        catch { /* ignore */ }
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
