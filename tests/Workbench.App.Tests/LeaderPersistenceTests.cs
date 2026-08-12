using Workbench.App.Tests.Support;
using Workbench.App.ViewModels.Leader;
using Workbench.App.ViewModels.Panes;
using Workbench.Core.Projects;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Providers;
using Workbench.Runtime.Registry;
using Workbench.Storage.Database;
using Workbench.Storage.Leaders;
using Workbench.Storage.Projects;
using Workbench.Storage.Settings;
using Workbench.App.Leader;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.Tests;

public sealed class LeaderPersistenceTests
{
    [Fact]
    public async Task First_send_persists_leader_epoch_pointer_and_visible_messages_once()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var runtime = context.CreateRuntime();
        runtime.QueueTurn(
            new AgentTextDelta("LEADER_", context.T0),
            new AgentTextDelta("OK", context.T0),
            context.Completed("LEADER_OK"));
        var pane = context.CreatePane(runtime);
        await pane.InitializeAsync();
        pane.DraftMessage = "hello";

        await pane.SendAsync();

        var leader = await context.Leaders.GetAsync(context.ProjectA.Id);
        var epoch = await context.Epochs.GetCurrentForProjectAsync(context.ProjectA.Id);
        var messages = await context.Messages.GetAllAsync(epoch!.Id);
        Assert.Equal(context.ProjectA.Id, leader!.ProjectId);
        Assert.Equal(epoch.Id, leader.CurrentEpochId);
        Assert.Equal(pane.SessionEpochId, epoch.Id);
        Assert.Equal(["user", "assistant"], messages.Select(message => message.Role));
        Assert.Equal(["hello", "LEADER_OK"], messages.Select(message => message.Text));
        Assert.Equal([1L, 2L], messages.Select(message => message.Sequence));
    }

    [Fact]
    public async Task Failed_turn_keeps_user_but_does_not_persist_fake_assistant()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var runtime = context.CreateRuntime();
        runtime.SendException = new IOException("transport failed");
        var pane = context.CreatePane(runtime);
        await pane.InitializeAsync();
        pane.DraftMessage = "keep user";

        await pane.SendAsync();

        var epoch = await context.Epochs.GetCurrentForProjectAsync(context.ProjectA.Id);
        var messages = await context.Messages.GetAllAsync(epoch!.Id);
        Assert.Equal("keep user", Assert.Single(messages).Text);
        Assert.DoesNotContain(messages, message => message.Role == "assistant");
    }

    [Fact]
    public async Task Empty_completion_text_persists_the_visible_streamed_assistant_once()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var runtime = context.CreateRuntime();
        runtime.QueueTurn(new AgentTextDelta("visible streamed text", context.T0), context.Completed(string.Empty));
        var pane = context.CreatePane(runtime);
        await pane.InitializeAsync();
        pane.DraftMessage = "go";

        await pane.SendAsync();

        var epoch = await context.Epochs.GetCurrentForProjectAsync(context.ProjectA.Id);
        var messages = await context.Messages.GetAllAsync(epoch!.Id);
        Assert.Equal("visible streamed text", Assert.Single(messages, message => message.Role == "assistant").Text);
    }

    [Fact]
    public async Task Session_creation_failure_does_not_persist_user_message_or_fake_epoch()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var runtime = context.CreateRuntime();
        runtime.CreateException = new IOException("cannot create");
        var pane = context.CreatePane(runtime);
        await pane.InitializeAsync();
        pane.DraftMessage = "not sent";

        await pane.SendAsync();

        Assert.Null(await context.Leaders.GetAsync(context.ProjectA.Id));
        Assert.Null(await context.Epochs.GetCurrentForProjectAsync(context.ProjectA.Id));
    }

    [Fact]
    public async Task First_send_rolls_back_leader_and_epoch_when_current_pointer_cannot_be_set()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        await context.FailCurrentEpochUpdatesAsync();
        var runtime = context.CreateRuntime();
        var pane = context.CreatePane(runtime);
        await pane.InitializeAsync();
        pane.DraftMessage = "not sent";

        await pane.SendAsync();

        Assert.Null(await context.Leaders.GetAsync(context.ProjectA.Id));
        Assert.Empty(await context.Epochs.GetAllForProjectAsync(context.ProjectA.Id));
        Assert.Empty(runtime.SentRequests);

        await context.AllowCurrentEpochUpdatesAsync();
        runtime.QueueTurn(context.Completed("retry ok"));
        pane.DraftMessage = "retry";
        await pane.SendAsync();

        Assert.Equal(2, runtime.CreatedSessions.Count);
        Assert.NotNull(await context.Epochs.GetCurrentForProjectAsync(context.ProjectA.Id));
    }

    [Fact]
    public async Task Manager_recreation_restores_epoch_transcript_and_session_metadata_without_runtime_call()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var firstRuntime = context.CreateRuntime();
        firstRuntime.QueueTurn(new AgentTextDelta("answer", context.T0), context.Completed("answer"));
        var first = context.CreatePane(firstRuntime);
        await first.InitializeAsync();
        first.DraftMessage = "question";
        await first.SendAsync();
        var expectedEpoch = first.SessionEpochId;
        var expectedSession = first.Session!;

        context.Time.SetUtcNow(context.T1);
        var secondRuntime = context.CreateRuntime();
        var restored = context.CreatePane(secondRuntime, newManager: true);
        await restored.InitializeAsync();

        Assert.Equal(["question", "answer"], restored.Messages.Select(message => message.Text));
        Assert.Equal(expectedEpoch, restored.SessionEpochId);
        Assert.Equal(expectedSession.Id, restored.Session!.Id);
        Assert.Equal(expectedSession.ExternalSessionId, restored.Session.ExternalSessionId);
        Assert.Equal(expectedSession.WorkingDirectory, restored.Session.WorkingDirectory);
        Assert.Equal(0, secondRuntime.GetModelsCallCount);
        Assert.Empty(secondRuntime.ResumedSessions);
    }

    [Fact]
    public async Task Restored_transcript_remains_visible_when_current_runtime_account_is_unavailable()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var firstRuntime = context.CreateRuntime();
        firstRuntime.QueueTurn(context.Completed("offline history"));
        var first = context.CreatePane(firstRuntime);
        await first.InitializeAsync();
        first.DraftMessage = "question";
        await first.SendAsync();
        var epochId = first.SessionEpochId;

        var restored = context.CreatePaneWithoutRuntime();
        await restored.InitializeAsync();

        Assert.Equal(["question", "offline history"], restored.Messages.Select(message => message.Text));
        Assert.Equal(epochId, restored.SessionEpochId);
        Assert.Equal("Current Leader session is unavailable.", restored.RuntimeStatus);
        Assert.False(restored.CanSend);
        Assert.True(restored.CanRetryRuntime);
    }

    [Fact]
    public async Task Restored_session_connects_and_resumes_lazily_on_first_send()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var firstRuntime = context.CreateRuntime();
        firstRuntime.QueueTurn(context.Completed("before"));
        var first = context.CreatePane(firstRuntime);
        await first.InitializeAsync();
        first.DraftMessage = "before";
        await first.SendAsync();

        var registry = new AgentRuntimeRegistry();
        var resumedRuntime = context.CreateRuntime();
        resumedRuntime.QueueTurn(context.Completed("after"));
        var reconnectCalls = 0;
        var restored = new LeaderPaneViewModel(
            context.ProjectA,
            registry,
            context.CreateManager(),
            () => Task.CompletedTask,
            reconnectRuntime: _ =>
            {
                reconnectCalls++;
                registry.Register(resumedRuntime);
                return Task.CompletedTask;
            });

        await restored.InitializeAsync();
        Assert.Equal(0, reconnectCalls);
        Assert.Equal(["before", "before"], restored.Messages.Select(message => message.Text));
        restored.DraftMessage = "after";
        await restored.SendAsync();

        Assert.Equal(1, reconnectCalls);
        Assert.Single(resumedRuntime.ResumedSessions);
        Assert.Empty(resumedRuntime.CreatedSessions);
        restored.DraftMessage = "again";
        Assert.True(restored.IsRuntimeAvailable);
        Assert.True(restored.CanSend);
    }

    [Fact]
    public async Task First_send_after_restart_lazy_resumes_current_epoch_without_creating_thread()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var firstRuntime = context.CreateRuntime();
        firstRuntime.QueueTurn(context.Completed("before"));
        var first = context.CreatePane(firstRuntime);
        await first.InitializeAsync();
        first.DraftMessage = "before";
        await first.SendAsync();
        var expectedEpoch = first.SessionEpochId;
        var expectedSession = first.Session!;

        var secondRuntime = context.CreateRuntime();
        secondRuntime.QueueTurn(context.Completed("after"));
        var restored = context.CreatePane(secondRuntime, newManager: true);
        await restored.InitializeAsync();
        restored.DraftMessage = "after";
        await restored.SendAsync();

        var resumed = Assert.Single(secondRuntime.ResumedSessions);
        Assert.Empty(secondRuntime.CreatedSessions);
        Assert.Equal(expectedEpoch, restored.SessionEpochId);
        Assert.Equal(expectedSession.Id, resumed.Id);
        Assert.Equal(expectedSession.ExternalSessionId, resumed.ExternalSessionId);
        Assert.Equal(expectedSession.Id, restored.Session!.Id);
        Assert.Equal(expectedSession.ExternalSessionId, restored.Session.ExternalSessionId);
    }

    [Fact]
    public async Task Identity_layers_remain_distinct_through_rehydration()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var runtime = context.CreateRuntime();
        runtime.QueueTurn(context.Completed("done"));
        var pane = context.CreatePane(runtime);
        await pane.InitializeAsync();
        pane.DraftMessage = "go";
        await pane.SendAsync();

        var restored = context.CreatePane(context.CreateRuntime(), newManager: true);
        await restored.InitializeAsync();

        Assert.Equal(context.ProjectA.Id, restored.ProjectLeaderId);
        Assert.NotEqual(restored.ProjectLeaderId, restored.SessionEpochId);
        Assert.NotEqual(restored.ProjectLeaderId, restored.Session!.Id.Value);
        Assert.NotEqual(restored.ProjectLeaderId.ToString(), restored.Session.ExternalSessionId);
        Assert.NotEqual(restored.SessionEpochId, restored.Session.Id.Value);
        Assert.NotEqual(restored.SessionEpochId.ToString(), restored.Session.ExternalSessionId);
    }

    [Fact]
    public async Task Completed_turn_updates_last_active_at_once_after_completion()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var runtime = context.CreateRuntime();
        runtime.QueueTurn(new AgentTextDelta("a", context.T0), context.Completed("a"));
        var pane = context.CreatePane(runtime);
        await pane.InitializeAsync();
        pane.DraftMessage = "go";
        await pane.SendAsync();
        var epochId = pane.SessionEpochId!.Value;
        context.Time.SetUtcNow(context.T1);
        runtime.QueueTurn(new AgentTextDelta("b", context.T1), context.Completed("b"));
        pane.DraftMessage = "again";

        await pane.SendAsync();

        var epoch = await context.Epochs.GetAsync(epochId);
        Assert.Equal(context.T0, epoch!.StartedAt);
        Assert.Equal(context.T1, epoch.LastActiveAt);
    }

    [Fact]
    public async Task Persisted_model_missing_from_catalog_is_not_silently_replaced()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var firstRuntime = context.CreateRuntime(modelId: "persisted-model");
        firstRuntime.QueueTurn(context.Completed("done"));
        var first = context.CreatePane(firstRuntime);
        await first.InitializeAsync();
        first.DraftMessage = "go";
        await first.SendAsync();

        var replacementCatalog = context.CreateRuntime(modelId: "different-model");
        var restored = context.CreatePane(replacementCatalog, newManager: true);
        await restored.InitializeAsync();

        Assert.Equal("persisted-model", restored.SelectedModel!.Profile.Model.ModelId);
        Assert.True(restored.IsModelSelectionLocked);
    }

    [Fact]
    public async Task Two_projects_restore_only_their_own_epoch_and_messages()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var runtime = context.CreateRuntime();
        runtime.QueueTurn(context.Completed("answer-a"));
        runtime.QueueTurn(context.Completed("answer-b"));
        var manager = context.CreateManager();
        var paneA = context.CreatePane(runtime, project: context.ProjectA, manager: manager);
        var paneB = context.CreatePane(runtime, project: context.ProjectB, manager: manager);
        await paneA.InitializeAsync();
        await paneB.InitializeAsync();
        paneA.DraftMessage = "question-a";
        await paneA.SendAsync();
        paneB.DraftMessage = "question-b";
        await paneB.SendAsync();

        var restartedManager = context.CreateManager();
        var restoredA = context.CreatePane(context.CreateRuntime(), project: context.ProjectA, manager: restartedManager);
        var restoredB = context.CreatePane(context.CreateRuntime(), project: context.ProjectB, manager: restartedManager);
        await restoredA.InitializeAsync();
        await restoredB.InitializeAsync();

        Assert.Equal(["question-a", "answer-a"], restoredA.Messages.Select(message => message.Text));
        Assert.Equal(["question-b", "answer-b"], restoredB.Messages.Select(message => message.Text));
        Assert.NotEqual(restoredA.SessionEpochId, restoredB.SessionEpochId);
    }
}

internal sealed class PersistentLeaderContext : IAsyncDisposable
{
    private readonly TemporaryDirectory _directory;
    private readonly WorkbenchDatabase _database;

    private PersistentLeaderContext(
        TemporaryDirectory directory,
        WorkbenchDatabase database,
        CoreProject projectA,
        CoreProject projectB,
        ProviderAccountId accountId)
    {
        _directory = directory;
        _database = database;
        ProjectA = projectA;
        ProjectB = projectB;
        AccountId = accountId;
        Leaders = new ProjectLeaderRepository(database);
        Epochs = new LeaderSessionEpochRepository(database);
        Messages = new LeaderMessageRepository(database);
        WorkbenchSettings = new WorkbenchSettingsRepository(database);
        ProjectSettings = new ProjectSettingsRepository(database);
        Time = new MutableTimeProvider(T0);
    }

    public DateTimeOffset T0 { get; } = DateTimeOffset.Parse("2026-03-01T09:00:00+00:00");
    public DateTimeOffset T1 { get; } = DateTimeOffset.Parse("2026-03-02T09:00:00+00:00");
    public CoreProject ProjectA { get; }
    public CoreProject ProjectB { get; }
    public ProviderAccountId AccountId { get; }
    public ProjectLeaderRepository Leaders { get; }
    public LeaderSessionEpochRepository Epochs { get; }
    public LeaderMessageRepository Messages { get; }
    public WorkbenchSettingsRepository WorkbenchSettings { get; }
    public ProjectSettingsRepository ProjectSettings { get; }
    public MutableTimeProvider Time { get; }

    public static async Task<PersistentLeaderContext> CreateAsync()
    {
        var directory = new TemporaryDirectory("persistent-leader");
        var database = new WorkbenchDatabase(Path.Combine(directory.Path, "workbench.db"));
        await database.InitializeAsync();
        var now = DateTimeOffset.Parse("2026-03-01T09:00:00+00:00");
        var projectA = new CoreProject(Guid.NewGuid(), "A", "C:/Projects/A", ProjectType.Generic, null, now, now);
        var projectB = new CoreProject(Guid.NewGuid(), "B", "C:/Projects/B", ProjectType.Generic, null, now, now);
        var projects = new ProjectRepository(database);
        await projects.UpsertAsync(projectA);
        await projects.UpsertAsync(projectB);
        return new PersistentLeaderContext(directory, database, projectA, projectB, new ProviderAccountId(Guid.NewGuid()));
    }

    public FakeAgentRuntime CreateRuntime(string modelId = "model-a") =>
        new(
            "Fake Provider",
            "Stable Account",
            AccountId,
            new ModelProfile(new ProviderId("fake-provider"), modelId, modelId, AgentCapability.Resume));

    public ProjectLeaderSessionManager CreateManager() =>
        new(Leaders, Epochs, Messages, Time);

    public LeaderSessionRotationStateService CreateRotationStateService() =>
        new(WorkbenchSettings, ProjectSettings, Epochs, Time, TimeZoneInfo.Utc);

    public LeaderSessionRolloverService CreateRolloverService(AgentRuntimeRegistry registry) =>
        new(registry, Leaders, Epochs, Messages, Time);

    public LeaderPaneViewModel CreatePane(
        FakeAgentRuntime runtime,
        bool newManager = false,
        CoreProject? project = null,
        ProjectLeaderSessionManager? manager = null)
    {
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        return new LeaderPaneViewModel(
            project ?? ProjectA,
            registry,
            manager ?? (newManager ? CreateManager() : CreateManager()),
            () => Task.CompletedTask);
    }

    public LeaderPaneViewModel CreatePaneWithoutRuntime() =>
        new(
            ProjectA,
            new AgentRuntimeRegistry(),
            CreateManager(),
            () => Task.CompletedTask);

    public AgentTurnCompleted Completed(string text) =>
        new(
            new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed, text, null),
            Time.GetUtcNow());

    public async Task FailCurrentEpochUpdatesAsync()
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TRIGGER fail_current_epoch_update
            BEFORE UPDATE OF current_epoch_id ON project_leaders
            BEGIN
                SELECT RAISE(ABORT, 'forced current epoch failure');
            END;
            """;
        await command.ExecuteNonQueryAsync();
    }

    public async Task AllowCurrentEpochUpdatesAsync()
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "DROP TRIGGER fail_current_epoch_update;";
        await command.ExecuteNonQueryAsync();
    }

    public ValueTask DisposeAsync()
    {
        _directory.Dispose();
        return ValueTask.CompletedTask;
    }
}
