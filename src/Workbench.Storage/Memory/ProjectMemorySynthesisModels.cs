namespace Workbench.Storage.Memory;

public enum ProjectMemorySynthesisJobStatus
{
    Pending,
    Running,
    Completed
}

public sealed record ProjectMemorySynthesisJob(
    Guid EpochId,
    Guid ProjectId,
    ProjectMemorySynthesisJobStatus Status,
    int AttemptCount,
    DateTimeOffset? LastAttemptedAt,
    DateTimeOffset? CompletedAt,
    string? LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ProjectMemorySynthesisStatus(int PendingCount, int RunningCount);

public sealed record ProjectMemorySynthesisItem(
    string Topic,
    string Content,
    IReadOnlyList<long> SourceMessageIds);

public sealed record ProjectMemorySynthesisApplication(
    Guid EpochId,
    Guid ProjectId,
    IReadOnlyList<ProjectMemorySynthesisItem> Learned,
    IReadOnlyList<ProjectMemorySynthesisItem> Candidates,
    DateTimeOffset CompletedAt);
