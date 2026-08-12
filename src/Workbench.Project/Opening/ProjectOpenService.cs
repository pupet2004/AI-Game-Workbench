using Workbench.Core.Layout;
using Workbench.Core.Projects;
using Workbench.Project.Detection;
using Workbench.Project.Git;
using Workbench.Storage.Projects;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.Project.Opening;

public sealed class ProjectOpenService
{
    private readonly ProjectRepository _projectRepository;
    private readonly ProjectLayoutRepository _layoutRepository;
    private readonly IGitInspector _gitInspector;
    private readonly TimeProvider _timeProvider;
    private readonly ProjectDetector _projectDetector = new();

    public ProjectOpenService(
        ProjectRepository projectRepository,
        ProjectLayoutRepository layoutRepository,
        IGitInspector gitInspector,
        TimeProvider? timeProvider = null)
    {
        _projectRepository = projectRepository ?? throw new ArgumentNullException(nameof(projectRepository));
        _layoutRepository = layoutRepository ?? throw new ArgumentNullException(nameof(layoutRepository));
        _gitInspector = gitInspector ?? throw new ArgumentNullException(nameof(gitInspector));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ProjectOpenResult> OpenAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        var normalizedPath = NormalizeDirectoryPath(folderPath);
        if (!Directory.Exists(normalizedPath))
        {
            throw new DirectoryNotFoundException($"Project directory does not exist: {normalizedPath}");
        }

        var detection = _projectDetector.Detect(normalizedPath);
        var git = await _gitInspector.InspectAsync(normalizedPath, cancellationToken);
        var now = _timeProvider.GetUtcNow();
        var existingProject = await _projectRepository.GetByRootPathAsync(normalizedPath, cancellationToken);
        var project = existingProject is null
            ? new CoreProject(Guid.NewGuid(), detection.SuggestedName, normalizedPath, detection.ProjectType, git.RepositoryRoot, now, now)
            : existingProject with
            {
                Name = detection.SuggestedName,
                Type = detection.ProjectType,
                GitRoot = git.RepositoryRoot,
                LastOpenedAt = now
            };

        await _projectRepository.UpsertAsync(project, cancellationToken);

        var layout = await _layoutRepository.GetAsync(project.Id, cancellationToken);
        if (layout is null)
        {
            layout = ProjectLayout.CreateDefault(project.Id);
            await _layoutRepository.SaveAsync(layout, cancellationToken);
        }

        return new ProjectOpenResult(project, layout, git);
    }

    private static string NormalizeDirectoryPath(string folderPath)
    {
        var fullPath = Path.GetFullPath(folderPath);
        var root = Path.GetPathRoot(fullPath);
        return string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
