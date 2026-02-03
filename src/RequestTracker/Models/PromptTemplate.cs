namespace RequestTracker.Models;

/// <summary>Defines a prompt template with a display name and Handlebars template body (e.g. uses {{context}}).</summary>
public sealed class PromptTemplate
{
    public string Name { get; set; } = "";
    public string Template { get; set; } = "";
}
