using Workbench.App.AgentHost;
using Workbench.App.Tests.Support;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Registry;

namespace Workbench.App.Tests;

public sealed class AgentHostTests
{
    [Fact]
    public async Task Hosted_turn_records_leader_source_and_emits_events()
    {
        var runtime = new FakeAgentRuntime();
        var session = await runtime.CreateSessionAsync(new CreateAgentSessionRequest(runtime.Account.Id, "model-a", "C:/Project"));
        runtime.QueueTurn(
            new AgentTextDelta("done", DateTimeOffset.UtcNow),
            new AgentTurnCompleted(new AgentResult(session.Id, AgentSessionStatus.Completed, "done", null), DateTimeOffset.UtcNow));
        var host = new InProcessAgentHost(Registry(runtime));
        var received = new List<HostedAgentEvent>();
        host.EventReceived += (_, item) => received.Add(item);

        var events = new List<AgentEvent>();
        await foreach (var item in host.RunTurnAsync(
                           session,
                           new HostedAgentIntent(AgentIntentSource.Leader, "continue the assignment")))
        {
            events.Add(item);
        }

        var snapshot = host.Attach(session);
        Assert.Equal(2, events.Count);
        Assert.Equal(events.Count, received.Count);
        Assert.Equal(AgentIntentSource.Leader, Assert.Single(snapshot.Intents).Source);
        Assert.Equal(AgentRequestSource.Leader, runtime.SentRequests.Single().Source);
        Assert.Equal(AgentSessionStatus.Completed, snapshot.Session.Status);
        Assert.False(snapshot.HasActiveTurn);
    }

    [Fact]
    public async Task Hosted_writer_serializes_turns_for_the_same_session()
    {
        var runtime = new FakeAgentRuntime { PauseBeforeEvents = true };
        var session = await runtime.CreateSessionAsync(new CreateAgentSessionRequest(runtime.Account.Id, "model-a", "C:/Project"));
        runtime.QueueTurn(new AgentTurnCompleted(new AgentResult(session.Id, AgentSessionStatus.Completed, "first", null), DateTimeOffset.UtcNow));
        runtime.QueueTurn(new AgentTurnCompleted(new AgentResult(session.Id, AgentSessionStatus.Completed, "second", null), DateTimeOffset.UtcNow));
        var host = new InProcessAgentHost(Registry(runtime));

        var first = ConsumeAsync(host.RunTurnAsync(session, new HostedAgentIntent(AgentIntentSource.User, "first")));
        await runtime.WaitForSendAsync();
        var second = ConsumeAsync(host.RunTurnAsync(session, new HostedAgentIntent(AgentIntentSource.User, "second")));
        await Task.Delay(50);
        Assert.Single(runtime.SentRequests);

        runtime.ReleaseSend();
        await first;
        await second;
        Assert.Equal(2, runtime.SentRequests.Count);
    }

    [Fact]
    public async Task Persisted_running_session_without_an_active_turn_uses_runtime_status()
    {
        var runtime = new FakeAgentRuntime { StatusOverride = AgentSessionStatus.Ready };
        var now = DateTimeOffset.UtcNow.AddHours(-1);
        var session = new AgentSession(
            AgentSessionId.New(), runtime.Account.Id, runtime.Provider.Id, "model-a", "C:/Project",
            "thread-stale", AgentSessionStatus.Running, now, now);
        var host = new InProcessAgentHost(Registry(runtime));
        host.RegisterSession(session);

        var observed = await host.GetStatusAsync(session);

        Assert.Equal(AgentSessionStatus.Ready, observed);
        Assert.Equal(1, runtime.GetStatusCallCount);
    }

    [Fact]
    public async Task Surface_preserves_user_and_leader_intent_sources_when_attached()
    {
        var runtime = new FakeAgentRuntime();
        var session = await runtime.CreateSessionAsync(new CreateAgentSessionRequest(runtime.Account.Id, "model-a", "C:/Project"));
        runtime.QueueTurn(new AgentTurnCompleted(new AgentResult(session.Id, AgentSessionStatus.Completed, "ok", null), DateTimeOffset.UtcNow));
        var surface = new HostedAgentSurfaceViewModel(new InProcessAgentHost(Registry(runtime)), session);

        await surface.SendAsync("用户补充", AgentIntentSource.User);
        await surface.SendAsync("Leader 任务", AgentIntentSource.Leader);

        Assert.Contains(surface.Entries, item => item.Kind == HostedSurfaceEntryKind.UserMessage && item.Source == AgentIntentSource.User);
        Assert.Contains(surface.Entries, item => item.Kind == HostedSurfaceEntryKind.LeaderMessage && item.Source == AgentIntentSource.Leader);
    }

    [Fact]
    public async Task Surface_merges_streaming_deltas_and_exposes_working_status()
    {
        var runtime = new FakeAgentRuntime();
        var session = await runtime.CreateSessionAsync(new CreateAgentSessionRequest(runtime.Account.Id, "model-a", "C:/Project"));
        runtime.QueueTurn(
            new AgentStatusChanged(AgentSessionStatus.Running, DateTimeOffset.UtcNow),
            new AgentTextDelta("正在", DateTimeOffset.UtcNow),
            new AgentTextDelta("检查", DateTimeOffset.UtcNow),
            new AgentTurnCompleted(new AgentResult(session.Id, AgentSessionStatus.Completed, "正在检查", null), DateTimeOffset.UtcNow));
        var surface = new HostedAgentSurfaceViewModel(new InProcessAgentHost(Registry(runtime)), session);

        var turn = surface.SendAsync("开始");
        await turn;

        Assert.Contains(surface.Entries, item => item.Kind == HostedSurfaceEntryKind.AssistantDelta && item.Text == "正在检查" && item.IsComplete);
        Assert.DoesNotContain(surface.Entries, item => item.Text == "正在");
        Assert.Equal("Completed", surface.StatusText);
    }

    [Fact]
    public async Task Attached_surface_projects_intents_and_events_from_other_surface_once()
    {
        var runtime = new FakeAgentRuntime();
        var session = await runtime.CreateSessionAsync(new CreateAgentSessionRequest(runtime.Account.Id, "model-a", "C:/Project"));
        runtime.QueueTurn(
            new AgentTextDelta("来自 Worker", DateTimeOffset.UtcNow),
            new AgentTurnCompleted(new AgentResult(session.Id, AgentSessionStatus.Completed, "来自 Worker", null), DateTimeOffset.UtcNow));
        var host = new InProcessAgentHost(Registry(runtime));
        var surface = new HostedAgentSurfaceViewModel(host, session);

        await foreach (var _ in host.RunTurnAsync(
                           session,
                           new HostedAgentIntent(AgentIntentSource.Leader, "Leader 后续指令")))
        {
        }

        Assert.Single(surface.Entries, item => item.Kind == HostedSurfaceEntryKind.LeaderMessage);
        Assert.Single(surface.Entries, item => item.Kind == HostedSurfaceEntryKind.AssistantDelta && item.Text == "来自 Worker");
    }

    private static AgentRuntimeRegistry Registry(FakeAgentRuntime runtime)
    {
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        return registry;
    }

    private static async Task ConsumeAsync(IAsyncEnumerable<AgentEvent> events)
    {
        await foreach (var _ in events)
        {
        }
    }
}
