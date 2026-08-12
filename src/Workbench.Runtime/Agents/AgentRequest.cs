namespace Workbench.Runtime.Agents;

public sealed record AgentRequest
{
    public AgentRequest(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        Text = text;
    }

    public string Text { get; }
}
