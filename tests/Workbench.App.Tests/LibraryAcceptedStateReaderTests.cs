using Workbench.App.Memory;
using Workbench.App.Continuity;
using Workbench.Core.Continuity;
using Workbench.Core.Projects;
using Workbench.Storage.Continuity;
using Workbench.Storage.Database;
using Workbench.Storage.Memory;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.Tests;

public sealed class LibraryAcceptedStateReaderTests
{
    private static readonly DateTimeOffset At =
        DateTimeOffset.Parse("2026-08-25T10:00:00.0000000+00:00");

    [Fact]
    public async Task Explicitly_linked_accepted_contribution_is_read_as_an_object_projection()
    {
        await using var fixture = await Fixture.CreateAsync();
        var decision = await fixture.AuthorContributionAsync("Enemy scaling is dynamic.");
        var libraryObject = await fixture.Library.CreateObjectAsync(
            fixture.ProjectRef.Value, "Design", "Enemy Scaling", At);
        var node = await fixture.Library.AddNodeAsync(
            fixture.ProjectRef.Value,
            libraryObject.Id,
            DateOnly.FromDateTime(At.UtcDateTime),
            "Dynamic scaling by progression.",
            [
                new(LibraryProjectionMaterialKinds.AuthorityDecision, decision.Decision.DecisionRef.Value.ToString(), null),
                new(LibraryProjectionMaterialKinds.AcceptedContribution, decision.Contribution.ContributionRef.Value.ToString(), null)
            ],
            At);

        var read = await fixture.Reader.ReadAsync(fixture.ProjectRef);

        var current = Assert.Single(read.CurrentContributions);
        var link = Assert.Single(current.LibraryProjections);
        Assert.Equal(libraryObject.Id, link.Object.Id);
        Assert.Equal(node.Id, link.Node.Id);
        Assert.Equal(decision.Decision.DecisionRef, link.AuthorityDecisionRef);
        Assert.Equal(decision.Contribution.ContributionRef, link.ContributionRef);
        Assert.Empty(read.ProjectLevelDecisions);
    }

    [Fact]
    public async Task Decision_without_explicit_object_mapping_remains_project_level()
    {
        await using var fixture = await Fixture.CreateAsync();
        var decision = await fixture.AuthorContributionAsync("Project-wide policy.");

        var read = await fixture.Reader.ReadAsync(fixture.ProjectRef);

        var projectLevel = Assert.Single(read.ProjectLevelDecisions);
        Assert.Equal(decision.Decision.DecisionRef, projectLevel.Decision.DecisionRef);
        Assert.Equal(decision.Contribution.ContributionRef, Assert.Single(projectLevel.Contributions).ContributionRef);
        Assert.Empty(projectLevel.LibraryProjections);
    }

    [Fact]
    public async Task Unrelated_legacy_style_materials_do_not_create_an_accepted_object_mapping()
    {
        await using var fixture = await Fixture.CreateAsync();
        var decision = await fixture.AuthorContributionAsync("Keep the old evidence readable.");
        var libraryObject = await fixture.Library.CreateObjectAsync(
            fixture.ProjectRef.Value, "Context", "Legacy", At);
        await fixture.Library.AddNodeAsync(
            fixture.ProjectRef.Value,
            libraryObject.Id,
            DateOnly.FromDateTime(At.UtcDateTime),
            "Legacy context remains readable.",
            [new("AgentSession", Guid.NewGuid().ToString(), "Legacy source")],
            At);

        var read = await fixture.Reader.ReadAsync(fixture.ProjectRef);

        Assert.Empty(Assert.Single(read.CurrentContributions).LibraryProjections);
        Assert.Contains(read.ProjectLevelDecisions, value => value.Decision.DecisionRef == decision.Decision.DecisionRef);
    }

    private sealed record ContributionSeed(AuthorityDecision Decision, AcceptedStateContribution Contribution);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _directory;
        private readonly WorkbenchDatabase _database;
        private readonly B1AuthorityCommandService _commands;
        private readonly UserPrincipalRef _principal = new("user:library-reader-tests");

        private Fixture(
            string directory,
            WorkbenchDatabase database,
            ProjectRef projectRef,
            LibraryAcceptedStateReader reader,
            ProjectLibraryEvolutionRepository library,
            B1AuthorityCommandService commands)
        {
            _directory = directory;
            _database = database;
            ProjectRef = projectRef;
            Reader = reader;
            Library = library;
            _commands = commands;
        }

        public ProjectRef ProjectRef { get; }
        public LibraryAcceptedStateReader Reader { get; }
        public ProjectLibraryEvolutionRepository Library { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), "AI.Game.Workbench.App.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var database = new WorkbenchDatabase(Path.Combine(directory, "library-reader.db"));
            await database.InitializeAsync();
            var project = new CoreProject(
                Guid.NewGuid(),
                "Library reader",
                Path.Combine(directory, "project"),
                ProjectType.Godot,
                null,
                At,
                At);
            await new B1ProjectGovernanceRepository(database).CreateGovernedProjectAsync(project, new UserPrincipalRef("user:library-reader-tests"));
            var authority = new B1AuthorityRepository(database);
            var library = new ProjectLibraryEvolutionRepository(database);
            return new(
                directory,
                database,
                new ProjectRef(project.Id),
                new LibraryAcceptedStateReader(authority, library),
                library,
                new B1AuthorityCommandService(authority, new B1AuthorityEvaluator(), TimeProvider.System));
        }

        public async Task<ContributionSeed> AuthorContributionAsync(string statement)
        {
            var decision = await _commands.AuthorAcceptedStateAsync(new AuthorAcceptedStateCommand(
                ProjectRef,
                _principal,
                new DecidingAuthorityRef.UserPrincipal(_principal),
                [],
                [new AcceptedContributionInstruction(
                    statement,
                    new ContributionScopeTarget.Project(ProjectRef),
                    null,
                    null)]));
            return new(decision, Assert.Single(decision.AcceptedStateContributions));
        }

        public ValueTask DisposeAsync()
        {
            try { Directory.Delete(_directory, true); } catch { }
            return ValueTask.CompletedTask;
        }
    }
}
