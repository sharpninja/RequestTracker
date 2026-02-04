using CommunityToolkit.Mvvm.ComponentModel;

namespace RequestTracker.Models;

/// <summary>Single message in the AI chat (user or assistant).</summary>
public partial class ChatMessage : ObservableObject
{
    [ObservableProperty]
    private string _role = ""; // "user" or "assistant"

    [ObservableProperty]
    private string _text = "";
}
