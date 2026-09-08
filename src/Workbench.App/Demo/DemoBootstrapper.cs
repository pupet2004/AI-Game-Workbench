using System.Text.Json;
using Workbench.App.Continuity;
using Workbench.App.ProjectWorld;
using Workbench.App.Services;
using Workbench.Core.Continuity;
using Workbench.Project.Opening;

namespace Workbench.App.Demo;

public static class DemoBootstrapper
{
    public static async Task RelocateProjectAsync(
        string projectRoot,
        string databasePath,
        string projectName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);

        await using var services = AppServices.CreateForDatabasePath(databasePath);
        await services.InitializeAsync(cancellationToken);
        var project = (await services.ProjectRepository.GetRecentAsync(100, cancellationToken))
            .FirstOrDefault(value => string.Equals(value.Name, projectName, StringComparison.Ordinal));
        if (project is null)
        {
            throw new InvalidOperationException($"Demo baseline project '{projectName}' was not found.");
        }

        await services.ProjectRepository.UpsertAsync(
            project with
            {
                RootPath = Path.GetFullPath(projectRoot),
                LastOpenedAt = services.TimeProvider.GetUtcNow()
            },
            cancellationToken);
    }

    public static async Task RunAsync(string projectRoot, string databasePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        Directory.CreateDirectory(projectRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        await using var services = AppServices.CreateForDatabasePath(databasePath);
        await services.InitializeAsync(cancellationToken);
        var opened = await services.ProjectOpenService.OpenAsync(projectRoot, cancellationToken);
        var projectRef = new ProjectRef(opened.Project.Id);
        var principal = new UserPrincipalRef("demo:user");

        if (await services.B1ProjectGovernance.GetAsync(projectRef, cancellationToken) is null)
        {
            await services.B1ProjectGovernance.CreateGovernedProjectForExistingProjectAsync(projectRef, principal, cancellationToken);
        }

        var state = await services.B1AuthorityRepository.LoadProjectStateAsync(projectRef, cancellationToken);
        if (state.Responsibilities.Count == 0)
        {
            await services.ProjectWorldInitialization.CommitAsync(
                new ProjectWorldInitializationRequest(
                    projectRef,
                    principal,
                    RoleKind.Worker,
                    "Maintain the Zero Hour continuity project.",
                    "Every accepted change remains recoverable with its source and handoff history.",
                    "Produce one bounded artifact and return a durable completion report."),
                cancellationToken);
        }

        var manifest = new
        {
            schema = "workbench.core-continuity-demo/v1",
            projectId = opened.Project.Id,
            projectName = opened.Project.Name,
            projectRoot = Path.GetFullPath(projectRoot),
            databasePath = Path.GetFullPath(databasePath),
            bootstrapPrincipal = principal.Value,
            createdAt = DateTimeOffset.UtcNow
        };
        await File.WriteAllTextAsync(
            Path.Combine(projectRoot, ".workbench-demo.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);
    }
}
