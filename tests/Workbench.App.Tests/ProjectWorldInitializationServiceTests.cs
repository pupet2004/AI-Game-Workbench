using Workbench.App.ProjectWorld;
using Workbench.Core.Continuity;
using Workbench.Core.Projects;
using Workbench.Storage.Continuity;
using Workbench.Storage.Database;
using Workbench.Storage.Projects;
using Workbench.App.Tests.Support;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.Tests;

public sealed class ProjectWorldInitializationServiceTests
{
    [Fact]
    public async Task Preview_is_non_persistent_and_commit_creates_one_atomic_initial_world()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = fixture.Request;

        var preview = await fixture.Initialization.PreviewAsync(request);

        Assert.Contains("one LogicalActor", preview.EffectsSummary);
        Assert.Equal(0L, await fixture.CountAsync("b1_authority_decisions"));
        Assert.Equal(0L, await fixture.CountAsync("b1_logical_actors"));
        Assert.Equal(0L, await fixture.CountAsync("b1_responsibilities"));
        Assert.Equal(0L, await fixture.CountAsync("b1_assignments"));

        var decision = await fixture.Initialization.CommitAsync(request);

        Assert.NotNull(decision.LogicalActorEstablishmentEffect);
        Assert.NotNull(decision.ResponsibilityEstablishmentEffect);
        Assert.NotNull(decision.AssignmentDelegationEffect);
        Assert.Equal(1L, await fixture.CountAsync("b1_authority_decisions"));
        Assert.Equal(1L, await fixture.CountAsync("b1_logical_actors"));
        Assert.Equal(1L, await fixture.CountAsync("b1_responsibilities"));
        Assert.Equal(1L, await fixture.CountAsync("b1_assignments"));
        Assert.Equal(1L, await fixture.CountAsync("b1_revisions"));
        Assert.Equal(0L, await fixture.CountAsync("b1_attempts"));
        Assert.Equal(0L, await fixture.CountAsync("b1_claims"));
        Assert.Equal(0L, await fixture.CountAsync("b1_handoffs"));
    }

    [Fact]
    public async Task Preview_rejects_a_world_that_already_has_authority_history()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Initialization.CommitAsync(fixture.Request);

        var exception = await Assert.ThrowsAsync<B1CommandException>(() =>
            fixture.Initialization.PreviewAsync(fixture.Request with { InitialAssignment = "Another assignment" }));

        Assert.Equal(B1FailureCode.InvalidDecisionShape, exception.Code);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly TemporaryDirectory _directory;
        private readonly WorkbenchDatabase _database;

        private Fixture(
            TemporaryDirectory directory,
            WorkbenchDatabase database,
            ProjectWorldInitializationService initialization,
            ProjectWorldInitializationRequest request,
            ProjectRepository projects)
        {
            _directory = directory;
            _database = database;
            Initialization = initialization;
            Request = request;
            Projects = projects;
        }

        public ProjectWorldInitializationService Initialization { get; }
        public ProjectWorldInitializationRequest Request { get; }
        public ProjectRepository Projects { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var directory = new TemporaryDirectory("project-world-init-db");
            var database = new WorkbenchDatabase(Path.Combine(directory.Path, "workbench.db"));
            await database.InitializeAsync();
            var project = new CoreProject(
                Guid.NewGuid(), "Initialization project", directory.Path, ProjectType.Godot, null,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            var projects = new ProjectRepository(database);
            await projects.UpsertAsync(project);
            var principal = new UserPrincipalRef("user:initialization-test");
            var governance = new B1ProjectGovernanceRepository(database);
            await governance.CreateGovernedProjectForExistingProjectAsync(new ProjectRef(project.Id), principal);
            var authorityRepository = new B1AuthorityRepository(database);
            var authorityCommands = new Workbench.App.Continuity.B1AuthorityCommandService(
                authorityRepository, new B1AuthorityEvaluator(), TimeProvider.System);
            return new Fixture(
                directory,
                database,
                new ProjectWorldInitializationService(authorityRepository, authorityCommands, governance),
                new ProjectWorldInitializationRequest(
                    new ProjectRef(project.Id), principal, RoleKind.Worker,
                    "Own gameplay implementation", "A clear first playable change", "Design the first combat prototype"),
                projects);
        }

        public async Task<long> CountAsync(string table)
        {
            await using var connection = _database.CreateConnection();
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table};";
            return (long)(await command.ExecuteScalarAsync())!;
        }

        public ValueTask DisposeAsync()
        {
            _directory.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
