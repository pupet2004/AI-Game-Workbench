using System.Text.Json;
using Workbench.App.Leader;
using Workbench.App.ViewModels.Panes;
using Workbench.App.Tests.Support;
using Workbench.Core.Continuity;
using Workbench.Storage.Tasks;
using Workbench.Storage.Workers;

namespace Workbench.App.Tests;

public sealed class AuthorityConfirmationRoutingTests
{
    [Fact]
    public async Task Leader_authority_confirmation_response_stays_ephemeral_and_never_enters_worker_routing()
    {
        var runtime = new FakeAgentRuntime();
        var registry = new Workbench.Runtime.Registry.AgentRuntimeRegistry();
        registry.Register(runtime);
        await using var context = await AppTestContext.CreateAsync(runtimeRegistry: registry);
        using var folder = new TemporaryDirectory("authority-confirmation-response");
        var workspace = context.CreateWorkspace(await context.Services.ProjectOpenService.OpenAsync(folder.Path));
        runtime.QueueTurn(new Workbench.Runtime.Agents.AgentTurnCompleted(
            new Workbench.Runtime.Agents.AgentResult(
                Workbench.Runtime.Agents.AgentSessionId.New(),
                Workbench.Runtime.Agents.AgentSessionStatus.Completed,
                "{\"response\":\"请确认世界约束。\",\"draft_proposal\":null,\"authority_confirmation\":{\"title\":\"Authority Confirmation / 零刻核心世界约束\",\"contributions\":[{\"statement\":\"从零刻开始，人类可以进行时间穿梭。\"}]},\"memory_commands\":null,\"summary_deltas\":null}",
                null),
            DateTimeOffset.UtcNow));

        await workspace.LeaderPane.InitializeAsync();
        workspace.LeaderPane.DraftMessage = "确认项目世界约束";
        await workspace.LeaderPane.SendAsync();

        Assert.True(workspace.LeaderPane.HasPendingAuthorityConfirmation);
        Assert.Empty(await context.Services.TaskRepository.ListAsync(workspace.Result.Project.Id));
        Assert.Empty(await context.Services.WorkerExecutionRepository.ListAsync(workspace.Result.Project.Id));
        Assert.Empty(await context.Services.WorkerRoutingStore.ListSessionsAsync(workspace.Result.Project.Id));
    }

    [Fact]
    public void Authority_confirmation_is_distinct_from_worker_draft_proposal()
    {
        var projectId = Guid.NewGuid();
        var json = $$"""
        {
          "response": "请确认项目世界约束。",
          "draft_proposal": null,
          "authority_confirmation": {
            "title": "Authority Confirmation / 零刻核心世界约束",
            "contributions": [
              { "statement": "从零刻开始，人类可以进行时间穿梭。" },
              { "statement": "世界只有一条闭合时间线，不存在平行宇宙。" }
            ]
          },
          "memory_commands": null,
          "summary_deltas": null
        }
        """;

        Assert.True(LeaderStructuredResponse.TryParse(json, projectId, out var parsed));
        Assert.Null(parsed.Proposal);
        Assert.NotNull(parsed.AuthorityConfirmation);
        Assert.Equal(projectId, parsed.AuthorityConfirmation!.ProjectId);
        Assert.Equal(2, parsed.AuthorityConfirmation.Contributions.Count);
        Assert.All(parsed.AuthorityConfirmation.Contributions, value => Assert.IsType<ContributionScopeTarget.Project>(value.Scope));
    }

    [Fact]
    public async Task Accepting_authority_confirmation_commits_authority_only_without_worker_side_effects()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var folder = new TemporaryDirectory("authority-confirmation-routing");
        var opened = await context.Services.ProjectOpenService.OpenAsync(folder.Path);
        var projectRef = new ProjectRef(opened.Project.Id);
        var principal = new UserPrincipalRef(context.Services.UserPrincipalProvider.GetCurrent().Value);
        await context.Services.B1ProjectGovernance.CreateGovernedProjectForExistingProjectAsync(projectRef, principal);

        var draft = new AuthorityConfirmationDraft(
            opened.Project.Id,
            "Authority Confirmation / 零刻核心世界约束",
            [
                new("从零刻开始，人类可以进行时间穿梭。", new ContributionScopeTarget.Project(projectRef), null, null),
                new("世界只有一条闭合时间线，不存在平行宇宙。", new ContributionScopeTarget.Project(projectRef), null, null)
            ]);

        var pane = new LeaderPaneViewModel(
            opened.Project,
            context.Services.RuntimeRegistry,
            context.LeaderSessions,
            () => Task.CompletedTask,
            acceptAuthorityConfirmation: (confirmation, cancellationToken) => context.Services.B1AuthorityCommands.AuthorAcceptedStateAsync(
                new AuthorAcceptedStateCommand(
                    projectRef,
                    principal,
                    new DecidingAuthorityRef.UserPrincipal(principal),
                    [],
                    confirmation.Contributions), cancellationToken));
        pane.PendingAuthorityConfirmation = draft;

        await pane.AcceptAuthorityConfirmationCommand.ExecuteAsync(null);

        var state = await context.Services.B1AuthorityRepository.LoadProjectStateAsync(projectRef);
        var accepted = B1Projector.Build(state).AcceptedProjectState.CurrentContributions;
        Assert.Equal(2, accepted.Count);
        Assert.Contains(accepted, value => value.Statement == "从零刻开始，人类可以进行时间穿梭。");
        Assert.Contains(accepted, value => value.Statement == "世界只有一条闭合时间线，不存在平行宇宙。");
        Assert.Null(pane.PendingAuthorityConfirmation);
        Assert.Empty(await context.Services.TaskRepository.ListAsync(opened.Project.Id));
        Assert.Empty(await new TaskEventRepository(context.Services.Database).ListForProjectAsync(opened.Project.Id, "WorkerSessionStarted", 100));
        Assert.Empty(await context.Services.WorkerExecutionRepository.ListAsync(opened.Project.Id));
    }

    [Fact]
    public async Task Rejecting_authority_confirmation_changes_no_authority_or_worker_state()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var folder = new TemporaryDirectory("authority-confirmation-reject");
        var opened = await context.Services.ProjectOpenService.OpenAsync(folder.Path);
        var projectRef = new ProjectRef(opened.Project.Id);
        var principal = new UserPrincipalRef(context.Services.UserPrincipalProvider.GetCurrent().Value);
        await context.Services.B1ProjectGovernance.CreateGovernedProjectForExistingProjectAsync(projectRef, principal);

        var pane = new LeaderPaneViewModel(opened.Project, context.Services.RuntimeRegistry, context.LeaderSessions, () => Task.CompletedTask)
        {
            PendingAuthorityConfirmation = new AuthorityConfirmationDraft(
                opened.Project.Id,
                "Authority Confirmation",
                [new("无限计算器只预测时空稳定风险，不控制人的思想。", new ContributionScopeTarget.Project(projectRef), null, null)])
        };

        pane.RejectAuthorityConfirmationCommand.Execute(null);

        Assert.Null(pane.PendingAuthorityConfirmation);
        Assert.Empty((await context.Services.B1AuthorityRepository.LoadProjectStateAsync(projectRef)).AuthorityDecisions);
        Assert.Empty(await context.Services.TaskRepository.ListAsync(opened.Project.Id));
        Assert.Empty(await context.Services.WorkerExecutionRepository.ListAsync(opened.Project.Id));
    }

    [Fact]
    public async Task Authority_acceptance_failure_keeps_draft_visible_and_does_not_fallback_to_worker()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var folder = new TemporaryDirectory("authority-confirmation-failure");
        var opened = await context.Services.ProjectOpenService.OpenAsync(folder.Path);
        var projectRef = new ProjectRef(opened.Project.Id);
        var draft = new AuthorityConfirmationDraft(
            opened.Project.Id,
            "Authority Confirmation",
            [new("从零刻开始，人类可以进行时间穿梭。", new ContributionScopeTarget.Project(projectRef), null, null)]);
        var pane = new LeaderPaneViewModel(
            opened.Project,
            context.Services.RuntimeRegistry,
            context.LeaderSessions,
            () => Task.CompletedTask,
            acceptAuthorityConfirmation: (_, _) => throw new InvalidOperationException("authority commit failed"));
        pane.PendingAuthorityConfirmation = draft;

        await pane.AcceptAuthorityConfirmationCommand.ExecuteAsync(null);

        Assert.Same(draft, pane.PendingAuthorityConfirmation);
        Assert.Contains("authority commit failed", pane.AuthorityConfirmationStatusMessage, StringComparison.Ordinal);
        Assert.Empty(await context.Services.TaskRepository.ListAsync(opened.Project.Id));
        Assert.Empty(await context.Services.WorkerExecutionRepository.ListAsync(opened.Project.Id));
    }
}
