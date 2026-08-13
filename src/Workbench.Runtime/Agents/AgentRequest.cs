namespace Workbench.Runtime.Agents;

public sealed record AgentRequest
{
    public AgentRequest(string text, string? outputSchema = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        Text = text;
        OutputSchema = outputSchema;
    }

    public string Text { get; }
    public string? OutputSchema { get; }
}
