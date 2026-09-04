using Workbench.Runtime.Agents;
using Workbench.Runtime.Providers;
using Workbench.Runtime.Providers.Codex;
using Xunit.Abstractions;

namespace Workbench.Runtime.Tests.Providers.Codex;

public sealed class CodexAppServerIntegrationTests(ITestOutputHelper output)
{
    [Fact(Skip = "Live provider integration is excluded from deterministic suite; run via dedicated live acceptance command.")]
    public async Task Real_codex_app_server_completes_provider_neutral_lifecycle()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("WORKBENCH_RUN_CODEX_INTEGRATION"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var executable = RequireEnvironmentVariable("WORKBENCH_CODEX_EXECUTABLE");
        var entryPoint = RequireEnvironmentVariable("WORKBENCH_CODEX_ENTRY");
        var repositoryRoot = RequireEnvironmentVariable("WORKBENCH_CODEX_CWD");
        var options = new CodexAppServerOptions(
            executable,
            [entryPoint, "app-server", "--stdio"],
            repositoryRoot,
            TimeSpan.FromMinutes(2));

        await using var runtime = await CodexAgentRuntime.ConnectAsync(
            options,
            ProviderAccountId.New());
        var models = await runtime.GetModelsAsync();
        Assert.NotEmpty(models);
        var model = models[0];
        output.WriteLine($"Model count: {models.Count}");
        output.WriteLine($"Selected model id: {model.ModelId}");

        var session = await runtime.CreateSessionAsync(
            new CreateAgentSessionRequest(runtime.Account.Id, model.ModelId));
        Assert.False(string.IsNullOrWhiteSpace(session.ExternalSessionId));
        Assert.NotEqual(session.Id.ToString(), session.ExternalSessionId);
        output.WriteLine($"Thread id: {session.ExternalSessionId}");

        var firstResult = await RunTurnAsync(
            runtime,
            session,
            "Read no files and run no commands. Reply with exactly: WORKBENCH_CODEX_OK");
        Assert.Contains("WORKBENCH_CODEX_OK", firstResult.FinalText);
        output.WriteLine($"Turn 1: {firstResult.FinalText}");

        var resumed = await runtime.ResumeSessionAsync(session);
        Assert.Equal(session.Id, resumed.Id);
        Assert.Equal(session.ExternalSessionId, resumed.ExternalSessionId);
        var secondResult = await RunTurnAsync(
            runtime,
            resumed,
            "Read no files and run no commands. Reply with exactly: WORKBENCH_RESUME_OK");
        Assert.Contains("WORKBENCH_RESUME_OK", secondResult.FinalText);
        output.WriteLine($"Turn 2: {secondResult.FinalText}");

        var transcript = await runtime.GetTranscriptAsync(resumed);
        Assert.Contains(transcript.OfType<AgentMessage>(), message =>
            message.Role == AgentMessageRole.User && message.Text.Contains("WORKBENCH_CODEX_OK", StringComparison.Ordinal));
        Assert.Contains(transcript.OfType<AgentMessage>(), message =>
            message.Role == AgentMessageRole.Assistant && message.Text.Contains("WORKBENCH_CODEX_OK", StringComparison.Ordinal));
        Assert.Contains(transcript.OfType<AgentMessage>(), message =>
            message.Role == AgentMessageRole.User && message.Text.Contains("WORKBENCH_RESUME_OK", StringComparison.Ordinal));
        Assert.Contains(transcript.OfType<AgentMessage>(), message =>
            message.Role == AgentMessageRole.Assistant && message.Text.Contains("WORKBENCH_RESUME_OK", StringComparison.Ordinal));
        output.WriteLine($"Transcript messages: {transcript.OfType<AgentMessage>().Count()}");

        var secondSession = await runtime.CreateSessionAsync(
            new CreateAgentSessionRequest(runtime.Account.Id, model.ModelId));
        Assert.NotEqual(session.ExternalSessionId, secondSession.ExternalSessionId);
        Assert.Equal(AgentSessionStatus.Ready, await runtime.GetStatusAsync(secondSession));
        output.WriteLine($"Second thread id: {secondSession.ExternalSessionId}");
    }

    private static async Task<AgentResult> RunTurnAsync(
        CodexAgentRuntime runtime,
        AgentSession session,
        string prompt)
    {
        AgentResult? result = null;
        var sawTextDelta = false;
        var sawRunningStatus = false;
        await foreach (var agentEvent in runtime.SendAsync(session, new AgentRequest(prompt)))
        {
            sawTextDelta |= agentEvent is AgentTextDelta;
            sawRunningStatus |= agentEvent is AgentStatusChanged { Status: AgentSessionStatus.Running };
            if (agentEvent is AgentTurnCompleted completed)
            {
                result = completed.Result;
            }
        }

        Assert.True(sawTextDelta);
        Assert.True(sawRunningStatus);
        return Assert.IsType<AgentResult>(result);
    }

    private static string RequireEnvironmentVariable(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Integration environment variable '{name}' is required.");
}
