namespace Workbench.Storage.Leaders;

public sealed record StoredProjectLeader(
    Guid ProjectId,
    Guid? CurrentEpochId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
