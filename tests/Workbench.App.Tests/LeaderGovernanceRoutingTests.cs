using Workbench.App.Leader;
using Workbench.App.ViewModels.Panes;
using Workbench.Core.Continuity;
using Workbench.App.Tests.Support;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Registry;
using Workbench.Storage.Memory;

namespace Workbench.App.Tests;

public sealed class LeaderGovernanceRoutingTests
{
    [Fact]
    public void World_rule_candidate_creates_only_an_ephemeral_authority_draft()
    {
        var projectId = Guid.NewGuid();
        var candidate = Candidate(LeaderEvolutionImpactClass.WorldRule, LeaderEvolutionRouteHint.AuthorityConfirmation);

        var suggestion = LeaderGovernanceRouteSuggestionBuilder.Create(projectId, candidate);

        Assert.True(suggestion.IsSafe);
        Assert.True(suggestion.CanPrepareDraft);
        Assert.NotNull(suggestion.AuthorityConfirmation);
        Assert.Null(suggestion.LibraryProposal);
        Assert.Equal(projectId, suggestion.AuthorityConfirmation!.ProjectId);
        Assert.Contains("无限计算器", suggestion.AuthorityConfirmation.Statements.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public void Content_candidate_creates_only_an_ephemeral_library_draft()
    {
        var suggestion = LeaderGovernanceRouteSuggestionBuilder.Create(
            Guid.NewGuid(),
            Candidate(LeaderEvolutionImpactClass.Content, LeaderEvolutionRouteHint.LibraryProposal));

        Assert.True(suggestion.IsSafe);
        Assert.True(suggestion.CanPrepareDraft);
        Assert.Null(suggestion.AuthorityConfirmation);
        Assert.NotNull(suggestion.LibraryProposal);
        Assert.Equal("Content", suggestion.LibraryProposal!.Category);
    }

    [Fact]
    public void Conflicting_route_is_manual_review_and_creates_no_draft()
    {
        var suggestion = LeaderGovernanceRouteSuggestionBuilder.Create(
            Guid.NewGuid(),
            Candidate(LeaderEvolutionImpactClass.WorldRule, LeaderEvolutionRouteHint.LibraryProposal));

        Assert.False(suggestion.IsSafe);
        Assert.True(suggestion.IsManualReview);
        Assert.False(suggestion.CanPrepareDraft);
        Assert.Null(suggestion.AuthorityConfirmation);
        Assert.Null(suggestion.LibraryProposal);
    }

    [Fact]
    public void Architecture_and_unclassified_candidates_remain_without_automatic_governance()
    {
        var architecture = LeaderGovernanceRouteSuggestionBuilder.Create(
            Guid.NewGuid(),
            Candidate(LeaderEvolutionImpactClass.Architecture, LeaderEvolutionRouteHint.Unclassified));
        var unclassified = LeaderGovernanceRouteSuggestionBuilder.Create(
            Guid.NewGuid(),
            Candidate(LeaderEvolutionImpactClass.Unclassified, LeaderEvolutionRouteHint.Unclassified));

        Assert.True(architecture.IsNoAction);
        Assert.True(unclassified.IsNoAction);
        Assert.Null(architecture.AuthorityConfirmation);
        Assert.Null(unclassified.LibraryProposal);
    }

    [Fact]
    public async Task Submitting_authority_preview_only_creates_pending_confirmation_until_final_acceptance()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var folder = new TemporaryDirectory("evolution-authority-submit");
        var opened = await context.Services.ProjectOpenService.OpenAsync(folder.Path);
        var projectRef = new ProjectRef(opened.Project.Id);
        var principal = new UserPrincipalRef(context.Services.UserPrincipalProvider.GetCurrent().Value);
        await context.Services.B1ProjectGovernance.CreateGovernedProjectForExistingProjectAsync(projectRef, principal);
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
        var suggestion = LeaderGovernanceRouteSuggestionBuilder.Create(
            opened.Project.Id,
            Candidate(LeaderEvolutionImpactClass.WorldRule, LeaderEvolutionRouteHint.AuthorityConfirmation));
        pane.PreparedGovernanceDraft = new LeaderGovernanceDraftPreview(suggestion, suggestion.AuthorityConfirmation, null);

        await pane.SubmitGovernanceDraftCommand.ExecuteAsync(null);

        Assert.NotNull(pane.PendingAuthorityConfirmation);
        Assert.Null(pane.PreparedGovernanceDraft);
        Assert.Empty((await context.Services.B1AuthorityRepository.LoadProjectStateAsync(projectRef)).AuthorityDecisions);

        await pane.AcceptAuthorityConfirmationCommand.ExecuteAsync(null);

        var state = await context.Services.B1AuthorityRepository.LoadProjectStateAsync(projectRef);
        Assert.NotEmpty(state.AuthorityDecisions);
        Assert.Null(pane.PendingAuthorityConfirmation);
    }

    [Fact]
    public async Task Submitting_library_preview_creates_pending_proposal_with_candidate_source_without_projection()
    {
        var runtime = new FakeAgentRuntime();
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        await using var context = await AppTestContext.CreateAsync(runtimeRegistry: registry);
        var workspace = await context.CreateWorkspaceForNewProjectAsync();
        runtime.QueueTurn(new AgentTurnCompleted(
            new AgentResult(
                AgentSessionId.New(),
                AgentSessionStatus.Completed,
                "{\"response\":\"ready\",\"draft_proposal\":null,\"memory_commands\":null}",
                null),
            DateTimeOffset.UtcNow));
        await workspace.LeaderPane.InitializeAsync();
        await workspace.LibraryPane.InitializeAsync();
        workspace.LeaderPane.DraftMessage = "Prepare the project context.";
        await workspace.LeaderPane.SendAsync();
        var candidate = Candidate(LeaderEvolutionImpactClass.CharacterOrObject, LeaderEvolutionRouteHint.LibraryProposal) with
        {
            Object = "林砚",
            ObjectKind = "Character",
            Before = "层级审核员",
            After = "时间异常调查员",
            SourceRef = "LeaderMessage:phase4-test"
        };
        var suggestion = LeaderGovernanceRouteSuggestionBuilder.Create(workspace.Result.Project.Id, candidate);
        workspace.LeaderPane.PreparedGovernanceDraft = new LeaderGovernanceDraftPreview(suggestion, null, suggestion.LibraryProposal);

        await workspace.LeaderPane.SubmitGovernanceDraftCommand.ExecuteAsync(null);

        var proposal = Assert.Single(await context.Services.ProjectMemoryApi.GetPendingLibraryProposalsAsync(workspace.Result.Project.Id));
        Assert.Equal("林砚", proposal.Draft.Topic);
        Assert.Contains(proposal.Draft.Materials, value => value.MaterialKind == "EvolutionCandidate" && value.Reference == candidate.SourceRef);
        Assert.Empty(await context.Services.ProjectLibraryEvolutionRepository.ListObjectsAsync(workspace.Result.Project.Id));

        await workspace.LibraryPane.InitializeAsync();
        workspace.LibraryPane.SelectedLibraryProposal = proposal;
        await workspace.LibraryPane.AcceptLibraryProposalAsync();

        Assert.Empty(await context.Services.ProjectMemoryApi.GetPendingLibraryProposalsAsync(workspace.Result.Project.Id));
        Assert.Single(await context.Services.ProjectLibraryEvolutionRepository.ListObjectsAsync(workspace.Result.Project.Id));
    }

    [Fact]
    public async Task Unsafe_governance_preview_cannot_be_submitted()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var folder = new TemporaryDirectory("evolution-unsafe-submit");
        var opened = await context.Services.ProjectOpenService.OpenAsync(folder.Path);
        var suggestion = LeaderGovernanceRouteSuggestionBuilder.Create(
            opened.Project.Id,
            Candidate(LeaderEvolutionImpactClass.WorldRule, LeaderEvolutionRouteHint.LibraryProposal));
        var pane = new LeaderPaneViewModel(opened.Project, context.Services.RuntimeRegistry, context.LeaderSessions, () => Task.CompletedTask)
        {
            PreparedGovernanceDraft = new LeaderGovernanceDraftPreview(suggestion, null, null)
        };

        await pane.SubmitGovernanceDraftCommand.ExecuteAsync(null);

        Assert.Null(pane.PendingAuthorityConfirmation);
        Assert.NotNull(pane.PreparedGovernanceDraft);
        Assert.Null(pane.AuthorityConfirmationStatusMessage);
        Assert.Null(pane.MemoryCommandStatus);
    }

    private static LeaderEvolutionCandidate Candidate(
        LeaderEvolutionImpactClass impactClass,
        LeaderEvolutionRouteHint routeHint) =>
        new(
            "无限计算器",
            "WorldRule",
            "ConstraintRevision",
            "可能控制人的时间行为",
            "只预测时空稳定性风险，不控制人的思想",
            impactClass,
            routeHint,
            "用户明确改变了项目设定",
            "current_user_message");
}
