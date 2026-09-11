using System.Text.Json;
using Workbench.App.Continuity;
using Workbench.App.ProjectWorld;
using Workbench.App.Worker;
using Workbench.App.Services;
using Workbench.Core.Continuity;
using Workbench.Core.Projects;
using Workbench.Core.Tasks;
using Workbench.Core.Workers;
using Workbench.Storage.Workers;
using Workbench.App.Tests.Support;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Registry;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.Tests.Continuity;

public sealed class CanonicalWorkerCompletionBridgeTests
{
    [Fact]
    public async Task Real_worker_router_completion_enters_canonical_governance_without_authority()
    {
        var runtime = new FakeAgentRuntime();
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        await using var context = await AppTestContext.CreateAsync(runtimeRegistry: registry);
        using var projectDirectory = new TemporaryDirectory("router-canonical-bridge");
        var now = context.Time.GetUtcNow();
        var project = new CoreProject(Guid.NewGuid(), "Router bridge", projectDirectory.Path, ProjectType.Generic, null, now, now);
        var principal = context.Services.UserPrincipalProvider.GetCurrent();
        await context.Services.B1ProjectGovernance.CreateGovernedProjectAsync(project, principal);
        await context.Services.ProjectWorldInitialization.CommitAsync(new ProjectWorldInitializationRequest(
            new ProjectRef(project.Id), principal, RoleKind.Worker,
            "Complete the bounded task", "A completed bounded result", "Implement the task."));
        var authorityDecisionCountBeforeWorker = (await context.Services.B1AuthorityRepository.LoadProjectStateAsync(new ProjectRef(project.Id)))
            .AuthorityDecisions.Count;

        var projection = await context.Services.B1Projections.GetProjectProjectionAsync(new ProjectRef(project.Id));
        var assignment = Assert.Single(projection.AcceptedProjectState.Assignments.Values);
        var assignmentRevision = projection.AcceptedProjectState.CurrentEffectiveRevisionRefs[assignment.AssignmentRef];
        var attempt = await context.Services.B1NonAuthoritativeCommands.CreateAttemptAndSelectAsync(
            new CreateAttemptCommand(new ProjectRef(project.Id), principal, new AttemptRef(Guid.NewGuid()),
                assignment.AssignmentRef, assignmentRevision, now), null);

        var profile = ExecutionProfile.Create(runtime.Provider.Id.Value, runtime.Account.Id.Value.ToString(), "model-a", "runtime");
        var taskRevision = new TaskRevision(Guid.NewGuid(), 1, "Complete task", "`result.txt`", "Do not change unrelated files",
            ["Create `result.txt`"], TaskRiskLevel.Low, profile, "test", TaskRevisionApprover.User, now, null);
        await context.Services.TaskRepository.CreateAsync(project.Id, new TaskDraft(
            taskRevision.TaskId, "Canonical task", taskRevision.Goal, taskRevision.Scope, taskRevision.OutOfScope,
            taskRevision.Acceptance, taskRevision.RiskLevel, profile, now, taskRevision));

        runtime.QueueTurn(new AgentTurnCompleted(
            new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed,
                JsonSerializer.Serialize(new { Kind = "FinalReport", Message = "Created result.txt.", ValidationSummary = "tests passed" }), null), now));
        var executionId = Guid.NewGuid();
        var identity = WorkerExecutionIdentity.Start(taskRevision.CreateReference(), "base", "main",
            ProviderAccountBinding.Create(profile.ProviderId, profile.ProviderAccountId), profile, "main", projectDirectory.Path);
        var result = await context.Services.WorkerSessionRouter.StartAsync(new WorkerStartRequest(
            project, taskRevision.TaskId, taskRevision.Id, "Canonical task", profile, "Implement the task.", null, "Worker",
            WaitForCompletion: true,
            ExecutionId: executionId,
            ExecutionIdentity: identity,
            B1AssignmentRef: assignment.AssignmentRef,
            B1AssignmentRevisionRef: assignmentRevision,
            B1AttemptRef: attempt.AttemptRef,
            B1LogicalActorRef: assignment.AssigneeActorRef,
            B1OperatorRef: principal));

        Assert.True(result.Succeeded);
        var completion = await context.Services.CanonicalWorkerCompletions.GetBySourceEventAsync(
            project.Id, (await new Workbench.Storage.Workers.TaskEventRepository(context.Services.Database)
                .ListForProjectAsync(project.Id, "WorkerFinalReportReceived", 10)).Single().EventId);
        Assert.NotNull(completion);
        Assert.Equal(CanonicalWorkerCompletionStatus.GovernanceReady, completion!.Status);
        var state = await context.Services.B1AuthorityRepository.LoadProjectStateAsync(new ProjectRef(project.Id));
        Assert.Single(state.Claims, value => value.ClaimRef == completion.Facts.ResultClaimRef);
        Assert.Single(state.Claims, value => value.ClaimRef == completion.Facts.ValidationClaimRef);
        Assert.Single(state.Handoffs, value => value.HandoffRef == completion.Facts.HandoffRef);
        Assert.Equal(authorityDecisionCountBeforeWorker, state.AuthorityDecisions.Count);
    }

    [Fact]
    public async Task Completion_bridges_idempotently_without_authority_then_guided_decision_governs_it()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var projectDirectory = new TemporaryDirectory("canonical-bridge-project");
        var now = context.Time.GetUtcNow();
        var project = new CoreProject(Guid.NewGuid(), "Canonical bridge", projectDirectory.Path, ProjectType.Generic, null, now, now);
        var principal = context.Services.UserPrincipalProvider.GetCurrent();
        await context.Services.B1ProjectGovernance.CreateGovernedProjectAsync(project, principal);
        await context.Services.ProjectWorldInitialization.CommitAsync(new ProjectWorldInitializationRequest(
            new ProjectRef(project.Id), principal, RoleKind.Worker,
            "Complete the bounded task", "A completed bounded result", "Implement the task."));

        var projection = await context.Services.B1Projections.GetProjectProjectionAsync(new ProjectRef(project.Id));
        var assignment = Assert.Single(projection.AcceptedProjectState.Assignments.Values);
        var revisionRef = projection.AcceptedProjectState.CurrentEffectiveRevisionRefs[assignment.AssignmentRef];
        var attempt = await context.Services.B1NonAuthoritativeCommands.CreateAttemptAsync(
            new CreateAttemptCommand(new ProjectRef(project.Id), principal, new AttemptRef(Guid.NewGuid()), assignment.AssignmentRef, revisionRef, now));
        await context.Services.B1NonAuthoritativeCommands.SelectCurrentAttemptAsync(
            new SelectCurrentAttemptCommand(new ProjectRef(project.Id), principal, assignment.AssignmentRef, null, attempt.AttemptRef));
        var binding = await context.Services.B1NonAuthoritativeCommands.CreateSessionBindingAsync(
            new CreateSessionBindingCommand(new ProjectRef(project.Id), principal,
                new SessionBinding(new SessionBindingRef(Guid.NewGuid()), attempt.AttemptRef, assignment.AssigneeActorRef,
                    new ExternalSessionRef("test:worker-session"), now)));

        var profile = ExecutionProfile.Create("test-provider", "test-account", "test-model", "test-runtime");
        var taskRevision = new TaskRevision(Guid.NewGuid(), 1, "Complete task", "`result.txt`", "Do not change unrelated files",
            ["Create `result.txt`"], TaskRiskLevel.Low, profile, "test", TaskRevisionApprover.User, now, null);
        var task = new TaskDraft(taskRevision.TaskId, "Canonical task", taskRevision.Goal, taskRevision.Scope,
            taskRevision.OutOfScope, taskRevision.Acceptance, taskRevision.RiskLevel, profile, now, taskRevision,
            TaskLifecycleStatus.Reviewing);
        await context.Services.TaskRepository.CreateAsync(project.Id, task);
        var executionId = Guid.NewGuid();
        await context.Services.WorkerExecutionRepository.CreateAsync(new StoredWorkerExecution(
            executionId, project.Id, taskRevision.TaskId, taskRevision.CreateReference(), taskRevision.CreateReference(),
            "base", "main", ProviderAccountBinding.Create(profile.ProviderId, profile.ProviderAccountId), profile,
            "worker/canonical", projectDirectory.Path, WorkerExecutionState.CompletedPendingReview, null, null, null, now, now));

        var facts = context.Services.CanonicalWorkerCompletionBridge.CreateFacts(
            new ProjectRef(project.Id), Guid.NewGuid(), taskRevision.TaskId, taskRevision.Id, executionId,
            attempt.AttemptRef, binding.SessionBindingRef, assignment.AssigneeActorRef,
            "Created result.txt.", "tests passed", [new EvidenceRef("test:evidence")], now);
        var beforeDecisions = (await context.Services.B1AuthorityRepository.LoadProjectStateAsync(new ProjectRef(project.Id))).AuthorityDecisions.Count;
        var first = await context.Services.CanonicalWorkerCompletionBridge.BridgeAsync(facts, principal);
        var second = await context.Services.CanonicalWorkerCompletionBridge.BridgeAsync(facts, principal);

        Assert.Equal(CanonicalWorkerCompletionStatus.GovernanceReady, first.Status);
        Assert.Equal(first.Facts.CompletionId, second.Facts.CompletionId);
        Assert.Equal(first.Facts.HandoffRef, second.Facts.HandoffRef);
        var bridged = await context.Services.B1AuthorityRepository.LoadProjectStateAsync(new ProjectRef(project.Id));
        Assert.Equal(beforeDecisions, bridged.AuthorityDecisions.Count);
        Assert.Single(bridged.Claims, value => value.ClaimRef == facts.ResultClaimRef);
        Assert.Single(bridged.Claims, value => value.ClaimRef == facts.ValidationClaimRef);
        Assert.Single(bridged.Handoffs, value => value.HandoffRef == facts.HandoffRef);

        var decision = await context.Services.GuidedDecision.CommitAsync(new GuidedDecisionRequest(
            new ProjectRef(project.Id), principal, facts.HandoffRef, AssignmentDisposition.Accepted,
            ContributionDecisionMode.Ignore, null, null));
        var governed = await context.Services.CanonicalWorkerCompletions.GetBySourceEventAsync(project.Id, facts.SourceEventId);

        Assert.NotNull(governed);
        Assert.Equal(CanonicalWorkerCompletionStatus.Governed, governed!.Status);
        Assert.Equal(decision.DecisionRef, governed.AuthorityDecisionRef);
        Assert.Equal(AssignmentDisposition.Accepted, decision.AssignmentDispositionEffect!.Disposition);

        var databasePath = context.DatabasePath;
        await context.Services.DisposeAsync();
        var reopened = AppServices.CreateForDatabasePath(databasePath, context.Time, new AgentRuntimeRegistry());
        await reopened.InitializeAsync();
        try
        {
            var boot = await reopened.LeaderBootContextBuilder.BuildAsync(project, "What is the current project state?");
            Assert.Contains("CURRENT AUTHORITY STATE (USER-ACCEPTED)", boot.Text, StringComparison.Ordinal);
            Assert.Contains("Accepted", boot.Text, StringComparison.Ordinal);
            Assert.Contains("Complete the bounded task", boot.Text, StringComparison.Ordinal);
        }
        finally
        {
            await reopened.DisposeAsync();
        }
    }

    [Fact]
    public async Task Invalid_bridge_operator_leaves_a_recoverable_pending_completion()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var projectDirectory = new TemporaryDirectory("canonical-bridge-pending");
        var now = context.Time.GetUtcNow();
        var project = new CoreProject(Guid.NewGuid(), "Pending bridge", projectDirectory.Path, ProjectType.Generic, null, now, now);
        var principal = context.Services.UserPrincipalProvider.GetCurrent();
        await context.Services.B1ProjectGovernance.CreateGovernedProjectAsync(project, principal);
        await context.Services.ProjectWorldInitialization.CommitAsync(new ProjectWorldInitializationRequest(
            new ProjectRef(project.Id), principal, RoleKind.Worker, "Bounded task", "Bounded result", "Do it."));
        var projection = await context.Services.B1Projections.GetProjectProjectionAsync(new ProjectRef(project.Id));
        var assignment = Assert.Single(projection.AcceptedProjectState.Assignments.Values);
        var revisionRef = projection.AcceptedProjectState.CurrentEffectiveRevisionRefs[assignment.AssignmentRef];
        var attempt = await context.Services.B1NonAuthoritativeCommands.CreateAttemptAsync(
            new CreateAttemptCommand(new ProjectRef(project.Id), principal, new AttemptRef(Guid.NewGuid()), assignment.AssignmentRef, revisionRef, now));
        var binding = await context.Services.B1NonAuthoritativeCommands.CreateSessionBindingAsync(
            new CreateSessionBindingCommand(new ProjectRef(project.Id), principal,
                new SessionBinding(new SessionBindingRef(Guid.NewGuid()), attempt.AttemptRef, assignment.AssigneeActorRef,
                    new ExternalSessionRef("test:pending"), now)));
        var profile = ExecutionProfile.Create("p", "a", "m", "r");
        var revision = new TaskRevision(Guid.NewGuid(), 1, "g", "s", "o", ["a"], TaskRiskLevel.Low, profile, "test", TaskRevisionApprover.User, now, null);
        await context.Services.TaskRepository.CreateAsync(project.Id, new TaskDraft(revision.TaskId, "t", "g", "s", "o", ["a"], TaskRiskLevel.Low, profile, now, revision, TaskLifecycleStatus.Reviewing));
        var executionId = Guid.NewGuid();
        await context.Services.WorkerExecutionRepository.CreateAsync(new StoredWorkerExecution(executionId, project.Id, revision.TaskId,
            revision.CreateReference(), revision.CreateReference(), "base", "main", ProviderAccountBinding.Create("p", "a"), profile,
            "worker/pending", projectDirectory.Path, WorkerExecutionState.CompletedPendingReview, null, null, null, now, now));
        var facts = context.Services.CanonicalWorkerCompletionBridge.CreateFacts(new ProjectRef(project.Id), Guid.NewGuid(), revision.TaskId,
            revision.Id, executionId, attempt.AttemptRef, binding.SessionBindingRef, assignment.AssigneeActorRef, "done", "green", [], now);

        await Assert.ThrowsAsync<B1CommandException>(() => context.Services.CanonicalWorkerCompletionBridge.BridgeAsync(
            facts, new UserPrincipalRef("user:not-bootstrap")));
        var pending = await context.Services.CanonicalWorkerCompletions.GetBySourceEventAsync(project.Id, facts.SourceEventId);
        Assert.NotNull(pending);
        Assert.Equal(CanonicalWorkerCompletionStatus.PendingBridge, pending!.Status);
        var recovered = await context.Services.CanonicalWorkerCompletionBridge.RecoverAsync(project.Id, principal);
        Assert.Equal(1, recovered);
    }
}
