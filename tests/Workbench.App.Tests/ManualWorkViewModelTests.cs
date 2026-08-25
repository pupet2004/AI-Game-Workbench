using Workbench.App.ProjectWorld;
using Workbench.App.Services;
using Workbench.App.Tests.Support;
using Workbench.Core.Continuity;
using Workbench.Core.Projects;
using Workbench.Project.Opening;
using Workbench.Storage.Projects;

namespace Workbench.App.Tests;

public sealed class ManualWorkViewModelTests
{
    [Fact]
    public async Task Begin_manual_work_creates_and_selects_only_an_attempt()
    {
        await using var fixture = await Fixture.CreateAsync();
        var viewModel = fixture.CreateViewModel();

        await viewModel.InitializeAsync();
        Assert.False(viewModel.HasAttempt);

        await viewModel.BeginOrContinueCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasAttempt);
        Assert.Equal(1L, await fixture.CountAsync("b1_attempts"));
        Assert.Equal(1L, await fixture.CountAsync("b1_assignment_routing"));
        Assert.Equal(0L, await fixture.CountAsync("b1_session_bindings"));
        Assert.Equal(0L, await fixture.CountAsync("b1_claims"));
        Assert.Equal(0L, await fixture.CountAsync("b1_handoffs"));
        Assert.Equal(1L, await fixture.CountAsync("b1_authority_decisions"));
    }

    [Fact]
    public async Task Continue_manual_work_uses_the_explicit_selection_and_does_not_create_another_attempt()
    {
        await using var fixture = await Fixture.CreateAsync();
        var viewModel = fixture.CreateViewModel();
        await viewModel.InitializeAsync();
        await viewModel.BeginOrContinueCommand.ExecuteAsync(null);
        var firstAttempt = viewModel.AttemptText;

        await viewModel.BeginOrContinueCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasAttempt);
        Assert.Equal(firstAttempt, viewModel.AttemptText);
        Assert.Equal(1L, await fixture.CountAsync("b1_attempts"));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly AppTestContext _context;
        private readonly TemporaryDirectory _folder;
        private readonly AssignmentRef _assignment;

        private Fixture(AppTestContext context, TemporaryDirectory folder, ProjectOpenResult result, AssignmentRef assignment)
        {
            _context = context;
            _folder = folder;
            Services = context.Services;
            Result = result;
            _assignment = assignment;
        }

        public AppServices Services { get; }
        public ProjectOpenResult Result { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var context = await AppTestContext.CreateAsync();
            var folder = new TemporaryDirectory("manual-work-project");
            var result = await context.Services.ProjectOpenService.OpenAsync(folder.Path);
            var principal = context.Services.UserPrincipalProvider.GetCurrent();
            await context.Services.B1ProjectGovernance.CreateGovernedProjectForExistingProjectAsync(
                new ProjectRef(result.Project.Id), principal);
            var decision = await context.Services.ProjectWorldInitialization.CommitAsync(
                new ProjectWorldInitializationRequest(
                    new(result.Project.Id), principal, RoleKind.Worker,
                    "Own gameplay implementation", "A clear first playable change", "Design the first combat prototype"));
            return new Fixture(
                context,
                folder,
                result,
                decision.AssignmentDelegationEffect!.Assignment.AssignmentRef);
        }

        public ManualWorkViewModel CreateViewModel() =>
            new(Services, Result, _assignment, () => Task.CompletedTask);

        public async Task<long> CountAsync(string table)
        {
            await using var connection = Services.Database.CreateConnection();
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table};";
            return (long)(await command.ExecuteScalarAsync())!;
        }

        public ValueTask DisposeAsync()
        {
            _folder.Dispose();
            return _context.DisposeAsync();
        }
    }
}
