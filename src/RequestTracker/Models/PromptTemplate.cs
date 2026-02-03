namespace RequestTracker.Models;

/// <summary>Defines a prompt template with a display name and template text (context is sent to the model with each query, not inserted into the template).</summary>
public sealed class PromptTemplate
{
    public string Name { get; set; } = "";
    public string Template { get; set; } = "";
}
