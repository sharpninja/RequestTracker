using System.Threading;
using System.Threading.Tasks;

namespace RequestTracker.Services;

/// <summary>Service that sends a user message and current log context to a local/remote LLM and returns the assistant reply.</summary>
public interface ILogAgentService
{
    /// <summary>Sends the user message with the given context to the agent and returns the assistant's reply. Model is optional (e.g. Ollama model name).</summary>
    Task<string> SendMessageAsync(string userMessage, string contextSummary, string? model = null, CancellationToken cancellationToken = default);
}
