using System.Text.Json;
using Workbench.Runtime.Providers.Codex;

namespace Workbench.Runtime.Tests.Providers.Codex;

public sealed class CodexProtocolClientTests
{
    [Fact]
    public async Task Codex_protocol_correlates_response_by_id()
    {
        var transport = new FakeCodexJsonLineTransport();
        await using var client = new CodexProtocolClient(transport);

        var responseTask = client.SendRequestAsync("model/list", new { limit = 10 });
        var request = JsonDocument.Parse(await transport.ReadClientLineAsync()).RootElement;
        await transport.SendServerLineAsync(JsonSerializer.Serialize(new
        {
            id = request.GetProperty("id").GetInt64(),
            result = new { value = "matched" }
        }));

        var response = await responseTask;

        Assert.Equal("matched", response.GetProperty("value").GetString());
    }

    [Fact]
    public async Task Codex_protocol_dispatches_notification()
    {
        var transport = new FakeCodexJsonLineTransport();
        await using var client = new CodexProtocolClient(transport);
        var received = new TaskCompletionSource<CodexProtocolMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.NotificationReceived += message => received.TrySetResult(message);

        await transport.SendServerLineAsync("""{"method":"item/agentMessage/delta","params":{"delta":"hello"}}""");

        var notification = await received.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("item/agentMessage/delta", notification.Method);
        Assert.Equal("hello", notification.Params.GetProperty("delta").GetString());
    }

    [Fact]
    public async Task Codex_protocol_detects_server_request()
    {
        var transport = new FakeCodexJsonLineTransport();
        await using var client = new CodexProtocolClient(transport);
        var received = new TaskCompletionSource<CodexServerRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ServerRequestReceived += request => received.TrySetResult(request);

        await transport.SendServerLineAsync("""{"id":42,"method":"item/commandExecution/requestApproval","params":{"itemId":"item-1"}}""");

        var request = await received.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(42, request.Id.GetInt64());
        Assert.Equal("item/commandExecution/requestApproval", request.Method);
    }

    [Fact]
    public async Task Codex_protocol_response_preserves_provider_request_id_and_result()
    {
        var transport = new FakeCodexJsonLineTransport();
        await using var client = new CodexProtocolClient(transport);
        using var request = JsonDocument.Parse("""{"id":"provider-approval-42"}""");

        await client.SendResponseAsync(
            request.RootElement.GetProperty("id"),
            new { decision = "acceptForSession" });
        var response = JsonDocument.Parse(await transport.ReadClientLineAsync()).RootElement;

        Assert.Equal("provider-approval-42", response.GetProperty("id").GetString());
        Assert.Equal("acceptForSession", response.GetProperty("result").GetProperty("decision").GetString());
        Assert.False(response.TryGetProperty("method", out _));
    }

    [Fact]
    public async Task Codex_process_exit_fails_pending_requests()
    {
        var transport = new FakeCodexJsonLineTransport();
        await using var client = new CodexProtocolClient(transport);
        var pending = client.SendRequestAsync("model/list", new { });
        await transport.ReadClientLineAsync();

        transport.CompleteServerOutput();

        await Assert.ThrowsAsync<CodexProtocolException>(() => pending);
    }

    [Fact]
    public async Task Codex_malformed_json_does_not_silently_succeed()
    {
        var transport = new FakeCodexJsonLineTransport();
        await using var client = new CodexProtocolClient(transport);
        var pending = client.SendRequestAsync("model/list", new { });
        await transport.ReadClientLineAsync();

        await transport.SendServerLineAsync("not-json");

        var error = await Assert.ThrowsAsync<CodexProtocolException>(() => pending);
        Assert.Contains("malformed", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
