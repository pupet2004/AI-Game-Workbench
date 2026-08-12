using Workbench.Runtime.Providers;

namespace Workbench.Runtime.Agents;

public sealed record AgentSession(
    AgentSessionId Id,
    ProviderAccountId AccountId,
    ProviderId ProviderId,
    string ModelId,
    string? ExternalSessionId,
    AgentSessionStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
