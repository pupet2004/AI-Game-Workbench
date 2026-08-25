using Workbench.App.Continuity;
using Workbench.App.Memory;
using Workbench.Core.Continuity;
using Workbench.Core.Projects;
using Workbench.Storage.Continuity;
using Workbench.Storage.Database;
using Workbench.Storage.Memory;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.Tests;

public sealed class LibraryProjectionRecoveryTests
{
    private static readonly DateTimeOffset At =
        DateTimeOffset.Parse("2026-08-25T12:00:00.0000000+00:00");

    [Fact]
    public async Task Removing_library_projection_does_not_change_recovered_b1_state()
    {
        await using var fixture = await Fixture.CreateAsync();
        var accepted = await fixture.AuthorContributionAsync("Combat uses progression scaling.");
        var libraryObject = await fixture.Library.CreateObjectAsync(
            fixture.ProjectRef.Value, "Design", "Combat", At);
        var node = await fixture.Library.AddNodeAsync(
            fixture.ProjectRef.Value,
            libraryObject.Id,
            DateOnly.FromDateTime(At.UtcDateTime),
            "Combat scaling projection.",
            [
                new(LibraryProjectionMaterialKinds.AuthorityDecision, accepted.DecisionRef.Value.ToString(), "B1 decision"),
                new(LibraryProjectionMaterialKinds.AcceptedContribution, accepted.ContributionRef.Value.ToString(), "Accepted contribution")
            ],
            At);

        var before = await fixture.ReadAcceptedStateAsync();
        await fixture.DeleteLibraryProjectionAsync(node.Id, libraryObject.Id);

        var recovered = await fixture.ReadAcceptedStateAfterRestartAsync();
        Assert.Equal(before.CurrentContributions, recovered.CurrentContributions);
        Assert.Contains(recovered.CurrentContributions, value =>
            value.ContributionRef == accepted.ContributionRef &&
            value.AuthorityDecisionRef == accepted.DecisionRef);

        var library = await fixture.ReadLibraryAfterRestartAsync();
        var projectLevel = Assert.Single(library.ProjectLevelDecisions);
        Assert.Equal(accepted.DecisionRef, projectLevel.Decision.DecisionRef);
        Assert.Empty(projectLevel.LibraryProjections);
    }

    [Fact]
    public async Task Restart_preserves_library_identity_and_explicit_authority_references()
    {
        await using var fixture = await Fixture.CreateAsync();
        var accepted = await fixture.AuthorContributionAsync("Enemy scaling is accepted.");
        var libraryObject = await fixture.Library.CreateObjectAsync(
            fixture.ProjectRef.Value, "Design", "Enemy Scaling", At);
        var node = await fixture.Library.AddNodeAsync(
            fixture.ProjectRef.Value,
            libraryObject.Id,
            DateOnly.FromDateTime(At.UtcDateTime),
            "Accepted scaling rule.",
            [
                new(LibraryProjectionMaterialKinds.AuthorityDecision, accepted.DecisionRef.Value.ToString(), null),
                new(LibraryProjectionMaterialKinds.AcceptedContribution, accepted.ContributionRef.Value.ToString(), null)
            ],
            At);

        var restarted = await fixture.ReadLibraryAfterRestartAsync();
        var projection = Assert.Single(
            Assert.Single(restarted.CurrentContributions).LibraryProjections);

        Assert.Equal(libraryObject.Id, projection.Object.Id);
        Assert.Equal(node.Id, projection.Node.Id);
        Assert.Equal(accepted.DecisionRef, projection.AuthorityDecisionRef);
        Assert.Equal(accepted.ContributionRef, projection.ContributionRef);
        Assert.Equal("Accepted scaling rule.", projection.Node.Content);
    }

    [Fact]
    public async Task Stale_library_write_does_not_change_b1_state()
    {
        await using var fixture = await Fixture.CreateAsync();
        var accepted = await fixture.AuthorContributionAsync("Project policy remains explicit.");
        var libraryObject = await fixture.Library.CreateObjectAsync(
            fixture.ProjectRef.Value, "Policy", "Project", At);
        var node = await fixture.Library.AddNodeAsync(
            fixture.ProjectRef.Value,
            libraryObject.Id,
            DateOnly.FromDateTime(At.UtcDateTime),
            "Original projection.",
            [
                new(LibraryProjectionMaterialKinds.AuthorityDecision, accepted.DecisionRef.Value.ToString(), null),
                new(LibraryProjectionMaterialKinds.AcceptedContribution, accepted.ContributionRef.Value.ToString(), null)
            ],
            At);

        var proposal = await fixture.Proposals.CreateProposalAsync(new ProjectLibraryProposalDraft(
            Guid.NewGuid(),
            fixture.ProjectRef.Value,
            null,
            LibraryProposalAction.UpdateNode,
            libraryObject.Id,
            node.Id,
            1,
            0,
            "Policy",
            "Project",
            DateOnly.FromDateTime(At.UtcDateTime),
            "Stale projection.",
            null,
            [
                new(LibraryProjectionMaterialKinds.AuthorityDecision, accepted.DecisionRef.Value.ToString(), null),
                new(LibraryProjectionMaterialKinds.AcceptedContribution, accepted.ContributionRef.Value.ToString(), null)
            ],
            At,
            accepted.DecisionRef.Value,
            accepted.ContributionRef.Value));

        await fixture.Library.UpdateNodeAsync(
            fixture.ProjectRef.Value,
            node.Id,
            "Newer projection.",
            1,
            [],
            At.AddMinutes(1));

        await Assert.ThrowsAsync<LibraryRevisionConflictException>(() =>
            fixture.Proposals.AcceptAsync(fixture.ProjectRef.Value, proposal.Id));

        var recovered = await fixture.ReadAcceptedStateAsync();
        var current = Assert.Single(recovered.CurrentContributions);
        Assert.Equal(accepted.ContributionRef, current.ContributionRef);
        Assert.Equal("Project policy remains explicit.", current.Statement);
    }

    private sealed record AcceptedSeed(
        AuthorityDecisionRef DecisionRef,
        AcceptedStateContributionRef ContributionRef);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _directory;
        private readonly WorkbenchDatabase _database;
        private readonly UserPrincipalRef _principal = new("user:library-recovery-tests");
        private readonly B1AuthorityCommandService _commands;

        private Fixture(
            string directory,
            WorkbenchDatabase database,
            ProjectRef projectRef,
            ProjectLibraryEvolutionRepository library,
            ProjectLibraryProposalService proposals,
            B1AuthorityCommandService commands)
        {
            _directory = directory;
            _database = database;
            ProjectRef = projectRef;
            Library = library;
            Proposals = proposals;
            _commands = commands;
        }

        public ProjectRef ProjectRef { get; }
        public ProjectLibraryEvolutionRepository Library { get; }
        public ProjectLibraryProposalService Proposals { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "AI.Game.Workbench.App.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var database = new WorkbenchDatabase(Path.Combine(directory, "library-recovery.db"));
            await database.InitializeAsync();
            var project = new CoreProject(
                Guid.NewGuid(),
                "Library recovery",
                Path.Combine(directory, "project"),
                ProjectType.Godot,
                null,
                At,
                At);
            await new B1ProjectGovernanceRepository(database).CreateGovernedProjectAsync(project, new UserPrincipalRef("user:library-recovery-tests"));
            var authority = new B1AuthorityRepository(database);
            return new(
                directory,
                database,
                new ProjectRef(project.Id),
                new ProjectLibraryEvolutionRepository(database),
                new ProjectLibraryProposalService(database, TimeProvider.System),
                new B1AuthorityCommandService(authority, new B1AuthorityEvaluator(), TimeProvider.System));
        }

        public async Task<AcceptedSeed> AuthorContributionAsync(string statement)
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
            var contribution = Assert.Single(decision.AcceptedStateContributions);
            return new(decision.DecisionRef, contribution.ContributionRef);
        }

        public async Task<AcceptedProjectState> ReadAcceptedStateAsync()
        {
            var authority = new B1AuthorityRepository(_database);
            return await new B1ProjectionService(authority).GetAcceptedProjectStateAsync(ProjectRef);
        }

        public async Task<AcceptedProjectState> ReadAcceptedStateAfterRestartAsync()
        {
            var restarted = new WorkbenchDatabase(_database.DatabasePath);
            await restarted.InitializeAsync();
            return await new B1ProjectionService(new B1AuthorityRepository(restarted))
                .GetAcceptedProjectStateAsync(ProjectRef);
        }

        public async Task<LibraryAcceptedStateReadModel> ReadLibraryAfterRestartAsync()
        {
            var restarted = new WorkbenchDatabase(_database.DatabasePath);
            await restarted.InitializeAsync();
            return await new LibraryAcceptedStateReader(
                    new B1AuthorityRepository(restarted),
                    new ProjectLibraryEvolutionRepository(restarted))
                .ReadAsync(ProjectRef);
        }

        public async Task DeleteLibraryProjectionAsync(Guid nodeId, Guid objectId)
        {
            await using var connection = _database.CreateConnection();
            await connection.OpenAsync();
            await using var transaction = (Microsoft.Data.Sqlite.SqliteTransaction)await connection.BeginTransactionAsync();
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM project_library_material_refs WHERE node_id=$node; DELETE FROM project_library_timeline_nodes WHERE id=$node; DELETE FROM project_library_objects WHERE id=$object;";
            command.Parameters.AddWithValue("$node", nodeId.ToString());
            command.Parameters.AddWithValue("$object", objectId.ToString());
            await command.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }

        public ValueTask DisposeAsync()
        {
            try { Directory.Delete(_directory, true); } catch { }
            return ValueTask.CompletedTask;
        }
    }
}
