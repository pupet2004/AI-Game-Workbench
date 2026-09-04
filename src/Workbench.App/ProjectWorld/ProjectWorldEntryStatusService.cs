using Workbench.App.Continuity;
using Workbench.App.Services;
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
            return new(projectRef, ProjectWorldEntryKind.PathUnavailable, false, LocalizationService.Current["Status.PathUnavailable"]);
        }

        var facts = await _governanceRepository.GetEntryFactsAsync(projectRef, cancellationToken);
        if (!facts.ProjectExists)
        {
            return new(projectRef, ProjectWorldEntryKind.UnmanagedProjectUnavailable, false,
                LocalizationService.Current["Status.NotRegistered"]);
        }

        if (facts.GovernanceExists)
        {
            try
            {
                var state = await _projectionService.GetProjectStateAsync(projectRef, cancellationToken);
                if (IsGovernanceOnly(state))
                {
                    return new(projectRef, ProjectWorldEntryKind.ProjectWorldSetupIncomplete,
                        facts.LegacyOriginExists, LocalizationService.Current["Status.SetupIncomplete"]);
                }

                if (!TryGetBootstrapLineage(state, out var lineage))
                {
                    return new(projectRef, ProjectWorldEntryKind.BootstrapRecoveryRequired,
                        facts.LegacyOriginExists, LocalizationService.Current["Status.RecoveryRequired"]);
                }

                // Bootstrap readiness is a historical invariant.  The initial
                // revision may later be disposed or superseded as part of the
                // normal project lifecycle, so validate the raw lineage rather
                // than requiring those records to remain current.
                _ = B1Projector.Build(state);
                if (!HasValidBootstrapLineage(state, lineage))
                {
                    return new(projectRef, ProjectWorldEntryKind.BootstrapRecoveryRequired,
                        facts.LegacyOriginExists, LocalizationService.Current["Status.RecoveryRequired"]);
                }

                return new(projectRef, ProjectWorldEntryKind.ProjectWorldReady, facts.LegacyOriginExists,
                    LocalizationService.Current["Status.Ready"]);
            }
            catch (InvalidDataException)
            {
                return new(projectRef, ProjectWorldEntryKind.CorruptProjectUnavailable,
                    facts.LegacyOriginExists, LocalizationService.Current["Status.Inconsistent"]);
            }
        }

        if (facts.LegacyOriginExists && !facts.B1HistoryExists)
        {
            return new(projectRef, ProjectWorldEntryKind.LegacySetupRequired, true,
                LocalizationService.Current["Status.LegacySetup"]);
        }

        if (!facts.LegacyOriginExists && !facts.B1HistoryExists)
        {
            if (await _governanceRepository.HasLegacyWorkspaceDataAsync(projectRef, cancellationToken))
            {
                return new(projectRef, ProjectWorldEntryKind.LegacyWorkspaceReady, false,
                    LocalizationService.Current["Status.WorkspaceAvailable"]);
            }

            return new(projectRef, ProjectWorldEntryKind.UnmanagedProjectUnavailable, false,
                LocalizationService.Current["Status.SetupRequired"]);
        }

        return new(projectRef, ProjectWorldEntryKind.CorruptProjectUnavailable,
            facts.LegacyOriginExists, LocalizationService.Current["Status.Inconsistent"]);
    }

    private static bool IsGovernanceOnly(B1ProjectState state) =>
        state.AuthorityDecisions.Count == 0 &&
        state.LogicalActors.Count == 0 &&
        state.Responsibilities.Count == 0 &&
        state.Assignments.Count == 0 &&
        state.Revisions.Count == 0;

    private static bool TryGetBootstrapLineage(B1ProjectState state, out BootstrapLineage lineage)
    {
        foreach (var decision in state.AuthorityDecisions.OrderBy(value => value.ProjectCommitSequence))
        {
            var actor = decision.LogicalActorEstablishmentEffect?.LogicalActor;
            var responsibility = decision.ResponsibilityEstablishmentEffect?.Responsibility;
            var delegation = decision.AssignmentDelegationEffect;
            if (actor is null || responsibility is null || delegation is null ||
                delegation.ReplacesAssignmentRef is not null)
            {
                continue;
            }

            var assignment = delegation.Assignment;
            var revision = delegation.InitialRevision;
            if (actor.ProjectRef != state.Governance.ProjectRef ||
                responsibility.ProjectRef != state.Governance.ProjectRef ||
                assignment.ResponsibilityRef != responsibility.ResponsibilityRef ||
                assignment.AssigneeActorRef != actor.LogicalActorRef ||
                assignment.InitialRevisionRef != revision.RevisionRef ||
                revision.AssignmentRef != assignment.AssignmentRef ||
                actor.AuthorizedByDecisionRef != decision.DecisionRef ||
                responsibility.AuthorizedByDecisionRef != decision.DecisionRef ||
                assignment.AuthorizedByDecisionRef != decision.DecisionRef ||
                revision.AuthorizedByDecisionRef != decision.DecisionRef ||
                revision.PriorRevisionRef is not null)
            {
                continue;
            }

            lineage = new(decision.DecisionRef, actor.LogicalActorRef, responsibility.ResponsibilityRef,
                assignment.AssignmentRef, revision.RevisionRef);
            return true;
        }

        lineage = default;
        return false;
    }

    private static bool HasValidBootstrapLineage(B1ProjectState state, BootstrapLineage lineage)
    {
        var decision = state.AuthorityDecisions.SingleOrDefault(value => value.DecisionRef == lineage.DecisionRef);
        if (decision is null)
        {
            return false;
        }

        var actor = decision.LogicalActorEstablishmentEffect?.LogicalActor;
        var responsibility = decision.ResponsibilityEstablishmentEffect?.Responsibility;
        var delegation = decision.AssignmentDelegationEffect;
        if (actor is null || responsibility is null || delegation is null ||
            delegation.ReplacesAssignmentRef is not null)
        {
            return false;
        }

        var assignment = delegation.Assignment;
        var revision = delegation.InitialRevision;
        return decision.ProjectRef == state.Governance.ProjectRef &&
            actor.LogicalActorRef == lineage.ActorRef &&
            responsibility.ResponsibilityRef == lineage.ResponsibilityRef &&
            assignment.AssignmentRef == lineage.AssignmentRef &&
            revision.RevisionRef == lineage.RevisionRef &&
            actor.ProjectRef == state.Governance.ProjectRef &&
            responsibility.ProjectRef == state.Governance.ProjectRef &&
            assignment.ResponsibilityRef == responsibility.ResponsibilityRef &&
            assignment.AssigneeActorRef == actor.LogicalActorRef &&
            assignment.InitialRevisionRef == revision.RevisionRef &&
            revision.AssignmentRef == assignment.AssignmentRef &&
            actor.AuthorizedByDecisionRef == decision.DecisionRef &&
            responsibility.AuthorizedByDecisionRef == decision.DecisionRef &&
            assignment.AuthorizedByDecisionRef == decision.DecisionRef &&
            revision.AuthorizedByDecisionRef == decision.DecisionRef &&
            revision.PriorRevisionRef is null;
    }

    private readonly record struct BootstrapLineage(
        AuthorityDecisionRef DecisionRef,
        LogicalActorRef ActorRef,
        ResponsibilityRef ResponsibilityRef,
        AssignmentRef AssignmentRef,
        RevisionRef RevisionRef);
}
