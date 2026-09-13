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
    public async Task Legacy_router_completion_is_adapted_into_acceptance_spine_without_authority()
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
        var stateBeforeWorker = await context.Services.B1AuthorityRepository.LoadProjectStateAsync(new ProjectRef(project.Id));
        var acceptedBeforeWorker = B1Projector.Build(stateBeforeWorker).AcceptedProjectState;

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
                JsonSerializer.Serialize(new
                {
                    Kind = "FinalReport",
                    Message = "Created result.txt.",
                    ValidationSummary = "tests passed",
                    ProposedChanges = new[] { "The project now produces result.txt." }
                }), null), now));
        var executionId = Guid.NewGuid();
        var identity = WorkerExecutionIdentity.Start(taskRevision.CreateReference(), "base", "main",
            ProviderAccountBinding.Create(profile.ProviderId, profile.ProviderAccountId), profile, "main", projectDirectory.Path);
        var result = await context.Services.WorkerSessionRouter.StartAsync(new WorkerStartRequest(
            project, taskRevision.TaskId, taskRevision.Id, "Canonical task", profile, "Implement the task.", null, "Worker",
            WaitForCompletion: true,
            ExecutionId: executionId,
            ExecutionIdentity: identity));

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
        Assert.Single(state.Handoffs.Single(value => value.HandoffRef == completion.Facts.HandoffRef).ProposedContributionClaimRefs);
        Assert.Contains(completion.Facts.ProposedChanges, value => value == "The project now produces result.txt.");
        Assert.Equal(stateBeforeWorker.AuthorityDecisions.Count, state.AuthorityDecisions.Count);
        Assert.Null(completion.AuthorityDecisionRef);
        AssertAcceptedStateUnchanged(acceptedBeforeWorker, B1Projector.Build(state).AcceptedProjectState);
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
            "Created result.txt.", "tests passed", [new EvidenceRef("test:evidence")], now,
            ["The project now produces result.txt."]);
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
            ContributionDecisionMode.AdoptVerbatim, null, null));
        var governed = await context.Services.CanonicalWorkerCompletions.GetBySourceEventAsync(project.Id, facts.SourceEventId);

        Assert.NotNull(governed);
        Assert.Equal(CanonicalWorkerCompletionStatus.Governed, governed!.Status);
        Assert.Equal(decision.DecisionRef, governed.AuthorityDecisionRef);
        Assert.Equal(AssignmentDisposition.Accepted, decision.AssignmentDispositionEffect!.Disposition);
        var accepted = await context.Services.B1Projections.GetAcceptedProjectStateAsync(new ProjectRef(project.Id));
        Assert.Contains(accepted.CurrentContributions, value =>
            value.Statement == "The project now produces result.txt." &&
            value.AuthorityDecisionRef == decision.DecisionRef);
        var summary = Assert.Single(await context.Services.ProjectSummaryRepository.QueryAsync(
            new Workbench.Storage.Memory.SummaryQuery(project.Id, 20)));
        Assert.Contains("Authority accepted Worker result", summary.Text, StringComparison.Ordinal);
        Assert.Contains(summary.SourceRefs, value =>
            value.SourceKind == "AuthorityDecision" &&
            value.SourceLocator == decision.DecisionRef.Value.ToString());

        var databasePath = context.DatabasePath;
        await context.Services.DisposeAsync();
        var reopened = AppServices.CreateForDatabasePath(databasePath, context.Time, new AgentRuntimeRegistry());
        await reopened.InitializeAsync();
        try
        {
            var recoveredCompletion = await reopened.CanonicalWorkerCompletions.GetBySourceEventAsync(
                project.Id,
                facts.SourceEventId);
            Assert.NotNull(recoveredCompletion);
            Assert.Equal(facts.ProposedChanges, recoveredCompletion!.Facts.ProposedChanges);
            Assert.Equal(CanonicalWorkerCompletionStatus.Governed, recoveredCompletion.Status);

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
    public async Task Reject_governs_completion_without_entering_proposed_change_into_accepted_state()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var projectDirectory = new TemporaryDirectory("canonical-bridge-reject");
        var prepared = await PrepareCompletionAsync(context, projectDirectory);
        var before = await context.Services.B1Projections.GetAcceptedProjectStateAsync(prepared.ProjectRef);

        await context.Services.CanonicalWorkerCompletionBridge.BridgeAsync(prepared.Facts, prepared.Principal);
        var decision = await context.Services.GuidedDecision.CommitAsync(new GuidedDecisionRequest(
            prepared.ProjectRef, prepared.Principal, prepared.Facts.HandoffRef,
            AssignmentDisposition.Rejected, ContributionDecisionMode.AdoptVerbatim, null, null));

        var completion = await context.Services.CanonicalWorkerCompletions.GetBySourceEventAsync(
            prepared.ProjectRef.Value, prepared.Facts.SourceEventId);
        var after = await context.Services.B1Projections.GetAcceptedProjectStateAsync(prepared.ProjectRef);

        Assert.Equal(CanonicalWorkerCompletionStatus.Governed, completion!.Status);
        Assert.Equal(decision.DecisionRef, completion.AuthorityDecisionRef);
        Assert.Empty(after.CurrentContributions);
        Assert.Equal(before.CurrentEffectiveRevisionRefs, after.CurrentEffectiveRevisionRefs);
        var summaries = await context.Services.ProjectSummaryRepository.QueryAsync(
            new Workbench.Storage.Memory.SummaryQuery(prepared.ProjectRef.Value, 20));
        Assert.Contains(summaries, value => value.Text.Contains("rejected", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Revise_governs_completion_and_activates_successor_revision_without_accepting_proposal()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var projectDirectory = new TemporaryDirectory("canonical-bridge-revise");
        var prepared = await PrepareCompletionAsync(context, projectDirectory);
        var before = await context.Services.B1Projections.GetAcceptedProjectStateAsync(prepared.ProjectRef);

        await context.Services.CanonicalWorkerCompletionBridge.BridgeAsync(prepared.Facts, prepared.Principal);
        var decision = await context.Services.GuidedDecision.CommitAsync(new GuidedDecisionRequest(
            prepared.ProjectRef, prepared.Principal, prepared.Facts.HandoffRef,
            AssignmentDisposition.RevisionRequired, ContributionDecisionMode.Ignore, null,
            "Revise the counter implementation and return with fresh evidence."));

        var completion = await context.Services.CanonicalWorkerCompletions.GetBySourceEventAsync(
            prepared.ProjectRef.Value, prepared.Facts.SourceEventId);
        var after = await context.Services.B1Projections.GetAcceptedProjectStateAsync(prepared.ProjectRef);

        Assert.Equal(CanonicalWorkerCompletionStatus.Governed, completion!.Status);
        Assert.Equal(decision.DecisionRef, completion.AuthorityDecisionRef);
        Assert.Empty(after.CurrentContributions);
        Assert.Equal(before.CurrentEffectiveRevisionRefs.Count, after.CurrentEffectiveRevisionRefs.Count);
        Assert.NotEqual(
            before.CurrentEffectiveRevisionRefs.Single().Value,
            after.CurrentEffectiveRevisionRefs.Single().Value);
        Assert.Equal(AssignmentDisposition.RevisionRequired, decision.AssignmentDispositionEffect!.Disposition);
        Assert.NotNull(decision.RevisionActivationEffect);
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

    private static async Task<PreparedCompletion> PrepareCompletionAsync(
        AppTestContext context,
        TemporaryDirectory projectDirectory)
    {
        var now = context.Time.GetUtcNow();
        var project = new CoreProject(Guid.NewGuid(), "Canonical decision", projectDirectory.Path,
            ProjectType.Generic, null, now, now);
        var principal = context.Services.UserPrincipalProvider.GetCurrent();
        await context.Services.B1ProjectGovernance.CreateGovernedProjectAsync(project, principal);
        await context.Services.ProjectWorldInitialization.CommitAsync(new ProjectWorldInitializationRequest(
            new ProjectRef(project.Id), principal, RoleKind.Worker,
            "Complete the bounded task", "A bounded result", "Keep the change local."));

        var projection = await context.Services.B1Projections.GetProjectProjectionAsync(new ProjectRef(project.Id));
        var assignment = Assert.Single(projection.AcceptedProjectState.Assignments.Values);
        var revisionRef = projection.AcceptedProjectState.CurrentEffectiveRevisionRefs[assignment.AssignmentRef];
        var attempt = await context.Services.B1NonAuthoritativeCommands.CreateAttemptAsync(
            new CreateAttemptCommand(new ProjectRef(project.Id), principal,
                new AttemptRef(Guid.NewGuid()), assignment.AssignmentRef, revisionRef, now));
        var binding = await context.Services.B1NonAuthoritativeCommands.CreateSessionBindingAsync(
            new CreateSessionBindingCommand(new ProjectRef(project.Id), principal,
                new SessionBinding(new SessionBindingRef(Guid.NewGuid()), attempt.AttemptRef,
                    assignment.AssigneeActorRef, new ExternalSessionRef("test:decision"), now)));

        var profile = ExecutionProfile.Create("decision-provider", "decision-account", "model", "test");
        var revision = new TaskRevision(Guid.NewGuid(), 1, "Complete task", "counter.js",
            "Do not change unrelated files", ["Return verified counter change"], TaskRiskLevel.Low,
            profile, "test", TaskRevisionApprover.User, now, null);
        await context.Services.TaskRepository.CreateAsync(project.Id, new TaskDraft(
            revision.TaskId, "Canonical decision task", revision.Goal, revision.Scope,
            revision.OutOfScope, revision.Acceptance, revision.RiskLevel, profile, now, revision,
            TaskLifecycleStatus.Reviewing));
        var executionId = Guid.NewGuid();
        await context.Services.WorkerExecutionRepository.CreateAsync(new StoredWorkerExecution(
            executionId, project.Id, revision.TaskId, revision.CreateReference(), revision.CreateReference(),
            "base", "main", ProviderAccountBinding.Create(profile.ProviderId, profile.ProviderAccountId),
            profile, "worker/decision", projectDirectory.Path, WorkerExecutionState.CompletedPendingReview,
            null, null, null, now, now));

        var facts = context.Services.CanonicalWorkerCompletionBridge.CreateFacts(
            new ProjectRef(project.Id), Guid.NewGuid(), revision.TaskId, revision.Id, executionId,
            attempt.AttemptRef, binding.SessionBindingRef, assignment.AssigneeActorRef,
            "Implemented the counter change.", "counter.js verified",
            [new EvidenceRef("test:counter")], now, ["The counter now increments by 2."]);
        return new PreparedCompletion(new ProjectRef(project.Id), principal, facts);
    }

    private sealed record PreparedCompletion(
        ProjectRef ProjectRef,
        UserPrincipalRef Principal,
        CanonicalWorkerCompletionFacts Facts);

    private static void AssertAcceptedStateUnchanged(
        AcceptedProjectState expected,
        AcceptedProjectState actual)
    {
        Assert.Equal(expected.ProjectRef, actual.ProjectRef);
        Assert.Equal(
            expected.LogicalActors.Keys.OrderBy(value => value.Value),
            actual.LogicalActors.Keys.OrderBy(value => value.Value));
        Assert.Equal(
            expected.Responsibilities.Keys.OrderBy(value => value.Value),
            actual.Responsibilities.Keys.OrderBy(value => value.Value));
        Assert.Equal(
            expected.Assignments.Keys.OrderBy(value => value.Value),
            actual.Assignments.Keys.OrderBy(value => value.Value));
        Assert.Equal(
            expected.Revisions.Keys.OrderBy(value => value.Value),
            actual.Revisions.Keys.OrderBy(value => value.Value));
        Assert.Equal(
            expected.RevisionDispositions.Keys.OrderBy(value => value.Value),
            actual.RevisionDispositions.Keys.OrderBy(value => value.Value));
        Assert.Equal(expected.CurrentEffectiveRevisionRefs, actual.CurrentEffectiveRevisionRefs);
        Assert.Equal(
            expected.CurrentDelegationAssignments.OrderBy(value => value.Value),
            actual.CurrentDelegationAssignments.OrderBy(value => value.Value));
        Assert.Equal(
            expected.CurrentContributions.Select(ContributionSignature),
            actual.CurrentContributions.Select(ContributionSignature));
    }

    private static string ContributionSignature(AcceptedStateContribution contribution) =>
        string.Join(
            "|",
            contribution.ContributionRef.Value.ToString("N"),
            contribution.Statement,
            contribution.Scope,
            contribution.SupersedesContributionRef?.Value.ToString("N"),
            contribution.AuthorityDecisionRef.Value.ToString("N"),
            contribution.SourceClaimRef?.Value.ToString("N"));
}
