using System.Text.Json;
using Workbench.Core.Continuity;
using Workbench.Core.Tasks;
using Workbench.Storage.Continuity;

namespace Workbench.App.Continuity;

// Called only by the user's draft confirmation, never by completion or automatic routing.
public sealed class ConfirmedWorkerDraftLaunchService(
    B1ProjectGovernanceRepository governanceRepository,
    B1AuthorityRepository authorityRepository,
    B1AuthorityCommandService authorityCommands,
    B1WorkerExecutionBridgeService workerBridge,
    CanonicalWorkerLaunchService launchService)
{
    public async Task<CanonicalWorkerLaunchContext?> PrepareAsync(
        Guid projectId, TaskRevision revision, CancellationToken cancellationToken = default)
    {
        var projectRef = new ProjectRef(projectId);
        var governance = await governanceRepository.GetAsync(projectRef, cancellationToken);
        if (governance is null) return null;
        var state = await authorityRepository.LoadProjectStateAsync(projectRef, cancellationToken);
        var accepted = B1Projector.Build(state).AcceptedProjectState;
        var link = await workerBridge.GetWorkerTaskLinkAsync(projectRef, revision.TaskId, cancellationToken);
        AssignmentRef assignmentRef;
        if (link is not null)
        {
            // A retry must stay on its original work, even if that work is no longer runnable.
            assignmentRef = link.AssignmentRef;
        }
        else
        {
            var contract = new AssignmentRevisionContract(JsonSerializer.Serialize(new
            {
                revision.TaskId, revision.Id, revision.Goal, revision.Scope,
                revision.OutOfScope, revision.Acceptance, revision.RiskLevel
            }));
            var priorDelegation = state.AuthorityDecisions.LastOrDefault(value =>
                value.AssignmentDelegationEffect?.InitialRevision.Contract == contract);
            if (priorDelegation?.AssignmentDelegationEffect is { } prior)
            {
                // Recover an authorization committed before an interrupted Worker start.
                assignmentRef = prior.Assignment.AssignmentRef;
            }
            else
            {
                if (accepted.CurrentDelegationAssignments.Count != 1)
                    throw new InvalidOperationException("Select the work to continue before starting this task.");
                assignmentRef = accepted.CurrentDelegationAssignments.Single();
                if (!accepted.CurrentEffectiveRevisionRefs.TryGetValue(assignmentRef, out var effective))
                    throw new InvalidOperationException("This work has no current version to start.");
                if (accepted.RevisionDispositions.ContainsKey(effective))
                {
                    var previous = accepted.Assignments[assignmentRef];
                    var principal = governance.BootstrapPrincipalRef;
                    var decision = await authorityCommands.DelegateAssignmentAsync(new DelegateAssignmentCommand(
                        projectRef, principal, new DecidingAuthorityRef.UserPrincipal(principal),
                        new AssignmentDelegationInstruction(
                            new ResponsibilityTarget.Existing(previous.ResponsibilityRef),
                            new AssignmentAssigneeTarget.Existing(previous.AssigneeActorRef),
                            contract, assignmentRef),
                        null, [], []), cancellationToken);
                    assignmentRef = decision.AssignmentDelegationEffect!.Assignment.AssignmentRef;
                }
            }
        }

        return await launchService.PrepareAsync(projectId, revision, assignmentRef, cancellationToken)
            ?? throw new InvalidOperationException("The selected work is no longer current. Review the task before retrying.");
    }
}
