using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RequestTracker.Models;
using RequestTracker.Services;

namespace RequestTracker.ViewModels;

public partial class ChatWindowViewModel : ViewModelBase
{
    private readonly ILogAgentService _agentService;
    private readonly Func<string> _getContext;
    private readonly Action<string?>? _onModelChanged;
    private readonly string? _initialModelFromConfig;
    private CancellationTokenSource? _sendCts;

    [ObservableProperty]
    private ObservableCollection<ChatMessage> _messages = new();

    [ObservableProperty]
    private string _currentInput = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private ObservableCollection<string> _availableModels = new();

    [ObservableProperty]
    private string? _selectedModel;

    [ObservableProperty]
    private ObservableCollection<PromptTemplate> _promptTemplates = new();

    public ChatWindowViewModel(ILogAgentService agentService, Func<string> getContext, string? initialModelFromConfig = null, Action<string?>? onModelChanged = null)
    {
        _agentService = agentService ?? throw new ArgumentNullException(nameof(agentService));
        _getContext = getContext ?? (() => "");
        _initialModelFromConfig = initialModelFromConfig;
        _onModelChanged = onModelChanged;
    }

    /// <summary>Parameterless constructor for design-time only.</summary>
    public ChatWindowViewModel() : this(new OllamaLogAgentService(), () => "", null, null) { }

    [RelayCommand]
    private void OpenAgentConfig()
    {
        AgentConfigIo.EnsureExists();
        OpenFileInDefaultEditor(AgentConfigIo.GetFilePath(), "config");
    }

    [RelayCommand]
    private void OpenPromptTemplates()
    {
        PromptTemplatesIo.EnsureExists();
        OpenFileInDefaultEditor(PromptTemplatesIo.GetFilePath(), "prompts");
    }

    private static void OpenFileInDefaultEditor(string path, string label)
    {
        if (!File.Exists(path))
            return;
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c start \"\" \"{fullPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    Arguments = fullPath,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
        }
        catch
        {
            // Ignore
        }
    }

    /// <summary>Loads prompt templates from agent config. Call when the chat window is opened.</summary>
    public void LoadPrompts()
    {
        var list = PromptTemplatesIo.GetPrompts();
        DispatchToUi(() =>
        {
            PromptTemplates.Clear();
            foreach (var p in list)
                PromptTemplates.Add(p);
        });
    }

    /// <summary>Returns the prompt template text as-is. Context is sent to the model separately with each query, not inserted into templates.</summary>
    private static string GetPromptText(PromptTemplate prompt)
    {
        if (prompt == null || string.IsNullOrEmpty(prompt.Template)) return "";
        return prompt.Template.Trim();
    }

    [RelayCommand]
    private void PopulatePrompt(PromptTemplate? prompt)
    {
        if (prompt == null) return;
        CurrentInput = GetPromptText(prompt);
    }

    [RelayCommand]
    private async Task SubmitPromptAsync(PromptTemplate? prompt)
    {
        if (prompt == null) return;
        CurrentInput = GetPromptText(prompt);
        if (string.IsNullOrWhiteSpace(CurrentInput)) return;
        await SendAsync().ConfigureAwait(false);
    }

    partial void OnSelectedModelChanged(string? value)
    {
        _onModelChanged?.Invoke(value);
    }

    /// <summary>Loads available Ollama models and sets SelectedModel to the first or default. Call when the chat window is opened.</summary>
    public async Task LoadModelsAsync()
    {
        try
        {
            var models = await OllamaLogAgentService.GetAvailableModelsAsync(null, CancellationToken.None).ConfigureAwait(false);
            DispatchToUi(() =>
            {
                AvailableModels.Clear();
                if (models.Length == 0)
                {
                    AvailableModels.Add("(No models - start Ollama)");
                    SelectedModel = null;
                }
                else
                {
                    foreach (var m in models) AvailableModels.Add(m);
                    var preferred = !string.IsNullOrEmpty(_initialModelFromConfig) && AvailableModels.Contains(_initialModelFromConfig)
                        ? _initialModelFromConfig
                        : (models.Length > 0 ? models[0] : null);
                    SelectedModel = preferred;
                }
            });
        }
        catch
        {
            DispatchToUi(() =>
            {
                AvailableModels.Clear();
                AvailableModels.Add("(Ollama not reachable)");
                SelectedModel = null;
            });
        }
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var text = (CurrentInput ?? "").Trim();
        if (string.IsNullOrEmpty(text)) return;

        CurrentInput = "";
        var userMsg = new ChatMessage { Role = "user", Text = text };
        Messages.Add(userMsg);
        var context = _getContext();
        IsLoading = true;
        SendCommand.NotifyCanExecuteChanged();

        _sendCts?.Cancel();
        _sendCts = new CancellationTokenSource();
        var token = _sendCts.Token;

        var assistantMsg = new ChatMessage { Role = "assistant", Text = "" };
        DispatchToUi(() => Messages.Add(assistantMsg));

        IProgress<string>? progress = null;
        progress = new Progress<string>(content =>
        {
            DispatchToUi(() =>
            {
                if (!token.IsCancellationRequested && Messages.Contains(assistantMsg))
                    assistantMsg.Text = content ?? "";
            });
        });

        try
        {
            var model = SelectedModel;
            var reply = await Task.Run(() => _agentService.SendMessageAsync(text, context, model, progress, token), token).ConfigureAwait(false);

            if (token.IsCancellationRequested) return;

            DispatchToUi(() =>
            {
                if (token.IsCancellationRequested) return;
                assistantMsg.Text = reply ?? "";
                IsLoading = false;
                SendCommand.NotifyCanExecuteChanged();
            });
        }
        catch (OperationCanceledException)
        {
            DispatchToUi(() =>
            {
                assistantMsg.Text = "[Cancelled]";
                IsLoading = false;
                SendCommand.NotifyCanExecuteChanged();
            });
        }
        catch (Exception ex)
        {
            DispatchToUi(() =>
            {
                assistantMsg.Text = "Error: " + ex.Message;
                IsLoading = false;
                SendCommand.NotifyCanExecuteChanged();
            });
        }
    }

    private bool CanSend() => !IsLoading;

    private static void DispatchToUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(() => action());
    }

    /// <summary>Cancels any in-flight send (e.g. when closing the window).</summary>
    public void CancelSend()
    {
        _sendCts?.Cancel();
    }

    /// <summary>Called when the user navigates; context is kept in sync and sent with the next user message (no chat message added to avoid spam).</summary>
    public void NotifyContextChanged(string fullContext)
    {
        // Context is retrieved via _getContext() when the user sends a message, so we do not add a visible message here.
    }
}
