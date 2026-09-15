using Workbench.App.ProjectWorld;
using Workbench.App.ViewModels;
using Workbench.App.Services;
using Workbench.App.Tests.Support;
using Workbench.Core.Continuity;
using Workbench.Core.Tasks;
using Workbench.Core.Workers;
using Workbench.Project.Opening;
using Workbench.Storage.Projects;
using Workbench.Storage.Workers;

namespace Workbench.App.Tests;

public sealed class ProjectWorldExplorerViewModelTests
{
    [Theory]
    [InlineData(AssignmentDisposition.Accepted, "Overview.Accepted")]
    [InlineData(AssignmentDisposition.Rejected, "Overview.Rejected")]
    [InlineData(AssignmentDisposition.RevisionRequired, "Overview.NeedsRevision")]
    public void Execution_label_uses_authority_disposition_after_completion(AssignmentDisposition disposition, string key)
    {
        var execution = new AgentExecutionItemView(Guid.NewGuid(), "Counter", WorkerExecutionState.CompletedPendingReview,
            DateTimeOffset.UtcNow, disposition);
        Assert.Equal(LocalizationService.Current[key], execution.StateText);
    }

    [Fact]
    public async Task Overview_prioritizes_live_work_over_newer_completed_work()
    {
        await using var fixture = await Fixture.CreateAsync();
        var explorer = new ProjectWorldExplorerViewModel(fixture.Services, fixture.OpenResult, () => Task.CompletedTask);
        explorer.AgentExecutions.Add(new(Guid.NewGuid(), "Finished", WorkerExecutionState.CompletedPendingReview, DateTimeOffset.UtcNow));
        explorer.AgentExecutions.Add(new(Guid.NewGuid(), "Still running", WorkerExecutionState.Running,
            DateTimeOffset.UtcNow.AddHours(-1), IsLive: true));
        Assert.Equal("Still running", explorer.CurrentWorkHeadline);
        Assert.Equal(LocalizationService.Current["Dynamic.Working"], explorer.CurrentWorkStatus);
    }

    [Fact]
    public async Task Overview_does_not_claim_a_persisted_running_execution_is_live_after_restart()
    {
        await using var fixture = await Fixture.CreateAsync();
        var explorer = new ProjectWorldExplorerViewModel(fixture.Services, fixture.OpenResult, () => Task.CompletedTask);
        explorer.AgentExecutions.Add(new(Guid.NewGuid(), "Old session", WorkerExecutionState.Running, DateTimeOffset.UtcNow));
        Assert.Equal(LocalizationService.Current["Overview.CheckExecution"], explorer.AgentStatusText);
        Assert.Equal(LocalizationService.Current["Overview.CheckExecution"], explorer.CurrentWorkStatus);
    }

    [Fact]
    public async Task Empty_project_world_is_shown_as_empty_without_fabricated_state()
    {
        await using var fixture = await Fixture.CreateAsync();
        var explorer = new ProjectWorldExplorerViewModel(fixture.Services, fixture.OpenResult, () => Task.CompletedTask);

        await explorer.InitializeAsync();

        Assert.False(explorer.HasAcceptedState);
        Assert.Empty(explorer.AcceptedState);
        Assert.Empty(explorer.ActiveWork);
        Assert.Contains("No accepted", explorer.ProjectStateLabel);
        Assert.Contains("first task", explorer.ProjectPulseText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0", explorer.AcceptedStatementCountText);
        Assert.Single(explorer.NeedsAttention);
    }

    [Fact]
    public async Task Explorer_shows_accepted_state_assignment_and_decision_after_initialization()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Services.ProjectWorldInitialization.CommitAsync(new ProjectWorldInitializationRequest(
            new(fixture.OpenResult.Project.Id), fixture.Principal, RoleKind.Worker,
            "Own gameplay implementation", "A clear first playable change", "Design the first combat prototype"));
        var explorer = new ProjectWorldExplorerViewModel(fixture.Services, fixture.OpenResult, () => Task.CompletedTask);

        await explorer.InitializeAsync();

        Assert.False(explorer.HasAcceptedState);
        Assert.Empty(explorer.AcceptedState);
        Assert.Single(explorer.ActiveWork);
        Assert.Single(explorer.RecentDecisions);
        Assert.Contains("Assignment + Revision", explorer.RecentDecisions[0].EffectsText);
        Assert.Equal("Design the first combat prototype", explorer.CurrentWorkHeadline);
    }

    [Fact]
    public async Task Overview_does_not_promote_summary_context_to_a_recent_accepted_change()
    {
        await using var fixture = await Fixture.CreateAsync();
        var explorer = new ProjectWorldExplorerViewModel(fixture.Services, fixture.OpenResult, () => Task.CompletedTask);
        await explorer.InitializeAsync();
        explorer.RecentSummaries.Add(new(DateTimeOffset.UtcNow,
            Workbench.Storage.Memory.SummaryDeltaKind.Change, "Unaccepted proposal", []));

        Assert.Equal(LocalizationService.Current["Overview.NoRecentChange"], explorer.RecentChangeText);
    }

    [Fact]
    public async Task Explorer_exposes_deterministic_project_evolution_entries()
    {
        await using var fixture = await Fixture.CreateAsync();
        var now = fixture.Services.TimeProvider.GetUtcNow();
        var libraryObject = await fixture.Services.ProjectLibraryEvolutionRepository.CreateObjectAsync(
            fixture.OpenResult.Project.Id, "Chapter", "Chapter 1", now);
        await fixture.Services.ProjectLibraryEvolutionRepository.AddNodeAsync(
            fixture.OpenResult.Project.Id, libraryObject.Id, new DateOnly(2026, 9, 6), "Chapter completed", [], now);

        var explorer = new ProjectWorldExplorerViewModel(fixture.Services, fixture.OpenResult, () => Task.CompletedTask);
        await explorer.InitializeAsync();

        Assert.Contains(explorer.EvolutionEntries, entry => entry.Category == ProjectEvolutionCategory.Library &&
            entry.Summary.Contains("Chapter completed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Explorer_shows_only_current_assignments_after_successor_replacement()
    {
        await using var fixture = await Fixture.CreateAsync();
        var projectRef = new ProjectRef(fixture.OpenResult.Project.Id);
        await fixture.Services.ProjectWorldInitialization.CommitAsync(new ProjectWorldInitializationRequest(
            projectRef,
            fixture.Principal,
            RoleKind.Worker,
            "Own gameplay implementation",
            "A clear first playable change",
            "Design the first combat prototype"));

        var before = await fixture.Services.B1AuthorityRepository.LoadProjectStateAsync(projectRef);
        var replaced = Assert.Single(B1Projector.Build(before).AcceptedProjectState.CurrentDelegationAssignments);
        var projection = B1Projector.Build(before).AcceptedProjectState;
        var assignment = projection.Assignments[replaced];
        await fixture.Services.B1AuthorityCommands.DelegateAssignmentAsync(
            new DelegateAssignmentCommand(
                projectRef,
                fixture.Principal,
                new DecidingAuthorityRef.UserPrincipal(fixture.Principal),
                new AssignmentDelegationInstruction(
                    new ResponsibilityTarget.Existing(assignment.ResponsibilityRef),
                    new AssignmentAssigneeTarget.Existing(assignment.AssigneeActorRef),
                    new AssignmentRevisionContract("Continue with the next bounded change"),
                    replaced),
                null,
                [],
                []));

        var explorer = new ProjectWorldExplorerViewModel(fixture.Services, fixture.OpenResult, () => Task.CompletedTask);
        await explorer.InitializeAsync();

        Assert.Single(explorer.ActiveWork);
        Assert.DoesNotContain(explorer.ActiveWork, item => item.AssignmentRef == replaced);
    }

    [Fact]
    public async Task Explorer_shows_persisted_agent_execution_state_separately_from_assignments()
    {
        await using var fixture = await Fixture.CreateAsync();
        var projectRef = new ProjectRef(fixture.OpenResult.Project.Id);
        await fixture.Services.ProjectWorldInitialization.CommitAsync(new ProjectWorldInitializationRequest(
            projectRef,
            fixture.Principal,
            RoleKind.Worker,
            "Own gameplay implementation",
            "A clear first playable change",
            "Design the first combat prototype"));

        var profile = ExecutionProfile.Create(
            "fake-provider",
            Guid.NewGuid().ToString(),
            "model-a",
            "fake-runtime");
        var revision = new TaskRevision(
            Guid.NewGuid(),
            1,
            "Implement the first playable slice",
            "Project code",
            "Unrelated files",
            ["The slice is implemented."],
            TaskRiskLevel.Low,
            profile,
            "Explorer test",
            TaskRevisionApprover.User,
            fixture.Services.TimeProvider.GetUtcNow(),
            null);
        await fixture.Services.TaskRepository.CreateAsync(
            fixture.OpenResult.Project.Id,
            new TaskDraft(
                revision.TaskId,
                "First playable slice",
                revision.Goal,
                revision.Scope,
                revision.OutOfScope,
                revision.Acceptance,
                revision.RiskLevel,
                profile,
                revision.CreatedAt,
                revision));
        await fixture.Services.WorkerExecutionRepository.CreateAsync(new StoredWorkerExecution(
            Guid.NewGuid(),
            fixture.OpenResult.Project.Id,
            revision.TaskId,
            revision.CreateReference(),
            revision.CreateReference(),
            "unversioned",
            "worktree",
            ProviderAccountBinding.Create(profile.ProviderId, profile.ProviderAccountId),
            profile,
            "worker/explorer",
            fixture.OpenResult.Project.RootPath,
            WorkerExecutionState.CompletedPendingReview,
            null,
            null,
            null,
            fixture.Services.TimeProvider.GetUtcNow(),
            fixture.Services.TimeProvider.GetUtcNow()));

        var explorer = new ProjectWorldExplorerViewModel(
            fixture.Services,
            fixture.OpenResult,
            () => Task.CompletedTask);
        await explorer.InitializeAsync();

        var execution = Assert.Single(explorer.AgentExecutions);
        Assert.Equal("First playable slice", execution.TaskText);
        Assert.Contains("decision", execution.StateText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1", explorer.AgentExecutionSummary, StringComparison.Ordinal);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly AppTestContext _context;
        private readonly TemporaryDirectory _folder;
        private Fixture(AppTestContext context, TemporaryDirectory folder, ProjectOpenResult openResult, UserPrincipalRef principal)
        {
            _context = context;
            _folder = folder;
            Services = context.Services;
            OpenResult = openResult;
            Principal = principal;
        }

        public AppServices Services { get; }
        public ProjectOpenResult OpenResult { get; }
        public UserPrincipalRef Principal { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var context = await AppTestContext.CreateAsync();
            var folder = new TemporaryDirectory("explorer-project");
            var openResult = await context.Services.ProjectOpenService.OpenAsync(folder.Path);
            var principal = new UserPrincipalRef("user:explorer-test");
            await context.Services.B1ProjectGovernance.CreateGovernedProjectForExistingProjectAsync(
                new ProjectRef(openResult.Project.Id), principal);
            return new Fixture(context, folder, openResult, principal);
        }

        public ValueTask DisposeAsync()
        {
            _folder.Dispose();
            return _context.DisposeAsync();
        }
    }
}
