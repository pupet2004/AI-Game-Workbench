using System.Text.Json;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Providers;
using Workbench.Runtime.Providers.Codex;

namespace Workbench.Runtime.Tests.Providers.Codex;

public sealed class CodexRuntimeMappingTests
{
    [Fact]
    public void Codex_text_delta_maps_to_agent_event()
    {
        var payload = JsonDocument.Parse("""{"delta":"hello","itemId":"item-1","threadId":"thread-1","turnId":"turn-1"}""").RootElement;

        var agentEvent = CodexRuntimeMapper.MapNotification("item/agentMessage/delta", payload, AgentSessionId.New());

        var delta = Assert.IsType<AgentTextDelta>(agentEvent);
        Assert.Equal("hello", delta.Text);
    }

    [Fact]
    public void Codex_turn_completion_maps_to_generic_status_and_result()
    {
        var sessionId = AgentSessionId.New();
        var payload = JsonDocument.Parse("""{"threadId":"thread-1","turn":{"id":"turn-1","items":[],"status":"completed"}}""").RootElement;

        var agentEvent = CodexRuntimeMapper.MapNotification("turn/completed", payload, sessionId, "final text");

        var completed = Assert.IsType<AgentTurnCompleted>(agentEvent);
        Assert.Equal(AgentSessionStatus.Completed, completed.Result.FinalStatus);
        Assert.Equal("final text", completed.Result.FinalText);
        Assert.Equal(sessionId, completed.Result.SessionId);
    }

    [Fact]
    public void Codex_external_thread_id_maps_to_external_session_id()
    {
        var accountId = ProviderAccountId.New();
        var result = JsonDocument.Parse("""{"thread":{"id":"thread-real-42"},"model":"model-a"}""").RootElement;

        var session = CodexRuntimeMapper.MapSession(result, accountId, "model-a");

        Assert.Equal("thread-real-42", session.ExternalSessionId);
        Assert.NotEqual(Guid.Empty, session.Id.Value);
        Assert.NotEqual(session.Id.ToString(), session.ExternalSessionId);
    }

    [Fact]
    public void Codex_model_result_maps_to_model_profile()
    {
        var result = JsonDocument.Parse("""{"data":[{"id":"opaque-model-id","model":"opaque-model-id","displayName":"Server Model","description":"","hidden":false,"isDefault":true,"defaultReasoningEffort":"high","supportedReasoningEfforts":[]}]}""").RootElement;

        var models = CodexRuntimeMapper.MapModels(result);

        var model = Assert.Single(models);
        Assert.Equal(new ProviderId("codex"), model.ProviderId);
        Assert.Equal("opaque-model-id", model.ModelId);
        Assert.Equal("Server Model", model.DisplayName);
        Assert.True(model.Capabilities.HasFlag(AgentCapability.PersistentSession));
        Assert.True(model.Capabilities.HasFlag(AgentCapability.Resume));
    }

    [Fact]
    public void Codex_spike_does_not_claim_approval_support_without_a_decision_contract()
    {
        var result = JsonDocument.Parse("""{"data":[{"id":"model","displayName":"Model"}]}""").RootElement;

        var model = Assert.Single(CodexRuntimeMapper.MapModels(result));

        Assert.False(model.Capabilities.HasFlag(AgentCapability.Approval));
    }

    [Fact]
    public void Generic_turn_completion_event_carries_agent_result()
    {
        var result = new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed, "done", null);

        AgentEvent agentEvent = new AgentTurnCompleted(result, DateTimeOffset.UtcNow);

        Assert.Same(result, Assert.IsType<AgentTurnCompleted>(agentEvent).Result);
    }

    [Fact]
    public void Codex_transcript_maps_user_and_assistant_messages()
    {
        var result = JsonDocument.Parse("""
            {
              "thread": {
                "turns": [{
                  "id": "turn-1",
                  "status": "completed",
                  "items": [
                    {"id":"user-1","type":"userMessage","content":[{"type":"text","text":"question"}]},
                    {"id":"agent-1","type":"agentMessage","text":"answer"}
                  ]
                }]
              }
            }
            """).RootElement;

        var transcript = CodexRuntimeMapper.MapTranscript(result);

        Assert.Collection(
            transcript,
            item =>
            {
                var message = Assert.IsType<AgentMessage>(item);
                Assert.Equal(AgentMessageRole.User, message.Role);
                Assert.Equal("question", message.Text);
            },
            item =>
            {
                var message = Assert.IsType<AgentMessage>(item);
                Assert.Equal(AgentMessageRole.Assistant, message.Role);
                Assert.Equal("answer", message.Text);
            });
    }
}
