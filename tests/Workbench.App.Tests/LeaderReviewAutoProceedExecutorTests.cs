using System.Text.Json;
using Workbench.App.Leader;
using Workbench.App.Tests.Support;
using Workbench.Core.Projects;
using Workbench.Core.Leaders;
using Workbench.Core.Tasks;
using Workbench.Storage.Tasks;
using Workbench.Storage.Workers;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.Tests;

public sealed class LeaderReviewAutoProceedExecutorTests
{
    [Theory]
    [InlineData(LeaderAuthorityMode.Cautious, LeaderReviewActionLevel.L1LocalFix, LeaderReviewOutcome.Pass, TaskLifecycleStatus.Completed)]
    [InlineData(LeaderAuthorityMode.Cautious, LeaderReviewActionLevel.L2TaskRework, LeaderReviewOutcome.Pass, TaskLifecycleStatus.Reviewing)]
    [InlineData(LeaderAuthorityMode.Balanced, LeaderReviewActionLevel.L2TaskRework, LeaderReviewOutcome.Pass, TaskLifecycleStatus.Reviewing)]
    [InlineData(LeaderAuthorityMode.Autonomous, LeaderReviewActionLevel.L2TaskRework, LeaderReviewOutcome.Pass, TaskLifecycleStatus.Completed)]
    [InlineData(LeaderAuthorityMode.Autonomous, LeaderReviewActionLevel.L1LocalFix, LeaderReviewOutcome.Fix, TaskLifecycleStatus.Reviewing)]
    [InlineData(LeaderAuthorityMode.Autonomous, LeaderReviewActionLevel.L1LocalFix, LeaderReviewOutcome.Continue, TaskLifecycleStatus.Reviewing)]
    [InlineData(LeaderAuthorityMode.Autonomous, LeaderReviewActionLevel.L3DecisionRequired, LeaderReviewOutcome.Pass, TaskLifecycleStatus.Reviewing)]
    public async Task Only_pass_auto_proceed_closes_the_assignment(LeaderAuthorityMode authority, LeaderReviewActionLevel action, LeaderReviewOutcome outcome, TaskLifecycleStatus expected)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Context.Services.WorkbenchSettingsRepository.SaveLeaderAuthorityModeAsync(authority);
        var decision = await fixture.RecordDecisionAsync(outcome, action);

        var result = await fixture.Executor.TryExecuteAsync(fixture.Project.Id, fixture.TaskId, fixture.Revision.Id, decision.FinalReportEventId);

        Assert.Contains(result.Kind, new[] { LeaderReviewAutoProceedResultKind.Completed, LeaderReviewAutoProceedResultKind.NoWork });
        Assert.Equal(expected, (await fixture.Context.Services.TaskRepository.GetAsync(fixture.Project.Id, fixture.TaskId))!.Status);
    }

    [Fact]
    public async Task Project_override_is_used_and_duplicate_execution_is_idempotent()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Context.Services.WorkbenchSettingsRepository.SaveLeaderAuthorityModeAsync(LeaderAuthorityMode.Cautious);
        await fixture.Context.Services.ProjectSettingsRepository.SaveLeaderAuthorityModeOverrideAsync(fixture.Project.Id, LeaderAuthorityMode.Autonomous);
        var decision = await fixture.RecordDecisionAsync(LeaderReviewOutcome.Pass, LeaderReviewActionLevel.L2TaskRework);

        Assert.Equal(LeaderReviewAutoProceedResultKind.Completed, (await fixture.Executor.TryExecuteAsync(fixture.Project.Id, fixture.TaskId, fixture.Revision.Id, decision.FinalReportEventId)).Kind);
        Assert.Equal(LeaderReviewAutoProceedResultKind.NoWork, (await fixture.Executor.TryExecuteAsync(fixture.Project.Id, fixture.TaskId, fixture.Revision.Id, decision.FinalReportEventId)).Kind);
        Assert.Equal(TaskLifecycleStatus.Completed, (await fixture.Context.Services.TaskRepository.GetAsync(fixture.Project.Id, fixture.TaskId))!.Status);
        Assert.Equal(1L, await fixture.CountEventsAsync("AssignmentAutoCompleted"));
        Assert.Equal(0L, await fixture.CountEventsAsync("LeaderReviewDecisionRecorded") - 1);
    }

    [Fact]
    public async Task Recovery_completes_pending_pass_and_does_not_touch_messages_or_runtime()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Context.Services.WorkbenchSettingsRepository.SaveLeaderAuthorityModeAsync(LeaderAuthorityMode.Balanced);
        var decision = await fixture.RecordDecisionAsync(LeaderReviewOutcome.Pass, LeaderReviewActionLevel.L1LocalFix);

        var results = await fixture.Executor.RecoverAsync(fixture.Project.Id);

        Assert.Equal(LeaderReviewAutoProceedResultKind.Completed, Assert.Single(results).Kind);
        Assert.Equal(TaskLifecycleStatus.Completed, (await fixture.Context.Services.TaskRepository.GetAsync(fixture.Project.Id, fixture.TaskId))!.Status);
        Assert.Equal(0L, await fixture.CountTableAsync("leader_messages"));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(AppTestContext context, CoreProject project, Guid taskId, TaskRevision revision, LeaderReviewAutoProceedExecutor executor)
        { Context = context; Project = project; TaskId = taskId; Revision = revision; Executor = executor; }
        public AppTestContext Context { get; } public CoreProject Project { get; } public Guid TaskId { get; } public TaskRevision Revision { get; } public LeaderReviewAutoProceedExecutor Executor { get; }
        public static async Task<Fixture> CreateAsync()
        {
            var context = await AppTestContext.CreateAsync(); var now = context.Time.GetUtcNow(); var project = new CoreProject(Guid.NewGuid(), "E1", "C:/E1", ProjectType.Generic, null, now, now); await context.Services.ProjectRepository.UpsertAsync(project);
            var taskId = Guid.NewGuid(); var profile = ExecutionProfile.Create("provider", "account", "model", "runtime"); var revision = new TaskRevision(taskId, 1, "goal", "scope", "out", ["accept"], TaskRiskLevel.Low, profile, "initial", TaskRevisionApprover.User, now, null);
            await context.Services.TaskRepository.CreateAsync(project.Id, new TaskDraft(taskId, "Task", "goal", "scope", "out", ["accept"], TaskRiskLevel.Low, profile, now, revision, TaskLifecycleStatus.Reviewing));
            return new Fixture(context, project, taskId, revision, new LeaderReviewAutoProceedExecutor(context.Services.TaskRepository, new AssignmentReviewStateRepository(context.Services.Database), context.Services.LeaderAuthoritySettings, context.Time));
        }
        public async Task<StoredLeaderReviewDecision> RecordDecisionAsync(LeaderReviewOutcome outcome, LeaderReviewActionLevel action)
        {
            var reportId = Guid.NewGuid(); var events = new TaskEventRepository(Context.Services.Database); await events.AppendAsync(new StoredTaskEvent(reportId, Project.Id, TaskId, null, "WorkerFinalReportReceived", JsonSerializer.Serialize(new { WorkerSessionId = Guid.NewGuid(), Message = "done" }), Context.Time.GetUtcNow()));
            var state = new AssignmentReviewStateRepository(Context.Services.Database); Assert.Equal(AssignmentStateTransitionResult.Applied, await state.TryRecordLeaderReviewDecisionAsync(new LeaderReviewDecisionPersistenceRequest(Guid.NewGuid(), Project.Id, TaskId, Revision.Id, reportId, outcome.ToString(), action.ToString(), "ReportOnly", "summary", null, "next", null, Context.Time.GetUtcNow())));
            return (await state.GetLeaderReviewDecisionAsync(Project.Id, TaskId, Revision.Id, reportId))!;
        }
        public async Task<long> CountEventsAsync(string type) => await CountTableAsync("task_events", type);
        public async Task<long> CountTableAsync(string table, string? type = null)
        { await using var c = Context.Services.Database.CreateConnection(); await c.OpenAsync(); var q = c.CreateCommand(); q.CommandText = type is null ? $"SELECT COUNT(*) FROM {table};" : $"SELECT COUNT(*) FROM {table} WHERE event_type=$t;"; if (type is not null) q.Parameters.AddWithValue("$t", type); return Convert.ToInt64(await q.ExecuteScalarAsync()); }
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }
}
