using System.Text.Json;
using Workbench.App.Leader;
using Workbench.App.Worker;
using Workbench.Core.Projects;
using Workbench.Core.Tasks;
using Workbench.Runtime.Agents;
using Workbench.Storage.Tasks;
using Workbench.Storage.Workers;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.Tests;

public sealed class LeaderReviewInputBuilderTests
{
    [Fact]
    public async Task Reviewing_assignment_builds_report_first_input_from_its_current_revision_and_bound_final_report()
    {
        await using var fixture = await Fixture.CreateAsync();
        var report = await fixture.AddFinalReportAsync();

        var input = await fixture.Builder.BuildAsync(fixture.Project.Id, fixture.TaskId, report.EventId);

        Assert.NotNull(input);
        Assert.Equal(fixture.Project.Id, input.ProjectId);
        Assert.Equal("Review Project", input.ProjectName);
        Assert.Equal(fixture.TaskId, input.TaskId);
        Assert.Equal(fixture.CurrentRevision.Id, input.TaskRevisionId);
        Assert.Equal("current goal", input.Goal);
        Assert.Equal(["current acceptance"], input.AcceptanceCriteria);
        Assert.Equal("current scope", input.Scope);
        Assert.Equal("current out of scope", input.OutOfScope);
        Assert.Equal(report.WorkerSessionId.Value, input.WorkerSessionId);
        Assert.Equal(report.EventId, input.FinalReportEventId);
        Assert.Equal("final report body", input.FinalReport.Body);
        Assert.Equal("validation preserved verbatim", input.FinalReport.ValidationSummary);
        Assert.Equal(TaskLifecycleStatus.Reviewing, input.AssignmentStatus);
    }

    [Fact]
    public async Task Stale_final_report_bound_to_an_old_revision_is_rejected_without_latest_guessing()
    {
        await using var fixture = await Fixture.CreateAsync();
        var stale = await fixture.AddFinalReportAsync(taskRevisionId: fixture.InitialRevision.Id);

        var input = await fixture.Builder.BuildAsync(fixture.Project.Id, fixture.TaskId, stale.EventId);

        Assert.Null(input);
    }

    [Fact]
    public async Task Final_report_with_cross_task_handoff_binding_is_rejected()
    {
        await using var fixture = await Fixture.CreateAsync();
        var report = await fixture.AddFinalReportAsync(handoffTaskId: Guid.NewGuid());

        var input = await fixture.Builder.BuildAsync(fixture.Project.Id, fixture.TaskId, report.EventId);

        Assert.Null(input);
    }

    [Fact]
    public async Task Wrong_project_plain_handoff_or_missing_final_report_produces_no_review_input()
    {
        await using var fixture = await Fixture.CreateAsync();
        var report = await fixture.AddFinalReportAsync();
        var plainHandoff = await fixture.AppendPlainHandoffAsync();

        Assert.Null(await fixture.Builder.BuildAsync(Guid.NewGuid(), fixture.TaskId, report.EventId));
        Assert.Null(await fixture.Builder.BuildAsync(fixture.Project.Id, fixture.TaskId, plainHandoff));
        Assert.Null(await fixture.Builder.BuildAsync(fixture.Project.Id, fixture.TaskId, Guid.NewGuid()));
    }

    [Theory]
    [InlineData(TaskLifecycleStatus.Working)]
    [InlineData(TaskLifecycleStatus.NeedsLeaderDecision)]
    [InlineData(TaskLifecycleStatus.Completed)]
    [InlineData(TaskLifecycleStatus.Cancelled)]
    public async Task Non_reviewing_assignment_produces_no_review_input(TaskLifecycleStatus status)
    {
        await using var fixture = await Fixture.CreateAsync(status);

        Assert.Null(await fixture.Builder.BuildAsync(fixture.Project.Id, fixture.TaskId, Guid.NewGuid()));
    }

    [Fact]
    public async Task Building_review_input_is_a_read_only_operation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var report = await fixture.AddFinalReportAsync();
        var before = await fixture.SnapshotAsync();

        _ = await fixture.Builder.BuildAsync(fixture.Project.Id, fixture.TaskId, report.EventId);

        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly AppTestContext _context;

        private Fixture(AppTestContext context, CoreProject project, Guid taskId, TaskRevision initialRevision, TaskRevision currentRevision)
        {
            _context = context;
            Project = project;
            TaskId = taskId;
            InitialRevision = initialRevision;
            CurrentRevision = currentRevision;
            Builder = new LeaderReviewInputBuilder(
                context.Services.ProjectRepository,
                context.Services.TaskRepository,
                context.Services.TaskRevisionRepository,
                new TaskEventRepository(context.Services.Database));
        }

        public CoreProject Project { get; }
        public Guid TaskId { get; }
        public TaskRevision InitialRevision { get; }
        public TaskRevision CurrentRevision { get; }
        public LeaderReviewInputBuilder Builder { get; }

        public static async Task<Fixture> CreateAsync(TaskLifecycleStatus initialStatus = TaskLifecycleStatus.Reviewing)
        {
            var context = await AppTestContext.CreateAsync();
            var now = context.Time.GetUtcNow();
            var project = new CoreProject(Guid.NewGuid(), "Review Project", "C:/ReviewProject", ProjectType.Generic, null, now, now);
            await context.Services.ProjectRepository.UpsertAsync(project);
            var profile = ExecutionProfile.Create("provider", "account", "model", "runtime");
            var taskId = Guid.NewGuid();
            var r1 = new TaskRevision(taskId, 1, "old goal", "old scope", "old out of scope", ["old acceptance"], TaskRiskLevel.Low, profile, "initial", TaskRevisionApprover.User, now, null);
            var draft = new TaskDraft(taskId, "Review assignment", r1.Goal, r1.Scope, r1.OutOfScope, r1.Acceptance, TaskRiskLevel.Low, profile, now, r1, initialStatus);
            await context.Services.TaskRepository.CreateAsync(project.Id, draft);
            var r2 = new TaskRevision(taskId, 2, "current goal", "current scope", "current out of scope", ["current acceptance"], TaskRiskLevel.Low, profile, "current", TaskRevisionApprover.User, now.AddMinutes(1), r1.Id);
            Assert.True(await context.Services.TaskRevisionRepository.CreateSuccessorAsync(project.Id, taskId, r1.Id, r2));
            return new Fixture(context, project, taskId, r1, r2);
        }

        public async Task<Report> AddFinalReportAsync(Guid? taskRevisionId = null, Guid? handoffTaskId = null)
        {
            var workerSessionId = AgentSessionId.New();
            var eventId = Guid.NewGuid();
            await new AssignmentReviewStateRepository(_context.Services.Database).TryTransitionAsync(
                Project.Id, TaskId, TaskLifecycleStatus.Reviewing, TaskLifecycleStatus.Working, Guid.NewGuid(), "Temporary", "{}", _context.Time.GetUtcNow());
            await new AssignmentReviewStateRepository(_context.Services.Database).TryTransitionAsync(
                Project.Id, TaskId, TaskLifecycleStatus.Working, TaskLifecycleStatus.Reviewing, eventId, "WorkerFinalReportReceived",
                JsonSerializer.Serialize(new { WorkerSessionId = workerSessionId.Value, Message = "final report body", ValidationSummary = "validation preserved verbatim" }), _context.Time.GetUtcNow());
            var handoff = new WorkerHandoff(Project.Id, handoffTaskId ?? TaskId, workerSessionId, "Worker", AgentSessionStatus.Completed,
                "final report body", _context.Time.GetUtcNow(), WorkerHandoffKind.FinalReport, "validation preserved verbatim", eventId, taskRevisionId ?? CurrentRevision.Id);
            await new TaskEventRepository(_context.Services.Database).AppendAsync(new StoredTaskEvent(Guid.NewGuid(), Project.Id, TaskId, null,
                "WorkerToLeaderHandoff", JsonSerializer.Serialize(handoff), _context.Time.GetUtcNow()));
            return new Report(eventId, workerSessionId);
        }

        public async Task<Guid> AppendPlainHandoffAsync()
        {
            var eventId = Guid.NewGuid();
            await new TaskEventRepository(_context.Services.Database).AppendAsync(new StoredTaskEvent(eventId, Project.Id, TaskId, null,
                "WorkerToLeaderHandoff", "plain handoff", _context.Time.GetUtcNow()));
            return eventId;
        }

        public async Task<(TaskLifecycleStatus Status, int EventCount, int LeaderMessageCount)> SnapshotAsync()
        {
            var task = (await _context.Services.TaskRepository.GetAsync(Project.Id, TaskId))!;
            var events = await new TaskEventRepository(_context.Services.Database).ListAsync(Project.Id, TaskId, 100);
            await using var connection = _context.Services.Database.CreateConnection();
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM leader_messages;";
            return (task.Status, events.Count, Convert.ToInt32(await command.ExecuteScalarAsync()));
        }

        public ValueTask DisposeAsync() => _context.DisposeAsync();
    }

    private sealed record Report(Guid EventId, AgentSessionId WorkerSessionId);
}
