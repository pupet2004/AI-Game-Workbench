namespace Workbench.Core.Projects;

public sealed record Project(
    Guid Id,
    string Name,
    string RootPath,
    ProjectType Type,
    string? GitRoot,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastOpenedAt);
