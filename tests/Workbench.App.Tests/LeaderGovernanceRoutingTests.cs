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
        var candidateId = Guid.NewGuid();
        var candidate = Candidate(LeaderEvolutionImpactClass.WorldRule, LeaderEvolutionRouteHint.AuthorityConfirmation) with
        {
            CandidateId = candidateId
        };

        var suggestion = LeaderGovernanceRouteSuggestionBuilder.Create(projectId, candidate);

        Assert.True(suggestion.IsSafe);
        Assert.True(suggestion.CanPrepareDraft);
        Assert.NotNull(suggestion.AuthorityConfirmation);
        Assert.Null(suggestion.LibraryProposal);
        Assert.Equal(projectId, suggestion.AuthorityConfirmation!.ProjectId);
        Assert.Contains("无限计算器", suggestion.AuthorityConfirmation.Statements.Single(), StringComparison.Ordinal);
        Assert.Equal(
            candidateId,
            Assert.IsType<ConsideredRef.EvolutionCandidate>(Assert.Single(suggestion.AuthorityConfirmation.ConsideredRefs)).CandidateId);
    }

    [Fact]
    public void World_rule_authority_draft_projects_only_the_accepted_fact()
    {
        var candidate = Candidate(
            LeaderEvolutionImpactClass.WorldRule,
            LeaderEvolutionRouteHint.AuthorityConfirmation) with
        {
            Object = "因果编号职责边界",
            Before = null,
            After = "因果编号只负责标识和追踪同一次时间旅行产生的因果链，不参与风险评分，也不能阻止任何人的时间旅行。该规则拟作为正式世界规则，待用户确认后才进入 Accepted Project State。"
        };

        var suggestion = LeaderGovernanceRouteSuggestionBuilder.Create(Guid.NewGuid(), candidate);

        Assert.Equal(
            "因果编号职责边界: 因果编号只负责标识和追踪同一次时间旅行产生的因果链，不参与风险评分，也不能阻止任何人的时间旅行。",
            suggestion.AuthorityConfirmation!.Statements.Single());
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
        var candidateId = Guid.NewGuid();
        await context.Services.ProjectEvolutionCandidateRepository.SaveAsync(new(
            candidateId, opened.Project.Id, null, null, "current_user_message", "无限计算器", "WorldRule",
            "ConstraintRevision", null, "只预测时空稳定性风险，不控制人的思想", "WorldRule", "AuthorityConfirmation",
            "用户明确改变了项目设定", ProjectEvolutionCandidateStatus.Observed, context.Time.GetUtcNow()));
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
                    confirmation.ConsideredRefs,
                    confirmation.Contributions), cancellationToken));
        var suggestion = LeaderGovernanceRouteSuggestionBuilder.Create(
            opened.Project.Id,
            Candidate(LeaderEvolutionImpactClass.WorldRule, LeaderEvolutionRouteHint.AuthorityConfirmation) with
            {
                CandidateId = candidateId
            });
        pane.PreparedGovernanceDraft = new LeaderGovernanceDraftPreview(suggestion, suggestion.AuthorityConfirmation, null);

        await pane.SubmitGovernanceDraftCommand.ExecuteAsync(null);

        Assert.NotNull(pane.PendingAuthorityConfirmation);
        Assert.Null(pane.PreparedGovernanceDraft);
        Assert.Empty((await context.Services.B1AuthorityRepository.LoadProjectStateAsync(projectRef)).AuthorityDecisions);

        await pane.AcceptAuthorityConfirmationCommand.ExecuteAsync(null);

        var state = await context.Services.B1AuthorityRepository.LoadProjectStateAsync(projectRef);
        Assert.Contains(new ConsideredRef.EvolutionCandidate(candidateId), state.AuthorityDecisions.Single().ConsideredRefs);
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
            CandidateId = Guid.NewGuid(),
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
        var candidateReference = $"workbench:evolution-candidate/{candidate.CandidateId:N}";
        Assert.Contains(proposal.Draft.Materials, value => value.MaterialKind == "EvolutionCandidate" && value.Reference == candidateReference);
        Assert.Empty(await context.Services.ProjectLibraryEvolutionRepository.ListObjectsAsync(workspace.Result.Project.Id));

        await workspace.LibraryPane.InitializeAsync();
        workspace.LibraryPane.SelectedLibraryProposal = proposal;
        await workspace.LibraryPane.AcceptLibraryProposalAsync();

        Assert.Empty(await context.Services.ProjectMemoryApi.GetPendingLibraryProposalsAsync(workspace.Result.Project.Id));
        var libraryObject = Assert.Single(await context.Services.ProjectLibraryEvolutionRepository.ListObjectsAsync(workspace.Result.Project.Id));
        var node = Assert.Single(await context.Services.ProjectLibraryEvolutionRepository.GetTimelineAsync(workspace.Result.Project.Id, libraryObject.Id));
        Assert.Contains(
            await context.Services.ProjectLibraryEvolutionRepository.GetMaterialReferencesAsync(workspace.Result.Project.Id, node.Id),
            value => value.MaterialKind == "EvolutionCandidate" && value.Reference == candidateReference);
    }

    [Fact]
    public async Task Restarted_leader_restores_durable_candidates_for_governance()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var folder = new TemporaryDirectory("evolution-candidate-recovery");
        var project = (await context.Services.ProjectOpenService.OpenAsync(folder.Path)).Project;
        var candidateId = Guid.NewGuid();
        await context.Services.ProjectEvolutionCandidateRepository.SaveAsync(new(
            candidateId,
            project.Id,
            null,
            Guid.NewGuid(),
            "current_user_message",
            "因果编号黑市",
            "StoryDirection",
            "DirectionConfirmed",
            null,
            "下一章围绕因果编号黑市展开。",
            "Content",
            "LibraryProposal",
            "这是已经确定且会持续参与主线的剧情方向。",
            ProjectEvolutionCandidateStatus.Observed,
            context.Time.GetUtcNow()));
        var pane = new LeaderPaneViewModel(
            project,
            context.Services.RuntimeRegistry,
            context.LeaderSessions,
            () => Task.CompletedTask,
            evolutionCandidateRepository: context.Services.ProjectEvolutionCandidateRepository);

        await pane.InitializeAsync();

        var restored = Assert.Single(pane.EvolutionCandidates);
        Assert.Equal(candidateId, restored.CandidateId);
        var suggestion = Assert.Single(pane.GovernanceSuggestions);
        Assert.True(suggestion.CanPrepareDraft);
        Assert.Equal(
            $"workbench:evolution-candidate/{candidateId:N}",
            suggestion.LibraryProposal!.SourceRef);
    }

    [Fact]
    public void Direct_governance_draft_reuses_the_matching_durable_candidate_identity()
    {
        var causalId = Guid.NewGuid();
        var marketId = Guid.NewGuid();
        ProjectEvolutionCandidate[] candidates =
        [
            new(causalId, Guid.NewGuid(), null, null, "current_user_message", "无限计算器", "WorldRule", "ConstraintRevision", null,
                "无限计算器为每次时间旅行生成唯一因果编号。", "WorldRule", "AuthorityConfirmation", "正式能力变化。", ProjectEvolutionCandidateStatus.Observed, DateTimeOffset.Parse("2026-09-07T10:00:00Z")),
            new(marketId, Guid.NewGuid(), null, null, "current_user_message", "下一章剧情：因果编号黑市", "StoryDirection", "DirectionConfirmed", null,
                "下一章围绕因果编号黑市展开。", "Content", "NoGovernance", "已确定的剧情方向。", ProjectEvolutionCandidateStatus.Observed, DateTimeOffset.Parse("2026-09-07T11:00:00Z"))
        ];

        var authority = EvolutionCandidateGovernanceSourceResolver.Resolve(
            candidates,
            LeaderEvolutionRouteHint.AuthorityConfirmation,
            "因果编号只负责标识和追踪同一次时间旅行产生的因果链，不参与风险评分。"
        );
        var library = EvolutionCandidateGovernanceSourceResolver.Resolve(
            candidates,
            LeaderEvolutionRouteHint.LibraryProposal,
            "Story Direction Next Chapter 因果编号黑市 主角接触伪造编号的中间人。"
        );

        Assert.Equal(causalId, authority!.CandidateId);
        Assert.Equal(marketId, library!.CandidateId);
        Assert.Null(EvolutionCandidateGovernanceSourceResolver.Resolve(
            candidates,
            LeaderEvolutionRouteHint.LibraryProposal,
            "完全无关的新剧情素材"));
    }

    [Fact]
    public async Task Direct_authority_confirmation_is_enriched_with_matching_candidate_source()
    {
        var runtime = new FakeAgentRuntime();
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        await using var context = await AppTestContext.CreateAsync(runtimeRegistry: registry);
        var workspace = await context.CreateWorkspaceForNewProjectAsync();
        var candidateId = Guid.NewGuid();
        await context.Services.ProjectEvolutionCandidateRepository.SaveAsync(new(
            candidateId, workspace.Result.Project.Id, null, null, "current_user_message", "因果编号", "WorldRule",
            "ConstraintRevision", null, "因果编号只负责标识和追踪因果链。", "WorldRule", "AuthorityConfirmation",
            "正式规则。", ProjectEvolutionCandidateStatus.Observed, context.Time.GetUtcNow()));
        runtime.QueueTurn(new AgentTurnCompleted(new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed,
            """
            {"response":"请确认。","draft_proposal":null,"memory_commands":null,
             "authority_confirmation":{"title":"因果编号职责边界","contributions":[{"statement":"因果编号只负责标识和追踪因果链。"}]},
             "summary_deltas":null,"evolution_candidates":[]}
            """, null), context.Time.GetUtcNow()));
        await workspace.LeaderPane.InitializeAsync();
        workspace.LeaderPane.DraftMessage = "确认因果编号职责边界。";

        await workspace.LeaderPane.SendAsync();

        Assert.Equal(
            $"workbench:evolution-candidate/{candidateId:N}",
            workspace.LeaderPane.PendingAuthorityConfirmation!.SourceRef);
    }

    [Fact]
    public async Task Direct_library_proposal_is_enriched_with_matching_candidate_source()
    {
        var runtime = new FakeAgentRuntime();
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        await using var context = await AppTestContext.CreateAsync(runtimeRegistry: registry);
        var workspace = await context.CreateWorkspaceForNewProjectAsync();
        var candidateId = Guid.NewGuid();
        await context.Services.ProjectEvolutionCandidateRepository.SaveAsync(new(
            candidateId, workspace.Result.Project.Id, null, null, "current_user_message", "因果编号黑市", "StoryDirection",
            "DirectionConfirmed", null, "下一章围绕因果编号黑市展开。", "Content", "NoGovernance",
            "确定的剧情方向。", ProjectEvolutionCandidateStatus.Observed, context.Time.GetUtcNow()));
        runtime.QueueTurn(new AgentTurnCompleted(new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed,
            """
            {"response":"项目库提案已准备。","draft_proposal":null,
             "memory_commands":{"library_proposal":{"action":"CreateNode","target_object_id":null,"target_node_id":null,
             "expected_node_revision":null,"expected_overview_revision":null,"category":"Story Direction","topic":"因果编号黑市",
             "local_date":"2026-09-07","node_content":"下一章围绕因果编号黑市展开。","current_overview":null,
             "materials":[{"kind":"UserMessage","reference":"current_user_message","label":"source"}],"occurred_at":null}},
             "authority_confirmation":null,"summary_deltas":null,"evolution_candidates":[]}
            """, null), context.Time.GetUtcNow()));
        await workspace.LeaderPane.InitializeAsync();
        workspace.LeaderPane.DraftMessage = "准备因果编号黑市项目库提案。";

        await workspace.LeaderPane.SendAsync();

        var proposal = Assert.Single(
            await context.Services.ProjectMemoryApi.GetPendingLibraryProposalsAsync(workspace.Result.Project.Id));
        Assert.Contains(
            proposal.Draft.Materials,
            value => value.MaterialKind == "EvolutionCandidate" &&
                     value.Reference == $"workbench:evolution-candidate/{candidateId:N}");
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
