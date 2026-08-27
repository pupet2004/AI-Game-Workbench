using System.Text.Json;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Providers;
using Workbench.Runtime.Providers.Codex;

namespace Workbench.Runtime.Tests.Providers.Codex;

public sealed class CodexAgentRuntimeTests
{
    [Fact]
    public async Task Codex_runtime_initializes_before_model_discovery()
    {
        var transport = new FakeCodexJsonLineTransport();
        await using var client = new CodexProtocolClient(transport);
        var options = CreateOptions();
        var accountId = ProviderAccountId.New();
        var runtimeTask = CodexAgentRuntime.CreateForProtocolAsync(client, options, accountId);

        var initialize = JsonDocument.Parse(await transport.ReadClientLineAsync()).RootElement;
        Assert.Equal("initialize", initialize.GetProperty("method").GetString());
        Assert.Equal("ai_game_workbench", initialize.GetProperty("params").GetProperty("clientInfo").GetProperty("name").GetString());
        await RespondAsync(transport, initialize, new { userAgent = "codex", platformFamily = "windows", platformOs = "windows", codexHome = "C:/Codex" });
        var initialized = JsonDocument.Parse(await transport.ReadClientLineAsync()).RootElement;
        Assert.Equal("initialized", initialized.GetProperty("method").GetString());
        var runtime = await runtimeTask;

        var modelsTask = runtime.GetModelsAsync();
        var modelList = JsonDocument.Parse(await transport.ReadClientLineAsync()).RootElement;
        Assert.Equal("model/list", modelList.GetProperty("method").GetString());
        await RespondAsync(transport, modelList, new
        {
            data = new[] { new { id = "server-model", displayName = "Server Model" } },
            nextCursor = (string?)null
        });

        Assert.Equal("server-model", Assert.Single(await modelsTask).ModelId);
        await runtime.DisposeAsync();
    }

    [Fact]
    public async Task Working_directory_maps_to_codex_thread_start_and_session()
    {
        var (runtime, transport) = await CreateInitializedRuntimeAsync();
        await using var disposableRuntime = runtime;
        var accountId = runtime.Account.Id;

        var sessionTask = runtime.CreateSessionAsync(new CreateAgentSessionRequest(
            accountId,
            "server-model",
            "C:/Projects/Game"));
        var request = JsonDocument.Parse(await transport.ReadClientLineAsync()).RootElement;

        Assert.Equal("thread/start", request.GetProperty("method").GetString());
        var parameters = request.GetProperty("params");
        Assert.Equal("read-only", parameters.GetProperty("sandbox").GetString());
        Assert.Equal("on-request", parameters.GetProperty("approvalPolicy").GetString());
        Assert.Equal("C:/Projects/Game", parameters.GetProperty("cwd").GetString());
        await RespondAsync(transport, request, new { thread = new { id = "thread-42" }, model = "server-model" });

        var session = await sessionTask;
        Assert.Equal("thread-42", session.ExternalSessionId);
        Assert.Equal("C:/Projects/Game", session.WorkingDirectory);
    }

    [Fact]
    public async Task Null_working_directory_is_sent_as_protocol_null_and_preserved()
    {
        var (runtime, transport) = await CreateInitializedRuntimeAsync();
        await using var disposableRuntime = runtime;

        var sessionTask = runtime.CreateSessionAsync(
            new CreateAgentSessionRequest(runtime.Account.Id, "server-model"));
        var request = JsonDocument.Parse(await transport.ReadClientLineAsync()).RootElement;

        Assert.Equal(JsonValueKind.Null, request.GetProperty("params").GetProperty("cwd").ValueKind);
        await RespondAsync(transport, request, new { thread = new { id = "thread-42" }, model = "server-model" });

        Assert.Null((await sessionTask).WorkingDirectory);
    }

    [Fact]
    public async Task Resumed_session_preserves_working_directory_context()
    {
        var (runtime, transport) = await CreateInitializedRuntimeAsync();
        await using var disposableRuntime = runtime;
        var session = CreateSession(runtime.Account.Id, "thread-42", "C:/Projects/Game");

        var resumeTask = runtime.ResumeSessionAsync(session);
        var request = JsonDocument.Parse(await transport.ReadClientLineAsync()).RootElement;

        Assert.Equal("thread/resume", request.GetProperty("method").GetString());
        Assert.Equal("C:/Projects/Game", request.GetProperty("params").GetProperty("cwd").GetString());
        await RespondAsync(transport, request, new { thread = new { id = "thread-42" }, model = "server-model" });

        var resumed = await resumeTask;
        Assert.Equal(session.Id, resumed.Id);
        Assert.Equal("C:/Projects/Game", resumed.WorkingDirectory);
    }

    [Fact]
    public async Task Codex_server_request_emits_provider_neutral_approval_event()
    {
        var (runtime, transport) = await CreateInitializedRuntimeAsync();
        await using var disposableRuntime = runtime;
        var session = CreateSession(runtime.Account.Id, "thread-42", "C:/Projects/Game");
        await using var enumerator = runtime.SendAsync(session, new AgentRequest("safe prompt")).GetAsyncEnumerator();
        var moveNext = enumerator.MoveNextAsync().AsTask();
        var turnStart = JsonDocument.Parse(await transport.ReadClientLineAsync()).RootElement;
        await RespondAsync(transport, turnStart, new { turn = new { id = "turn-7", items = Array.Empty<object>(), status = "inProgress" } });

        await transport.SendServerLineAsync("""{"id":900,"method":"item/commandExecution/requestApproval","params":{"threadId":"thread-42","turnId":"turn-7","itemId":"item-1","startedAtMs":0,"reason":"Run the requested command?","command":"dotnet test"}}""");

        Assert.True(await moveNext);
        var approval = Assert.IsType<AgentApprovalRequested>(enumerator.Current);
        Assert.Equal(session.Id, approval.SessionId);
        Assert.Equal("Run the requested command?", approval.Summary);
        Assert.Equal(4, approval.Options.Count);
        Assert.NotEqual(Guid.Empty, approval.RequestId.Value);
    }

    [Fact]
    public async Task Approval_decision_writes_correct_codex_response_and_cleans_pending_request()
    {
        var (runtime, transport) = await CreateInitializedRuntimeAsync();
        await using var disposableRuntime = runtime;
        var session = CreateSession(runtime.Account.Id, "thread-42", "C:/Projects/Game");
        await using var enumerator = runtime.SendAsync(session, new AgentRequest("safe prompt")).GetAsyncEnumerator();
        var moveNext = enumerator.MoveNextAsync().AsTask();
        var turnStart = JsonDocument.Parse(await transport.ReadClientLineAsync()).RootElement;
        await RespondAsync(transport, turnStart, new { turn = new { id = "turn-7", items = Array.Empty<object>(), status = "inProgress" } });
        await transport.SendServerLineAsync("""{"id":"approval-provider-7","method":"item/fileChange/requestApproval","params":{"threadId":"thread-42","turnId":"turn-7","itemId":"item-1","startedAtMs":0,"reason":"Apply the proposed file changes?"}}""");
        Assert.True(await moveNext);
        var approval = Assert.IsType<AgentApprovalRequested>(enumerator.Current);
        var approveOnce = Assert.Single(approval.Options, option => option.Id == "approve-once");

        await runtime.RespondToApprovalAsync(
            session,
            new AgentApprovalDecision(approval.RequestId, approveOnce.Id));
        var response = JsonDocument.Parse(await transport.ReadClientLineAsync()).RootElement;

        Assert.Equal("approval-provider-7", response.GetProperty("id").GetString());
        Assert.Equal("accept", response.GetProperty("result").GetProperty("decision").GetString());
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.RespondToApprovalAsync(
            session,
            new AgentApprovalDecision(approval.RequestId, approveOnce.Id)));
    }

    [Fact]
    public async Task Unknown_approval_request_and_option_are_rejected()
    {
        var (runtime, transport) = await CreateInitializedRuntimeAsync();
        await using var disposableRuntime = runtime;
        var session = CreateSession(runtime.Account.Id, "thread-42", "C:/Projects/Game");

        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.RespondToApprovalAsync(
            session,
            new AgentApprovalDecision(AgentApprovalRequestId.New(), "approve-once")));

        await using var enumerator = runtime.SendAsync(session, new AgentRequest("safe prompt")).GetAsyncEnumerator();
        var moveNext = enumerator.MoveNextAsync().AsTask();
        var turnStart = JsonDocument.Parse(await transport.ReadClientLineAsync()).RootElement;
        await RespondAsync(transport, turnStart, new { turn = new { id = "turn-7", items = Array.Empty<object>(), status = "inProgress" } });
        await transport.SendServerLineAsync("""{"id":901,"method":"item/commandExecution/requestApproval","params":{"threadId":"thread-42","turnId":"turn-7","itemId":"item-1","startedAtMs":0}}""");
        Assert.True(await moveNext);
        var approval = Assert.IsType<AgentApprovalRequested>(enumerator.Current);

        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.RespondToApprovalAsync(
            session,
            new AgentApprovalDecision(approval.RequestId, "not-a-legal-option")));
    }

    [Fact]
    public async Task User_input_server_request_is_not_treated_as_approval()
    {
        var (runtime, transport) = await CreateInitializedRuntimeAsync();
        await using var disposableRuntime = runtime;
        var session = CreateSession(runtime.Account.Id, "thread-42", "C:/Projects/Game");
        await using var enumerator = runtime.SendAsync(session, new AgentRequest("safe prompt")).GetAsyncEnumerator();
        var moveNext = enumerator.MoveNextAsync().AsTask();
        var turnStart = JsonDocument.Parse(await transport.ReadClientLineAsync()).RootElement;
        await RespondAsync(transport, turnStart, new { turn = new { id = "turn-7", items = Array.Empty<object>(), status = "inProgress" } });

        await transport.SendServerLineAsync("""{"id":902,"method":"item/tool/requestUserInput","params":{"threadId":"thread-42","turnId":"turn-7","itemId":"item-1","questions":[]}}""");
        await transport.SendServerLineAsync("""{"method":"turn/completed","params":{"threadId":"thread-42","turn":{"id":"turn-7","items":[],"status":"completed"}}}""");

        Assert.True(await moveNext);
        Assert.IsType<AgentTurnCompleted>(enumerator.Current);
    }

    [Fact]
    public async Task Turn_completion_cleans_pending_approval()
    {
        var (runtime, transport) = await CreateInitializedRuntimeAsync();
        await using var disposableRuntime = runtime;
        var session = CreateSession(runtime.Account.Id, "thread-42", "C:/Projects/Game");
        await using var enumerator = runtime.SendAsync(session, new AgentRequest("safe prompt")).GetAsyncEnumerator();
        var approvalMoveNext = enumerator.MoveNextAsync().AsTask();
        var turnStart = JsonDocument.Parse(await transport.ReadClientLineAsync()).RootElement;
        await RespondAsync(transport, turnStart, new { turn = new { id = "turn-7", items = Array.Empty<object>(), status = "inProgress" } });
        await transport.SendServerLineAsync("""{"id":903,"method":"item/fileChange/requestApproval","params":{"threadId":"thread-42","turnId":"turn-7","itemId":"item-1","startedAtMs":0}}""");
        Assert.True(await approvalMoveNext);
        var approval = Assert.IsType<AgentApprovalRequested>(enumerator.Current);

        var completionMoveNext = enumerator.MoveNextAsync().AsTask();
        await transport.SendServerLineAsync("""{"method":"turn/completed","params":{"threadId":"thread-42","turn":{"id":"turn-7","items":[],"status":"completed"}}}""");
        Assert.True(await completionMoveNext);
        Assert.IsType<AgentTurnCompleted>(enumerator.Current);

        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.RespondToApprovalAsync(
            session,
            new AgentApprovalDecision(approval.RequestId, "approve-once")));
    }

    [Fact]
    public async Task Process_exit_cleans_pending_approval()
    {
        var (runtime, transport) = await CreateInitializedRuntimeAsync();
        await using var disposableRuntime = runtime;
        var session = CreateSession(runtime.Account.Id, "thread-42", "C:/Projects/Game");
        await using var enumerator = runtime.SendAsync(session, new AgentRequest("safe prompt")).GetAsyncEnumerator();
        var moveNext = enumerator.MoveNextAsync().AsTask();
        var turnStart = JsonDocument.Parse(await transport.ReadClientLineAsync()).RootElement;
        await RespondAsync(transport, turnStart, new { turn = new { id = "turn-7", items = Array.Empty<object>(), status = "inProgress" } });
        await transport.SendServerLineAsync("""{"id":904,"method":"item/commandExecution/requestApproval","params":{"threadId":"thread-42","turnId":"turn-7","itemId":"item-1","startedAtMs":0}}""");
        Assert.True(await moveNext);
        var approval = Assert.IsType<AgentApprovalRequested>(enumerator.Current);

        transport.CompleteServerOutput();
        await runtime.ProtocolCompletion.WaitAsync(TimeSpan.FromSeconds(2));

        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.RespondToApprovalAsync(
            session,
            new AgentApprovalDecision(approval.RequestId, "approve-once")));
    }

    [Fact]
    public async Task Codex_stop_sends_turn_interrupt_for_active_turn()
    {
        var (runtime, transport) = await CreateInitializedRuntimeAsync();
        await using var disposableRuntime = runtime;
        var session = CreateSession(runtime.Account.Id, "thread-42");
        await using var enumerator = runtime.SendAsync(session, new AgentRequest("safe prompt")).GetAsyncEnumerator();
        var moveNext = enumerator.MoveNextAsync().AsTask();
        var turnStart = JsonDocument.Parse(await transport.ReadClientLineAsync()).RootElement;
        await RespondAsync(transport, turnStart, new { turn = new { id = "turn-7", items = Array.Empty<object>(), status = "inProgress" } });

        var stopTask = runtime.StopAsync(session);
        var interrupt = JsonDocument.Parse(await transport.ReadClientLineAsync()).RootElement;
        Assert.Equal("turn/interrupt", interrupt.GetProperty("method").GetString());
        Assert.Equal("turn-7", interrupt.GetProperty("params").GetProperty("turnId").GetString());
        await RespondAsync(transport, interrupt, new { });
        await stopTask;
        await transport.SendServerLineAsync("""{"method":"turn/completed","params":{"threadId":"thread-42","turn":{"id":"turn-7","items":[],"status":"interrupted"}}}""");
        Assert.True(await moveNext);
    }

    [Fact]
    public async Task Codex_steer_sends_expected_turn_id_and_text_input()
    {
        var (runtime, transport) = await CreateInitializedRuntimeAsync();
        await using var disposableRuntime = runtime;
        var session = CreateSession(runtime.Account.Id, "thread-42");
        await using var enumerator = runtime.SendAsync(session, new AgentRequest("safe prompt")).GetAsyncEnumerator();
        var moveNext = enumerator.MoveNextAsync().AsTask();
        var turnStart = JsonDocument.Parse(await transport.ReadClientLineAsync()).RootElement;
        await RespondAsync(transport, turnStart, new { turn = new { id = "turn-7", items = Array.Empty<object>(), status = "inProgress" } });

        var steerTask = runtime.SteerAsync(session, new AgentRequest("focus on the failing test"));
        var steer = JsonDocument.Parse(await transport.ReadClientLineAsync()).RootElement;
        Assert.Equal("turn/steer", steer.GetProperty("method").GetString());
        Assert.Equal("thread-42", steer.GetProperty("params").GetProperty("threadId").GetString());
        Assert.Equal("turn-7", steer.GetProperty("params").GetProperty("expectedTurnId").GetString());
        Assert.Equal("text", steer.GetProperty("params").GetProperty("input")[0].GetProperty("type").GetString());
        Assert.Equal("focus on the failing test", steer.GetProperty("params").GetProperty("input")[0].GetProperty("text").GetString());
        await RespondAsync(transport, steer, new { });
        await steerTask;

        await transport.SendServerLineAsync("""{"method":"turn/completed","params":{"threadId":"thread-42","turn":{"id":"turn-7","items":[],"status":"completed"}}}""");
        Assert.True(await moveNext);
    }

    [Fact]
    public async Task Codex_image_input_maps_to_host_local_image_path()
    {
        var (runtime, transport) = await CreateInitializedRuntimeAsync();
        await using var disposableRuntime = runtime;
        var session = CreateSession(runtime.Account.Id, "thread-42");
        await using var enumerator = runtime.SendAsync(
            session,
            new AgentRequest("Describe this image.", inputs: [new AgentInputPart("image", "C:/Temp/image.png", "image.png")]))
            .GetAsyncEnumerator();
        var moveNext = enumerator.MoveNextAsync().AsTask();
        var turnStart = JsonDocument.Parse(await transport.ReadClientLineAsync()).RootElement;
        Assert.Equal("localImage", turnStart.GetProperty("params").GetProperty("input")[1].GetProperty("type").GetString());
        Assert.Equal("C:/Temp/image.png", turnStart.GetProperty("params").GetProperty("input")[1].GetProperty("path").GetString());
        await RespondAsync(transport, turnStart, new { turn = new { id = "turn-7", items = Array.Empty<object>(), status = "inProgress" } });

        await transport.SendServerLineAsync("""{"method":"turn/completed","params":{"threadId":"thread-42","turn":{"id":"turn-7","items":[],"status":"completed"}}}""");
        Assert.True(await moveNext);
    }

    private static async Task<(CodexAgentRuntime Runtime, FakeCodexJsonLineTransport Transport)> CreateInitializedRuntimeAsync()
    {
        var transport = new FakeCodexJsonLineTransport();
        var client = new CodexProtocolClient(transport);
        var runtimeTask = CodexAgentRuntime.CreateForProtocolAsync(client, CreateOptions(), ProviderAccountId.New());
        var initialize = JsonDocument.Parse(await transport.ReadClientLineAsync()).RootElement;
        await RespondAsync(transport, initialize, new { userAgent = "codex", platformFamily = "windows", platformOs = "windows", codexHome = "C:/Codex" });
        await transport.ReadClientLineAsync();
        return (await runtimeTask, transport);
    }

    private static CodexAppServerOptions CreateOptions() =>
        new("C:/Tools/node.exe", ["C:/Tools/codex.js", "app-server", "--stdio"], "C:/Projects/Workbench");

    private static AgentSession CreateSession(
        ProviderAccountId accountId,
        string externalId,
        string? workingDirectory = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new AgentSession(AgentSessionId.New(), accountId, new ProviderId("codex"), "server-model", workingDirectory, externalId, AgentSessionStatus.Ready, now, now);
    }

    private static ValueTask RespondAsync(FakeCodexJsonLineTransport transport, JsonElement request, object result) =>
        transport.SendServerLineAsync(JsonSerializer.Serialize(new { id = request.GetProperty("id").GetInt64(), result }));
}
