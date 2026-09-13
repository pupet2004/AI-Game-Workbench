using Workbench.Core.Tasks;
using Workbench.Storage.Database;
using Workbench.Storage.Tasks;
using Workbench.Storage.Tests.Database;

namespace Workbench.Storage.Tests.Tasks;

public sealed class AssignmentReviewStateTests
{
    [Fact]
    public async Task Reviewing_assignment_records_one_bound_leader_review_decision_without_changing_status()
    {
        await using var fixture = await Fixture.CreateAsync(TaskLifecycleStatus.Reviewing);
        var finalReportId = Guid.NewGuid();
        await fixture.States.TryTransitionAsync(fixture.ProjectId, fixture.Task.TaskId,
            TaskLifecycleStatus.Reviewing, TaskLifecycleStatus.Working, Guid.NewGuid(), "Temporary", "{}", fixture.Now);
        await fixture.States.TryTransitionAsync(fixture.ProjectId, fixture.Task.TaskId,
            TaskLifecycleStatus.Working, TaskLifecycleStatus.Reviewing, finalReportId, "WorkerFinalReportReceived", "{}", fixture.Now);
        var request = new LeaderReviewDecisionPersistenceRequest(Guid.NewGuid(), fixture.ProjectId, fixture.Task.TaskId,
            fixture.Task.CurrentRevision.Id, finalReportId, "Pass", "L1LocalFix", "ReportOnly", "Summary", null, "None", null, fixture.Now);

        var result = await fixture.States.TryRecordLeaderReviewDecisionAsync(request);

        Assert.Equal(AssignmentStateTransitionResult.Applied, result);
        var state = (await fixture.States.GetRecoveryStateAsync(fixture.ProjectId, fixture.Task.TaskId))!;
        Assert.Equal(TaskLifecycleStatus.Reviewing, state.Task.Status);
        Assert.Single(state.Events, item => item.Type == "LeaderReviewDecisionRecorded");
        var restored = await new AssignmentReviewStateRepository(new WorkbenchDatabase(fixture.DatabasePath)).GetLeaderReviewDecisionAsync(fixture.ProjectId, fixture.Task.TaskId, fixture.Task.CurrentRevision.Id, finalReportId);
        Assert.NotNull(restored);
        Assert.Equal("Pass", restored.Outcome);
        Assert.Equal("ReportOnly", restored.ReviewDepth);
    }

    [Fact]
    public async Task Review_decision_replays_are_idempotent_by_event_and_final_report_source()
    {
        await using var fixture = await Fixture.CreateAsync(TaskLifecycleStatus.Reviewing);
        var source = Guid.NewGuid();
        await fixture.States.TryTransitionAsync(fixture.ProjectId, fixture.Task.TaskId, TaskLifecycleStatus.Reviewing, TaskLifecycleStatus.Working, Guid.NewGuid(), "Temporary", "{}", fixture.Now);
        await fixture.States.TryTransitionAsync(fixture.ProjectId, fixture.Task.TaskId, TaskLifecycleStatus.Working, TaskLifecycleStatus.Reviewing, source, "WorkerFinalReportReceived", "{}", fixture.Now);
        var first = new LeaderReviewDecisionPersistenceRequest(Guid.NewGuid(), fixture.ProjectId, fixture.Task.TaskId, fixture.Task.CurrentRevision.Id, source, "Pass", "L1LocalFix", "ReportOnly", "first", null, "none", null, fixture.Now);
        Assert.Equal(AssignmentStateTransitionResult.Applied, await fixture.States.TryRecordLeaderReviewDecisionAsync(first));
        Assert.Equal(AssignmentStateTransitionResult.Idempotent, await fixture.States.TryRecordLeaderReviewDecisionAsync(first));
        Assert.Equal(AssignmentStateTransitionResult.Idempotent, await fixture.States.TryRecordLeaderReviewDecisionAsync(first with { EventId = Guid.NewGuid(), Summary = "second" }));
        var state = (await fixture.States.GetRecoveryStateAsync(fixture.ProjectId, fixture.Task.TaskId))!;
        Assert.Single(state.Events, item => item.Type == "LeaderReviewDecisionRecorded");
    }

    [Fact]
    public async Task Review_decision_rejects_wrong_project_task_revision_and_source_without_writes()
    {
        await using var fixture = await Fixture.CreateAsync(TaskLifecycleStatus.Reviewing);
        var source = await AddFinalReportAsync(fixture);
        var before = (await fixture.States.GetRecoveryStateAsync(fixture.ProjectId, fixture.Task.TaskId))!;
        var request = Decision(fixture, source);
        var invalid = new[]
        {
            request with { ProjectId = Guid.NewGuid() },
            request with { TaskId = Guid.NewGuid() },
            request with { TaskRevisionId = Guid.NewGuid() },
            request with { FinalReportEventId = Guid.NewGuid() }
        };
        foreach (var item in invalid) Assert.NotEqual(AssignmentStateTransitionResult.Applied, await fixture.States.TryRecordLeaderReviewDecisionAsync(item));
        var after = (await fixture.States.GetRecoveryStateAsync(fixture.ProjectId, fixture.Task.TaskId))!;
        Assert.Equal(TaskLifecycleStatus.Reviewing, after.Task.Status);
        Assert.Equal(before.Events.Count, after.Events.Count);
        Assert.DoesNotContain(after.Events, item => item.Type == "LeaderReviewDecisionRecorded");
    }

    [Fact]
    public async Task Review_decision_rejects_cross_task_source_and_non_reviewing_states_without_partial_write()
    {
        await using var fixture = await Fixture.CreateAsync(TaskLifecycleStatus.Reviewing);
        var source = await AddFinalReportAsync(fixture);
        var otherTask = Guid.NewGuid();
        var otherRevision = Guid.NewGuid();
        await InsertTaskAsync(fixture, otherTask, otherRevision, TaskLifecycleStatus.Reviewing);
        await fixture.States.TryTransitionAsync(fixture.ProjectId, otherTask, TaskLifecycleStatus.Reviewing, TaskLifecycleStatus.Working, Guid.NewGuid(), "Temporary", "{}", fixture.Now);
        var otherSource = Guid.NewGuid();
        await fixture.States.TryTransitionAsync(fixture.ProjectId, otherTask, TaskLifecycleStatus.Working, TaskLifecycleStatus.Reviewing, otherSource, "WorkerFinalReportReceived", "{}", fixture.Now);
        Assert.NotEqual(AssignmentStateTransitionResult.Applied, await fixture.States.TryRecordLeaderReviewDecisionAsync(Decision(fixture, otherSource)));
        var working = await Fixture.CreateAsync(TaskLifecycleStatus.Working);
        await using (working)
        {
            Assert.NotEqual(AssignmentStateTransitionResult.Applied, await working.States.TryRecordLeaderReviewDecisionAsync(Decision(working, Guid.NewGuid())));
            Assert.Empty((await working.States.GetRecoveryStateAsync(working.ProjectId, working.Task.TaskId))!.Events);
        }
        await using var completed = await Fixture.CreateAsync(TaskLifecycleStatus.Completed);
        Assert.NotEqual(AssignmentStateTransitionResult.Applied, await completed.States.TryRecordLeaderReviewDecisionAsync(Decision(completed, Guid.NewGuid())));
        Assert.Equal(TaskLifecycleStatus.Completed, (await completed.States.GetRecoveryStateAsync(completed.ProjectId, completed.Task.TaskId))!.Task.Status);
        Assert.DoesNotContain((await fixture.States.GetRecoveryStateAsync(fixture.ProjectId, fixture.Task.TaskId))!.Events, item => item.Type == "LeaderReviewDecisionRecorded");
    }

    [Fact]
    public async Task Review_decision_rejects_unbounded_persistence_request_without_write()
    {
        await using var fixture = await Fixture.CreateAsync(TaskLifecycleStatus.Reviewing);
        var source = await AddFinalReportAsync(fixture);
        var result = await fixture.States.TryRecordLeaderReviewDecisionAsync(Decision(fixture, source) with { Summary = new string('x', 601) });
        Assert.Equal(AssignmentStateTransitionResult.Conflict, result);
        Assert.DoesNotContain((await fixture.States.GetRecoveryStateAsync(fixture.ProjectId, fixture.Task.TaskId))!.Events, item => item.Type == "LeaderReviewDecisionRecorded");
    }

    private static LeaderReviewDecisionPersistenceRequest Decision(Fixture fixture, Guid source) => new(Guid.NewGuid(), fixture.ProjectId, fixture.Task.TaskId, fixture.Task.CurrentRevision.Id, source, "Pass", "L1LocalFix", "ReportOnly", "Summary", null, "None", null, fixture.Now);
    private static async Task<Guid> AddFinalReportAsync(Fixture fixture) { var source=Guid.NewGuid(); await fixture.States.TryTransitionAsync(fixture.ProjectId, fixture.Task.TaskId, TaskLifecycleStatus.Reviewing, TaskLifecycleStatus.Working, Guid.NewGuid(), "Temporary", "{}", fixture.Now); await fixture.States.TryTransitionAsync(fixture.ProjectId, fixture.Task.TaskId, TaskLifecycleStatus.Working, TaskLifecycleStatus.Reviewing, source, "WorkerFinalReportReceived", "{}", fixture.Now); return source; }
    private static async Task InsertTaskAsync(Fixture fixture, Guid taskId, Guid revisionId, TaskLifecycleStatus status) { await using var c=fixture.StatesDatabase.CreateConnection(); await c.OpenAsync(); var q=c.CreateCommand(); q.CommandText="INSERT INTO tasks(id,project_id,title,status,current_revision_id,created_at,updated_at) VALUES($i,$p,'Other',$s,$r,$a,$a);";q.Parameters.AddWithValue("$i",taskId.ToString());q.Parameters.AddWithValue("$p",fixture.ProjectId.ToString());q.Parameters.AddWithValue("$s",status.ToString());q.Parameters.AddWithValue("$r",revisionId.ToString());q.Parameters.AddWithValue("$a",fixture.Now.ToString("O"));await q.ExecuteNonQueryAsync(); }
    [Fact]
    public async Task Transition_updates_current_status_and_appends_one_recovery_event_atomically()
    {
        await using var fixture = await Fixture.CreateAsync();
        var eventId = Guid.NewGuid();

        var result = await fixture.States.TryTransitionAsync(fixture.ProjectId, fixture.Task.TaskId,
            TaskLifecycleStatus.Draft, TaskLifecycleStatus.ReadyToStart, eventId,
            "AssignmentReady", "{}", fixture.Now);

        Assert.Equal(AssignmentStateTransitionResult.Applied, result);
        var state = await fixture.States.GetRecoveryStateAsync(fixture.ProjectId, fixture.Task.TaskId);
        Assert.NotNull(state);
        Assert.Equal(TaskLifecycleStatus.ReadyToStart, state.Task.Status);
        var entry = Assert.Single(state.Events);
        Assert.Equal(eventId, entry.EventId);
        Assert.Equal("AssignmentReady", entry.Type);
        Assert.Equal("{}", entry.Payload);
    }

    [Fact]
    public async Task Duplicate_event_is_idempotent_and_does_not_append_or_mutate_again()
    {
        await using var fixture = await Fixture.CreateAsync();
        var eventId = Guid.NewGuid();
        await fixture.States.TryTransitionAsync(fixture.ProjectId, fixture.Task.TaskId,
            TaskLifecycleStatus.Draft, TaskLifecycleStatus.ReadyToStart, eventId, "AssignmentReady", "{}", fixture.Now);

        var duplicate = await fixture.States.TryTransitionAsync(fixture.ProjectId, fixture.Task.TaskId,
            TaskLifecycleStatus.Draft, TaskLifecycleStatus.ReadyToStart, eventId, "AssignmentReady", "{}", fixture.Now.AddMinutes(1));

        Assert.Equal(AssignmentStateTransitionResult.Idempotent, duplicate);
        var state = (await fixture.States.GetRecoveryStateAsync(fixture.ProjectId, fixture.Task.TaskId))!;
        Assert.Single(state.Events);
        Assert.Equal(fixture.Now, state.Task.UpdatedAt);
    }

    [Fact]
    public async Task Stale_expected_state_writes_nothing()
    {
        await using var fixture = await Fixture.CreateAsync();
        var result = await fixture.States.TryTransitionAsync(fixture.ProjectId, fixture.Task.TaskId,
            TaskLifecycleStatus.ReadyToStart, TaskLifecycleStatus.Working, Guid.NewGuid(), "AssignmentStarted", "{}", fixture.Now);

        Assert.Equal(AssignmentStateTransitionResult.Conflict, result);
        var state = (await fixture.States.GetRecoveryStateAsync(fixture.ProjectId, fixture.Task.TaskId))!;
        Assert.Equal(TaskLifecycleStatus.Draft, state.Task.Status);
        Assert.Empty(state.Events);
    }

    [Fact]
    public async Task Cross_project_transition_is_rejected_without_mutating_assignment()
    {
        await using var fixture = await Fixture.CreateAsync();
        var result = await fixture.States.TryTransitionAsync(Guid.NewGuid(), fixture.Task.TaskId,
            TaskLifecycleStatus.Draft, TaskLifecycleStatus.ReadyToStart, Guid.NewGuid(), "AssignmentReady", "{}", fixture.Now);

        Assert.Equal(AssignmentStateTransitionResult.NotFound, result);
        var state = (await fixture.States.GetRecoveryStateAsync(fixture.ProjectId, fixture.Task.TaskId))!;
        Assert.Equal(TaskLifecycleStatus.Draft, state.Task.Status);
        Assert.Empty(state.Events);
    }

    [Fact]
    public async Task Transition_does_not_create_or_change_task_revision()
    {
        await using var fixture = await Fixture.CreateAsync();
        var originalRevision = fixture.Task.CurrentRevision.Id;

        await fixture.States.TryTransitionAsync(fixture.ProjectId, fixture.Task.TaskId,
            TaskLifecycleStatus.Draft, TaskLifecycleStatus.ReadyToStart, Guid.NewGuid(), "AssignmentReady", "{}", fixture.Now);

        var task = (await fixture.Tasks.GetAsync(fixture.ProjectId, fixture.Task.TaskId))!;
        Assert.Equal(originalRevision, task.CurrentRevisionId);
        Assert.Single(await fixture.Revisions.ListAsync(fixture.Task.TaskId));
    }

    [Fact]
    public async Task Completed_transition_preserves_task_and_worker_references()
    {
        await using var fixture = await Fixture.CreateAsync(TaskLifecycleStatus.Reviewing);

        var result = await fixture.States.TryTransitionAsync(fixture.ProjectId, fixture.Task.TaskId,
            TaskLifecycleStatus.Reviewing, TaskLifecycleStatus.Completed, Guid.NewGuid(), "AssignmentCompleted", "{}", fixture.Now);

        Assert.Equal(AssignmentStateTransitionResult.Applied, result);
        Assert.NotNull(await fixture.Tasks.GetAsync(fixture.ProjectId, fixture.Task.TaskId));
    }

    [Fact]
    public async Task Recovery_state_survives_database_restart()
    {
        await using var fixture = await Fixture.CreateAsync();
        var eventId = Guid.NewGuid();
        await fixture.States.TryTransitionAsync(fixture.ProjectId, fixture.Task.TaskId,
            TaskLifecycleStatus.Draft, TaskLifecycleStatus.ReadyToStart, eventId, "AssignmentReady", "{}", fixture.Now);

        var restarted = new AssignmentReviewStateRepository(new WorkbenchDatabase(fixture.DatabasePath));
        var state = (await restarted.GetRecoveryStateAsync(fixture.ProjectId, fixture.Task.TaskId))!;
        Assert.Equal(TaskLifecycleStatus.ReadyToStart, state.Task.Status);
        Assert.Equal(eventId, Assert.Single(state.Events).EventId);
    }

    [Fact]
    public async Task Existing_draft_row_remains_readable_after_migration()
    {
        await using var fixture = await Fixture.CreateAsync();
        var task = await fixture.Tasks.GetAsync(fixture.ProjectId, fixture.Task.TaskId);

        Assert.NotNull(task);
        Assert.Equal(TaskLifecycleStatus.Draft, task.Status);
    }

    [Fact]
    public async Task Legacy_v11_task_and_event_survive_status_constraint_migration()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        var projectId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        const string timestamp = "2026-08-14T09:00:00.0000000+00:00";
        await HistoricalMigrationTestDatabase.InitializeThroughAsync(database, 11);

        await using (var connection = database.CreateConnection())
        {
            await connection.OpenAsync();
            var seed = connection.CreateCommand();
            seed.CommandText = """
                INSERT INTO projects(id,name,root_path,project_type,git_root,created_at,last_opened_at)
                    VALUES($projectId,'Legacy','C:/Legacy',0,NULL,$timestamp,$timestamp);
                INSERT INTO tasks(id,project_id,title,status,current_revision_id,created_at,updated_at)
                    VALUES($taskId,$projectId,'Legacy','Draft',$revisionId,$timestamp,$timestamp);
                INSERT INTO task_events(id,project_id,task_id,event_type,payload_json,created_at)
                    VALUES($eventId,$projectId,$taskId,'LegacyEvent','{}',$timestamp);
                """;
            seed.Parameters.AddWithValue("$projectId", projectId.ToString());
            seed.Parameters.AddWithValue("$taskId", taskId.ToString());
            seed.Parameters.AddWithValue("$revisionId", revisionId.ToString());
            seed.Parameters.AddWithValue("$eventId", eventId.ToString());
            seed.Parameters.AddWithValue("$timestamp", timestamp);
            await seed.ExecuteNonQueryAsync();
        }

        await database.InitializeAsync();

        var state = (await new AssignmentReviewStateRepository(database).GetRecoveryStateAsync(projectId, taskId))!;
        Assert.Equal(TaskLifecycleStatus.Draft, state.Task.Status);
        Assert.Equal(eventId, Assert.Single(state.Events).EventId);
        await using var verified = database.CreateConnection();
        await verified.OpenAsync();
        var version = verified.CreateCommand();
        version.CommandText = "PRAGMA user_version;";
        Assert.Equal(31L, Convert.ToInt64(await version.ExecuteScalarAsync()));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly TemporaryDatabase _temporary;
        public string DatabasePath => _temporary.DatabasePath;
        public WorkbenchDatabase StatesDatabase { get; }
        public Guid ProjectId { get; } = Guid.NewGuid();
        public DateTimeOffset Now { get; } = new(2026, 8, 14, 9, 0, 0, TimeSpan.Zero);
        public TaskDraft Task { get; }
        public TaskRepository Tasks { get; }
        public TaskRevisionRepository Revisions { get; }
        public AssignmentReviewStateRepository States { get; }

        private Fixture(TemporaryDatabase temporary, WorkbenchDatabase database, TaskLifecycleStatus initialStatus)
        {
            _temporary = temporary;
            StatesDatabase = database;
            Tasks = new TaskRepository(database);
            Revisions = new TaskRevisionRepository(database);
            States = new AssignmentReviewStateRepository(database);
            var profile = ExecutionProfile.Create("provider", "account", "model", "runtime");
            var taskId = Guid.NewGuid();
            var revision = new TaskRevision(taskId, 1, "goal", "scope", "out", ["accept"], TaskRiskLevel.Low,
                profile, "initial", TaskRevisionApprover.User, Now, null);
            Task = new TaskDraft(taskId, "Title", "goal", "scope", "out", ["accept"], TaskRiskLevel.Low,
                profile, Now, revision, initialStatus);
        }

        public static async Task<Fixture> CreateAsync(TaskLifecycleStatus initialStatus = TaskLifecycleStatus.Draft)
        {
            var temporary = new TemporaryDatabase();
            var database = new WorkbenchDatabase(temporary.DatabasePath);
            await database.InitializeAsync();
            var fixture = new Fixture(temporary, database, initialStatus);
            await using var connection = database.CreateConnection();
            await connection.OpenAsync();
            var project = connection.CreateCommand();
            project.CommandText = "INSERT INTO projects(id,name,root_path,project_type,created_at,last_opened_at) VALUES($id,'P','C:/P',0,$at,$at);";
            project.Parameters.AddWithValue("$id", fixture.ProjectId.ToString());
            project.Parameters.AddWithValue("$at", fixture.Now.ToString("O"));
            await project.ExecuteNonQueryAsync();
            await fixture.Tasks.CreateAsync(fixture.ProjectId, fixture.Task);
            return fixture;
        }

        public ValueTask DisposeAsync() => _temporary.DisposeAsync();
    }
}
