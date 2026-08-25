using Workbench.App.Continuity;
using Workbench.Core.Continuity;
using Workbench.Core.Projects;
using Workbench.Storage.Continuity;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.ProjectWorld;

public sealed class ProjectWorldEntryStatusService(
    B1ProjectGovernanceRepository governanceRepository,
    B1ProjectionService projectionService)
{
    private readonly B1ProjectGovernanceRepository _governanceRepository =
        governanceRepository ?? throw new ArgumentNullException(nameof(governanceRepository));
    private readonly B1ProjectionService _projectionService =
        projectionService ?? throw new ArgumentNullException(nameof(projectionService));

    public async Task<ProjectWorldEntryStatus> GetStatusAsync(
        CoreProject project,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        var projectRef = new ProjectRef(project.Id);
        if (!Directory.Exists(project.RootPath))
        {
            return new(projectRef, ProjectWorldEntryKind.PathUnavailable, false, "Unavailable");
        }

        var facts = await _governanceRepository.GetEntryFactsAsync(projectRef, cancellationToken);
        if (!facts.ProjectExists)
        {
            return new(projectRef, ProjectWorldEntryKind.UnmanagedProjectUnavailable, false,
                "Project is not registered in Workbench");
        }

        if (facts.GovernanceExists)
        {
            try
            {
                await _projectionService.GetProjectProjectionAsync(projectRef, cancellationToken);
                return new(projectRef, ProjectWorldEntryKind.ProjectWorldReady, facts.LegacyOriginExists,
                    "Project World ready");
            }
            catch (InvalidDataException)
            {
                return new(projectRef, ProjectWorldEntryKind.CorruptProjectUnavailable,
                    facts.LegacyOriginExists, "Project World unavailable · inconsistent state");
            }
        }

        if (facts.LegacyOriginExists && !facts.B1HistoryExists)
        {
            return new(projectRef, ProjectWorldEntryKind.LegacySetupRequired, true,
                "Setup required · Legacy data available");
        }

        if (!facts.LegacyOriginExists && !facts.B1HistoryExists)
        {
            if (await _governanceRepository.HasLegacyWorkspaceDataAsync(projectRef, cancellationToken))
            {
                return new(projectRef, ProjectWorldEntryKind.LegacyWorkspaceReady, false,
                    "Existing project workspace available");
            }

            return new(projectRef, ProjectWorldEntryKind.UnmanagedProjectUnavailable, false,
                "Project setup required");
        }

        return new(projectRef, ProjectWorldEntryKind.CorruptProjectUnavailable,
            facts.LegacyOriginExists, "Project World unavailable · inconsistent state");
    }
}
