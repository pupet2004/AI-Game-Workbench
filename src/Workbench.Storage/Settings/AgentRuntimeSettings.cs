namespace Workbench.Storage.Settings;

public sealed record AgentRuntimeSettings(string AgentId, bool IsEnabled, string? ExecutablePath)
{
    public string? NormalizedExecutablePath => string.IsNullOrWhiteSpace(ExecutablePath)
        ? null
        : ExecutablePath.Trim();
}
