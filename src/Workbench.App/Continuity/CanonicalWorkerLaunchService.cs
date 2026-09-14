using Workbench.Core.Continuity;
using Workbench.Core.Tasks;
using Workbench.Storage.Continuity;

namespace Workbench.App.Continuity;

public sealed record CanonicalWorkerLaunchContext(
    ProjectRef ProjectRef,
    UserPrincipalRef OperatorRef,
    AssignmentRef AssignmentRef,
    RevisionRef AssignmentRevisionRef,
    AttemptRef AttemptRef,
    LogicalActorRef LogicalActorRef,
    SessionBindingRef? SessionBindingRef);

public sealed class CanonicalWorkerLaunchService(
    B1AuthorityRepository authorityRepository,
    B1NonAuthoritativeCommandService routingCommands,
    B1ProjectGovernanceRepository governanceRepository,
    TimeProvider timeProvider,
    B1WorkerExecutionBridgeService? workerBridge = null)
{
    public Task<CanonicalWorkerLaunchContext?> PrepareAsync(
        Guid projectId,
        TaskRevision taskRevision,
        CancellationToken cancellationToken = default) =>
        PrepareAsync(projectId, taskRevision, null, cancellationToken);

    public async Task<CanonicalWorkerLaunchContext?> PrepareAsync(
        Guid projectId,
        TaskRevision taskRevision,
        AssignmentRef? requestedAssignmentRef,
        CancellationToken cancellationToken = default)
    {
        var project = new ProjectRef(projectId);
        var governance = await governanceRepository.GetAsync(project, cancellationToken);
        if (governance is null) return null;
        var state = await authorityRepository.LoadProjectStateAsync(project, cancellationToken);
        var projection = B1Projector.Build(state);
        var linkedAssignmentRef = requestedAssignmentRef is null && workerBridge is not null
            ? (await workerBridge.GetWorkerTaskLinkAsync(project, taskRevision.TaskId, cancellationToken))?.AssignmentRef
            : null;
        var effectiveRequestedAssignmentRef = requestedAssignmentRef ?? linkedAssignmentRef;
        Assignment assignment;
        if (effectiveRequestedAssignmentRef is { } requested)
        {
            if (!projection.AcceptedProjectState.CurrentDelegationAssignments.Contains(requested) ||
                !projection.AcceptedProjectState.Assignments.TryGetValue(requested, out assignment!))
            {
                return null;
            }
        }
        else
        {
            var candidates = projection.AcceptedProjectState.CurrentDelegationAssignments
                .Select(value => projection.AcceptedProjectState.Assignments[value])
                .Where(value => projection.AcceptedProjectState.CurrentEffectiveRevisionRefs.ContainsKey(value.AssignmentRef))
                .ToArray();
            if (candidates.Length != 1) return null;
            assignment = candidates[0];
        }

        if (!projection.AcceptedProjectState.CurrentEffectiveRevisionRefs.ContainsKey(assignment.AssignmentRef))
        {
            return null;
        }

        var revisionRef = projection.AcceptedProjectState.CurrentEffectiveRevisionRefs[assignment.AssignmentRef];
        if (!projection.EffectiveCurrentAttemptRefs.TryGetValue(assignment.AssignmentRef, out var selected) || selected is null)
        {
            projection.StoredAttemptSelections.TryGetValue(assignment.AssignmentRef, out var expected);
            var attempt = await routingCommands.CreateAttemptAndSelectAsync(
                new CreateAttemptCommand(project, governance.BootstrapPrincipalRef, new AttemptRef(Guid.NewGuid()), assignment.AssignmentRef, revisionRef, timeProvider.GetUtcNow()), expected, cancellationToken);
            selected = attempt.AttemptRef;
        }
        return new(project, governance.BootstrapPrincipalRef, assignment.AssignmentRef, revisionRef, selected.Value, assignment.AssigneeActorRef,
            projection.EffectiveCurrentBindingRefs.GetValueOrDefault(selected.Value));
    }
}
