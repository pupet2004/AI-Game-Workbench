namespace Workbench.Runtime.Agents;

public sealed record AgentRequest
{
    public AgentRequest(string text, string? outputSchema = null, IReadOnlyList<AgentInputPart>? inputs = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        Text = text;
        OutputSchema = outputSchema;
        Inputs = inputs ?? [];
    }

    public string Text { get; }
    public string? OutputSchema { get; }

    public IReadOnlyList<AgentInputPart> Inputs { get; }
}

public sealed record AgentInputPart(string Type, string Value, string? Name = null)
{
    public bool IsImage => string.Equals(Type, "image", StringComparison.OrdinalIgnoreCase);

    public bool IsFile => string.Equals(Type, "file", StringComparison.OrdinalIgnoreCase);
}
