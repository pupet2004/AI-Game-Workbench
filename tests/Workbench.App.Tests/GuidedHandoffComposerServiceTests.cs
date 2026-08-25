using Workbench.App.ProjectWorld;
using Workbench.App.Services;
using Workbench.App.Tests.Support;
using Workbench.Core.Continuity;
using Workbench.Project.Opening;

namespace Workbench.App.Tests;

public sealed class GuidedHandoffComposerServiceTests
{
    [Fact]
    public async Task Composer_records_typed_claims_and_one_selected_handoff_without_authority_change()
    {
        await using var fixture = await Fixture.CreateAsync();
        var before = await fixture.Services.B1Projections.GetProjectProjectionAsync(
            new ProjectRef(fixture.Result.Project.Id));
        var handoff = await fixture.Services.GuidedHandoffComposer.RecordAsync(
            fixture.Attempt,
            fixture.Assignment,
            new GuidedHandoffRequest(
                new(fixture.Result.Project.Id),
                fixture.Principal,
                "Combat prototype completed",
                ["Manual playtest completed"],
                ["Enemy scaling still needs review"],
                ["Use card-based combat for the first prototype"],
                "Tune enemy scaling after the first playtest",
                ["file:combat-design.md"]));

        Assert.NotEqual(default, handoff.HandoffRef);
        Assert.Equal(1L, await fixture.CountAsync("b1_handoffs"));
        Assert.Equal(5L, await fixture.CountAsync("b1_claims"));
        Assert.Equal(1L, await fixture.CountAsync("b1_attempt_routing"));
        Assert.Equal(1L, await fixture.CountAsync("b1_assignment_routing"));
        Assert.Equal(1L, await fixture.CountAsync("b1_authority_decisions"));
        var after = await fixture.Services.B1Projections.GetProjectProjectionAsync(
            new ProjectRef(fixture.Result.Project.Id));
        Assert.Equal(before.AcceptedProjectState.LogicalActors.Keys, after.AcceptedProjectState.LogicalActors.Keys);
        Assert.Equal(before.AcceptedProjectState.Responsibilities.Keys, after.AcceptedProjectState.Responsibilities.Keys);
        Assert.Equal(before.AcceptedProjectState.Assignments.Keys, after.AcceptedProjectState.Assignments.Keys);
        Assert.Equal(before.AcceptedProjectState.CurrentContributions.Count, after.AcceptedProjectState.CurrentContributions.Count);
        Assert.Equal(handoff.HandoffRef.Value.ToString(), await fixture.SelectedHandoffAsync(fixture.Attempt));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly AppTestContext _context;
        private readonly TemporaryDirectory _folder;

        private Fixture(
            AppTestContext context,
            TemporaryDirectory folder,
            ProjectOpenResult result,
            UserPrincipalRef principal,
            AssignmentRef assignment,
            AttemptRef attempt)
        {
            _context = context;
            _folder = folder;
            Services = context.Services;
            Result = result;
            Principal = principal;
            Assignment = assignment;
            Attempt = attempt;
        }

        public AppServices Services { get; }
        public ProjectOpenResult Result { get; }
        public UserPrincipalRef Principal { get; }
        public AssignmentRef Assignment { get; }
        public AttemptRef Attempt { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var context = await AppTestContext.CreateAsync();
            var folder = new TemporaryDirectory("handoff-project");
            var result = await context.Services.ProjectOpenService.OpenAsync(folder.Path);
            var principal = context.Services.UserPrincipalProvider.GetCurrent();
            await context.Services.B1ProjectGovernance.CreateGovernedProjectForExistingProjectAsync(
                new(result.Project.Id), principal);
            var decision = await context.Services.ProjectWorldInitialization.CommitAsync(
                new ProjectWorldInitializationRequest(
                    new(result.Project.Id), principal, RoleKind.Worker,
                    "Own gameplay implementation", "A clear first playable change", "Design the first combat prototype"));
            var assignment = decision.AssignmentDelegationEffect!.Assignment.AssignmentRef;
            var revision = decision.AssignmentDelegationEffect.InitialRevision.RevisionRef;
            await context.Services.B1NonAuthoritativeCommands.CreateAttemptAndSelectAsync(
                new CreateAttemptCommand(
                    new(result.Project.Id), principal, new AttemptRef(Guid.NewGuid()), assignment, revision,
                    context.Time.GetUtcNow()),
                expectedStored: null);
            var state = await context.Services.B1AuthorityRepository.LoadProjectStateAsync(new(result.Project.Id));
            var attempt = state.Attempts.Single().AttemptRef;
            return new Fixture(context, folder, result, principal, assignment, attempt);
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

        public async Task<string?> SelectedHandoffAsync(AttemptRef attempt)
        {
            await using var connection = Services.Database.CreateConnection();
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = "SELECT selected_handoff_id FROM b1_attempt_routing WHERE attempt_id=$attempt;";
            command.Parameters.AddWithValue("$attempt", attempt.Value.ToString());
            return (string?)await command.ExecuteScalarAsync();
        }

        public ValueTask DisposeAsync()
        {
            _folder.Dispose();
            return _context.DisposeAsync();
        }
    }
}
