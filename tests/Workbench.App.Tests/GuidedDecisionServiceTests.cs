using Workbench.App.ProjectWorld;
using Workbench.App.Continuity;
using Workbench.App.Services;
using Workbench.App.Tests.Support;
using Workbench.Core.Continuity;
using Workbench.Project.Opening;

namespace Workbench.App.Tests;

public sealed class GuidedDecisionServiceTests
{
    [Theory]
    [InlineData("Accept", AssignmentDisposition.Accepted, ContributionDecisionMode.AdoptVerbatim)]
    [InlineData("RequestRevision", AssignmentDisposition.RevisionRequired, ContributionDecisionMode.Ignore)]
    [InlineData("Reject", AssignmentDisposition.Rejected, ContributionDecisionMode.Ignore)]
    public async Task Product_review_actions_preview_the_selected_disposition_without_committing(
        string action,
        AssignmentDisposition expectedDisposition,
        ContributionDecisionMode expectedContributionMode)
    {
        await using var fixture = await Fixture.CreateAsync();
        var viewModel = new GuidedDecisionViewModel(
            fixture.Services,
            fixture.Result,
            fixture.Handoff,
            () => Task.CompletedTask);

        await viewModel.InitializeAsync();
        switch (action)
        {
            case "Accept":
                await viewModel.AcceptCommand.ExecuteAsync(null);
                break;
            case "RequestRevision":
                await viewModel.RequestRevisionCommand.ExecuteAsync(null);
                break;
            case "Reject":
                await viewModel.RejectCommand.ExecuteAsync(null);
                break;
        }

        Assert.Equal(expectedDisposition, viewModel.SelectedDisposition);
        Assert.Equal(expectedContributionMode, viewModel.SelectedContributionMode);
        Assert.True(viewModel.IsPreviewVisible);
        Assert.DoesNotContain("Assignment", viewModel.PreviewText, StringComparison.Ordinal);
        Assert.DoesNotContain("UserPrincipal", viewModel.PreviewText, StringComparison.Ordinal);
        Assert.Equal(1L, await fixture.CountAsync("b1_authority_decisions"));
        Assert.Equal(0L, await fixture.CountAsync("b1_accepted_state_contributions"));
    }

    [Theory]
    [InlineData("disposition")]
    [InlineData("contribution")]
    [InlineData("statement")]
    [InlineData("revision")]
    [InlineData("successor")]
    public async Task Editing_a_decision_invalidates_preview_and_prevents_unpreviewed_commit(string field)
    {
        await using var fixture = await Fixture.CreateAsync();
        var viewModel = new GuidedDecisionViewModel(
            fixture.Services, fixture.Result, fixture.Handoff, () => Task.CompletedTask);
        await viewModel.InitializeAsync();
        await viewModel.AcceptCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsPreviewVisible);

        switch (field)
        {
            case "disposition": viewModel.SelectedDisposition = AssignmentDisposition.Rejected; break;
            case "contribution": viewModel.SelectedContributionMode = ContributionDecisionMode.Ignore; break;
            case "statement": viewModel.EditedContributionStatement = "Edited change"; break;
            case "revision": viewModel.NewRevisionContract = "Fix validation"; break;
            case "successor": viewModel.SuccessorAssignmentContract = "Next work"; break;
        }

        Assert.False(viewModel.IsPreviewVisible);
        Assert.Null(viewModel.PreviewText);
        await viewModel.ConfirmDecisionCommand.ExecuteAsync(null);
        Assert.Equal(1L, await fixture.CountAsync("b1_authority_decisions"));
        Assert.Equal(0L, await fixture.CountAsync("b1_accepted_state_contributions"));
    }

    [Theory]
    [InlineData(AssignmentDisposition.Rejected)]
    [InlineData(AssignmentDisposition.RevisionRequired)]
    public async Task Product_review_confirm_retains_submission_without_accepting_changes(
        AssignmentDisposition disposition)
    {
        await using var fixture = await Fixture.CreateAsync();
        var viewModel = new GuidedDecisionViewModel(
            fixture.Services, fixture.Result, fixture.Handoff, () => Task.CompletedTask);
        await viewModel.InitializeAsync();
        var claimCount = await fixture.CountAsync("b1_claims");
        if (disposition == AssignmentDisposition.Rejected)
            await viewModel.RejectCommand.ExecuteAsync(null);
        else
            await viewModel.RequestRevisionCommand.ExecuteAsync(null);
        await viewModel.ConfirmDecisionCommand.ExecuteAsync(null);

        Assert.Null(viewModel.ErrorMessage);
        Assert.Equal(2L, await fixture.CountAsync("b1_authority_decisions"));
        Assert.Equal(0L, await fixture.CountAsync("b1_accepted_state_contributions"));
        Assert.Equal(claimCount, await fixture.CountAsync("b1_claims"));
        var state = await fixture.Services.B1AuthorityRepository.LoadProjectStateAsync(new(fixture.Result.Project.Id));
        Assert.Contains(state.Handoffs, value => value.HandoffRef == fixture.Handoff);
        Assert.Contains(state.AuthorityDecisions, value => value.AssignmentDispositionEffect?.Disposition == disposition);
    }

    [Fact]
    public async Task Advanced_preview_preserves_edited_contribution_mode()
    {
        await using var fixture = await Fixture.CreateAsync();
        var viewModel = new GuidedDecisionViewModel(
            fixture.Services, fixture.Result, fixture.Handoff, () => Task.CompletedTask);
        await viewModel.InitializeAsync();
        viewModel.SelectedContributionMode = ContributionDecisionMode.EditAndEstablish;
        viewModel.EditedContributionStatement = "Use turn-based card combat";
        await viewModel.PreviewDecisionCommand.ExecuteAsync(null);
        Assert.Contains(viewModel.EditedContributionStatement, viewModel.PreviewText, StringComparison.Ordinal);
        await viewModel.ConfirmDecisionCommand.ExecuteAsync(null);
        var state = await fixture.Services.B1Projections.GetAcceptedProjectStateAsync(new(fixture.Result.Project.Id));
        Assert.Equal("Use turn-based card combat", Assert.Single(state.CurrentContributions).Statement);
    }

    [Fact]
    public async Task Overview_continue_and_review_both_open_the_queue_without_making_a_decision()
    {
        await using var fixture = await Fixture.CreateAsync();
        var queueVisits = 0;
        var directVisits = 0;
        var overview = new ProjectWorldExplorerViewModel(fixture.Services, fixture.Result,
            () => Task.CompletedTask,
            openReview: () => { queueVisits++; return Task.CompletedTask; },
            openGuidedDecision: _ => { directVisits++; return Task.CompletedTask; });
        await overview.InitializeAsync();
        await overview.ContinueProjectCommand.ExecuteAsync(null);
        await overview.ReviewHandoffCommand.ExecuteAsync(fixture.Handoff);

        Assert.Equal(2, queueVisits);
        Assert.Equal(0, directVisits);
        Assert.Equal(1L, await fixture.CountAsync("b1_authority_decisions"));
    }

    [Fact]
    public async Task Overview_removes_finished_work_only_after_accept_and_shows_the_accepted_change()
    {
        await using var fixture = await Fixture.CreateAsync();
        var overview = new ProjectWorldExplorerViewModel(fixture.Services, fixture.Result, () => Task.CompletedTask);
        await overview.InitializeAsync();
        Assert.Single(overview.ActiveWork);
        Assert.Single(overview.PendingHandoffs);
        Assert.Empty(overview.AcceptedState);

        await fixture.Services.GuidedDecision.CommitAsync(new GuidedDecisionRequest(
            new(fixture.Result.Project.Id), fixture.Principal, fixture.Handoff,
            AssignmentDisposition.Accepted, ContributionDecisionMode.AdoptVerbatim, null, null));
        await overview.InitializeAsync();

        Assert.Null(overview.ErrorMessage);
        Assert.Empty(overview.ActiveWork);
        Assert.Empty(overview.PendingHandoffs);
        Assert.Equal("Use card-based combat", overview.RecentChangeText);
    }

    [Fact]
    public async Task Preview_is_non_persistent_and_confirm_commits_disposition_and_contribution()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = new GuidedDecisionRequest(
            new(fixture.Result.Project.Id),
            fixture.Principal,
            fixture.Handoff,
            AssignmentDisposition.Accepted,
            ContributionDecisionMode.AdoptVerbatim,
            null,
            null);

        var preview = await fixture.Services.GuidedDecision.PreviewAsync(request);

        Assert.Contains(preview.Effects, value => value.Contains("Accepted Project"));
        Assert.Equal(1L, await fixture.CountAsync("b1_authority_decisions"));
        Assert.Equal(0L, await fixture.CountAsync("b1_accepted_state_contributions"));

        var decision = await fixture.Services.GuidedDecision.CommitAsync(request);

        Assert.Equal(AssignmentDisposition.Accepted, decision.AssignmentDispositionEffect!.Disposition);
        Assert.Equal(2L, await fixture.CountAsync("b1_authority_decisions"));
        Assert.Equal(1L, await fixture.CountAsync("b1_accepted_state_contributions"));
        var projection = await fixture.Services.B1Projections.GetProjectProjectionAsync(new(fixture.Result.Project.Id));
        Assert.Single(projection.AcceptedProjectState.CurrentContributions);
    }

    [Fact]
    public async Task Confirm_navigates_to_the_project_overview_after_the_authority_commit()
    {
        await using var fixture = await Fixture.CreateAsync();
        var returnedToOverview = false;
        var viewModel = new GuidedDecisionViewModel(
            fixture.Services,
            fixture.Result,
            fixture.Handoff,
            () => Task.CompletedTask,
            () =>
            {
                returnedToOverview = true;
                return Task.CompletedTask;
            });

        await viewModel.InitializeAsync();
        viewModel.SelectedDisposition = AssignmentDisposition.Accepted;
        await viewModel.PreviewDecisionCommand.ExecuteAsync(null);
        await viewModel.ConfirmDecisionCommand.ExecuteAsync(null);

        Assert.True(returnedToOverview);
        Assert.Contains("recorded", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.IsPreviewVisible);
        Assert.Single((await fixture.Services.B1Projections.GetAcceptedProjectStateAsync(
            new ProjectRef(fixture.Result.Project.Id))).CurrentContributions);
    }

    [Fact]
    public async Task Accept_defaults_to_adopting_the_proposed_project_change()
    {
        await using var fixture = await Fixture.CreateAsync();
        var viewModel = new GuidedDecisionViewModel(
            fixture.Services,
            fixture.Result,
            fixture.Handoff,
            () => Task.CompletedTask);

        await viewModel.InitializeAsync();

        Assert.Equal(AssignmentDisposition.Accepted, viewModel.SelectedDisposition);
        Assert.Equal(ContributionDecisionMode.AdoptVerbatim, viewModel.SelectedContributionMode);
    }

    [Fact]
    public async Task Accepted_decision_creates_explicit_successor_assignment_after_the_accept()
    {
        await using var fixture = await Fixture.CreateAsync();
        var before = await fixture.Services.B1AuthorityRepository.LoadProjectStateAsync(
            new ProjectRef(fixture.Result.Project.Id));
        var currentAssignment = Assert.Single(
            B1Projector.Build(before).AcceptedProjectState.CurrentDelegationAssignments);

        var request = new GuidedDecisionRequest(
            new(fixture.Result.Project.Id),
            fixture.Principal,
            fixture.Handoff,
            AssignmentDisposition.Accepted,
            ContributionDecisionMode.AdoptVerbatim,
            null,
            null,
            "Implement the next bounded gameplay change.");

        var preview = await fixture.Services.GuidedDecision.PreviewAsync(request);
        Assert.Contains(preview.Effects, effect =>
            effect.Contains("successor Assignment", StringComparison.Ordinal));

        await fixture.Services.GuidedDecision.CommitAsync(request);

        var after = await fixture.Services.B1AuthorityRepository.LoadProjectStateAsync(
            new ProjectRef(fixture.Result.Project.Id));
        var projection = B1Projector.Build(after);
        var successor = Assert.Single(
            projection.AcceptedProjectState.CurrentDelegationAssignments
                .Where(value => value != currentAssignment)
                .Select(value => projection.AcceptedProjectState.Assignments[value]));
        var successorRevision = projection.AcceptedProjectState.Revisions[
            projection.AcceptedProjectState.CurrentEffectiveRevisionRefs[successor.AssignmentRef]];
        Assert.Equal("Implement the next bounded gameplay change.", successorRevision.Contract.WorkContract);
        Assert.Contains(after.AuthorityDecisions, decision =>
            decision.AssignmentDelegationEffect?.ReplacesAssignmentRef == currentAssignment);
    }

    [Fact]
    public async Task Guided_decision_view_model_can_schedule_the_next_assignment_after_accept()
    {
        await using var fixture = await Fixture.CreateAsync();
        var viewModel = new GuidedDecisionViewModel(
            fixture.Services,
            fixture.Result,
            fixture.Handoff,
            () => Task.CompletedTask);
        var before = await fixture.Services.B1AuthorityRepository.LoadProjectStateAsync(
            new ProjectRef(fixture.Result.Project.Id));
        var currentAssignment = Assert.Single(
            B1Projector.Build(before).AcceptedProjectState.CurrentDelegationAssignments);

        await viewModel.InitializeAsync();
        viewModel.SuccessorAssignmentContract = "Add a Reset button to the counter.";
        await viewModel.PreviewDecisionCommand.ExecuteAsync(null);

        Assert.Contains("Next work", viewModel.PreviewText, StringComparison.Ordinal);
        Assert.Contains(viewModel.SuccessorAssignmentContract, viewModel.PreviewText, StringComparison.Ordinal);

        await viewModel.ConfirmDecisionCommand.ExecuteAsync(null);

        var state = await fixture.Services.B1AuthorityRepository.LoadProjectStateAsync(
            new ProjectRef(fixture.Result.Project.Id));
        var projection = B1Projector.Build(state);
        var successor = Assert.Single(
            projection.AcceptedProjectState.CurrentDelegationAssignments
                .Where(value => value != currentAssignment)
                .Select(value => projection.AcceptedProjectState.Assignments[value]));
        var successorRevision = projection.AcceptedProjectState.Revisions[
            projection.AcceptedProjectState.CurrentEffectiveRevisionRefs[successor.AssignmentRef]];

        Assert.Equal("Add a Reset button to the counter.", successorRevision.Contract.WorkContract);
    }

    [Fact]
    public async Task Rejected_decision_does_not_create_a_successor_even_when_requested()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = new GuidedDecisionRequest(
            new(fixture.Result.Project.Id),
            fixture.Principal,
            fixture.Handoff,
            AssignmentDisposition.Rejected,
            ContributionDecisionMode.Ignore,
            null,
            null,
            "This must not be scheduled after rejection.");

        await fixture.Services.GuidedDecision.CommitAsync(request);

        var after = await fixture.Services.B1AuthorityRepository.LoadProjectStateAsync(
            new ProjectRef(fixture.Result.Project.Id));
        Assert.Single(after.Assignments);
        Assert.Single(after.AuthorityDecisions, decision =>
            decision.AssignmentDelegationEffect is not null);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly AppTestContext _context;
        private readonly TemporaryDirectory _folder;

        private Fixture(AppTestContext context, TemporaryDirectory folder, ProjectOpenResult result, UserPrincipalRef principal, HandoffRef handoff)
        {
            _context = context;
            _folder = folder;
            Services = context.Services;
            Result = result;
            Principal = principal;
            Handoff = handoff;
        }

        public AppServices Services { get; }
        public ProjectOpenResult Result { get; }
        public UserPrincipalRef Principal { get; }
        public HandoffRef Handoff { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var context = await AppTestContext.CreateAsync();
            var folder = new TemporaryDirectory("decision-project");
            var result = await context.Services.ProjectOpenService.OpenAsync(folder.Path);
            var principal = context.Services.UserPrincipalProvider.GetCurrent();
            await context.Services.B1ProjectGovernance.CreateGovernedProjectForExistingProjectAsync(new(result.Project.Id), principal);
            var init = await context.Services.ProjectWorldInitialization.CommitAsync(
                new ProjectWorldInitializationRequest(new(result.Project.Id), principal, RoleKind.Worker,
                    "Own gameplay implementation", "A clear first playable change", "Design the first combat prototype"));
            var assignment = init.AssignmentDelegationEffect!.Assignment.AssignmentRef;
            var revision = init.AssignmentDelegationEffect.InitialRevision.RevisionRef;
            await context.Services.B1NonAuthoritativeCommands.CreateAttemptAndSelectAsync(
                new CreateAttemptCommand(new(result.Project.Id), principal, new AttemptRef(Guid.NewGuid()), assignment, revision, context.Time.GetUtcNow()), null);
            var state = await context.Services.B1AuthorityRepository.LoadProjectStateAsync(new(result.Project.Id));
            var attempt = state.Attempts.Single().AttemptRef;
            var handoff = await context.Services.GuidedHandoffComposer.RecordAsync(
                attempt,
                assignment,
                new GuidedHandoffRequest(new(result.Project.Id), principal, "Prototype completed", [], [], ["Use card-based combat"], null, []));
            return new Fixture(context, folder, result, principal, handoff.HandoffRef);
        }

        public async Task<long> CountAsync(string table)
        {
            await using var connection = Services.Database.CreateConnection();
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE project_id=$project;";
            command.Parameters.AddWithValue("$project", Result.Project.Id.ToString());
            return (long)(await command.ExecuteScalarAsync())!;
        }

        public ValueTask DisposeAsync()
        {
            _folder.Dispose();
            return _context.DisposeAsync();
        }
    }
}
