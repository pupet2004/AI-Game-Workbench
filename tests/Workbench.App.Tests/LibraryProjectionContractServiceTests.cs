using Workbench.App.Continuity;
using Workbench.App.Memory;
using Workbench.Core.Continuity;
using Workbench.Core.Projects;
using Workbench.Storage.Continuity;
using Workbench.Storage.Database;
using Workbench.Storage.Memory;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.Tests;

public sealed class LibraryProjectionContractServiceTests
{
    private static readonly DateTimeOffset At =
        DateTimeOffset.Parse("2026-08-25T10:00:00.0000000+00:00");

    [Fact]
    public async Task Valid_projection_requires_decision_and_contribution_and_adds_trace_materials_without_writing_library()
    {
        await using var fixture = await Fixture.CreateAsync();
        var decision = await fixture.AuthorContributionAsync("Enemy scaling is dynamic.");
        var libraryObject = await fixture.Library.CreateObjectAsync(
            fixture.ProjectRef.Value, "Design", "Enemy Scaling", At);
        var before = await fixture.Library.ListObjectsAsync(fixture.ProjectRef.Value);

        var validated = await fixture.Contracts.ValidateAsync(new(
            Guid.NewGuid(),
            fixture.ProjectRef,
            LibraryProposalAction.UpdateNode,
            libraryObject.Id,
            (await fixture.Library.AddNodeAsync(
                fixture.ProjectRef.Value,
                libraryObject.Id,
                DateOnly.FromDateTime(At.UtcDateTime),
                "Dynamic scaling by progression.",
                [],
                At)).Id,
            1,
            0,
            "Design",
            "Enemy Scaling",
            DateOnly.FromDateTime(At.UtcDateTime),
            "Dynamic scaling by progression.",
            "Enemy scaling is dynamic.",
            new(
                decision.Decision.DecisionRef,
                decision.Contribution.ContributionRef,
                null,
                null,
                "summary:2026-08-25",
                [new("GitCommit", "abc123", "Implementation evidence")]),
            At));

        Assert.Equal(decision.Decision.DecisionRef, validated.Decision.DecisionRef);
        Assert.Equal(decision.Contribution.ContributionRef, validated.Contribution.ContributionRef);
        Assert.Contains(validated.MaterialReferences, value => value.MaterialKind == "AuthorityDecision");
        Assert.Contains(validated.MaterialReferences, value => value.MaterialKind == "AcceptedContribution");
        Assert.Contains(validated.MaterialReferences, value => value.MaterialKind == "Summary");
        Assert.Equal(before, await fixture.Library.ListObjectsAsync(fixture.ProjectRef.Value));
    }

    [Fact]
    public async Task Unknown_decision_is_rejected_before_any_library_write()
    {
        await using var fixture = await Fixture.CreateAsync();
        var before = await fixture.Library.ListObjectsAsync(fixture.ProjectRef.Value);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Contracts.ValidateAsync(new(
                Guid.NewGuid(),
                fixture.ProjectRef,
                LibraryProposalAction.CreateNode,
                null,
                null,
                null,
                0,
                "Design",
                "Enemy Scaling",
                DateOnly.FromDateTime(At.UtcDateTime),
                "Unattributed state.",
                null,
                new(
                    new AuthorityDecisionRef(Guid.NewGuid()),
                    new AcceptedStateContributionRef(Guid.NewGuid()),
                    null,
                    null,
                    null,
                    []),
                At)));

        Assert.Contains("AuthorityDecision", exception.Message, StringComparison.Ordinal);
        Assert.Equal(before, await fixture.Library.ListObjectsAsync(fixture.ProjectRef.Value));
    }

    [Fact]
    public async Task Cross_project_object_is_rejected_instead_of_guessing_or_relabeling_it()
    {
        await using var fixture = await Fixture.CreateAsync();
        var decision = await fixture.AuthorContributionAsync("Project-level policy.");
        var otherProject = await fixture.CreateOtherProjectAsync();
        var otherObject = await fixture.Library.CreateObjectAsync(
            otherProject.Value, "Design", "Other", At);
        var otherNode = await fixture.Library.AddNodeAsync(
            otherProject.Value,
            otherObject.Id,
            DateOnly.FromDateTime(At.UtcDateTime),
            "Other state.",
            [],
            At);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Contracts.ValidateAsync(new(
                Guid.NewGuid(),
                fixture.ProjectRef,
                LibraryProposalAction.UpdateNode,
                otherObject.Id,
                otherNode.Id,
                1,
                0,
                "Design",
                "Other",
                DateOnly.FromDateTime(At.UtcDateTime),
                "Cross-project state.",
                null,
                new(
                    decision.Decision.DecisionRef,
                    decision.Contribution.ContributionRef,
                    null,
                    null,
                    null,
                    []),
                At)));

        Assert.Contains("owned by this project", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Manual_b1_projection_proposal_has_no_fake_session_and_keeps_authority_provenance()
    {
        await using var fixture = await Fixture.CreateAsync();
        var decision = await fixture.AuthorContributionAsync("Manual accepted state.");

        var proposal = await fixture.Contracts.CreateProposalAsync(new(
            Guid.NewGuid(),
            fixture.ProjectRef,
            LibraryProposalAction.CreateNode,
            null,
            null,
            null,
            0,
            "Design",
            "Manual State",
            DateOnly.FromDateTime(At.UtcDateTime),
            "Manual accepted state.",
            "Current manual state.",
            new(
                decision.Decision.DecisionRef,
                decision.Contribution.ContributionRef,
                null,
                null,
                "summary:manual",
                []),
            At));

        Assert.Null(proposal.SourceSessionId);
        Assert.Equal(decision.Decision.DecisionRef.Value, proposal.Draft.AuthorityDecisionId);
        Assert.Equal(decision.Contribution.ContributionRef.Value, proposal.Draft.AcceptedContributionId);
        Assert.Contains(proposal.Draft.Materials, value => value.MaterialKind == "AuthorityDecision");
        var restored = await fixture.Proposals.GetAsync(fixture.ProjectRef.Value, proposal.Id);
        Assert.NotNull(restored);
        Assert.Equal(proposal.Id, restored.Id);
        Assert.Equal(proposal.SourceSessionId, restored.SourceSessionId);
        Assert.Equal(proposal.Draft.AuthorityDecisionId, restored.Draft.AuthorityDecisionId);
        Assert.Equal(proposal.Draft.AcceptedContributionId, restored.Draft.AcceptedContributionId);
    }

    private sealed record ContributionSeed(AuthorityDecision Decision, AcceptedStateContribution Contribution);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _directory;
        private readonly WorkbenchDatabase _database;
        private readonly B1AuthorityCommandService _commands;
        private readonly ProjectLibraryProposalService _proposalService;
        private readonly UserPrincipalRef _principal = new("user:library-projection-tests");

        private Fixture(
            string directory,
            WorkbenchDatabase database,
            ProjectRef projectRef,
            LibraryProjectionContractService contracts,
            ProjectLibraryEvolutionRepository library,
            B1AuthorityCommandService commands,
            ProjectLibraryProposalService proposalService)
        {
            _directory = directory;
            _database = database;
            ProjectRef = projectRef;
            Contracts = contracts;
            Library = library;
            _commands = commands;
            _proposalService = proposalService;
        }

        public ProjectRef ProjectRef { get; }
        public LibraryProjectionContractService Contracts { get; }
        public ProjectLibraryEvolutionRepository Library { get; }
        public ProjectLibraryProposalService Proposals => _proposalService;

        public static async Task<Fixture> CreateAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), "AI.Game.Workbench.App.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var database = new WorkbenchDatabase(Path.Combine(directory, "library-projection.db"));
            await database.InitializeAsync();
            var project = new CoreProject(
                Guid.NewGuid(),
                "Library projection",
                Path.Combine(directory, "project"),
                ProjectType.Godot,
                null,
                At,
                At);
            await new B1ProjectGovernanceRepository(database).CreateGovernedProjectAsync(
                project,
                new UserPrincipalRef("user:library-projection-tests"));
            var authority = new B1AuthorityRepository(database);
            var library = new ProjectLibraryEvolutionRepository(database);
            var proposals = new ProjectLibraryProposalService(database, TimeProvider.System);
            return new(
                directory,
                database,
                new ProjectRef(project.Id),
                new LibraryProjectionContractService(authority, library, proposals),
                library,
                new B1AuthorityCommandService(authority, new B1AuthorityEvaluator(), TimeProvider.System),
                proposals);
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

        public async Task<ProjectRef> CreateOtherProjectAsync()
        {
            var project = new CoreProject(
                Guid.NewGuid(),
                "Other",
                Path.Combine(_directory, "other"),
                ProjectType.Godot,
                null,
                At,
                At);
            await new B1ProjectGovernanceRepository(_database).CreateGovernedProjectAsync(project, _principal);
            return new ProjectRef(project.Id);
        }

        public ValueTask DisposeAsync()
        {
            try { Directory.Delete(_directory, true); } catch { }
            return ValueTask.CompletedTask;
        }
    }
}
