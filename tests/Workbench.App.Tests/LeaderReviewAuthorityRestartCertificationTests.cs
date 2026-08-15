using System.Text.Json;
using Workbench.App.Leader;
using Workbench.App.Services;
using Workbench.App.Worker;
using Workbench.App.Tests.Support;
using Workbench.Core.Leaders;
using Workbench.Core.Projects;
using Workbench.Core.Tasks;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Providers;
using Workbench.Storage.Leaders;
using Workbench.Storage.Reviews;
using Workbench.Storage.Tasks;
using Workbench.Storage.Workers;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.Tests;

public sealed class LeaderReviewAuthorityRestartCertificationTests
{
    [Fact]
    public async Task Typed_gate_absence_wins_over_legacy_ask_user_events()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.DeleteTypedGateAsync();
        await fixture.RestartAsync();

        var recovered = await fixture.Services.LeaderReviewAskUserGate.RecoverAsync(fixture.Project.Id);

        Assert.Empty(recovered);
        Assert.Null(await fixture.TypedState.GetGateAsync(fixture.Project.Id, fixture.TaskId, fixture.ReviewDecisionId));
    }

    [Fact]
    public async Task Typed_open_gate_survives_corrupt_legacy_question_detail()
    {
        await using var fixture = await Fixture.CreateAsync();
        var beforeMessages = await fixture.CountMessagesAsync();
        await fixture.CorruptLegacyReviewEventsAsync();
        await fixture.RestartAsync();

        var recovered = await fixture.Services.LeaderReviewAskUserGate.RecoverAsync(fixture.Project.Id);

        Assert.Equal(LeaderReviewAskUserGateResultKind.Existing, Assert.Single(recovered).Kind);
        Assert.Equal(beforeMessages, await fixture.CountMessagesAsync());
        Assert.Equal("Open", (await fixture.TypedState.GetGateAsync(fixture.Project.Id, fixture.TaskId, fixture.ReviewDecisionId))!.State);
    }

    [Fact]
    public async Task Responded_source_messages_can_disappear_and_state_survives_restart()
    {
        await using var fixture = await Fixture.CreateAsync();
        var response = await fixture.Messages.AppendAsync(fixture.EpochId, "user", "source to delete", fixture.Time.GetUtcNow());
        Assert.NotNull(await fixture.Services.LeaderReviewUserResponseBinder.BindAsync(fixture.Project.Id, response.Id));
        var before = await fixture.TypedState.GetGateAsync(fixture.Project.Id, fixture.TaskId, fixture.ReviewDecisionId);
        Assert.NotNull(before);
        Assert.Equal("Responded", before.State);
        Assert.NotNull(before.RespondedAt);

        await using (var connection = fixture.Services.Database.CreateConnection())
        {
            await connection.OpenAsync();
            var delete = connection.CreateCommand();
            delete.CommandText = "DELETE FROM leader_messages WHERE id=$question OR id=$response;";
            delete.Parameters.AddWithValue("$question", fixture.QuestionMessageId);
            delete.Parameters.AddWithValue("$response", response.Id);
            Assert.Equal(2, await delete.ExecuteNonQueryAsync());
        }
        await fixture.RestartAsync();

        await fixture.RecoverReviewOwnersAsync();
        var after = await fixture.TypedState.GetGateAsync(fixture.Project.Id, fixture.TaskId, fixture.ReviewDecisionId);
        Assert.NotNull(after);
        Assert.Equal("Responded", after.State);
        Assert.Equal(before.RespondedAt, after.RespondedAt);
        Assert.Null(after.QuestionMessageId);
        Assert.Null(after.UserMessageId);
        Assert.NotNull(await fixture.TypedState.GetDecisionByFinalReportAsync(fixture.Project.Id, fixture.TaskId, fixture.FinalReportEventId));
        Assert.Empty(await fixture.Messages.GetAllAsync(fixture.EpochId));

        var later = await fixture.Messages.AppendAsync(fixture.EpochId, "user", "must not rebind", fixture.Time.GetUtcNow().AddSeconds(1));
        Assert.Null(await fixture.Services.LeaderReviewUserResponseBinder.BindAsync(fixture.Project.Id, later.Id));
        Assert.Equal(1L, await fixture.CountRowsAsync("task_review_user_gates"));
    }

    [Fact]
    public async Task Review_state_recovers_when_agent_runtime_is_unavailable()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.Empty(fixture.Services.RuntimeRegistry.Runtimes);
        Assert.Throws<KeyNotFoundException>(() => fixture.Services.RuntimeRegistry.GetByAccount(new ProviderAccountId(Guid.NewGuid())));
        var beforeMessages = await fixture.CountMessagesAsync();
        await fixture.RestartAsync();
        Assert.Empty(fixture.Services.RuntimeRegistry.Runtimes);

        await fixture.RecoverReviewOwnersAsync();

        var decision = await fixture.TypedState.GetDecisionByFinalReportAsync(fixture.Project.Id, fixture.TaskId, fixture.FinalReportEventId);
        Assert.NotNull(decision);
        Assert.Equal(fixture.ReviewDecisionId, decision.ReviewDecisionId);
        Assert.Equal(nameof(LeaderReviewOutcome.AskUser), decision.Outcome);
        Assert.Equal(nameof(LeaderReviewActionLevel.L3DecisionRequired), decision.ActionLevel);
        Assert.Equal(nameof(LeaderAuthorityResolution.AskUser), decision.AuthorityResolution);
        Assert.Equal(LeaderAuthorityMode.Balanced, decision.AuthorityMode);
        Assert.Equal("Open", (await fixture.TypedState.GetGateAsync(fixture.Project.Id, fixture.TaskId, fixture.ReviewDecisionId))!.State);
        Assert.Equal(beforeMessages, await fixture.CountMessagesAsync());
    }

    [Fact]
    public async Task Missing_final_report_source_does_not_remove_typed_review_authority()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.DeleteFinalReportSourceAsync();
        await fixture.RestartAsync();

        await fixture.RecoverReviewOwnersAsync();

        var decision = await fixture.TypedState.GetDecisionByFinalReportAsync(fixture.Project.Id, fixture.TaskId, fixture.FinalReportEventId);
        Assert.NotNull(decision);
        Assert.Equal(fixture.ReviewDecisionId, decision.ReviewDecisionId);
        var gate = await fixture.TypedState.GetGateAsync(fixture.Project.Id, fixture.TaskId, fixture.ReviewDecisionId);
        Assert.NotNull(gate);
        Assert.Equal("Open", gate.State);
    }

    [Fact]
    public async Task Legacy_not_recorded_authority_survives_restart_without_inference()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using (var connection = fixture.Services.Database.CreateConnection())
        {
            await connection.OpenAsync();
            var historical = connection.CreateCommand();
            historical.CommandText = "UPDATE task_review_decisions SET authority_mode=NULL,authority_mode_recording='LegacyNotRecorded' WHERE review_decision_id=$id;";
            historical.Parameters.AddWithValue("$id", fixture.ReviewDecisionId.ToString());
            Assert.Equal(1, await historical.ExecuteNonQueryAsync());
        }
        await fixture.Services.ProjectSettingsRepository.SaveLeaderAuthorityModeOverrideAsync(fixture.Project.Id, LeaderAuthorityMode.Autonomous);
        await fixture.RestartAsync();

        await fixture.RecoverReviewOwnersAsync();

        var decision = await fixture.TypedState.GetDecisionByFinalReportAsync(fixture.Project.Id, fixture.TaskId, fixture.FinalReportEventId);
        Assert.NotNull(decision);
        Assert.Null(decision.AuthorityMode);
        Assert.Equal("LegacyNotRecorded", decision.AuthorityModeRecording);
        Assert.Equal(nameof(LeaderAuthorityResolution.AskUser), decision.AuthorityResolution);
        Assert.Equal(LeaderAuthorityMode.Autonomous, await fixture.Services.LeaderAuthoritySettings.GetEffectiveLeaderAuthorityModeAsync(fixture.Project.Id));

        var persistedAgain = await fixture.TypedState.GetDecisionAsync(fixture.Project.Id, fixture.TaskId, fixture.ReviewDecisionId);
        Assert.NotNull(persistedAgain);
        Assert.Null(persistedAgain.AuthorityMode);
        Assert.Equal("LegacyNotRecorded", persistedAgain.AuthorityModeRecording);
    }

    [Fact]
    public async Task Corrupt_legacy_review_json_does_not_replace_typed_authority_on_reopen()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.CorruptLegacyReviewEventsAsync();
        await fixture.RestartAsync();

        var result = await fixture.Services.LeaderReviewAskUserGate.TryOpenAsync(
            fixture.Project.Id,
            fixture.TaskId,
            fixture.Revision.Id,
            fixture.FinalReportEventId);

        Assert.Equal(LeaderReviewAskUserGateResultKind.Existing, result.Kind);
        var decision = await fixture.TypedState.GetDecisionByFinalReportAsync(
            fixture.Project.Id,
            fixture.TaskId,
            fixture.FinalReportEventId);
        Assert.NotNull(decision);
        Assert.Equal(fixture.ReviewDecisionId, decision.ReviewDecisionId);
        Assert.Equal(nameof(LeaderReviewOutcome.AskUser), decision.Outcome);
        Assert.Equal(nameof(LeaderReviewActionLevel.L3DecisionRequired), decision.ActionLevel);
        Assert.Equal(nameof(LeaderAuthorityResolution.AskUser), decision.AuthorityResolution);
        Assert.Equal(LeaderAuthorityMode.Balanced, decision.AuthorityMode);
        Assert.Equal("Recorded", decision.AuthorityModeRecording);
        Assert.Equal("Open", (await fixture.TypedState.GetGateAsync(fixture.Project.Id, fixture.TaskId, fixture.ReviewDecisionId))!.State);
    }

    [Fact]
    public async Task Open_gate_restarts_through_real_owner_without_duplicates_or_llm()
    {
        await using var fixture = await Fixture.CreateAsync();
        var beforeMessages = await fixture.CountMessagesAsync();
        var beforeDecisions = await fixture.CountRowsAsync("task_review_decisions");
        var beforeGates = await fixture.CountRowsAsync("task_review_user_gates");
        await fixture.CorruptLegacyReviewEventsAsync();
        await fixture.RestartAsync();

        var recovered = await fixture.Services.LeaderReviewAskUserGate.RecoverAsync(fixture.Project.Id);

        Assert.Equal(LeaderReviewAskUserGateResultKind.Existing, Assert.Single(recovered).Kind);
        var gate = await fixture.TypedState.GetGateAsync(fixture.Project.Id, fixture.TaskId, fixture.ReviewDecisionId);
        Assert.NotNull(gate);
        Assert.Equal("Open", gate.State);
        Assert.Equal(fixture.QuestionMessageId, gate.QuestionMessageId);
        Assert.Contains(await fixture.Messages.GetAllAsync(fixture.EpochId), message => message.Id == fixture.QuestionMessageId);
        Assert.Equal(beforeMessages, await fixture.CountMessagesAsync());
        Assert.Equal(beforeDecisions, await fixture.CountRowsAsync("task_review_decisions"));
        Assert.Equal(beforeGates, await fixture.CountRowsAsync("task_review_user_gates"));
    }

    [Fact]
    public async Task Responded_gate_restarts_with_first_binding_and_no_rebind()
    {
        await using var fixture = await Fixture.CreateAsync();
        var firstResponse = await fixture.Messages.AppendAsync(
            fixture.EpochId,
            "user",
            "first response",
            fixture.Time.GetUtcNow());
        var binding = await fixture.Services.LeaderReviewUserResponseBinder.BindAsync(fixture.Project.Id, firstResponse.Id);
        Assert.NotNull(binding);
        var before = await fixture.TypedState.GetGateAsync(fixture.Project.Id, fixture.TaskId, fixture.ReviewDecisionId);
        Assert.NotNull(before);
        Assert.Equal("Responded", before.State);
        await fixture.CorruptLegacyReviewEventsAsync();
        await fixture.RestartAsync();

        Assert.Empty(await fixture.Services.LeaderReviewAskUserGate.RecoverAsync(fixture.Project.Id));
        var after = await fixture.TypedState.GetGateAsync(fixture.Project.Id, fixture.TaskId, fixture.ReviewDecisionId);
        Assert.NotNull(after);
        Assert.Equal("Responded", after.State);
        Assert.Equal(before.RespondedAt, after.RespondedAt);
        Assert.Equal(firstResponse.Id, after.UserMessageId);
        var decision = await fixture.TypedState.GetDecisionByFinalReportAsync(fixture.Project.Id, fixture.TaskId, fixture.FinalReportEventId);
        Assert.NotNull(decision);
        Assert.Equal(fixture.ReviewDecisionId, decision.ReviewDecisionId);
        Assert.Equal(firstResponse.Id, await fixture.Services.LeaderReviewUserResponseBinder.GetBoundUserMessageIdAsync(fixture.Project.Id, fixture.TaskId, fixture.ReviewDecisionId));

        var secondResponse = await fixture.Messages.AppendAsync(
            fixture.EpochId,
            "user",
            "second response",
            fixture.Time.GetUtcNow().AddSeconds(1));
        Assert.Null(await fixture.Services.LeaderReviewUserResponseBinder.BindAsync(fixture.Project.Id, secondResponse.Id));
        Assert.Equal(1L, await fixture.CountRowsAsync("task_review_user_gates"));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly TemporaryDirectory _directory;

        private Fixture(
            TemporaryDirectory directory,
            AppServices services,
            MutableTimeProvider time,
            CoreProject project,
            Guid taskId,
            TaskRevision revision,
            Guid epochId,
            Guid finalReportEventId,
            Guid reviewDecisionId,
            long questionMessageId)
        {
            _directory = directory;
            Services = services;
            Time = time;
            Project = project;
            TaskId = taskId;
            Revision = revision;
            EpochId = epochId;
            FinalReportEventId = finalReportEventId;
            ReviewDecisionId = reviewDecisionId;
            QuestionMessageId = questionMessageId;
            Messages = new LeaderMessageRepository(services.Database);
            TypedState = new LeaderReviewStateRepository(services.Database);
        }

        public AppServices Services { get; private set; }
        public MutableTimeProvider Time { get; }
        public CoreProject Project { get; }
        public Guid TaskId { get; }
        public TaskRevision Revision { get; }
        public Guid EpochId { get; }
        public Guid FinalReportEventId { get; }
        public Guid ReviewDecisionId { get; }
        public long QuestionMessageId { get; }
        public LeaderMessageRepository Messages { get; private set; }
        public LeaderReviewStateRepository TypedState { get; private set; }

        public static async Task<Fixture> CreateAsync()
        {
            var directory = new TemporaryDirectory("review-authority-certification");
            var time = new MutableTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00.0000000+00:00"));
            var services = AppServices.CreateForDatabasePath(Path.Combine(directory.Path, "workbench.db"), time);
            await services.InitializeAsync();

            var now = time.GetUtcNow();
            var project = new CoreProject(Guid.NewGuid(), "C1", "C:/C1", ProjectType.Generic, null, now, now);
            await services.ProjectRepository.UpsertAsync(project);
            var taskId = Guid.NewGuid();
            var profile = ExecutionProfile.Create("provider", "account", "model", "runtime");
            var revision = new TaskRevision(taskId, 1, "goal", "scope", "out", ["accept"], TaskRiskLevel.Low, profile, "initial", TaskRevisionApprover.User, now, null);
            await services.TaskRepository.CreateAsync(project.Id, new TaskDraft(taskId, "Task", "goal", "scope", "out", ["accept"], TaskRiskLevel.Low, profile, now, revision, TaskLifecycleStatus.Reviewing));

            var epochId = Guid.NewGuid();
            await services.ProjectLeaderRepository.CreateCurrentEpochAsync(
                new StoredProjectLeader(project.Id, null, now, now),
                new StoredLeaderSessionEpoch(epochId, project.Id, "provider", Guid.NewGuid(), "model", Guid.NewGuid(), "leader", project.RootPath, now, now, null, null, null));

            var finalReportEventId = Guid.NewGuid();
            var workerSessionId = Guid.NewGuid();
            await new TaskEventRepository(services.Database).AppendAsync(new StoredTaskEvent(
                finalReportEventId,
                project.Id,
                taskId,
                null,
                "WorkerFinalReportReceived",
                 JsonSerializer.Serialize(new { WorkerSessionId = workerSessionId, Message = "final report" }),
                 now));
            await new TaskEventWorkerRoutingStore(new TaskEventRepository(services.Database)).AppendHandoffAsync(
                new WorkerHandoff(project.Id, taskId, new AgentSessionId(workerSessionId), "Worker", AgentSessionStatus.Completed,
                    "final report", now, WorkerHandoffKind.FinalReport, null, finalReportEventId, revision.Id));

            var reviewDecisionId = Guid.NewGuid();
            var legacy = new AssignmentReviewStateRepository(services.Database);
            Assert.Equal(AssignmentStateTransitionResult.Applied, await legacy.TryRecordLeaderReviewDecisionAsync(new LeaderReviewDecisionPersistenceRequest(
                reviewDecisionId,
                project.Id,
                taskId,
                revision.Id,
                finalReportEventId,
                nameof(LeaderReviewOutcome.AskUser),
                nameof(LeaderReviewActionLevel.L3DecisionRequired),
                "ReportOnly",
                "summary",
                "issue",
                "choose",
                "note",
                now)));
            var typed = new LeaderReviewStateRepository(services.Database);
            Assert.Equal(LeaderReviewWriteResult.Applied, await typed.InsertDecisionIfAbsentAsync(new LeaderReviewDecisionWriteRequest(
                reviewDecisionId,
                project.Id,
                taskId,
                revision.Id,
                finalReportEventId,
                nameof(LeaderReviewOutcome.AskUser),
                nameof(LeaderReviewActionLevel.L3DecisionRequired),
                nameof(LeaderAuthorityResolution.AskUser),
                LeaderAuthorityMode.Balanced,
                now)));

            var opened = await services.LeaderReviewAskUserGate.TryOpenAsync(project.Id, taskId, revision.Id, finalReportEventId);
            Assert.Equal(LeaderReviewAskUserGateResultKind.Opened, opened.Kind);
            var gate = await typed.GetGateAsync(project.Id, taskId, reviewDecisionId);
            Assert.NotNull(gate);
            Assert.Equal("Open", gate.State);
            return new Fixture(directory, services, time, project, taskId, revision, epochId, finalReportEventId, reviewDecisionId, gate.QuestionMessageId!.Value);
        }

        public async Task CorruptLegacyReviewEventsAsync()
        {
            await using var connection = Services.Database.CreateConnection();
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = "UPDATE task_events SET payload_json = '{malformed' WHERE project_id=$p AND event_type IN ('LeaderReviewDecisionRecorded','AssignmentNeedsUserDecision','LeaderReviewUserResponseReceived');";
            command.Parameters.AddWithValue("$p", Project.Id.ToString());
            await command.ExecuteNonQueryAsync();
        }

        public async Task DeleteTypedGateAsync()
        {
            await using var connection = Services.Database.CreateConnection();
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM task_review_user_gates WHERE project_id=$p AND review_decision_id=$d;";
            command.Parameters.AddWithValue("$p", Project.Id.ToString());
            command.Parameters.AddWithValue("$d", ReviewDecisionId.ToString());
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        public async Task DeleteFinalReportSourceAsync()
        {
            await using var connection = Services.Database.CreateConnection();
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM task_events WHERE project_id=$p AND id=$id;";
            command.Parameters.AddWithValue("$p", Project.Id.ToString());
            command.Parameters.AddWithValue("$id", FinalReportEventId.ToString());
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        public async Task RestartAsync()
        {
            await Services.DisposeAsync();
            Services = AppServices.CreateForDatabasePath(_directory.Path + "\\workbench.db", Time);
            await Services.InitializeAsync();
            Messages = new LeaderMessageRepository(Services.Database);
            TypedState = new LeaderReviewStateRepository(Services.Database);
        }

        public async Task RecoverReviewOwnersAsync()
        {
            await Services.LeaderReviewOrchestrator.RecoverAsync(Project.Id);
            await Services.LeaderReviewAutoProceed.RecoverAsync(Project.Id);
            await Services.LeaderReviewAskUserGate.RecoverAsync(Project.Id);
        }

        public async Task<long> CountMessagesAsync()
        {
            await using var connection = Services.Database.CreateConnection();
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM leader_messages WHERE epoch_id=$e;";
            command.Parameters.AddWithValue("$e", EpochId.ToString());
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }

        public async Task<long> CountRowsAsync(string table)
        {
            await using var connection = Services.Database.CreateConnection();
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE project_id=$p;";
            command.Parameters.AddWithValue("$p", Project.Id.ToString());
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            _directory.Dispose();
        }
    }
}
