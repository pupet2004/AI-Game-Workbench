using System.Text.Json;
using Workbench.App.Leader;
using Workbench.App.Services;
using Workbench.App.Tests.Support;
using Workbench.Core.Leaders;
using Workbench.Core.Projects;
using Workbench.Core.Tasks;
using Workbench.Core.Workers;
using Workbench.Runtime.Providers;
using Workbench.Runtime.Registry;
using Workbench.Storage.Leaders;
using Workbench.Storage.Reviews;
using Workbench.Storage.Tasks;
using Workbench.Storage.Workers;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.Tests;

public sealed class ProviderIndependentProjectRecoveryCertificationTests
{
    [Fact]
    public async Task Open_gate_recovers_from_project_open_without_provider_or_transcript()
    {
        await using var fixture = await Fixture.CreateAsync();
        var before = await fixture.SnapshotAsync();
        await fixture.DeleteTranscriptAsync();
        await fixture.RestartAndOpenAsync();

        AssertProviderUnavailable(fixture.Services);
        var reopened = await fixture.Services.ProjectOpenService.OpenAsync(fixture.ProjectRoot);
        Assert.Equal(fixture.Project.Id, reopened.Project.Id);
        await fixture.AssertKernelStateAsync("Open", expectResponse: false);

        var after = await fixture.SnapshotAsync();
        Assert.Equal(before.ProjectId, after.ProjectId);
        Assert.Equal(before.TaskCount, after.TaskCount);
        Assert.Equal(before.RevisionCount, after.RevisionCount);
        Assert.Equal(before.ExecutionCount, after.ExecutionCount);
        Assert.Equal(before.DecisionCount, after.DecisionCount);
        Assert.Equal(before.GateCount, after.GateCount);
    }

    [Fact]
    public async Task Responded_gate_recovers_first_binding_and_reopen_is_idempotent()
    {
        await using var fixture = await Fixture.CreateAsync(respond: true);
        var before = await fixture.SnapshotAsync();
        await fixture.RestartAndOpenAsync();

        AssertProviderUnavailable(fixture.Services);
        var firstOpen = await fixture.Services.ProjectOpenService.OpenAsync(fixture.ProjectRoot);
        var secondOpen = await fixture.Services.ProjectOpenService.OpenAsync(fixture.ProjectRoot);
        Assert.Equal(fixture.Project.Id, firstOpen.Project.Id);
        Assert.Equal(fixture.Project.Id, secondOpen.Project.Id);
        await fixture.AssertKernelStateAsync("Responded", expectResponse: true);

        var after = await fixture.SnapshotAsync();
        Assert.Equal(before.ProjectId, after.ProjectId);
        Assert.Equal(before.TaskCount, after.TaskCount);
        Assert.Equal(before.RevisionCount, after.RevisionCount);
        Assert.Equal(before.ExecutionCount, after.ExecutionCount);
        Assert.Equal(before.DecisionCount, after.DecisionCount);
        Assert.Equal(before.GateCount, after.GateCount);
    }

    private static void AssertProviderUnavailable(AppServices services)
    {
        Assert.Empty(services.RuntimeRegistry.Runtimes);
        Assert.Throws<KeyNotFoundException>(() => services.RuntimeRegistry.GetByAccount(new ProviderAccountId(Guid.NewGuid())));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly TemporaryDirectory _directory;
        private readonly MutableTimeProvider _time;
        private readonly string _databasePath;

        private Fixture(TemporaryDirectory directory, MutableTimeProvider time, string databasePath, AppServices services, CoreProject project, Guid taskId, TaskRevision revision, Guid executionId, Guid epochId, Guid finalReportEventId, Guid reviewDecisionId, long? responseMessageId, string projectRoot)
        {
            _directory = directory;
            _time = time;
            _databasePath = databasePath;
            Services = services;
            Project = project;
            TaskId = taskId;
            Revision = revision;
            ExecutionId = executionId;
            EpochId = epochId;
            FinalReportEventId = finalReportEventId;
            ReviewDecisionId = reviewDecisionId;
            ResponseMessageId = responseMessageId;
            ProjectRoot = projectRoot;
        }

        public AppServices Services { get; private set; }
        public CoreProject Project { get; }
        public string ProjectRoot { get; }
        public Guid TaskId { get; }
        public TaskRevision Revision { get; }
        public Guid ExecutionId { get; }
        public Guid EpochId { get; }
        public Guid FinalReportEventId { get; }
        public Guid ReviewDecisionId { get; }
        public long? ResponseMessageId { get; }

        public static async Task<Fixture> CreateAsync(bool respond = false)
        {
            var directory = new TemporaryDirectory("provider-independent-recovery");
            var projectRoot = Path.Combine(directory.Path, "project");
            Directory.CreateDirectory(projectRoot);
            var time = new MutableTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00.0000000+00:00"));
            var databasePath = Path.Combine(directory.Path, "workbench.db");
            var services = AppServices.CreateForDatabasePath(databasePath, time, new AgentRuntimeRegistry());
            await services.InitializeAsync();
            var project = (await services.ProjectOpenService.OpenAsync(projectRoot)).Project;
            var now = time.GetUtcNow();
            var taskId = Guid.NewGuid();
            var profile = ExecutionProfile.Create("offline-provider", "offline-account", "offline-model", "offline-runtime");
            var revision = new TaskRevision(taskId, 1, "recover goal", "recover scope", "recover exclusions", ["state is recoverable"], TaskRiskLevel.Low, profile, "initial", TaskRevisionApprover.User, now, null);
            await services.TaskRepository.CreateAsync(project.Id, new TaskDraft(taskId, "Recoverable task", revision.Goal, revision.Scope, revision.OutOfScope, revision.Acceptance, revision.RiskLevel, profile, now, revision, TaskLifecycleStatus.Reviewing));

            var epochId = Guid.NewGuid();
            await services.ProjectLeaderRepository.CreateCurrentEpochAsync(
                new StoredProjectLeader(project.Id, null, now, now),
                new StoredLeaderSessionEpoch(epochId, project.Id, "offline-provider", Guid.NewGuid(), "offline-model", Guid.NewGuid(), "native-session-not-restored", projectRoot, now, now, null, null, null));

            var executionId = Guid.NewGuid();
            await new WorkerExecutionRepository(services.Database).CreateAsync(new StoredWorkerExecution(
                executionId,
                project.Id,
                taskId,
                revision.CreateReference(),
                revision.CreateReference(),
                "base-commit-123",
                "master",
                ProviderAccountBinding.Create("offline-provider", "offline-account"),
                profile,
                "worker/recoverable-task",
                Path.Combine(directory.Path, "worker"),
                WorkerExecutionState.CompletedPendingReview,
                "native-agent-session",
                "native-external-session",
                Path.Combine(directory.Path, "worker"),
                now,
                now));

            var finalReportEventId = Guid.NewGuid();
            await new TaskEventRepository(services.Database).AppendAsync(new StoredTaskEvent(
                finalReportEventId,
                project.Id,
                taskId,
                executionId,
                "WorkerFinalReportReceived",
                JsonSerializer.Serialize(new { Message = "persisted final report" }),
                now));

            var reviewDecisionId = Guid.NewGuid();
            var legacyReview = new AssignmentReviewStateRepository(services.Database);
            Assert.Equal(AssignmentStateTransitionResult.Applied, await legacyReview.TryRecordLeaderReviewDecisionAsync(new LeaderReviewDecisionPersistenceRequest(
                reviewDecisionId,
                project.Id,
                taskId,
                revision.Id,
                finalReportEventId,
                nameof(LeaderReviewOutcome.AskUser),
                nameof(LeaderReviewActionLevel.L3DecisionRequired),
                "ReportOnly",
                "persisted summary",
                null,
                "choose",
                null,
                now)));
            var typedReview = new LeaderReviewStateRepository(services.Database);
            Assert.Equal(LeaderReviewWriteResult.Applied, await typedReview.InsertDecisionIfAbsentAsync(new LeaderReviewDecisionWriteRequest(
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

            Assert.Equal(LeaderReviewAskUserGateResultKind.Opened, (await services.LeaderReviewAskUserGate.TryOpenAsync(project.Id, taskId, revision.Id, finalReportEventId)).Kind);
            long? responseMessageId = null;
            if (respond)
            {
                var gate = await typedReview.GetGateAsync(project.Id, taskId, reviewDecisionId);
                var response = await new LeaderMessageRepository(services.Database).AppendAsync(epochId, "user", "first persisted response", now.AddMinutes(1));
                Assert.NotNull(gate);
                Assert.NotNull(await services.LeaderReviewUserResponseBinder.BindAsync(project.Id, response.Id));
                responseMessageId = response.Id;
            }

            return new Fixture(directory, time, databasePath, services, project, taskId, revision, executionId, epochId, finalReportEventId, reviewDecisionId, responseMessageId, projectRoot);
        }

        public async Task RestartAndOpenAsync()
        {
            await Services.DisposeAsync();
            Services = AppServices.CreateForDatabasePath(_databasePath, _time, new AgentRuntimeRegistry());
            await Services.InitializeAsync();
            await Services.ProjectOpenService.OpenAsync(ProjectRoot);
        }

        public async Task DeleteTranscriptAsync()
        {
            await using var connection = Services.Database.CreateConnection();
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM leader_messages WHERE epoch_id=$epochId;";
            command.Parameters.AddWithValue("$epochId", EpochId.ToString());
            await command.ExecuteNonQueryAsync();
        }

        public async Task AssertKernelStateAsync(string expectedGateState, bool expectResponse)
        {
            var project = await Services.ProjectRepository.GetByIdAsync(Project.Id);
            Assert.NotNull(project);
            Assert.Equal(ProjectRoot, project.RootPath);

            var leader = await Services.ProjectLeaderRepository.GetAsync(Project.Id);
            Assert.NotNull(leader);
            Assert.Equal(EpochId, leader.CurrentEpochId);
            var epoch = await Services.LeaderSessionEpochRepository.GetAsync(EpochId);
            Assert.NotNull(epoch);
            Assert.Equal(Project.Id, epoch.ProjectId);

            var task = await Services.TaskRepository.GetAsync(Project.Id, TaskId);
            Assert.NotNull(task);
            Assert.Equal(TaskLifecycleStatus.NeedsUserDecision, task.Status);
            Assert.Equal(Revision.Id, task.CurrentRevisionId);
            var revisions = await Services.TaskRevisionRepository.ListAsync(Project.Id, TaskId);
            Assert.Equal(Revision.Id, Assert.Single(revisions).Id);

            var execution = await new WorkerExecutionRepository(Services.Database).GetAsync(Project.Id, ExecutionId);
            Assert.NotNull(execution);
            Assert.Equal(WorkerExecutionState.CompletedPendingReview, execution.State);
            Assert.Equal("base-commit-123", execution.BaseCommit);
            Assert.Equal("master", execution.TargetBranch);
            Assert.Equal(Revision.Id, execution.ExecutionStartRevision.RevisionId);

            var typed = new LeaderReviewStateRepository(Services.Database);
            var decision = await typed.GetDecisionByFinalReportAsync(Project.Id, TaskId, FinalReportEventId);
            Assert.NotNull(decision);
            Assert.Equal(ReviewDecisionId, decision.ReviewDecisionId);
            Assert.Equal(nameof(LeaderAuthorityResolution.AskUser), decision.AuthorityResolution);
            Assert.Equal(LeaderAuthorityMode.Balanced, decision.AuthorityMode);
            var gate = await typed.GetGateAsync(Project.Id, TaskId, ReviewDecisionId);
            Assert.NotNull(gate);
            Assert.Equal(expectedGateState, gate.State);
            if (expectResponse)
            {
                Assert.NotNull(gate.QuestionMessageId);
                Assert.Equal(ResponseMessageId, gate.UserMessageId);
                Assert.NotNull(gate.RespondedAt);
                Assert.Equal(ResponseMessageId, await Services.LeaderReviewUserResponseBinder.GetBoundUserMessageIdAsync(Project.Id, TaskId, ReviewDecisionId));
            }
            else
            {
                Assert.Null(gate.QuestionMessageId);
                Assert.Null(gate.UserMessageId);
                Assert.Null(gate.RespondedAt);
            }
        }

        public async Task<RecoverySnapshot> SnapshotAsync()
        {
            await using var connection = Services.Database.CreateConnection();
            await connection.OpenAsync();
            async Task<long> CountAsync(string table)
            {
                var command = connection.CreateCommand();
                command.CommandText = table switch
                {
                    "projects" => "SELECT COUNT(*) FROM projects WHERE id=$projectId;",
                    "task_revisions" => "SELECT COUNT(*) FROM task_revisions revision JOIN tasks task ON task.id=revision.task_id WHERE task.project_id=$projectId;",
                    _ => $"SELECT COUNT(*) FROM {table} WHERE project_id=$projectId;"
                };
                command.Parameters.AddWithValue("$projectId", Project.Id.ToString());
                return Convert.ToInt64(await command.ExecuteScalarAsync());
            }
            return new(
                Project.Id,
                await CountAsync("tasks"),
                await CountAsync("task_revisions"),
                await CountAsync("worker_executions"),
                await CountAsync("task_review_decisions"),
                await CountAsync("task_review_user_gates"));
        }

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            _directory.Dispose();
        }
    }

    private sealed record RecoverySnapshot(Guid ProjectId, long TaskCount, long RevisionCount, long ExecutionCount, long DecisionCount, long GateCount);
}
