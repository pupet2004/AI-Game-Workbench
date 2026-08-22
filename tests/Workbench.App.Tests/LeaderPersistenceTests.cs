using System.Text.Json;
using Workbench.App.Tests.Support;
using Workbench.App.ViewModels.Leader;
using Workbench.App.ViewModels.Panes;
using Workbench.Core.Projects;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Providers;
using Workbench.Runtime.Registry;
using Workbench.Storage.Database;
using Workbench.Storage.Leaders;
using Workbench.Storage.Memory;
using Workbench.Storage.Projects;
using Workbench.Storage.Settings;
using Workbench.Storage.Tasks;
using Workbench.App.Leader;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.Tests;

public sealed class LeaderPersistenceTests
{
    [Fact]
    public async Task Completed_turn_persists_visible_reply_and_summary_metadata_before_append()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        await context.FailSummaryAppendsAsync();
        var runtime = context.CreateRuntime();
        runtime.QueueTurn(context.Completed(SummaryResponse("Visible reply.", "Durable decision.")));
        var pane = context.CreatePane(runtime);
        await pane.InitializeAsync();
        pane.DraftMessage = "decide";

        await pane.SendAsync();

        var epoch = await context.Epochs.GetCurrentForProjectAsync(context.ProjectA.Id);
        var assistant = Assert.Single(
            await context.Messages.GetAllAsync(epoch!.Id),
            message => message.Role == "assistant");
        Assert.Equal("Visible reply.", assistant.Text);
        Assert.NotNull(assistant.ResultId);
        Assert.NotNull(assistant.SummaryDeltaPayloadJson);
        Assert.Null(assistant.SummaryPersistedAt);
        Assert.Contains(pane.Messages, message => message.Text == "Visible reply.");
        Assert.Empty(await context.ReadSummariesAsync(context.ProjectA.Id));
    }

    [Fact]
    public async Task Completed_turn_uses_one_result_id_for_all_summary_ordinals()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var runtime = context.CreateRuntime();
        runtime.QueueTurn(context.Completed(TwoSummaryResponse()));
        var pane = context.CreatePane(runtime);
        await pane.InitializeAsync();
        pane.DraftMessage = "decide";

        await pane.SendAsync();

        var epoch = await context.Epochs.GetCurrentForProjectAsync(context.ProjectA.Id);
        var assistant = Assert.Single(
            await context.Messages.GetAllAsync(epoch!.Id),
            message => message.Role == "assistant");
        var summaries = await context.ReadSummariesAsync(context.ProjectA.Id);
        Assert.Equal([0, 1], summaries.OrderBy(item => item.DeltaOrdinal).Select(item => item.DeltaOrdinal));
        Assert.All(summaries, item => Assert.Equal(assistant.ResultId, item.ResultId));
    }

    [Fact]
    public async Task Summary_durable_payload_contains_only_summary_delta_array()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var runtime = context.CreateRuntime();
        runtime.QueueTurn(context.Completed(SummaryResponse("VISIBLE SENTINEL", "SUMMARY SENTINEL")));
        var pane = context.CreatePane(runtime);
        await pane.InitializeAsync();
        pane.DraftMessage = "decide";

        await pane.SendAsync();

        var epoch = await context.Epochs.GetCurrentForProjectAsync(context.ProjectA.Id);
        var assistant = Assert.Single(
            await context.Messages.GetAllAsync(epoch!.Id),
            message => message.Role == "assistant");
        using var document = JsonDocument.Parse(Assert.IsType<string>(assistant.SummaryDeltaPayloadJson));
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        var delta = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(
            ["kind", "occurred_at", "source_refs", "text"],
            delta.EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal("SUMMARY SENTINEL", delta.GetProperty("text").GetString());
        Assert.DoesNotContain("VISIBLE SENTINEL", assistant.SummaryDeltaPayloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain("response", assistant.SummaryDeltaPayloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain("summary_deltas", assistant.SummaryDeltaPayloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain("result_id", assistant.SummaryDeltaPayloadJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_summary_preserves_existing_assistant_persistence_without_pending_metadata()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var runtime = context.CreateRuntime();
        runtime.QueueTurn(context.Completed(NoSummaryResponse("Visible only.")));
        var pane = context.CreatePane(runtime);
        await pane.InitializeAsync();
        pane.DraftMessage = "answer";

        await pane.SendAsync();

        var epoch = await context.Epochs.GetCurrentForProjectAsync(context.ProjectA.Id);
        var assistant = Assert.Single(
            await context.Messages.GetAllAsync(epoch!.Id),
            message => message.Role == "assistant");
        Assert.Equal("Visible only.", assistant.Text);
        Assert.Null(assistant.ResultId);
        Assert.Null(assistant.SummaryDeltaPayloadJson);
        Assert.Null(assistant.SummaryPersistedAt);
        Assert.Empty(await context.Messages.GetPendingSummaryResultsAsync());
        Assert.Empty(await context.ReadSummariesAsync(context.ProjectA.Id));
    }

    [Fact]
    public async Task Fresh_operation_gets_new_result_id()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var runtime = context.CreateRuntime();
        runtime.QueueTurn(context.Completed(SummaryResponse("First.", "First decision.")));
        runtime.QueueTurn(context.Completed(SummaryResponse("Second.", "Second decision.")));
        var pane = context.CreatePane(runtime);
        await pane.InitializeAsync();

        pane.DraftMessage = "first";
        await pane.SendAsync();
        pane.DraftMessage = "second";
        await pane.SendAsync();

        var epoch = await context.Epochs.GetCurrentForProjectAsync(context.ProjectA.Id);
        var resultIds = (await context.Messages.GetAllAsync(epoch!.Id))
            .Where(message => message.Role == "assistant")
            .Select(message => Assert.IsType<Guid>(message.ResultId))
            .ToArray();
        Assert.Equal(2, resultIds.Length);
        Assert.NotEqual(resultIds[0], resultIds[1]);
    }

    [Fact]
    public async Task Summary_append_failure_keeps_visible_reply_and_pending_metadata()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        await context.FailSummaryAppendsAsync();
        var runtime = context.CreateRuntime();
        runtime.QueueTurn(context.Completed(SummaryResponse("Still visible.", "Retry later.")));
        var pane = context.CreatePane(runtime);
        await pane.InitializeAsync();
        pane.DraftMessage = "decide";

        await pane.SendAsync();

        Assert.Contains(pane.Messages, message => message.Text == "Still visible.");
        Assert.DoesNotContain(pane.Messages, message => message.Text.Contains("Summary", StringComparison.Ordinal));
        Assert.Equal("Summary persistence is pending and will be retried.", pane.MemoryCommandStatus);
        var pending = Assert.Single(await context.Messages.GetPendingSummaryResultsAsync());
        Assert.NotEqual(Guid.Empty, pending.ResultId);
    }

    [Fact]
    public async Task Successful_append_marks_summary_persisted()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var runtime = context.CreateRuntime();
        runtime.QueueTurn(context.Completed(SummaryResponse("Visible.", "Stored.")));
        var pane = context.CreatePane(runtime);
        await pane.InitializeAsync();
        pane.DraftMessage = "decide";

        await pane.SendAsync();

        var epoch = await context.Epochs.GetCurrentForProjectAsync(context.ProjectA.Id);
        var assistant = Assert.Single(
            await context.Messages.GetAllAsync(epoch!.Id),
            message => message.Role == "assistant");
        Assert.NotNull(assistant.SummaryPersistedAt);
        Assert.Empty(await context.Messages.GetPendingSummaryResultsAsync());
        Assert.Null(pane.MemoryCommandStatus);
    }

    [Theory]
    [InlineData(AgentSessionStatus.Failed)]
    [InlineData(AgentSessionStatus.Interrupted)]
    [InlineData(AgentSessionStatus.Stopped)]
    public async Task Failed_or_interrupted_turn_does_not_persist_summary(AgentSessionStatus status)
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var runtime = context.CreateRuntime();
        runtime.QueueTurn(context.Completed(SummaryResponse("Not durable.", "Do not store."), status));
        var pane = context.CreatePane(runtime);
        await pane.InitializeAsync();
        pane.DraftMessage = "decide";

        await pane.SendAsync();

        Assert.Empty(await context.Messages.GetPendingSummaryResultsAsync());
        Assert.Empty(await context.ReadSummariesAsync(context.ProjectA.Id));
    }

    [Fact]
    public async Task Empty_visible_response_with_summary_persists_durable_assistant_result()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var runtime = context.CreateRuntime();
        runtime.QueueTurn(context.Completed(SummaryResponse(string.Empty, "Invisible but durable.")));
        var pane = context.CreatePane(runtime);
        await pane.InitializeAsync();
        pane.DraftMessage = "decide";

        await pane.SendAsync();

        var epoch = await context.Epochs.GetCurrentForProjectAsync(context.ProjectA.Id);
        var assistant = Assert.Single(
            await context.Messages.GetAllAsync(epoch!.Id),
            message => message.Role == "assistant");
        Assert.Equal(string.Empty, assistant.Text);
        Assert.NotNull(assistant.ResultId);
        Assert.NotNull(assistant.SummaryPersistedAt);
        Assert.Single(await context.ReadSummariesAsync(context.ProjectA.Id));
    }

    [Fact]
    public async Task Empty_structured_response_never_persists_streamed_envelope()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var envelope = SummaryResponse(string.Empty, "Invisible but durable.");
        var runtime = context.CreateRuntime();
        runtime.QueueTurn(
            new AgentTextDelta(envelope, context.T0),
            context.Completed(envelope));
        var pane = context.CreatePane(runtime);
        await pane.InitializeAsync();
        pane.DraftMessage = "decide";

        await pane.SendAsync();

        var epoch = await context.Epochs.GetCurrentForProjectAsync(context.ProjectA.Id);
        var assistant = Assert.Single(
            await context.Messages.GetAllAsync(epoch!.Id),
            message => message.Role == "assistant");
        Assert.Equal(string.Empty, assistant.Text);
        Assert.Equal(
            string.Empty,
            Assert.Single(pane.Messages, message => message.Role == LeaderMessageRole.Assistant).Text);
        Assert.DoesNotContain("summary_deltas", assistant.Text, StringComparison.Ordinal);
        Assert.NotNull(assistant.SummaryPersistedAt);
    }

    [Fact]
    public async Task Missing_durable_message_identity_does_not_append_summary()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var runtime = context.CreateRuntime();
        runtime.QueueTurn(context.Completed(SummaryResponse("Visible.", "Must not orphan.")));
        var pane = context.CreatePane(runtime, manager: new ProjectLeaderSessionManager());
        await pane.InitializeAsync();
        pane.DraftMessage = "decide";

        await pane.SendAsync();

        Assert.Empty(await context.ReadSummariesAsync(context.ProjectA.Id));
        Assert.Equal("Summary persistence is pending and will be retried.", pane.MemoryCommandStatus);
    }

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
    public async Task Restored_session_connects_at_initialize_and_resumes_lazily_on_first_send()
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
        Assert.Equal(1, reconnectCalls);
        Assert.Equal(["before", "before"], restored.Messages.Select(message => message.Text));
        Assert.Empty(resumedRuntime.ResumedSessions);
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

    private static string SummaryResponse(string response, string summaryText) => $$"""
        {"response":"{{response}}","draft_proposal":null,"memory_commands":null,"summary_deltas":[{"occurred_at":"2026-08-20T10:15:30.0000000+00:00","kind":"Decision","text":"{{summaryText}}","source_refs":[{"source_kind":"LeaderMessage","source_locator":"message-42"}]}]}
        """;

    private static string TwoSummaryResponse() => """
        {"response":"Visible.","draft_proposal":null,"memory_commands":null,"summary_deltas":[{"occurred_at":"2026-08-20T10:15:30.0000000+00:00","kind":"Decision","text":"First.","source_refs":[]},{"occurred_at":"2026-08-20T10:16:30.0000000+00:00","kind":"Constraint","text":"Second.","source_refs":[]}]}
        """;

    private static string NoSummaryResponse(string response) => $$"""
        {"response":"{{response}}","draft_proposal":null,"memory_commands":null,"summary_deltas":null}
        """;
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
        Summaries = new ProjectSummaryRepository(database);
        Tasks = new TaskRepository(database);
        Memories = new ProjectMemoryRepository(database);
        WorkbenchSettings = new WorkbenchSettingsRepository(database);
        ProjectSettings = new ProjectSettingsRepository(database);
        Time = new MutableTimeProvider(T0);
    }

    public DateTimeOffset T0 { get; } = DateTimeOffset.Parse("2026-03-01T09:00:00+00:00");
    public DateTimeOffset T1 { get; } = DateTimeOffset.Parse("2026-03-02T09:00:00+00:00");
    public WorkbenchDatabase Database => _database;
    public CoreProject ProjectA { get; }
    public CoreProject ProjectB { get; }
    public ProviderAccountId AccountId { get; }
    public ProjectLeaderRepository Leaders { get; }
    public LeaderSessionEpochRepository Epochs { get; }
    public LeaderMessageRepository Messages { get; }
    public ProjectSummaryRepository Summaries { get; }
    public TaskRepository Tasks { get; }
    public ProjectMemoryRepository Memories { get; }
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
        new(registry, Leaders, Messages, Time);

    public ILeaderBootContextBuilder CreateBootBuilder() =>
        new LeaderBootContextBuilder(Memories, Epochs);

    public LeaderPaneViewModel CreatePane(
        FakeAgentRuntime runtime,
        bool newManager = false,
        CoreProject? project = null,
        ProjectLeaderSessionManager? manager = null,
        ILeaderBootContextBuilder? bootContextBuilder = null)
    {
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        return new LeaderPaneViewModel(
            project ?? ProjectA,
            registry,
            manager ?? (newManager ? CreateManager() : CreateManager()),
            () => Task.CompletedTask,
            bootContextBuilder: bootContextBuilder,
            taskRepository: Tasks,
            projectSummaryRepository: Summaries);
    }

    public LeaderPaneViewModel CreatePaneWithoutRuntime() =>
        new(
            ProjectA,
            new AgentRuntimeRegistry(),
            CreateManager(),
            () => Task.CompletedTask);

    public AgentTurnCompleted Completed(
        string text,
        AgentSessionStatus status = AgentSessionStatus.Completed) =>
        new(
            new AgentResult(AgentSessionId.New(), status, text, null),
            Time.GetUtcNow());

    public Task<IReadOnlyList<StoredSummaryEntry>> ReadSummariesAsync(Guid projectId) =>
        Summaries.QueryAsync(new SummaryQuery(projectId, 200));

    public async Task FailSummaryAppendsAsync()
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TRIGGER fail_summary_append
            BEFORE INSERT ON project_summary_entries
            BEGIN
                SELECT RAISE(ABORT, 'forced summary append failure');
            END;
            """;
        await command.ExecuteNonQueryAsync();
    }

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
