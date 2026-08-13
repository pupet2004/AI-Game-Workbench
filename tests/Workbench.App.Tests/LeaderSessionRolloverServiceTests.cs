using System.Text;
using Workbench.App.Leader;
using Workbench.App.Tests.Support;
using Workbench.App.ViewModels.Panes;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Registry;

namespace Workbench.App.Tests;

public sealed class LeaderSessionRolloverServiceTests
{
    [Fact]
    public async Task Semantic_handoff_uses_old_session_without_polluting_messages_or_last_active()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var runtime = context.CreateRuntime();
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        runtime.QueueTurn(context.Completed("old answer"));
        var pane = CreatePane(context, registry);
        await pane.InitializeAsync();
        pane.DraftMessage = "old question";
        await pane.SendAsync();
        var oldEpoch = await context.Epochs.GetCurrentForProjectAsync(context.ProjectA.Id);
        var oldSession = pane.Session!;
        context.Time.SetUtcNow(context.T1);
        const string semantic = "CURRENT FOCUS\nM1-05B\n\nNEXT STEP\nContinue safely";
        runtime.QueueTurn(context.Completed(semantic));
        var service = CreateService(context, registry);

        var result = await service.RolloverAsync(
            context.ProjectA, oldSession, oldEpoch!, false, "Manual");

        var archived = await context.Epochs.GetAsync(oldEpoch!.Id);
        Assert.Equal(LeaderHandoffSource.Semantic, result.HandoffSource);
        Assert.Equal(semantic, archived!.HandoffSummary);
        Assert.Equal(context.T0, archived.LastActiveAt);
        Assert.Equal(["old question", "old answer"],
            (await context.Messages.GetAllAsync(oldEpoch.Id)).Select(message => message.Text));
        Assert.Equal(oldSession.Id, runtime.SentSessions[1].Id);
        Assert.Contains("Do not read files.", runtime.SentRequests[1].Text, StringComparison.Ordinal);
        Assert.Equal(result.NewEpoch.Id, (await context.Leaders.GetAsync(context.ProjectA.Id))!.CurrentEpochId);
        Assert.NotEqual(oldEpoch.Id, result.NewEpoch.Id);
        Assert.NotEqual(oldSession.Id.Value, result.NewEpoch.AgentSessionId);
        Assert.NotEqual(oldSession.ExternalSessionId, result.NewEpoch.ExternalSessionId);
    }

    [Fact]
    public async Task Handoff_approval_uses_bounded_fallback_without_approval_or_project_scan()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var runtime = context.CreateRuntime();
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        runtime.QueueTurn(context.Completed("visible marker M105B_ALPHA"));
        var pane = CreatePane(context, registry);
        await pane.InitializeAsync();
        pane.DraftMessage = new string('早', 2500);
        await pane.SendAsync();
        var oldEpoch = await context.Epochs.GetCurrentForProjectAsync(context.ProjectA.Id);
        runtime.QueueTurn(new AgentApprovalRequested(
            AgentApprovalRequestId.New(),
            pane.Session!.Id,
            "Read a file?",
            [new AgentApprovalOption("allow-once", "Allow once")],
            context.T0));
        context.Time.SetUtcNow(context.T1);
        var service = CreateService(context, registry);

        var result = await service.RolloverAsync(
            context.ProjectA, pane.Session!, oldEpoch!, false, "Manual");

        Assert.Equal(LeaderHandoffSource.Fallback, result.HandoffSource);
        Assert.Contains("Project: A", result.HandoffSummary, StringComparison.Ordinal);
        Assert.Contains("Root: C:/Projects/A", result.HandoffSummary, StringComparison.Ordinal);
        Assert.Contains("Assistant:\nvisible marker M105B_ALPHA", result.HandoffSummary, StringComparison.Ordinal);
        Assert.True(Encoding.UTF8.GetByteCount(result.HandoffSummary) <= LeaderHandoffBuilder.MaxHandoffUtf8Bytes);
        Assert.Empty(runtime.ApprovalDecisions);
        Assert.Contains(pane.Session!, runtime.StoppedSessions);
    }

    [Fact]
    public async Task Empty_or_oversized_semantic_handoff_uses_deterministic_fallback()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var runtime = context.CreateRuntime();
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        runtime.QueueTurn(context.Completed("old"));
        var pane = CreatePane(context, registry);
        await pane.InitializeAsync();
        pane.DraftMessage = "question";
        await pane.SendAsync();
        var oldEpoch = await context.Epochs.GetCurrentForProjectAsync(context.ProjectA.Id);
        context.Time.SetUtcNow(context.T1);
        runtime.QueueTurn(context.Completed(new string('x', LeaderHandoffBuilder.MaxHandoffUtf8Bytes + 1)));

        var result = await CreateService(context, registry).RolloverAsync(
            context.ProjectA, pane.Session!, oldEpoch!, false, "Manual");

        Assert.Equal(LeaderHandoffSource.Fallback, result.HandoffSource);
        Assert.Contains("RECENT CONVERSATION", result.HandoffSummary, StringComparison.Ordinal);
        Assert.True(Encoding.UTF8.GetByteCount(result.HandoffSummary) <= LeaderHandoffBuilder.MaxHandoffUtf8Bytes);
    }

    [Fact]
    public async Task Empty_semantic_result_and_resume_failure_each_use_fallback()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var runtime = context.CreateRuntime();
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        runtime.QueueTurn(context.Completed("old"));
        var pane = CreatePane(context, registry);
        await pane.InitializeAsync();
        pane.DraftMessage = "question";
        await pane.SendAsync();
        var oldEpoch = await context.Epochs.GetCurrentForProjectAsync(context.ProjectA.Id);
        context.Time.SetUtcNow(context.T1);
        runtime.QueueTurn(context.Completed(string.Empty));

        var emptyResult = await CreateService(context, registry).RolloverAsync(
            context.ProjectA, pane.Session!, oldEpoch!, false, "Manual");

        Assert.Equal(LeaderHandoffSource.Fallback, emptyResult.HandoffSource);

        await using var secondContext = await PersistentLeaderContext.CreateAsync();
        var secondRuntime = secondContext.CreateRuntime();
        var secondRegistry = new AgentRuntimeRegistry();
        secondRegistry.Register(secondRuntime);
        secondRuntime.QueueTurn(secondContext.Completed("old"));
        var secondPane = CreatePane(secondContext, secondRegistry);
        await secondPane.InitializeAsync();
        secondPane.DraftMessage = "question";
        await secondPane.SendAsync();
        var secondOldEpoch = await secondContext.Epochs.GetCurrentForProjectAsync(secondContext.ProjectA.Id);
        secondContext.Time.SetUtcNow(secondContext.T1);
        secondRuntime.ResumeException = new IOException("old session unavailable");

        var resumeResult = await CreateService(secondContext, secondRegistry).RolloverAsync(
            secondContext.ProjectA, secondPane.Session!, secondOldEpoch!, true, "Manual");

        Assert.Equal(LeaderHandoffSource.Fallback, resumeResult.HandoffSource);
        Assert.Empty(secondRuntime.SentRequests.Skip(1));
    }

    [Fact]
    public async Task Missing_completion_and_handoff_runtime_failure_each_use_fallback()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var runtime = context.CreateRuntime();
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        runtime.QueueTurn(context.Completed("visible source"));
        var pane = CreatePane(context, registry);
        await pane.InitializeAsync();
        pane.DraftMessage = "question";
        await pane.SendAsync();
        var oldEpoch = await context.Epochs.GetCurrentForProjectAsync(context.ProjectA.Id);

        var missingCompletion = await CreateService(context, registry).RolloverAsync(
            context.ProjectA, pane.Session!, oldEpoch!, false, "Manual");

        Assert.Equal(LeaderHandoffSource.Fallback, missingCompletion.HandoffSource);
        Assert.Contains("visible source", missingCompletion.HandoffSummary, StringComparison.Ordinal);

        await using var failedContext = await PersistentLeaderContext.CreateAsync();
        var failedRuntime = failedContext.CreateRuntime();
        var failedRegistry = new AgentRuntimeRegistry();
        failedRegistry.Register(failedRuntime);
        failedRuntime.QueueTurn(failedContext.Completed("other visible source"));
        var failedPane = CreatePane(failedContext, failedRegistry);
        await failedPane.InitializeAsync();
        failedPane.DraftMessage = "question";
        await failedPane.SendAsync();
        var failedOldEpoch = await failedContext.Epochs.GetCurrentForProjectAsync(failedContext.ProjectA.Id);
        failedRuntime.SendException = new IOException("handoff runtime failed");

        var runtimeFailure = await CreateService(failedContext, failedRegistry).RolloverAsync(
            failedContext.ProjectA, failedPane.Session!, failedOldEpoch!, false, "Manual");

        Assert.Equal(LeaderHandoffSource.Fallback, runtimeFailure.HandoffSource);
        Assert.Contains("other visible source", runtimeFailure.HandoffSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fresh_runtime_must_return_distinct_session_and_external_identities()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var runtime = context.CreateRuntime();
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        runtime.QueueTurn(context.Completed("old"));
        var pane = CreatePane(context, registry);
        await pane.InitializeAsync();
        pane.DraftMessage = "question";
        await pane.SendAsync();
        var oldEpoch = await context.Epochs.GetCurrentForProjectAsync(context.ProjectA.Id);
        var oldSession = pane.Session!;
        runtime.QueueTurn(context.Completed("CURRENT FOCUS\nhandoff"));
        runtime.CreateSessionOverride = _ => oldSession;

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(context, registry).RolloverAsync(
            context.ProjectA, oldSession, oldEpoch!, false, "Manual"));

        Assert.Equal(oldEpoch!.Id, (await context.Leaders.GetAsync(context.ProjectA.Id))!.CurrentEpochId);
        Assert.Null((await context.Epochs.GetAsync(oldEpoch.Id))!.EndedAt);
        Assert.DoesNotContain(oldSession, runtime.StoppedSessions);
    }

    [Fact]
    public async Task Fresh_runtime_must_return_a_non_empty_external_session_identity()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var runtime = context.CreateRuntime();
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        runtime.QueueTurn(context.Completed("old"));
        var pane = CreatePane(context, registry);
        await pane.InitializeAsync();
        pane.DraftMessage = "question";
        await pane.SendAsync();
        var oldEpoch = await context.Epochs.GetCurrentForProjectAsync(context.ProjectA.Id);
        var oldSession = pane.Session!;
        runtime.QueueTurn(context.Completed("CURRENT FOCUS\nhandoff"));
        runtime.CreateSessionOverride = _ => oldSession with
        {
            Id = AgentSessionId.New(),
            ExternalSessionId = null
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(context, registry).RolloverAsync(
            context.ProjectA, oldSession, oldEpoch!, false, "Manual"));

        Assert.Equal(oldEpoch!.Id, (await context.Leaders.GetAsync(context.ProjectA.Id))!.CurrentEpochId);
        Assert.Null((await context.Epochs.GetAsync(oldEpoch.Id))!.EndedAt);
        Assert.Contains(runtime.CreatedSessions.Last(), runtime.StoppedSessions);
    }

    [Fact]
    public async Task Cleanup_failure_does_not_replace_storage_rollover_failure()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var runtime = context.CreateRuntime();
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        runtime.QueueTurn(context.Completed("old"));
        var pane = CreatePane(context, registry);
        await pane.InitializeAsync();
        pane.DraftMessage = "question";
        await pane.SendAsync();
        var oldEpoch = await context.Epochs.GetCurrentForProjectAsync(context.ProjectA.Id);
        runtime.QueueTurn(context.Completed("CURRENT FOCUS\nhandoff"));
        await context.FailCurrentEpochUpdatesAsync();
        runtime.StopException = new IOException("cleanup failed");

        var error = await Assert.ThrowsAnyAsync<Exception>(() => CreateService(context, registry).RolloverAsync(
            context.ProjectA, pane.Session!, oldEpoch!, false, "Manual"));

        Assert.DoesNotContain("cleanup failed", error.Message, StringComparison.Ordinal);
        Assert.Contains("forced current epoch failure", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Agent_error_invalidates_semantic_handoff_even_if_completion_follows()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var runtime = context.CreateRuntime();
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        runtime.QueueTurn(context.Completed("visible fallback source"));
        var pane = CreatePane(context, registry);
        await pane.InitializeAsync();
        pane.DraftMessage = "question";
        await pane.SendAsync();
        var oldEpoch = await context.Epochs.GetCurrentForProjectAsync(context.ProjectA.Id);
        runtime.QueueTurn(
            new AgentError("handoff failed", context.T0),
            context.Completed("CURRENT FOCUS\nshould not be trusted"));

        var result = await CreateService(context, registry).RolloverAsync(
            context.ProjectA, pane.Session!, oldEpoch!, false, "Manual");

        Assert.Equal(LeaderHandoffSource.Fallback, result.HandoffSource);
        Assert.Contains("visible fallback source", result.HandoffSummary, StringComparison.Ordinal);
        Assert.DoesNotContain("should not be trusted", result.HandoffSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_persisted_runtime_account_does_not_switch_to_another_account()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var originalRuntime = context.CreateRuntime();
        var originalRegistry = new AgentRuntimeRegistry();
        originalRegistry.Register(originalRuntime);
        originalRuntime.QueueTurn(context.Completed("old"));
        var pane = CreatePane(context, originalRegistry);
        await pane.InitializeAsync();
        pane.DraftMessage = "question";
        await pane.SendAsync();
        var oldEpoch = await context.Epochs.GetCurrentForProjectAsync(context.ProjectA.Id);
        var wrongRuntime = new FakeAgentRuntime();
        var wrongRegistry = new AgentRuntimeRegistry();
        wrongRegistry.Register(wrongRuntime);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => CreateService(context, wrongRegistry).RolloverAsync(
            context.ProjectA, pane.Session!, oldEpoch!, false, "Manual"));

        Assert.Empty(wrongRuntime.CreatedSessions);
        Assert.Equal(oldEpoch!.Id, (await context.Leaders.GetAsync(context.ProjectA.Id))!.CurrentEpochId);
    }

    [Fact]
    public async Task Storage_failure_keeps_old_current_and_best_effort_stops_fresh_session()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var runtime = context.CreateRuntime();
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        runtime.QueueTurn(context.Completed("old"));
        var pane = CreatePane(context, registry);
        await pane.InitializeAsync();
        pane.DraftMessage = "question";
        await pane.SendAsync();
        var oldEpoch = await context.Epochs.GetCurrentForProjectAsync(context.ProjectA.Id);
        runtime.QueueTurn(context.Completed("CURRENT FOCUS\nold"));
        context.Time.SetUtcNow(context.T1);
        await context.FailCurrentEpochUpdatesAsync();

        await Assert.ThrowsAnyAsync<Exception>(() => CreateService(context, registry).RolloverAsync(
            context.ProjectA, pane.Session!, oldEpoch!, false, "Manual"));

        Assert.Equal(oldEpoch!.Id, (await context.Leaders.GetAsync(context.ProjectA.Id))!.CurrentEpochId);
        Assert.Null((await context.Epochs.GetAsync(oldEpoch.Id))!.EndedAt);
        Assert.Equal(2, runtime.CreatedSessions.Count);
        Assert.Equal(runtime.CreatedSessions[1].Id, Assert.Single(runtime.StoppedSessions).Id);
        Assert.Single(await context.Epochs.GetAllForProjectAsync(context.ProjectA.Id));
    }

    [Fact]
    public async Task Unified_boot_context_uses_immediate_predecessor_and_is_not_persisted()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var runtime = context.CreateRuntime();
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        runtime.QueueTurn(context.Completed("old"));
        var pane = CreatePane(context, registry);
        await pane.InitializeAsync();
        pane.DraftMessage = "question";
        await pane.SendAsync();
        var first = await context.Epochs.GetCurrentForProjectAsync(context.ProjectA.Id);
        context.Time.SetUtcNow(context.T1);
        runtime.QueueTurn(context.Completed("CURRENT FOCUS\nIMMEDIATE_MARKER"));
        var service = CreateService(context, registry);
        var rollover = await service.RolloverAsync(context.ProjectA, pane.Session!, first!, false, "Manual");

        var firstRequest = await context.CreateBootBuilder().BuildAsync(context.ProjectA, "real user text");
        await context.Messages.AppendAsync(rollover.NewEpoch.Id, "user", "real user text", context.T1);

        Assert.Contains("WORKBENCH PROJECT CONTEXT", firstRequest.Text, StringComparison.Ordinal);
        Assert.Contains("IMMEDIATE_MARKER", firstRequest.Text, StringComparison.Ordinal);
        Assert.Contains("CURRENT USER MESSAGE", firstRequest.Text, StringComparison.Ordinal);
        Assert.EndsWith("real user text", firstRequest.Text, StringComparison.Ordinal);
        var persisted = Assert.Single(await context.Messages.GetAllAsync(rollover.NewEpoch.Id));
        Assert.Equal("real user text", persisted.Text);
        Assert.DoesNotContain("WORKBENCH PROJECT CONTEXT", persisted.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Fallback_with_huge_project_identity_stays_bounded_and_keeps_latest_message_utf8_safe()
    {
        var project = new Workbench.Core.Projects.Project(
            Guid.NewGuid(), new string('名', 3000), "C:/" + new string('根', 3000),
            Workbench.Core.Projects.ProjectType.Generic, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var epoch = new Workbench.Storage.Leaders.StoredLeaderSessionEpoch(
            Guid.NewGuid(), project.Id, "provider", Guid.NewGuid(), "model", Guid.NewGuid(), "external", "C:/",
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, null, null);
        var messages = new[]
        {
            new Workbench.Storage.Leaders.StoredLeaderMessage(
                1, epoch.Id, 1, "assistant", "LATEST_UTF8_MARKER_你好", DateTimeOffset.UnixEpoch)
        };

        var fallback = LeaderHandoffBuilder.BuildFallback(project, epoch, messages);

        Assert.True(Encoding.UTF8.GetByteCount(fallback) <= LeaderHandoffBuilder.MaxHandoffUtf8Bytes);
        Assert.Contains("LATEST_UTF8_MARKER_你好", fallback, StringComparison.Ordinal);
        Assert.DoesNotContain('\uFFFD', fallback);
    }

    private static LeaderPaneViewModel CreatePane(PersistentLeaderContext context, AgentRuntimeRegistry registry) =>
        new(context.ProjectA, registry, context.CreateManager(), () => Task.CompletedTask);

    private static LeaderSessionRolloverService CreateService(
        PersistentLeaderContext context,
        AgentRuntimeRegistry registry) =>
        new(registry, context.Leaders, context.Messages, context.Time);
}
