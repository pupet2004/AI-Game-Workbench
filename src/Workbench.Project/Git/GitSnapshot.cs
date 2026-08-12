namespace Workbench.Project.Git;

public sealed record GitSnapshot(
    bool GitInstalled,
    bool IsRepository,
    string? RepositoryRoot,
    string? HeadCommit,
    string? BranchName,
    bool IsDetachedHead,
    bool IsDirty,
    string? Error);
