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
    public async Task Codex_create_session_sends_read_only_thread_start()
    {
        var (runtime, transport) = await CreateInitializedRuntimeAsync();
        await using var disposableRuntime = runtime;
        var accountId = runtime.Account.Id;

        var sessionTask = runtime.CreateSessionAsync(new CreateAgentSessionRequest(accountId, "server-model"));
        var request = JsonDocument.Parse(await transport.ReadClientLineAsync()).RootElement;

        Assert.Equal("thread/start", request.GetProperty("method").GetString());
        var parameters = request.GetProperty("params");
        Assert.Equal("read-only", parameters.GetProperty("sandbox").GetString());
        Assert.Equal("never", parameters.GetProperty("approvalPolicy").GetString());
        Assert.Equal("C:/Projects/Workbench", parameters.GetProperty("cwd").GetString());
        await RespondAsync(transport, request, new { thread = new { id = "thread-42" }, model = "server-model" });

        Assert.Equal("thread-42", (await sessionTask).ExternalSessionId);
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

    private static AgentSession CreateSession(ProviderAccountId accountId, string externalId)
    {
        var now = DateTimeOffset.UtcNow;
        return new AgentSession(AgentSessionId.New(), accountId, new ProviderId("codex"), "server-model", externalId, AgentSessionStatus.Ready, now, now);
    }

    private static ValueTask RespondAsync(FakeCodexJsonLineTransport transport, JsonElement request, object result) =>
        transport.SendServerLineAsync(JsonSerializer.Serialize(new { id = request.GetProperty("id").GetInt64(), result }));
}
