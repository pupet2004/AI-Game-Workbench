using Workbench.App.Leader;
using Workbench.App.Tests.Support;
using Workbench.App.ViewModels.Leader;
using Workbench.Runtime.Agents;
using Workbench.Storage.Memory;

namespace Workbench.App.Tests;

public sealed class LeaderBootIntegrationTests
{
    [Fact]
    public async Task First_ever_send_injects_memory_once_but_persists_and_displays_only_original_text()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        await AddMemoryAsync(context, context.ProjectA.Id, "Formal", "Memory Boot Smoke", "MEMORY_BOOT_FORMAL_731", context.T0);
        var runtime = context.CreateRuntime();
        runtime.QueueTurn(context.Completed("MEMORY_BOOT_FORMAL_731"));
        runtime.QueueTurn(context.Completed("later"));
        var pane = context.CreatePane(runtime, bootContextBuilder: context.CreateBootBuilder());
        await pane.InitializeAsync();

        pane.DraftMessage = "What is the marker?";
        await pane.SendAsync();
        pane.DraftMessage = "Continue.";
        await pane.SendAsync();

        Assert.Contains("WORKBENCH PROJECT CONTEXT", runtime.SentRequests[0].Text, StringComparison.Ordinal);
        Assert.Contains("MEMORY_BOOT_FORMAL_731", runtime.SentRequests[0].Text, StringComparison.Ordinal);
        Assert.Equal("Continue.", runtime.SentRequests[1].Text);
        Assert.Equal(["What is the marker?", "MEMORY_BOOT_FORMAL_731", "Continue.", "later"],
            pane.Messages.Select(message => message.Text));
        var epoch = await context.Epochs.GetCurrentForProjectAsync(context.ProjectA.Id);
        Assert.NotNull(epoch!.BootContextDeliveredAt);
        var persisted = await context.Messages.GetAllAsync(epoch.Id);
        Assert.Equal("What is the marker?", persisted[0].Text);
        Assert.DoesNotContain("WORKBENCH PROJECT CONTEXT", persisted[0].Text, StringComparison.Ordinal);
        Assert.DoesNotContain("MEMORY_BOOT_FORMAL_731", persisted[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Memory_load_failure_preserves_draft_and_creates_no_epoch_message_or_runtime_turn()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var runtime = context.CreateRuntime();
        var pane = context.CreatePane(runtime, bootContextBuilder: new ThrowingBootBuilder());
        await pane.InitializeAsync();
        pane.DraftMessage = "keep this draft";

        await pane.SendAsync();

        Assert.Equal("keep this draft", pane.DraftMessage);
        Assert.Empty(runtime.CreateRequests);
        Assert.Empty(runtime.SentRequests);
        Assert.Null(await context.Leaders.GetAsync(context.ProjectA.Id));
        Assert.Contains(pane.Messages, message => message.Text == "Project memory could not be loaded.");
        Assert.DoesNotContain(pane.Messages, message => message.Text == "keep this draft");
    }

    [Fact]
    public async Task Failure_before_first_runtime_event_keeps_boot_pending_for_retry()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        await AddMemoryAsync(context, context.ProjectA.Id, "Learned", "Marker", "LEARNED_BOOT_MARKER_512", null);
        var runtime = context.CreateRuntime();
        runtime.SendException = new TimeoutException("not accepted");
        var pane = context.CreatePane(runtime, bootContextBuilder: context.CreateBootBuilder());
        await pane.InitializeAsync();
        pane.DraftMessage = "first attempt";

        await pane.SendAsync();
        var pending = await context.Epochs.GetCurrentForProjectAsync(context.ProjectA.Id);
        Assert.Null(pending!.BootContextDeliveredAt);

        runtime.SendException = null;
        runtime.QueueTurn(context.Completed("ok"));
        pane.DraftMessage = "retry";
        await pane.SendAsync();

        Assert.All(runtime.SentRequests, request =>
            Assert.Contains("LEARNED_BOOT_MARKER_512", request.Text, StringComparison.Ordinal));
        Assert.Contains("provisional", runtime.SentRequests[1].Text, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull((await context.Epochs.GetAsync(pending.Id))!.BootContextDeliveredAt);
    }

    [Fact]
    public async Task Restart_after_pre_acceptance_failure_reinjects_boot_into_same_epoch()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        await AddMemoryAsync(context, context.ProjectA.Id, "Formal", "Restart", "RESTART_BOOT_MARKER", context.T0);
        var failedRuntime = context.CreateRuntime();
        failedRuntime.SendException = new IOException("connection lost");
        var first = context.CreatePane(failedRuntime, bootContextBuilder: context.CreateBootBuilder());
        await first.InitializeAsync();
        first.DraftMessage = "attempt";
        await first.SendAsync();
        var epochId = first.SessionEpochId;

        var restartedRuntime = context.CreateRuntime();
        restartedRuntime.QueueTurn(context.Completed("done"));
        var restarted = context.CreatePane(
            restartedRuntime,
            newManager: true,
            bootContextBuilder: context.CreateBootBuilder());
        await restarted.InitializeAsync();
        restarted.DraftMessage = "retry after restart";
        await restarted.SendAsync();

        Assert.Equal(epochId, restarted.SessionEpochId);
        Assert.Single(restartedRuntime.ResumedSessions);
        Assert.Contains("RESTART_BOOT_MARKER", Assert.Single(restartedRuntime.SentRequests).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task First_runtime_event_marks_boot_delivered_even_if_stream_fails_afterward()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        await AddMemoryAsync(context, context.ProjectA.Id, "Formal", "Accepted", "ACCEPTED_BOOT_MARKER", context.T0);
        var runtime = context.CreateRuntime();
        runtime.QueueTurn(new AgentTextDelta("accepted", context.T0));
        runtime.SendExceptionAfterFirstEvent = new IOException("stream lost");
        var pane = context.CreatePane(runtime, bootContextBuilder: context.CreateBootBuilder());
        await pane.InitializeAsync();
        pane.DraftMessage = "first";

        await pane.SendAsync();
        Assert.NotNull((await context.Epochs.GetCurrentForProjectAsync(context.ProjectA.Id))!.BootContextDeliveredAt);

        runtime.SendExceptionAfterFirstEvent = null;
        runtime.QueueTurn(context.Completed("done"));
        pane.DraftMessage = "retry";
        await pane.SendAsync();

        Assert.DoesNotContain("WORKBENCH PROJECT CONTEXT", runtime.SentRequests[1].Text, StringComparison.Ordinal);
        Assert.Equal("retry", runtime.SentRequests[1].Text);
    }

    [Fact]
    public async Task First_turn_loads_memory_at_send_time_and_later_turns_do_not_live_patch_the_epoch()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var runtime = context.CreateRuntime();
        runtime.QueueTurn(context.Completed("first answer"));
        runtime.QueueTurn(context.Completed("second answer"));
        var pane = context.CreatePane(runtime, bootContextBuilder: context.CreateBootBuilder());
        await pane.InitializeAsync();
        await AddMemoryAsync(context, context.ProjectA.Id, "Formal", "Late Before Send", "SEND_TIME_MARKER", context.T0);

        pane.DraftMessage = "first";
        await pane.SendAsync();
        await AddMemoryAsync(context, context.ProjectA.Id, "Formal", "Late After Send", "NO_LIVE_PATCH_MARKER", context.T0);
        pane.DraftMessage = "second";
        await pane.SendAsync();

        Assert.Contains("SEND_TIME_MARKER", runtime.SentRequests[0].Text, StringComparison.Ordinal);
        Assert.Equal("second", runtime.SentRequests[1].Text);
        Assert.DoesNotContain("NO_LIVE_PATCH_MARKER", runtime.SentRequests[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resuming_an_epoch_with_a_delivered_conversation_does_not_inject_again()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        await AddMemoryAsync(context, context.ProjectA.Id, "Formal", "Resume", "RESUME_MARKER", context.T0);
        var firstRuntime = context.CreateRuntime();
        firstRuntime.QueueTurn(context.Completed("first answer"));
        var first = context.CreatePane(firstRuntime, bootContextBuilder: context.CreateBootBuilder());
        await first.InitializeAsync();
        first.DraftMessage = "first";
        await first.SendAsync();

        var resumedRuntime = context.CreateRuntime();
        resumedRuntime.QueueTurn(context.Completed("second answer"));
        var resumed = context.CreatePane(
            resumedRuntime,
            newManager: true,
            bootContextBuilder: context.CreateBootBuilder());
        await resumed.InitializeAsync();
        resumed.DraftMessage = "second";
        await resumed.SendAsync();

        Assert.Equal("second", Assert.Single(resumedRuntime.SentRequests).Text);
        Assert.DoesNotContain("RESUME_MARKER", resumedRuntime.SentRequests[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Builder_reads_only_the_requested_projects_memory()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        await AddMemoryAsync(context, context.ProjectA.Id, "Formal", "A", "PROJECT_A_MARKER", context.T0);
        await AddMemoryAsync(context, context.ProjectB.Id, "Formal", "B", "PROJECT_B_MARKER", context.T0);

        var request = await context.CreateBootBuilder().BuildAsync(context.ProjectA, "question");

        Assert.Contains("PROJECT_A_MARKER", request.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("PROJECT_B_MARKER", request.Text, StringComparison.Ordinal);
    }

    private static Task AddMemoryAsync(
        PersistentLeaderContext context,
        Guid projectId,
        string layer,
        string topic,
        string content,
        DateTimeOffset? certifiedAt)
    {
        var memory = new ProjectMemoryItem(
            Guid.NewGuid(), projectId, layer, topic, content, "Active",
            context.T0, context.T0, certifiedAt);
        return context.Memories.AddAsync(memory, [new ProjectMemorySource("Manual", "boot-test")]);
    }

    private sealed class ThrowingBootBuilder : ILeaderBootContextBuilder
    {
        public Task<AgentRequest> BuildAsync(
            Workbench.Core.Projects.Project project,
            string originalUserText,
            CancellationToken cancellationToken = default) =>
            Task.FromException<AgentRequest>(new IOException("memory unavailable"));
    }
}
