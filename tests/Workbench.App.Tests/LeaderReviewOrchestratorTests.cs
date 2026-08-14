using System.Text.Json;
using Workbench.App.Leader;
using Workbench.App.Tests.Support;
using Workbench.Core.Projects;
using Workbench.Core.Tasks;
using Workbench.Runtime.Agents;
using Workbench.Storage.Leaders;
using Workbench.Storage.Tasks;
using Workbench.Storage.Workers;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.Tests;

public sealed class LeaderReviewOrchestratorTests
{
    [Fact]
    public async Task Bound_final_report_is_reviewed_and_recorded_without_changing_assignment_state()
    {
        await using var fixture = await Fixture.CreateAsync();
        var report = await fixture.AddFinalReportAsync();
        fixture.QueueDecision(report.EventId, "PASS");

        var result = await fixture.Orchestrator.TryReviewAsync(fixture.Project.Id, fixture.TaskId, report.EventId);

        Assert.Equal(LeaderReviewOrchestrationResultKind.Recorded, result.Kind);
        Assert.Single(fixture.Runtime.SentRequests);
        Assert.Equal(TaskLifecycleStatus.Reviewing, (await fixture.Context.Services.TaskRepository.GetAsync(fixture.Project.Id, fixture.TaskId))!.Status);
        Assert.Equal("Pass", (await fixture.Decisions.GetLeaderReviewDecisionAsync(fixture.Project.Id, fixture.TaskId, fixture.Revision.Id, report.EventId))!.Outcome);
    }

    [Theory]
    [InlineData("PASS")]
    [InlineData("FIX")]
    [InlineData("CONTINUE")]
    [InlineData("ASK_USER")]
    public async Task Every_decision_is_persisted_without_executing_an_outcome(string outcome)
    {
        await using var fixture = await Fixture.CreateAsync();
        var report = await fixture.AddFinalReportAsync();
        fixture.QueueDecision(report.EventId, outcome);

        var result = await fixture.Orchestrator.TryReviewAsync(fixture.Project.Id, fixture.TaskId, report.EventId);

        Assert.Equal(LeaderReviewOrchestrationResultKind.Recorded, result.Kind);
        Assert.Equal(TaskLifecycleStatus.Reviewing, (await fixture.Context.Services.TaskRepository.GetAsync(fixture.Project.Id, fixture.TaskId))!.Status);
        Assert.Empty(fixture.Runtime.CreateRequests);
    }

    [Fact]
    public async Task Existing_decision_does_not_invoke_runtime()
    {
        await using var fixture = await Fixture.CreateAsync();
        var report = await fixture.AddFinalReportAsync();
        await fixture.RecordDecisionAsync(report.EventId);

        var result = await fixture.Orchestrator.TryReviewAsync(fixture.Project.Id, fixture.TaskId, report.EventId);

        Assert.Equal(LeaderReviewOrchestrationResultKind.ExistingDecision, result.Kind);
        Assert.Empty(fixture.Runtime.SentRequests);
    }

    [Theory]
    [InlineData(TaskLifecycleStatus.Working)]
    [InlineData(TaskLifecycleStatus.NeedsLeaderDecision)]
    [InlineData(TaskLifecycleStatus.Completed)]
    public async Task Non_reviewing_assignments_do_not_invoke_runtime(TaskLifecycleStatus status)
    {
        await using var fixture = await Fixture.CreateAsync(status);
        var result = await fixture.Orchestrator.TryReviewAsync(fixture.Project.Id, fixture.TaskId, Guid.NewGuid());

        Assert.Equal(LeaderReviewOrchestrationResultKind.NoWork, result.Kind);
        Assert.Empty(fixture.Runtime.SentRequests);
    }

    [Fact]
    public async Task Missing_leader_session_and_invalid_binding_leave_review_retryable()
    {
        await using var noLeader = await Fixture.CreateAsync(createLeader: false);
        var report = await noLeader.AddFinalReportAsync();
        Assert.Equal(LeaderReviewOrchestrationResultKind.RuntimeFailure, (await noLeader.Orchestrator.TryReviewAsync(noLeader.Project.Id, noLeader.TaskId, report.EventId)).Kind);
        Assert.Empty(noLeader.Runtime.SentRequests);

        await using var invalid = await Fixture.CreateAsync();
        Assert.Equal(LeaderReviewOrchestrationResultKind.ValidationFailure, (await invalid.Orchestrator.TryReviewAsync(invalid.Project.Id, invalid.TaskId, Guid.NewGuid())).Kind);
        Assert.Empty(invalid.Runtime.SentRequests);
    }

    [Fact]
    public async Task Runtime_and_structured_output_failures_can_be_retried()
    {
        await using var fixture = await Fixture.CreateAsync();
        var report = await fixture.AddFinalReportAsync();
        fixture.Runtime.QueueTurn(new AgentError("failed", DateTimeOffset.UtcNow));
        Assert.Equal(LeaderReviewOrchestrationResultKind.RuntimeFailure, (await fixture.Orchestrator.TryReviewAsync(fixture.Project.Id, fixture.TaskId, report.EventId)).Kind);
        fixture.Runtime.QueueTurn(new AgentTurnCompleted(new AgentResult(fixture.LeaderSession.Id, AgentSessionStatus.Completed, "{", null), DateTimeOffset.UtcNow));
        Assert.Equal(LeaderReviewOrchestrationResultKind.StructuredOutputFailure, (await fixture.Orchestrator.TryReviewAsync(fixture.Project.Id, fixture.TaskId, report.EventId)).Kind);
        fixture.QueueDecision(report.EventId, "PASS");
        Assert.Equal(LeaderReviewOrchestrationResultKind.Recorded, (await fixture.Orchestrator.TryReviewAsync(fixture.Project.Id, fixture.TaskId, report.EventId)).Kind);
    }

    [Fact]
    public async Task Recovery_processes_only_pending_bound_reports_and_is_idempotent()
    {
        await using var fixture = await Fixture.CreateAsync();
        var report = await fixture.AddFinalReportAsync();
        fixture.QueueDecision(report.EventId, "PASS");

        var results = await fixture.Orchestrator.RecoverAsync(fixture.Project.Id);
        var again = await fixture.Orchestrator.RecoverAsync(fixture.Project.Id);

        Assert.Contains(results, item => item.Kind == LeaderReviewOrchestrationResultKind.Recorded);
        Assert.Empty(again);
        Assert.Single(fixture.Runtime.SentRequests);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(AppTestContext context, CoreProject project, Guid taskId, TaskRevision revision, FakeAgentRuntime runtime, AgentSession leaderSession, LeaderReviewOrchestrator orchestrator)
        { Context = context; Project = project; TaskId = taskId; Revision = revision; Runtime = runtime; LeaderSession = leaderSession; Orchestrator = orchestrator; Decisions = new AssignmentReviewStateRepository(context.Services.Database); }
        public AppTestContext Context { get; } public CoreProject Project { get; } public Guid TaskId { get; } public TaskRevision Revision { get; } public FakeAgentRuntime Runtime { get; } public AgentSession LeaderSession { get; } public LeaderReviewOrchestrator Orchestrator { get; } public AssignmentReviewStateRepository Decisions { get; }
        public static async Task<Fixture> CreateAsync(TaskLifecycleStatus status = TaskLifecycleStatus.Reviewing, bool createLeader = true)
        {
            var context = await AppTestContext.CreateAsync(); var now = context.Time.GetUtcNow(); var runtime = new FakeAgentRuntime(); context.Services.RuntimeRegistry.Register(runtime);
            var project = new CoreProject(Guid.NewGuid(), "Review", "C:/review", ProjectType.Generic, null, now, now); await context.Services.ProjectRepository.UpsertAsync(project);
            var taskId = Guid.NewGuid(); var profile = ExecutionProfile.Create("provider", "account", "model", "runtime"); var revision = new TaskRevision(taskId, 1, "goal", "scope", "out", ["accept"], TaskRiskLevel.Low, profile, "initial", TaskRevisionApprover.User, now, null);
            await context.Services.TaskRepository.CreateAsync(project.Id, new TaskDraft(taskId, "Task", "goal", "scope", "out", ["accept"], TaskRiskLevel.Low, profile, now, revision, status));
            var leader = new AgentSession(AgentSessionId.New(), runtime.Account.Id, runtime.Provider.Id, "model-a", project.RootPath, "leader", AgentSessionStatus.Ready, now, now);
            if (createLeader) await context.Services.ProjectLeaderRepository.CreateCurrentEpochAsync(new StoredProjectLeader(project.Id, null, now, now), new StoredLeaderSessionEpoch(Guid.NewGuid(), project.Id, runtime.Provider.Id.Value, runtime.Account.Id.Value, "model-a", leader.Id.Value, leader.ExternalSessionId, leader.WorkingDirectory, now, now, null, null, null));
            var orchestrator = new LeaderReviewOrchestrator(new LeaderReviewInputBuilder(context.Services.ProjectRepository, context.Services.TaskRepository, context.Services.TaskRevisionRepository, new TaskEventRepository(context.Services.Database)), new LeaderReviewRuntimeAdapter(), new AssignmentReviewStateRepository(context.Services.Database), context.Services.TaskRepository, context.Services.ProjectLeaderRepository, context.Services.LeaderSessionEpochRepository, context.Services.RuntimeRegistry, context.Time);
            return new Fixture(context, project, taskId, revision, runtime, leader, orchestrator);
        }
        public async Task<(Guid EventId, AgentSessionId WorkerSessionId)> AddFinalReportAsync()
        {
            var worker = AgentSessionId.New(); var eventId = Guid.NewGuid(); var events = new TaskEventRepository(Context.Services.Database);
            await new AssignmentReviewStateRepository(Context.Services.Database).TryTransitionAsync(Project.Id, TaskId, TaskLifecycleStatus.Reviewing, TaskLifecycleStatus.Working, Guid.NewGuid(), "Temporary", "{}", Context.Time.GetUtcNow());
            await new AssignmentReviewStateRepository(Context.Services.Database).TryTransitionAsync(Project.Id, TaskId, TaskLifecycleStatus.Working, TaskLifecycleStatus.Reviewing, eventId, "WorkerFinalReportReceived", JsonSerializer.Serialize(new { WorkerSessionId = worker.Value, Message = "done", ValidationSummary = "green" }), Context.Time.GetUtcNow());
            await events.AppendAsync(new StoredTaskEvent(Guid.NewGuid(), Project.Id, TaskId, null, "WorkerToLeaderHandoff", JsonSerializer.Serialize(new Workbench.App.Worker.WorkerHandoff(Project.Id, TaskId, worker, "Worker", AgentSessionStatus.Completed, "done", Context.Time.GetUtcNow(), Workbench.App.Worker.WorkerHandoffKind.FinalReport, "green", eventId, Revision.Id)), Context.Time.GetUtcNow()));
            return (eventId, worker);
        }
        public void QueueDecision(Guid eventId, string outcome) => Runtime.QueueTurn(new AgentTurnCompleted(new AgentResult(LeaderSession.Id, AgentSessionStatus.Completed, $$"""{"taskId":"{{TaskId}}","taskRevisionId":"{{Revision.Id}}","finalReportEventId":"{{eventId}}","outcome":"{{outcome}}","actionLevel":"{{(outcome == "ASK_USER" ? "L3_DECISION_REQUIRED" : "L1_LOCAL_FIX")}}","reviewDepth":"REPORT_ONLY","summary":"ok","nextAction":"none"}""", null), DateTimeOffset.UtcNow));
        public Task RecordDecisionAsync(Guid eventId) => Decisions.TryRecordLeaderReviewDecisionAsync(new LeaderReviewDecisionPersistenceRequest(Guid.NewGuid(), Project.Id, TaskId, Revision.Id, eventId, "Pass", "L1LocalFix", "ReportOnly", "ok", null, "none", null, Context.Time.GetUtcNow()));
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }
}
