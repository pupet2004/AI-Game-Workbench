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
    TimeProvider timeProvider)
{
    public async Task<CanonicalWorkerLaunchContext?> PrepareAsync(Guid projectId, TaskRevision taskRevision, CancellationToken cancellationToken = default)
    {
        var project = new ProjectRef(projectId);
        var governance = await governanceRepository.GetAsync(project, cancellationToken);
        if (governance is null) return null;
        var state = await authorityRepository.LoadProjectStateAsync(project, cancellationToken);
        var projection = B1Projector.Build(state);
        var candidates = projection.AcceptedProjectState.CurrentDelegationAssignments
            .Select(value => projection.AcceptedProjectState.Assignments[value])
            .Where(assignment => projection.AcceptedProjectState.CurrentEffectiveRevisionRefs.ContainsKey(assignment.AssignmentRef))
            .ToArray();
        if (candidates.Length != 1) return null;
        var assignment = candidates[0];
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
