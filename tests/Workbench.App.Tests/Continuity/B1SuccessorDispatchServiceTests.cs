using System.Text.Json;
using Workbench.App.Continuity;
using Workbench.App.Services;
using Workbench.App.Tests.Support;
using Workbench.Core.Continuity;
using Workbench.Core.Projects;
using Workbench.Core.Tasks;
using Workbench.Core.Workers;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Registry;
using Workbench.Storage.Workers;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.Tests.Continuity;

public sealed class B1SuccessorDispatchServiceTests
{
    [Fact]
    public async Task Explicit_successor_assignment_dispatches_when_other_assignments_are_current()
    {
        await using var context = await AppTestContext.CreateAsync(
            runtimeRegistry: CreateRegistry(out var runtime));
        var project = await CreateGovernedProjectAsync(context, "Explicit successor");
        var principal = new UserPrincipalRef("U1");
        var state = await context.Services.B1AuthorityRepository.LoadProjectStateAsync(new ProjectRef(project.Id));
        var replaced = Assert.Single(B1Projector.Build(state).AcceptedProjectState.CurrentDelegationAssignments);

        await context.Services.B1AuthorityCommands.EstablishResponsibilityAsync(
            new EstablishResponsibilityCommand(
                new ProjectRef(project.Id),
                principal,
                new DecidingAuthorityRef.UserPrincipal(principal),
                new ResponsibilityContract("Keep a second lane", "Second lane complete", AuthorityBoundary.Empty),
                new AssignmentDelegationInstruction(
                    new ResponsibilityTarget.EstablishedByThisDecision(),
                    new AssignmentAssigneeTarget.EstablishedByThisDecision(),
                    new AssignmentRevisionContract("Second lane"),
                    null),
                RoleKind.Worker,
                [],
                []));

        var profile = ExecutionProfile.Create(runtime.Provider.Id.Value, runtime.Account.Id.Value.ToString(), "model-a", "fake-runtime");
        var revision = NewRevision(profile, "Successor task");
        var task = new TaskDraft(
            revision.TaskId,
            "Successor task",
            revision.Goal,
            revision.Scope,
            revision.OutOfScope,
            revision.Acceptance,
            revision.RiskLevel,
            profile,
            revision.CreatedAt,
            revision);
        var identity = WorkerExecutionIdentity.Start(
            revision.CreateReference(),
            "base",
            "main",
            ProviderAccountBinding.Create(profile.ProviderId, profile.ProviderAccountId),
            profile,
            "worker/successor",
            project.RootPath);
        runtime.QueueTurn(new AgentTurnCompleted(
            new AgentResult(
                AgentSessionId.New(),
                AgentSessionStatus.Completed,
                JsonSerializer.Serialize(new
                {
                    Kind = "FinalReport",
                    Message = "Successor completed.",
                    ValidationSummary = "passed",
                    ProposedChanges = new[] { "Successor change is ready." }
                }),
                null),
            DateTimeOffset.UtcNow));

        var result = await context.Services.B1SuccessorDispatch.CreateAndDispatchAsync(
            new B1SuccessorDispatchRequest(
                project,
                new ProjectRef(project.Id),
                principal,
                replaced,
                "Run the next bounded change",
                task,
                identity,
                "Run the next bounded change.",
                "Successor Worker",
                WaitForCompletion: true));

        Assert.Equal(B1SuccessorDispatchStatus.Started, result.Status);
        Assert.True(result.WorkerStart.Succeeded, result.Error);
        Assert.NotNull(result.DelegationDecision.AssignmentDelegationEffect);
        var accepted = await context.Services.B1Projections.GetAcceptedProjectStateAsync(new ProjectRef(project.Id));
        Assert.Equal(2, accepted.CurrentDelegationAssignments.Count);
        Assert.Contains(
            result.DelegationDecision.AssignmentDelegationEffect!.Assignment.AssignmentRef,
            accepted.CurrentDelegationAssignments);
        var recoveredLaunch = await context.Services.CanonicalWorkerLaunch.PrepareAsync(project.Id, revision);
        Assert.NotNull(recoveredLaunch);
        Assert.Equal(
            result.DelegationDecision.AssignmentDelegationEffect.Assignment.AssignmentRef,
            recoveredLaunch!.AssignmentRef);
        Assert.Single(await context.Services.TaskRepository.ListAsync(project.Id));
        Assert.Single(await context.Services.WorkerExecutionRepository.ListAsync(project.Id));
        Assert.NotNull(await context.Services.CanonicalWorkerCompletions.GetByWorkerExecutionAsync(
            project.Id,
            (await context.Services.WorkerExecutionRepository.ListAsync(project.Id)).Single().ExecutionId));
    }

    [Fact]
    public async Task Runtime_failure_leaves_durable_recovery_marker_and_retry_reuses_authority()
    {
        using var directory = new TemporaryDirectory("successor-retry");
        var databasePath = Path.Combine(directory.Path, "workbench.db");
        var projectPath = Path.Combine(directory.Path, "project");
        Directory.CreateDirectory(projectPath);

        var project = new CoreProject(
            Guid.NewGuid(),
            "Retry successor",
            projectPath,
            ProjectType.Generic,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        var principal = new UserPrincipalRef("U1");
        var executionId = Guid.NewGuid();
        var profile = ExecutionProfile.Create("fake-provider", Guid.NewGuid().ToString(), "model-a", "fake-runtime");
        var revision = NewRevision(profile, "Retry task");
        var task = new TaskDraft(
            revision.TaskId,
            "Retry task",
            revision.Goal,
            revision.Scope,
            revision.OutOfScope,
            revision.Acceptance,
            revision.RiskLevel,
            profile,
            revision.CreatedAt,
            revision);
        var identity = WorkerExecutionIdentity.Start(
            revision.CreateReference(),
            "base",
            "main",
            ProviderAccountBinding.Create(profile.ProviderId, profile.ProviderAccountId),
            profile,
            "worker/retry",
            projectPath);
        B1SuccessorDispatchRequest? request = null;

        await using (var unavailable = AppServices.CreateForDatabasePath(databasePath, TimeProvider.System, new AgentRuntimeRegistry()))
        {
            await unavailable.InitializeAsync();
            await unavailable.B1ProjectGovernance.CreateGovernedProjectAsync(project, principal);
            var initial = await unavailable.B1AuthorityCommands.EstablishResponsibilityAsync(
                new EstablishResponsibilityCommand(
                    new ProjectRef(project.Id),
                    principal,
                    new DecidingAuthorityRef.UserPrincipal(principal),
                    new ResponsibilityContract("Own retry work", "Retry complete", AuthorityBoundary.Empty),
                    new AssignmentDelegationInstruction(
                        new ResponsibilityTarget.EstablishedByThisDecision(),
                        new AssignmentAssigneeTarget.EstablishedByThisDecision(),
                        new AssignmentRevisionContract("Initial retry work"),
                        null),
                    RoleKind.Worker,
                    [],
                    []));
            request = new B1SuccessorDispatchRequest(
                project,
                new ProjectRef(project.Id),
                principal,
                initial.AssignmentDelegationEffect!.Assignment.AssignmentRef,
                "Retry after runtime recovery",
                task,
                identity,
                "Retry after runtime recovery.",
                "Retry Worker",
                ExecutionId: executionId);

            var failed = await unavailable.B1SuccessorDispatch.CreateAndDispatchAsync(request);
            Assert.Equal(B1SuccessorDispatchStatus.FailedAfterAuthorityCommit, failed.Status);
            Assert.False(failed.WorkerStart.Succeeded);
            Assert.Single(await unavailable.TaskRepository.ListAsync(project.Id));
            Assert.Equal(2, (await unavailable.B1AuthorityRepository.LoadProjectStateAsync(new ProjectRef(project.Id))).AuthorityDecisions.Count);
            Assert.Single(await new TaskEventRepository(unavailable.Database)
                .ListForProjectAsync(project.Id, "SuccessorDispatchFailed", 10));
        }

        var registry = new AgentRuntimeRegistry();
        var runtime = new Workbench.App.Tests.Support.FakeAgentRuntime(
            "Fake Provider",
            "Recovered Account",
            new Workbench.Runtime.Providers.ProviderAccountId(Guid.Parse(profile.ProviderAccountId)));
        registry.Register(runtime);
        runtime.QueueTurn(new AgentTurnCompleted(
            new AgentResult(
                AgentSessionId.New(),
                AgentSessionStatus.Completed,
                JsonSerializer.Serialize(new
                {
                    Kind = "FinalReport",
                    Message = "Retry completed.",
                    ValidationSummary = "passed",
                    ProposedChanges = new[] { "Retry change is ready." }
                }),
                null),
            DateTimeOffset.UtcNow));

        await using var recovered = AppServices.CreateForDatabasePath(databasePath, TimeProvider.System, registry);
        await recovered.InitializeAsync();
        var retried = await recovered.B1SuccessorDispatch.CreateAndDispatchAsync(request!);

        Assert.Equal(B1SuccessorDispatchStatus.Started, retried.Status);
        Assert.True(retried.WorkerStart.Succeeded, retried.Error);
        var state = await recovered.B1AuthorityRepository.LoadProjectStateAsync(new ProjectRef(project.Id));
        Assert.Equal(2, state.AuthorityDecisions.Count);
        Assert.Single(await recovered.TaskRepository.ListAsync(project.Id));
        Assert.Single(await recovered.WorkerExecutionRepository.ListAsync(project.Id));
    }

    private static AgentRuntimeRegistry CreateRegistry(out Workbench.App.Tests.Support.FakeAgentRuntime runtime)
    {
        runtime = new Workbench.App.Tests.Support.FakeAgentRuntime();
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        return registry;
    }

    private static TaskRevision NewRevision(ExecutionProfile profile, string goal) =>
        new(
            Guid.NewGuid(),
            1,
            goal,
            "Only the successor task scope",
            "Do not change unrelated files",
            ["The successor change is complete."],
            TaskRiskLevel.Low,
            profile,
            "successor dispatch test",
            TaskRevisionApprover.User,
            DateTimeOffset.UtcNow,
            null);

    private static async Task<CoreProject> CreateGovernedProjectAsync(AppTestContext context, string name)
    {
        var project = new CoreProject(
            Guid.NewGuid(),
            name,
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            ProjectType.Generic,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        Directory.CreateDirectory(project.RootPath);
        var principal = new UserPrincipalRef("U1");
        await context.Services.B1ProjectGovernance.CreateGovernedProjectAsync(project, principal);
        await context.Services.ProjectWorldInitialization.CommitAsync(
            new Workbench.App.ProjectWorld.ProjectWorldInitializationRequest(
                new ProjectRef(project.Id),
                principal,
                RoleKind.Worker,
                "Own the first lane",
                "First lane complete",
                "Build the first lane"));
        return project;
    }
}
