using Workbench.Runtime.Providers;

namespace Workbench.Runtime.Agents;

public sealed record AgentSession
{
    public AgentSession(
        AgentSessionId id,
        ProviderAccountId accountId,
        ProviderId providerId,
        string modelId,
        string? workingDirectory,
        string? externalSessionId,
        AgentSessionStatus status,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        if (workingDirectory is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        }

        Id = id;
        AccountId = accountId;
        ProviderId = providerId;
        ModelId = modelId;
        WorkingDirectory = workingDirectory;
        ExternalSessionId = externalSessionId;
        Status = status;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public AgentSessionId Id { get; init; }

    public ProviderAccountId AccountId { get; init; }

    public ProviderId ProviderId { get; init; }

    public string ModelId { get; init; }

    public string? WorkingDirectory { get; init; }

    public string? ExternalSessionId { get; init; }

    public AgentSessionStatus Status { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}
