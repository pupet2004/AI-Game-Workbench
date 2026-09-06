using Workbench.App.ProjectWorld;
using Workbench.App.ViewModels;
using Workbench.App.Services;
using Workbench.App.Tests.Support;
using Workbench.Core.Continuity;
using Workbench.Project.Opening;
using Workbench.Storage.Projects;

namespace Workbench.App.Tests;

public sealed class ProjectWorldExplorerViewModelTests
{
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
