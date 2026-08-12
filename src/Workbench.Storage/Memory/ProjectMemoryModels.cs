namespace Workbench.Storage.Memory;

public sealed record ProjectActivityEvent(Guid Id, Guid ProjectId, string EventType, string Summary, string SourceType, string? SourceRef, DateTimeOffset OccurredAt, DateTimeOffset CreatedAt);
public sealed record ProjectMemoryItem(Guid Id, Guid ProjectId, string Layer, string Topic, string Content, string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? CertifiedAt);
public sealed record ProjectMemorySource(string SourceType, string SourceRef);
