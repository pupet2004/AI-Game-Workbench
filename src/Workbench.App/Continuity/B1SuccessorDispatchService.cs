using System.Text.Json;
using Workbench.App.Worker;
using Workbench.Core.Continuity;
using Workbench.Core.Tasks;
using Workbench.Core.Workers;
using Workbench.Runtime.Agents;
using Workbench.Storage.Continuity;
using Workbench.Storage.Tasks;
using Workbench.Storage.Workers;

namespace Workbench.App.Continuity;

public sealed record B1SuccessorDispatchRequest(
    Workbench.Core.Projects.Project Project,
    ProjectRef ProjectRef,
    UserPrincipalRef UserPrincipalRef,
    AssignmentRef ReplacesAssignmentRef,
    string SuccessorAssignmentContract,
    TaskDraft Task,
    WorkerExecutionIdentity ExecutionIdentity,
    string WorkerPrompt,
    string WorkerLabel,
    AgentAccessMode AccessMode = AgentAccessMode.Restricted,
    bool WaitForCompletion = false,
    Func<StoredCanonicalWorkerCompletion, CancellationToken, Task>? OnCanonicalCompletion = null,
    Guid? ExecutionId = null);

public enum B1SuccessorDispatchStatus
{
    Started,
    FailedAfterAuthorityCommit
}

public sealed record B1SuccessorDispatchResult(
    AuthorityDecision DelegationDecision,
    TaskDraft Task,
    WorkerStartResult WorkerStart,
    B1SuccessorDispatchStatus Status,
    string? Error);

public sealed class B1SuccessorDispatchService(
    B1AuthorityRepository authorityRepository,
    B1AuthorityCommandService authorityCommands,
    TaskRepository tasks,
    TaskEventRepository taskEvents,
    CanonicalWorkerLaunchService canonicalWorkerLaunch,
    WorkerSessionRouter workerRouter)
{
    public async Task<B1SuccessorDispatchResult> CreateAndDispatchAsync(
        B1SuccessorDispatchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Project);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SuccessorAssignmentContract);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkerPrompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkerLabel);

        if (request.Task.TaskId != request.ExecutionIdentity.TaskId ||
            request.Task.CurrentRevision.CreateReference() != request.ExecutionIdentity.ExecutionStartRevision)
        {
            throw new ArgumentException("The Task and typed execution identity must reference the same revision.", nameof(request));
        }
        if (!SamePath(request.Task.CurrentRevision.RecommendedExecutionProfile, request.ExecutionIdentity.ExecutionProfile))
        {
            throw new ArgumentException("The Task execution profile must match the typed execution identity.", nameof(request));
        }

        var state = await authorityRepository.LoadProjectStateAsync(request.ProjectRef, cancellationToken);
        var projection = B1Projector.Build(state);
        var successorContract = new AssignmentRevisionContract(request.SuccessorAssignmentContract.Trim());
        var delegation = state.AuthorityDecisions
            .Where(value => value.AssignmentDelegationEffect?.ReplacesAssignmentRef == request.ReplacesAssignmentRef)
            .OrderByDescending(value => value.ProjectCommitSequence)
            .FirstOrDefault(value =>
                value.AssignmentDelegationEffect?.InitialRevision.Contract == successorContract)
            ;
        if (delegation is null)
        {
            if (!projection.AcceptedProjectState.CurrentDelegationAssignments
                    .Contains(request.ReplacesAssignmentRef))
            {
                throw new B1CommandException(
                    B1FailureCode.InvalidReference,
                    "The replaced Assignment is not current in Accepted Project State.");
            }

            var assignment = projection.AcceptedProjectState.Assignments[request.ReplacesAssignmentRef];
            delegation = await authorityCommands.DelegateAssignmentAsync(
                new DelegateAssignmentCommand(
                    request.ProjectRef,
                    request.UserPrincipalRef,
                    new DecidingAuthorityRef.UserPrincipal(request.UserPrincipalRef),
                    new AssignmentDelegationInstruction(
                        new ResponsibilityTarget.Existing(assignment.ResponsibilityRef),
                        new AssignmentAssigneeTarget.Existing(assignment.AssigneeActorRef),
                        successorContract,
                        assignment.AssignmentRef),
                    null,
                    [],
                    []),
                cancellationToken);
        }

        try
        {
            var existingTask = await tasks.GetAsync(request.ProjectRef.Value, request.Task.TaskId, cancellationToken);
            if (existingTask is null)
            {
                await tasks.CreateAsync(request.ProjectRef.Value, request.Task, cancellationToken);
            }
            else if (existingTask.CurrentRevisionId != request.Task.CurrentRevision.Id)
            {
                return Failure(delegation, request.Task,
                    "The successor TaskId is already bound to another TaskRevision.");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Failure(delegation, request.Task, exception.Message);
        }

        var launch = await canonicalWorkerLaunch.PrepareAsync(
            request.ProjectRef.Value,
            request.Task.CurrentRevision,
            delegation.AssignmentDelegationEffect?.Assignment.AssignmentRef,
            cancellationToken);
        if (launch is null)
        {
            var error = "The successor Assignment could not be selected for dispatch.";
            await RecordFailureAsync(request, request.ExecutionId, delegation.AssignmentDelegationEffect?.Assignment.AssignmentRef, error, cancellationToken);
            return Failure(delegation, request.Task, error);
        }
        if (delegation.AssignmentDelegationEffect?.Assignment.AssignmentRef != launch.AssignmentRef)
        {
            var error = "The successor Assignment changed before Worker dispatch.";
            await RecordFailureAsync(request, request.ExecutionId, launch.AssignmentRef, error, cancellationToken);
            return Failure(delegation, request.Task, error);
        }

        var executionId = request.ExecutionId ?? Guid.NewGuid();
        var workerStart = await workerRouter.StartAsync(
            new WorkerStartRequest(
                request.Project,
                request.Task.TaskId,
                request.Task.CurrentRevision.Id,
                request.Task.Title,
                request.Task.RecommendedExecutionProfile,
                request.WorkerPrompt,
                null,
                request.WorkerLabel,
                ExecutionId: executionId,
                ExecutionIdentity: request.ExecutionIdentity,
                WaitForCompletion: request.WaitForCompletion,
                B1AssignmentRef: launch.AssignmentRef,
                B1AssignmentRevisionRef: launch.AssignmentRevisionRef,
                B1AttemptRef: launch.AttemptRef,
                B1SessionBindingRef: launch.SessionBindingRef,
                B1LogicalActorRef: launch.LogicalActorRef,
                B1OperatorRef: launch.OperatorRef,
                OnCanonicalCompletion: request.OnCanonicalCompletion,
                AccessMode: request.AccessMode),
            cancellationToken);
        if (!workerStart.Succeeded)
        {
            await RecordFailureAsync(request, executionId, launch.AssignmentRef, workerStart.Error ?? "The Worker could not be started.", cancellationToken);
            return new(delegation, request.Task, workerStart, B1SuccessorDispatchStatus.FailedAfterAuthorityCommit, workerStart.Error);
        }

        return new(delegation, request.Task, workerStart, B1SuccessorDispatchStatus.Started, null);
    }

    private B1SuccessorDispatchResult Failure(
        AuthorityDecision delegation,
        TaskDraft task,
        string error) =>
        new(
            delegation,
            task,
            new WorkerStartResult(false, null, error),
            B1SuccessorDispatchStatus.FailedAfterAuthorityCommit,
            error);

    private Task RecordFailureAsync(
        B1SuccessorDispatchRequest request,
        Guid? executionId,
        AssignmentRef? successorAssignmentRef,
        string error,
        CancellationToken cancellationToken) =>
        taskEvents.AppendAsync(
            new StoredTaskEvent(
                Guid.NewGuid(),
                request.ProjectRef.Value,
                request.Task.TaskId,
                executionId,
                "SuccessorDispatchFailed",
                JsonSerializer.Serialize(new
                {
                    request.ReplacesAssignmentRef,
                    SuccessorAssignmentRef = successorAssignmentRef,
                    Error = error
                }),
                DateTimeOffset.UtcNow),
            cancellationToken);

    private static bool SamePath(ExecutionProfile left, ExecutionProfile right) =>
        left.ProviderId == right.ProviderId &&
        left.ProviderAccountId == right.ProviderAccountId &&
        left.ModelProfileId == right.ModelProfileId &&
        left.AgentRuntimeId == right.AgentRuntimeId;
}
