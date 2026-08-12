using Workbench.App.Tests.Support;
using Workbench.App.ViewModels.Panes;
using Workbench.Core.Leaders;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Registry;

namespace Workbench.App.Tests;

public sealed class LeaderRolloverPolicyTests
{
    [Fact]
    public async Task Workspace_composition_enables_manual_rollover_after_first_real_send()
    {
        var runtime = new FakeAgentRuntime();
        var registry = CreateRegistry(runtime);
        await using var context = await AppTestContext.CreateAsync(runtimeRegistry: registry);
        var workspace = await context.CreateWorkspaceForNewProjectAsync();
        runtime.QueueTurn(new AgentTurnCompleted(
            new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed, "old", null),
            context.Time.GetUtcNow()));
        await workspace.LeaderPane.InitializeAsync();
        workspace.LeaderPane.DraftMessage = "first";
        await workspace.LeaderPane.SendAsync();
        var oldEpoch = workspace.LeaderPane.SessionEpochId;
        runtime.QueueTurn(new AgentTurnCompleted(
            new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed, "CURRENT FOCUS\ncomposed", null),
            context.Time.GetUtcNow()));

        await workspace.LeaderPane.StartNewBrainAsync();

        Assert.NotEqual(oldEpoch, workspace.LeaderPane.SessionEpochId);
    }

    [Fact]
    public async Task Due_auto_send_rolls_over_before_original_message_reaches_fresh_session()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var runtime = context.CreateRuntime();
        var registry = CreateRegistry(runtime);
        runtime.QueueTurn(context.Completed("old answer"));
        var pane = CreatePane(context, registry);
        await pane.InitializeAsync();
        pane.DraftMessage = "old question";
        await pane.SendAsync();
        var oldEpoch = await context.Epochs.GetCurrentForProjectAsync(context.ProjectA.Id);
        var oldSession = pane.Session!;
        await context.WorkbenchSettings.SaveLeaderSessionRotationPolicyAsync(LeaderSessionRotationPolicy.Auto);
        context.Time.SetUtcNow(context.T1);
        runtime.QueueTurn(context.Completed("CURRENT FOCUS\nAUTO_HANDOFF"));
        runtime.QueueTurn(context.Completed("new answer"));
        pane.DraftMessage = "original next-day text";

        await pane.SendAsync();

        var current = await context.Epochs.GetCurrentForProjectAsync(context.ProjectA.Id);
        Assert.Equal(context.ProjectA.Id, pane.ProjectLeaderId);
        Assert.NotEqual(oldEpoch!.Id, current!.Id);
        Assert.NotEqual(oldSession.Id.Value, current.AgentSessionId);
        Assert.NotEqual(oldSession.ExternalSessionId, current.ExternalSessionId);
        Assert.Equal(oldSession.Id, runtime.SentSessions[1].Id);
        Assert.Equal(new AgentSessionId(current.AgentSessionId), runtime.SentSessions[2].Id);
        Assert.Contains("WORKBENCH SESSION HANDOFF", runtime.SentRequests[2].Text, StringComparison.Ordinal);
        Assert.Contains("original next-day text", runtime.SentRequests[2].Text, StringComparison.Ordinal);
        Assert.Equal(["original next-day text", "new answer"],
            (await context.Messages.GetAllAsync(current.Id)).Select(message => message.Text));
        Assert.DoesNotContain(await context.Messages.GetAllAsync(oldEpoch.Id), message => message.Text == "original next-day text");
    }

    [Fact]
    public async Task Due_ask_intercepts_once_and_continue_previous_uses_old_epoch()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var runtime = context.CreateRuntime();
        var registry = CreateRegistry(runtime);
        runtime.QueueTurn(context.Completed("old"));
        var pane = CreatePane(context, registry);
        await pane.InitializeAsync();
        pane.DraftMessage = "first";
        await pane.SendAsync();
        var oldEpoch = pane.SessionEpochId;
        await context.WorkbenchSettings.SaveLeaderSessionRotationPolicyAsync(LeaderSessionRotationPolicy.Ask);
        context.Time.SetUtcNow(context.T1);
        pane.DraftMessage = "pending draft";

        await pane.SendAsync();

        Assert.True(pane.HasPendingRotationDecision);
        Assert.Equal("pending draft", pane.DraftMessage);
        Assert.Single(runtime.SentRequests);
        Assert.Equal(["first", "old"], pane.Messages.Select(message => message.Text));
        await Assert.ThrowsAsync<InvalidOperationException>(() => pane.SendAsync());
        runtime.QueueTurn(context.Completed("continued"));
        await pane.ContinuePreviousAsync();

        Assert.False(pane.HasPendingRotationDecision);
        Assert.Equal(oldEpoch, pane.SessionEpochId);
        Assert.Single(runtime.CreatedSessions);
        Assert.Equal("pending draft", runtime.SentRequests[1].Text);
        Assert.Equal(context.T1, (await context.Epochs.GetAsync(oldEpoch!.Value))!.LastActiveAt);
    }

    [Fact]
    public async Task Ask_start_fresh_sends_preserved_draft_through_shared_rollover_path()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var runtime = context.CreateRuntime();
        var registry = CreateRegistry(runtime);
        runtime.QueueTurn(context.Completed("old"));
        var pane = CreatePane(context, registry);
        await pane.InitializeAsync();
        pane.DraftMessage = "first";
        await pane.SendAsync();
        var oldEpoch = pane.SessionEpochId;
        await context.WorkbenchSettings.SaveLeaderSessionRotationPolicyAsync(LeaderSessionRotationPolicy.Ask);
        context.Time.SetUtcNow(context.T1);
        pane.DraftMessage = "fresh draft";
        await pane.SendAsync();
        runtime.QueueTurn(context.Completed("CURRENT FOCUS\nASK_HANDOFF"));
        runtime.QueueTurn(context.Completed("fresh answer"));

        await pane.StartFreshAsync();

        Assert.NotEqual(oldEpoch, pane.SessionEpochId);
        Assert.Equal(2, runtime.CreatedSessions.Count);
        Assert.Contains("fresh draft", runtime.SentRequests[2].Text, StringComparison.Ordinal);
        Assert.Equal("WorkdayBoundary", (await context.Epochs.GetAsync(oldEpoch!.Value))!.RolloverReason);
    }

    [Fact]
    public async Task Manual_only_due_send_reuses_old_epoch_without_handoff()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var runtime = context.CreateRuntime();
        var registry = CreateRegistry(runtime);
        runtime.QueueTurn(context.Completed("old"));
        var pane = CreatePane(context, registry);
        await pane.InitializeAsync();
        pane.DraftMessage = "first";
        await pane.SendAsync();
        var oldEpoch = pane.SessionEpochId;
        await context.WorkbenchSettings.SaveLeaderSessionRotationPolicyAsync(LeaderSessionRotationPolicy.ManualOnly);
        context.Time.SetUtcNow(context.T1);
        runtime.QueueTurn(context.Completed("same brain"));
        pane.DraftMessage = "next day";

        await pane.SendAsync();

        Assert.Equal(oldEpoch, pane.SessionEpochId);
        Assert.Single(runtime.CreatedSessions);
        Assert.Equal("next day", runtime.SentRequests[1].Text);
    }

    [Fact]
    public async Task Manual_new_brain_rolls_over_same_day_and_leaves_zero_message_epoch()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var runtime = context.CreateRuntime();
        var registry = CreateRegistry(runtime);
        runtime.QueueTurn(context.Completed("old"));
        var pane = CreatePane(context, registry);
        await pane.InitializeAsync();
        Assert.False(pane.CanStartNewBrain);
        await Assert.ThrowsAsync<InvalidOperationException>(() => pane.StartNewBrainAsync());
        pane.DraftMessage = "first";
        await pane.SendAsync();
        var oldEpoch = pane.SessionEpochId;
        runtime.QueueTurn(context.Completed("CURRENT FOCUS\nMANUAL_HANDOFF"));

        await pane.StartNewBrainAsync();

        Assert.True(pane.CanStartNewBrain);
        Assert.NotEqual(oldEpoch, pane.SessionEpochId);
        Assert.Empty(await context.Messages.GetAllAsync(pane.SessionEpochId!.Value));
        Assert.Equal("Manual", (await context.Epochs.GetAsync(oldEpoch!.Value))!.RolloverReason);
        Assert.Equal("Fresh Leader session started.", pane.RotationMessage);
    }

    [Fact]
    public async Task Manual_rollover_then_restart_before_first_send_injects_previous_handoff_once()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var firstRuntime = context.CreateRuntime();
        var firstRegistry = CreateRegistry(firstRuntime);
        firstRuntime.QueueTurn(context.Completed("old"));
        var firstPane = CreatePane(context, firstRegistry);
        await firstPane.InitializeAsync();
        firstPane.DraftMessage = "remember marker";
        await firstPane.SendAsync();
        firstRuntime.QueueTurn(context.Completed("CURRENT FOCUS\nRESTART_HANDOFF_MARKER"));
        await firstPane.StartNewBrainAsync();
        var newEpochId = firstPane.SessionEpochId;

        var restartedRuntime = context.CreateRuntime();
        var restartedRegistry = CreateRegistry(restartedRuntime);
        var restored = CreatePane(context, restartedRegistry);
        await restored.InitializeAsync();
        restartedRuntime.QueueTurn(context.Completed("marker restored"));
        restored.DraftMessage = "what marker?";
        await restored.SendAsync();

        Assert.Equal(newEpochId, restored.SessionEpochId);
        Assert.Single(restartedRuntime.ResumedSessions);
        Assert.Contains("RESTART_HANDOFF_MARKER", Assert.Single(restartedRuntime.SentRequests).Text, StringComparison.Ordinal);
        Assert.Equal(["what marker?", "marker restored"], restored.Messages.Select(message => message.Text));
        Assert.DoesNotContain(restored.Messages, message => message.Text.Contains("WORKBENCH SESSION HANDOFF", StringComparison.Ordinal));
        Assert.Equal(["what marker?", "marker restored"],
            (await context.Messages.GetAllAsync(newEpochId!.Value)).Select(message => message.Text));
    }

    [Fact]
    public async Task Manual_new_brain_is_rejected_while_busy_or_approval_is_pending()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var runtime = context.CreateRuntime();
        var registry = CreateRegistry(runtime);
        runtime.QueueTurn(context.Completed("old"));
        var pane = CreatePane(context, registry);
        await pane.InitializeAsync();
        pane.DraftMessage = "first";
        await pane.SendAsync();

        runtime.ResetTurnSignals();
        runtime.PauseBeforeEvents = true;
        runtime.QueueTurn(context.Completed("busy done"));
        pane.DraftMessage = "busy";
        var busyTurn = pane.SendAsync();
        await runtime.WaitForSendAsync();
        Assert.False(pane.CanStartNewBrain);
        await Assert.ThrowsAsync<InvalidOperationException>(() => pane.StartNewBrainAsync());
        runtime.ReleaseSend();
        await busyTurn;

        runtime.ResetTurnSignals();
        runtime.PauseBeforeEvents = false;
        runtime.PauseAfterApproval = true;
        runtime.QueueTurn(
            new AgentApprovalRequested(
                AgentApprovalRequestId.New(), pane.Session!.Id, "Approve?",
                [new AgentApprovalOption("allow-once", "Allow once")], context.T0),
            context.Completed("approval done"));
        pane.DraftMessage = "approval";
        var approvalTurn = pane.SendAsync();
        await runtime.WaitForApprovalAsync();
        Assert.False(pane.CanStartNewBrain);
        await Assert.ThrowsAsync<InvalidOperationException>(() => pane.StartNewBrainAsync());
        runtime.ReleaseApproval();
        await approvalTurn;
    }

    [Fact]
    public async Task Not_due_auto_send_reuses_current_epoch_without_internal_handoff()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var runtime = context.CreateRuntime();
        var registry = CreateRegistry(runtime);
        runtime.QueueTurn(context.Completed("old"));
        var pane = CreatePane(context, registry);
        await pane.InitializeAsync();
        pane.DraftMessage = "first";
        await pane.SendAsync();
        var epoch = pane.SessionEpochId;
        context.Time.SetUtcNow(context.T0.AddHours(1));
        runtime.QueueTurn(context.Completed("same day"));
        pane.DraftMessage = "second";

        await pane.SendAsync();

        Assert.Equal(epoch, pane.SessionEpochId);
        Assert.Single(runtime.CreatedSessions);
        Assert.Equal("second", runtime.SentRequests[1].Text);
    }

    [Fact]
    public async Task New_session_failure_keeps_auto_draft_and_old_epoch_current()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var runtime = context.CreateRuntime();
        var registry = CreateRegistry(runtime);
        runtime.QueueTurn(context.Completed("old"));
        var pane = CreatePane(context, registry);
        await pane.InitializeAsync();
        pane.DraftMessage = "first";
        await pane.SendAsync();
        var oldEpoch = pane.SessionEpochId;
        await context.WorkbenchSettings.SaveLeaderSessionRotationPolicyAsync(LeaderSessionRotationPolicy.Auto);
        context.Time.SetUtcNow(context.T1);
        runtime.QueueTurn(context.Completed("CURRENT FOCUS\nhandoff"));
        runtime.CreateException = new IOException("cannot create successor");
        pane.DraftMessage = "must survive";

        await pane.SendAsync();

        Assert.Equal("must survive", pane.DraftMessage);
        Assert.Equal(oldEpoch, pane.SessionEpochId);
        Assert.Null((await context.Epochs.GetAsync(oldEpoch!.Value))!.EndedAt);
        Assert.DoesNotContain(await context.Messages.GetAllAsync(oldEpoch.Value), message => message.Text == "must survive");
    }

    [Fact]
    public async Task Manual_new_brain_failure_keeps_old_epoch_and_surfaces_inline_error()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var runtime = context.CreateRuntime();
        var registry = CreateRegistry(runtime);
        runtime.QueueTurn(context.Completed("old"));
        var pane = CreatePane(context, registry);
        await pane.InitializeAsync();
        pane.DraftMessage = "first";
        await pane.SendAsync();
        var oldEpoch = pane.SessionEpochId;
        runtime.QueueTurn(context.Completed("CURRENT FOCUS\nhandoff"));
        runtime.CreateException = new IOException("cannot create successor");

        await pane.StartNewBrainAsync();

        Assert.Equal(oldEpoch, pane.SessionEpochId);
        Assert.Null((await context.Epochs.GetAsync(oldEpoch!.Value))!.EndedAt);
        Assert.Contains(pane.Messages, message => message.Text == "Could not start a fresh Leader session.");
    }

    private static AgentRuntimeRegistry CreateRegistry(FakeAgentRuntime runtime)
    {
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        return registry;
    }

    private static LeaderPaneViewModel CreatePane(
        PersistentLeaderContext context,
        AgentRuntimeRegistry registry) =>
        new(
            context.ProjectA,
            registry,
            context.CreateManager(),
            () => Task.CompletedTask,
            rotationState: context.CreateRotationStateService(),
            rolloverService: context.CreateRolloverService(registry));
}
