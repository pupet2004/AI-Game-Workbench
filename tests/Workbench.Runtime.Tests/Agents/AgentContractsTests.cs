using Workbench.Runtime.Agents;
using Workbench.Runtime.Providers;

namespace Workbench.Runtime.Tests.Agents;

public sealed class AgentContractsTests
{
    [Fact]
    public void Agent_session_has_workbench_owned_id()
    {
        var id = AgentSessionId.New();

        Assert.NotEqual(Guid.Empty, id.Value);
    }

    [Fact]
    public void Agent_session_can_hold_external_session_id()
    {
        var session = CreateSession("opaque-external-session");

        Assert.Equal("opaque-external-session", session.ExternalSessionId);
        Assert.IsType<Guid>(session.Id.Value);
    }

    [Fact]
    public void Agent_session_status_is_provider_neutral()
    {
        Assert.Equal(
            [
                "Created",
                "Ready",
                "Running",
                "WaitingApproval",
                "Completed",
                "Interrupted",
                "Failed",
                "Stopped",
                "Archived"
            ],
            Enum.GetNames<AgentSessionStatus>());
    }

    [Fact]
    public void Agent_request_preserves_text()
    {
        var request = new AgentRequest("Implement the selected change.");

        Assert.Equal("Implement the selected change.", request.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Agent_request_rejects_empty_text(string text)
    {
        Assert.Throws<ArgumentException>(() => new AgentRequest(text));
    }

    [Fact]
    public void Runtime_capabilities_can_express_resume_support()
    {
        var capabilities = AgentCapability.PersistentSession | AgentCapability.Resume;

        Assert.True(capabilities.HasFlag(AgentCapability.Resume));
    }

    [Fact]
    public void Runtime_capabilities_can_express_missing_resume_support()
    {
        var capabilities = AgentCapability.StructuredEvents | AgentCapability.Stop;

        Assert.False(capabilities.HasFlag(AgentCapability.Resume));
    }

    [Fact]
    public void Text_delta_event_preserves_text()
    {
        var occurredAt = DateTimeOffset.UtcNow;
        AgentEvent agentEvent = new AgentTextDelta("partial response", occurredAt);

        var delta = Assert.IsType<AgentTextDelta>(agentEvent);
        Assert.Equal("partial response", delta.Text);
        Assert.Equal(occurredAt, delta.OccurredAt);
    }

    [Fact]
    public void Approval_event_is_provider_neutral()
    {
        var approval = new AgentApprovalRequested(
            "approval-42",
            "Allow the requested operation?",
            DateTimeOffset.UtcNow);

        Assert.Equal("approval-42", approval.RequestId);
        Assert.Equal("Allow the requested operation?", approval.Description);
    }

    [Fact]
    public void Status_event_preserves_generic_status()
    {
        var status = new AgentStatusChanged(AgentSessionStatus.WaitingApproval, DateTimeOffset.UtcNow);

        Assert.Equal(AgentSessionStatus.WaitingApproval, status.Status);
    }

    [Fact]
    public void Agent_result_preserves_turn_outcome()
    {
        var sessionId = AgentSessionId.New();
        var result = new AgentResult(sessionId, AgentSessionStatus.Completed, "Complete", null);

        Assert.Equal(sessionId, result.SessionId);
        Assert.Equal(AgentSessionStatus.Completed, result.FinalStatus);
        Assert.Equal("Complete", result.FinalText);
        Assert.Null(result.Error);
    }

    private static AgentSession CreateSession(string? externalSessionId)
    {
        var now = DateTimeOffset.UtcNow;

        return new AgentSession(
            AgentSessionId.New(),
            ProviderAccountId.New(),
            new ProviderId("provider-a"),
            "model-a",
            externalSessionId,
            AgentSessionStatus.Ready,
            now,
            now);
    }
}
