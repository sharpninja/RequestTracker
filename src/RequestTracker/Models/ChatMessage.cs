namespace RequestTracker.Models;

/// <summary>Single message in the AI chat (user or assistant).</summary>
public class ChatMessage
{
    public string Role { get; set; } = ""; // "user" or "assistant"
    public string Text { get; set; } = "";
}
