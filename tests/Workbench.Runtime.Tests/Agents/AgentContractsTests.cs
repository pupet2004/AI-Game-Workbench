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
        Assert.Equal(AgentAccessMode.Restricted, request.AccessMode);
    }

    [Fact]
    public void Agent_request_can_select_full_runtime_access()
    {
        var request = new AgentRequest("Inspect and modify the project.", accessMode: AgentAccessMode.Full);

        Assert.Equal(AgentAccessMode.Full, request.AccessMode);
    }

    [Fact]
    public void Working_directory_is_preserved_in_session_request()
    {
        var request = new CreateAgentSessionRequest(
            ProviderAccountId.New(),
            "model-a",
            "C:/Projects/Game");

        Assert.Equal("C:/Projects/Game", request.WorkingDirectory);
        Assert.Equal(AgentAccessMode.Restricted, request.AccessMode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Session_request_rejects_blank_working_directory(string workingDirectory)
    {
        Assert.Throws<ArgumentException>(() => new CreateAgentSessionRequest(
            ProviderAccountId.New(),
            "model-a",
            workingDirectory));
    }

    [Fact]
    public void Session_without_working_directory_is_valid()
    {
        var request = new CreateAgentSessionRequest(ProviderAccountId.New(), "model-a");

        Assert.Null(request.WorkingDirectory);
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
        var requestId = AgentApprovalRequestId.New();
        var sessionId = AgentSessionId.New();
        var approval = new AgentApprovalRequested(
            requestId,
            sessionId,
            "Allow the requested operation?",
            [new AgentApprovalOption("approve-once", "Approve once", "Allow this operation once.")],
            DateTimeOffset.UtcNow);

        Assert.Equal(requestId, approval.RequestId);
        Assert.Equal(sessionId, approval.SessionId);
        Assert.Equal("Allow the requested operation?", approval.Summary);
        Assert.Equal("approve-once", Assert.Single(approval.Options).Id);
    }

    [Fact]
    public void Approval_decision_is_correlated_by_workbench_request_id()
    {
        var requestId = AgentApprovalRequestId.New();

        var decision = new AgentApprovalDecision(requestId, "decline");

        Assert.Equal(requestId, decision.RequestId);
        Assert.Equal("decline", decision.OptionId);
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
            null,
            externalSessionId,
            AgentSessionStatus.Ready,
            now,
            now);
    }
}
