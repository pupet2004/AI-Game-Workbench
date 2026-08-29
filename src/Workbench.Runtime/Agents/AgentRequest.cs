namespace Workbench.Runtime.Agents;

public sealed record AgentRequest
{
    public AgentRequestSource Source { get; }

    public AgentRequest(
        string text,
        string? outputSchema = null,
        IReadOnlyList<AgentInputPart>? inputs = null,
        AgentAccessMode accessMode = AgentAccessMode.Restricted,
        AgentRequestSource source = AgentRequestSource.Workbench)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        Text = text;
        OutputSchema = outputSchema;
        Inputs = inputs ?? [];
        AccessMode = accessMode;
        Source = source;
    }

    public string Text { get; }
    public string? OutputSchema { get; }

    public IReadOnlyList<AgentInputPart> Inputs { get; }

    public AgentAccessMode AccessMode { get; }
}

public enum AgentRequestSource
{
    User,
    Leader,
    Workbench
}

public sealed record AgentInputPart(string Type, string Value, string? Name = null)
{
    public bool IsImage => string.Equals(Type, "image", StringComparison.OrdinalIgnoreCase);

    public bool IsFile => string.Equals(Type, "file", StringComparison.OrdinalIgnoreCase);
}
