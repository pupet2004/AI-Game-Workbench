using Workbench.App.ProjectWorld;
using Workbench.App.Continuity;
using Workbench.App.Tests.Support;
using Workbench.Core.Continuity;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Providers;

namespace Workbench.App.Tests.Continuity;

public sealed class B1AgentParticipationAdapterTests
{
    [Fact]
    public async Task Agent_execution_records_attempt_binding_and_handoff_without_changing_accepted_state()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var folder = new TemporaryDirectory("b1-agent-adapter");
        var opened = await context.Services.ProjectOpenService.OpenAsync(folder.Path);
        var projectRef = new ProjectRef(opened.Project.Id);
        var principal = context.Services.UserPrincipalProvider.GetCurrent();
        await context.Services.B1ProjectGovernance.CreateGovernedProjectForExistingProjectAsync(projectRef, principal);
        await context.Services.ProjectWorldInitialization.CommitAsync(
            new ProjectWorldInitializationRequest(
                projectRef,
                principal,
                RoleKind.Worker,
                "Own the first bounded implementation",
                "A reviewed implementation result",
                "Implement the first slice"));

        var state = await context.Services.B1AuthorityRepository.LoadProjectStateAsync(projectRef);
        var assignment = Assert.Single(B1Projector.Build(state).AcceptedProjectState.Assignments.Values);
        var runtime = new FakeAgentRuntime();
        runtime.QueueTurn(new AgentTurnCompleted(
            new AgentResult(
                AgentSessionId.New(),
                AgentSessionStatus.Completed,
                "The bounded implementation is ready for review.",
                null),
            context.Time.GetUtcNow()));

        var result = await context.Services.B1AgentParticipation.ExecuteAsync(
            runtime,
            new B1AgentExecutionRequest(
                projectRef,
                principal,
                assignment.AssignmentRef,
                "model-a",
                folder.Path,
                "Implement the first slice and report the result.",
                [new EvidenceRef("test:run/1")]));

        Assert.Equal(assignment.AssignmentRef, result.Attempt.AssignmentRef);
        Assert.Equal(result.Attempt.AttemptRef, result.SessionBinding.AttemptRef);
        Assert.Equal("The bounded implementation is ready for review.", result.FinalText);
        Assert.Contains("Assignment contract:", runtime.SentRequests.Single().Text, StringComparison.Ordinal);

        var after = await context.Services.B1AuthorityRepository.LoadProjectStateAsync(projectRef);
        Assert.Single(after.Attempts);
        Assert.Single(after.SessionBindings);
        Assert.Single(after.Claims);
        Assert.Single(after.Handoffs);
        Assert.Empty(B1Projector.Build(after).AcceptedProjectState.CurrentContributions);
    }

    [Fact]
    public async Task Approval_request_is_not_auto_authorized_and_does_not_create_a_handoff()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var folder = new TemporaryDirectory("b1-agent-approval");
        var opened = await context.Services.ProjectOpenService.OpenAsync(folder.Path);
        var projectRef = new ProjectRef(opened.Project.Id);
        var principal = context.Services.UserPrincipalProvider.GetCurrent();
        await context.Services.B1ProjectGovernance.CreateGovernedProjectForExistingProjectAsync(projectRef, principal);
        await context.Services.ProjectWorldInitialization.CommitAsync(
            new ProjectWorldInitializationRequest(
                projectRef,
                principal,
                RoleKind.Worker,
                "Own bounded work",
                "A reviewed result",
                "Do the work"));
        var state = await context.Services.B1AuthorityRepository.LoadProjectStateAsync(projectRef);
        var assignment = Assert.Single(B1Projector.Build(state).AcceptedProjectState.Assignments.Values);
        var runtime = new FakeAgentRuntime();
        runtime.QueueTurn(new AgentApprovalRequested(
            new AgentApprovalRequestId(Guid.NewGuid()),
            AgentSessionId.New(),
            "Approve a provider action",
            [new AgentApprovalOption("allow", "Allow")],
            context.Time.GetUtcNow()));

        await Assert.ThrowsAsync<B1AgentParticipationException>(() =>
            context.Services.B1AgentParticipation.ExecuteAsync(
                runtime,
                new B1AgentExecutionRequest(
                    projectRef,
                    principal,
                    assignment.AssignmentRef,
                    "model-a",
                    folder.Path,
                    null,
                    [])));

        var after = await context.Services.B1AuthorityRepository.LoadProjectStateAsync(projectRef);
        Assert.Empty(after.Claims);
        Assert.Empty(after.Handoffs);
        Assert.Empty(B1Projector.Build(after).AcceptedProjectState.CurrentContributions);
    }

    [Fact]
    public async Task OpenCode_agent_using_a_DeepSeek_model_follows_the_same_non_authoritative_path()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var folder = new TemporaryDirectory("b1-opencode-deepseek");
        var opened = await context.Services.ProjectOpenService.OpenAsync(folder.Path);
        var projectRef = new ProjectRef(opened.Project.Id);
        var principal = context.Services.UserPrincipalProvider.GetCurrent();
        await context.Services.B1ProjectGovernance.CreateGovernedProjectForExistingProjectAsync(projectRef, principal);
        await context.Services.ProjectWorldInitialization.CommitAsync(
            new ProjectWorldInitializationRequest(projectRef, principal, RoleKind.Worker, "Own bounded work", "A reviewed result", "Do the work"));
        var assignment = Assert.Single(B1Projector.Build(await context.Services.B1AuthorityRepository.LoadProjectStateAsync(projectRef)).AcceptedProjectState.Assignments.Values);
        var runtime = new FakeAgentRuntime(
            providerName: "OpenCode",
            models: [new ModelProfile(new ProviderId("deepseek"), "deepseek/deepseek-v4-flash", "DeepSeek V4 Flash", AgentCapability.StructuredEvents)]);
        runtime.QueueTurn(new AgentTurnCompleted(
            new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed, "OpenCode completed the bounded work.", null),
            context.Time.GetUtcNow()));

        var result = await context.Services.B1AgentParticipation.ExecuteAsync(
            runtime,
            new B1AgentExecutionRequest(projectRef, principal, assignment.AssignmentRef,
                "deepseek/deepseek-v4-flash", folder.Path, null, []));

        Assert.Equal("deepseek/deepseek-v4-flash", result.Session.ModelId);
        Assert.Equal(new ProviderId("opencode"), result.Session.ProviderId);
        var after = await context.Services.B1AuthorityRepository.LoadProjectStateAsync(projectRef);
        Assert.Single(after.Attempts);
        Assert.Single(after.SessionBindings);
        Assert.Single(after.Handoffs);
        Assert.Empty(B1Projector.Build(after).AcceptedProjectState.CurrentContributions);
    }

    [Fact]
    public async Task A_different_agent_receives_the_selected_bounded_handoff_as_non_authoritative_context()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var folder = new TemporaryDirectory("b1-agent-handoff-continuation");
        var opened = await context.Services.ProjectOpenService.OpenAsync(folder.Path);
        var projectRef = new ProjectRef(opened.Project.Id);
        var principal = context.Services.UserPrincipalProvider.GetCurrent();
        await context.Services.B1ProjectGovernance.CreateGovernedProjectForExistingProjectAsync(projectRef, principal);
        await context.Services.ProjectWorldInitialization.CommitAsync(
            new ProjectWorldInitializationRequest(projectRef, principal, RoleKind.Worker, "Own bounded work", "A reviewed result", "Do the work"));
        var assignment = Assert.Single(B1Projector.Build(await context.Services.B1AuthorityRepository.LoadProjectStateAsync(projectRef)).AcceptedProjectState.Assignments.Values);
        var firstRuntime = new FakeAgentRuntime(providerName: "OpenCode");
        firstRuntime.QueueTurn(new AgentTurnCompleted(
            new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed, "OpenCode completed phase one.", null), context.Time.GetUtcNow()));
        var first = await context.Services.B1AgentParticipation.ExecuteAsync(
            firstRuntime,
            new B1AgentExecutionRequest(projectRef, principal, assignment.AssignmentRef, "deepseek/deepseek-v4-flash", folder.Path, null, []));

        var secondRuntime = new FakeAgentRuntime(providerName: "Codex");
        secondRuntime.QueueTurn(new AgentTurnCompleted(
            new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed, "Codex continued phase two.", null), context.Time.GetUtcNow()));
        await context.Services.B1AgentParticipation.ExecuteAsync(
            secondRuntime,
            new B1AgentExecutionRequest(projectRef, principal, assignment.AssignmentRef, "gpt-5.6", folder.Path, null, [], first.Attempt.AttemptRef));

        var prompt = Assert.Single(secondRuntime.SentRequests).Text;
        Assert.Contains("Current continuation Handoff (non-authoritative; verify before relying on it):", prompt, StringComparison.Ordinal);
        Assert.Contains("Result: OpenCode completed phase one.", prompt, StringComparison.Ordinal);
        var after = await context.Services.B1AuthorityRepository.LoadProjectStateAsync(projectRef);
        Assert.Equal(2, after.SessionBindings.Count);
        Assert.Equal(2, after.Handoffs.Count);
        Assert.Empty(B1Projector.Build(after).AcceptedProjectState.CurrentContributions);
    }
}
