namespace Workbench.Storage.Leaders;

public sealed record StoredLeaderSessionEpoch(
    Guid Id,
    Guid ProjectId,
    string ProviderId,
    Guid ProviderAccountId,
    string ModelId,
    Guid AgentSessionId,
    string? ExternalSessionId,
    string? WorkingDirectory,
    DateTimeOffset StartedAt,
    DateTimeOffset LastActiveAt,
    DateTimeOffset? EndedAt,
    string? RolloverReason,
    string? HandoffSummary,
    DateTimeOffset? BootContextDeliveredAt = null);
