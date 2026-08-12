namespace Workbench.Project.Git;

public interface IGitInspector
{
    Task<GitSnapshot> InspectAsync(string projectPath, CancellationToken cancellationToken = default);
}
