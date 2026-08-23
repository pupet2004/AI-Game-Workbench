using Workbench.App.Continuity;
using Workbench.Core.Continuity;
using Workbench.Core.Projects;
using Workbench.Storage.Continuity;
using Workbench.Storage.Database;
using Workbench.Storage.Leaders;
using Workbench.Storage.Memory;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.Tests.Continuity;

public sealed class B1ProjectionRecoveryTests
{
    [Fact]
    public async Task Restart_recovers_bootstrap_identities_contracts_and_decisions()
    {
        await using var fixture = await Fixture.CreateAsync();
        var seed = await fixture.SeedAsync();
        var contribution = await fixture.AuthorContributionAsync("Persist the release policy");

        var projection = await (await fixture.RestartProjectionAsync()).GetProjectProjectionAsync(fixture.ProjectRef);

        Assert.Equal(fixture.ProjectRef, projection.ProjectRef);
        Assert.Contains(seed.Actor, projection.AcceptedProjectState.LogicalActors.Keys);
        Assert.Contains(seed.Responsibility, projection.AcceptedProjectState.Responsibilities.Keys);
        Assert.Contains(seed.Assignment, projection.AcceptedProjectState.Assignments.Keys);
        Assert.Contains(seed.Revision, projection.AcceptedProjectState.Revisions.Keys);
        Assert.Contains(projection.AcceptedProjectState.CurrentContributions,
            value => value.ContributionRef == contribution && value.Statement == "Persist the release policy");
    }

    [Fact]
    public async Task Current_revision_and_single_disposition_rebuild_from_history()
    {
        await using var fixture = await Fixture.CreateAsync();
        var seed = await fixture.SeedAsync();
        var successor = await fixture.ActivateSuccessorAsync(seed);
        await fixture.DecideAsync(seed.Assignment, successor, AssignmentDisposition.Accepted);

        var projection = await (await fixture.RestartProjectionAsync()).GetProjectProjectionAsync(fixture.ProjectRef);

        Assert.Equal(successor, projection.AcceptedProjectState.CurrentEffectiveRevisionRefs[seed.Assignment]);
        var disposition = Assert.Single(projection.AcceptedProjectState.RevisionDispositions);
        Assert.Equal(successor, disposition.Key);
        Assert.Equal(AssignmentDisposition.Accepted, disposition.Value.Disposition);
    }

    [Fact]
    public async Task Current_delegation_and_effective_fulfillment_have_distinct_results()
    {
        await using var fixture = await Fixture.CreateAsync();
        var seed = await fixture.SeedAsync();
        await fixture.DecideAsync(seed.Assignment, seed.Revision, AssignmentDisposition.Accepted);

        var projection = await (await fixture.RestartProjectionAsync()).GetProjectProjectionAsync(fixture.ProjectRef);

        Assert.Contains(seed.Assignment, projection.AcceptedProjectState.CurrentDelegationAssignments);
        Assert.DoesNotContain(seed.Assignment, projection.EffectiveFulfillmentAssignments);
    }

    [Fact]
    public async Task Accepted_state_rebuilds_only_non_superseded_contributions()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.AuthorContributionAsync("Superseded position");
        var second = await fixture.AuthorContributionAsync("Current position", first);

        var accepted = await (await fixture.RestartProjectionAsync()).GetAcceptedProjectStateAsync(fixture.ProjectRef);

        var current = Assert.Single(accepted.CurrentContributions);
        Assert.Equal(second, current.ContributionRef);
        Assert.Equal("Current position", current.Statement);
    }

    [Fact]
    public async Task Deleting_no_cache_is_required_for_same_projection()
    {
        await using var fixture = await Fixture.CreateAsync();
        var seed = await fixture.SeedAsync();
        await fixture.AuthorContributionAsync("Durable only");

        var first = await (await fixture.RestartProjectionAsync()).GetProjectProjectionAsync(fixture.ProjectRef);
        var afterRestart = await (await fixture.RestartProjectionAsync()).GetProjectProjectionAsync(fixture.ProjectRef);

        Assert.Equal(first.ProjectRef, afterRestart.ProjectRef);
        Assert.Equal(
            first.AcceptedProjectState.CurrentContributions.Select(value => value.ContributionRef),
            afterRestart.AcceptedProjectState.CurrentContributions.Select(value => value.ContributionRef));
        Assert.Equal(
            first.AcceptedProjectState.CurrentEffectiveRevisionRefs,
            afterRestart.AcceptedProjectState.CurrentEffectiveRevisionRefs);
        Assert.Equal(seed.Revision, afterRestart.AcceptedProjectState.CurrentEffectiveRevisionRefs[seed.Assignment]);
    }

    [Fact]
    public async Task Stored_and_effective_routing_refs_both_survive_restart()
    {
        await using var fixture = await Fixture.CreateAsync();
        var seed = await fixture.SeedAsync();
        var routing = await fixture.AddSelectedRoutingAsync(seed);

        var projection = await (await fixture.RestartProjectionAsync()).GetProjectProjectionAsync(fixture.ProjectRef);

        Assert.Equal(routing.Attempt, projection.StoredAttemptSelections[seed.Assignment]);
        Assert.Equal(routing.Attempt, projection.EffectiveCurrentAttemptRefs[seed.Assignment]);
        Assert.Equal(routing.Handoff, projection.StoredHandoffSelections[routing.Attempt]);
        Assert.Equal(routing.Handoff, projection.EffectiveCurrentHandoffRefs[routing.Attempt]);
        Assert.Equal(routing.Binding, projection.StoredBindingSelections[routing.Attempt]);
        Assert.Equal(routing.Binding, projection.EffectiveCurrentBindingRefs[routing.Attempt]);
    }

    [Fact]
    public async Task Disposition_invalidates_effective_route_without_clearing_stored_refs()
    {
        await using var fixture = await Fixture.CreateAsync();
        var seed = await fixture.SeedAsync();
        var routing = await fixture.AddSelectedRoutingAsync(seed);
        await fixture.DecideAsync(seed.Assignment, seed.Revision, AssignmentDisposition.Accepted);

        var projection = await (await fixture.RestartProjectionAsync()).GetProjectProjectionAsync(fixture.ProjectRef);

        Assert.Equal(routing.Attempt, projection.StoredAttemptSelections[seed.Assignment]);
        Assert.Null(projection.EffectiveCurrentAttemptRefs[seed.Assignment]);
        Assert.Equal(routing.Handoff, projection.StoredHandoffSelections[routing.Attempt]);
        Assert.Null(projection.EffectiveCurrentHandoffRefs[routing.Attempt]);
        Assert.Equal(routing.Binding, projection.StoredBindingSelections[routing.Attempt]);
        Assert.Null(projection.EffectiveCurrentBindingRefs[routing.Attempt]);
    }

    [Fact]
    public async Task Projection_reads_no_summary_transcript_runtime_or_legacy_state()
    {
        await using var fixture = await Fixture.CreateAsync();
        var seed = await fixture.SeedAsync();
        await fixture.SeedIrrelevantLegacyRecordsAsync();

        var projection = await (await fixture.RestartProjectionAsync()).GetProjectProjectionAsync(fixture.ProjectRef);

        Assert.Equal(fixture.ProjectRef, projection.ProjectRef);
        Assert.Equal(seed.Revision, projection.AcceptedProjectState.CurrentEffectiveRevisionRefs[seed.Assignment]);
        Assert.Empty(projection.AcceptedProjectState.CurrentContributions);
        Assert.DoesNotContain(projection.AcceptedProjectState.CurrentContributions,
            value => value.Statement.Contains("LEGACY_", StringComparison.Ordinal));
    }

    private sealed record Seed(LogicalActorRef Actor, ResponsibilityRef Responsibility,
        AssignmentRef Assignment, RevisionRef Revision);

    private sealed record Routing(AttemptRef Attempt, HandoffRef Handoff, SessionBindingRef Binding);

    private sealed class Fixture : IAsyncDisposable
    {
        private static readonly DateTimeOffset At =
            DateTimeOffset.Parse("2026-08-22T12:00:00.0000000+00:00");

        private readonly string _directory;
        private readonly B1AuthorityCommandService _commands;
        private readonly B1NonAuthoritativeCommandService _nonAuthoritative;

        private Fixture(string directory, WorkbenchDatabase database, ProjectRef projectRef,
            UserPrincipalRef principalRef,
            B1AuthorityCommandService commands, B1NonAuthoritativeCommandService nonAuthoritative)
        {
            _directory = directory;
            Database = database;
            ProjectRef = projectRef;
            PrincipalRef = principalRef;
            _commands = commands;
            _nonAuthoritative = nonAuthoritative;
        }

        public WorkbenchDatabase Database { get; }
        public ProjectRef ProjectRef { get; }
        public UserPrincipalRef PrincipalRef { get; }
        private DecidingAuthorityRef BootstrapAuthority => new DecidingAuthorityRef.UserPrincipal(PrincipalRef);

        public static async Task<Fixture> CreateAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), "AI.Game.Workbench.App.Tests", Guid.NewGuid().ToString("N"));
            var database = new WorkbenchDatabase(Path.Combine(directory, "projection-recovery.db"));
            await database.InitializeAsync();
            var projectId = Guid.NewGuid();
            var principal = new UserPrincipalRef("user:b1-projection-recovery");
            await new B1ProjectGovernanceRepository(database).CreateGovernedProjectAsync(
                new CoreProject(projectId, "Projection recovery", Path.Combine(directory, "project"),
                    ProjectType.Godot, null, At, At), principal);
            var authority = new B1AuthorityRepository(database);
            return new Fixture(directory, database, new ProjectRef(projectId), principal,
                new B1AuthorityCommandService(authority, new B1AuthorityEvaluator(), TimeProvider.System),
                new B1NonAuthoritativeCommandService(
                    new B1RoutingRepository(database), new B1ClaimHandoffRepository(database)));
        }

        public async Task<B1ProjectionService> RestartProjectionAsync()
        {
            var restartedDatabase = new WorkbenchDatabase(Database.DatabasePath);
            await restartedDatabase.InitializeAsync();
            return new B1ProjectionService(new B1AuthorityRepository(restartedDatabase));
        }

        public async Task<Seed> SeedAsync()
        {
            var command = new EstablishResponsibilityCommand(
                ProjectRef,
                PrincipalRef,
                BootstrapAuthority,
                new ResponsibilityContract("Recover accepted state", "Durable projection", AuthorityBoundary.Empty),
                new AssignmentDelegationInstruction(
                    new ResponsibilityTarget.EstablishedByThisDecision(),
                    new AssignmentAssigneeTarget.EstablishedByThisDecision(),
                    new AssignmentRevisionContract("Initial recovery contract"),
                    null),
                RoleKind.Worker,
                [],
                []);
            var decision = await _commands.EstablishResponsibilityAsync(command);
            return new(
                decision.LogicalActorEstablishmentEffect!.LogicalActor.LogicalActorRef,
                decision.ResponsibilityEstablishmentEffect!.Responsibility.ResponsibilityRef,
                decision.AssignmentDelegationEffect!.Assignment.AssignmentRef,
                decision.AssignmentDelegationEffect.InitialRevision.RevisionRef);
        }

        public async Task<AcceptedStateContributionRef> AuthorContributionAsync(
            string statement, AcceptedStateContributionRef? supersedes = null)
        {
            var decision = await _commands.AuthorAcceptedStateAsync(new AuthorAcceptedStateCommand(
                ProjectRef, PrincipalRef, BootstrapAuthority, [],
                [new AcceptedContributionInstruction(
                    statement,
                    new ContributionScopeTarget.Project(ProjectRef),
                    supersedes,
                    null)]));
            return Assert.Single(decision.AcceptedStateContributions).ContributionRef;
        }

        public async Task<RevisionRef> ActivateSuccessorAsync(Seed seed)
        {
            var decision = await _commands.ActivateAssignmentRevisionAsync(new ActivateAssignmentRevisionCommand(
                ProjectRef, PrincipalRef, BootstrapAuthority,
                new RevisionActivationInstruction(
                    seed.Assignment, seed.Revision, new AssignmentRevisionContract("Recovered successor"), null),
                [],
                []));
            return decision.RevisionActivationEffect!.Revision.RevisionRef;
        }

        public Task DecideAsync(AssignmentRef assignment, RevisionRef revision, AssignmentDisposition disposition) =>
            _commands.DecideAssignmentAsync(new DecideAssignmentCommand(
                ProjectRef, PrincipalRef, BootstrapAuthority,
                new AssignmentDispositionInstruction(assignment, revision, disposition),
                null, null, [], []));

        public async Task<Routing> AddSelectedRoutingAsync(Seed seed)
        {
            var attempt = new AttemptRef(Guid.NewGuid());
            await _nonAuthoritative.CreateAttemptAndSelectAsync(
                new CreateAttemptCommand(ProjectRef, PrincipalRef, attempt, seed.Assignment, seed.Revision, At), null);
            var binding = new SessionBindingRef(Guid.NewGuid());
            await _nonAuthoritative.CreateSessionBindingAndSelectAsync(new CreateSessionBindingCommand(
                ProjectRef, PrincipalRef,
                new SessionBinding(binding, attempt, seed.Actor, new ExternalSessionRef("external:recovery"), At)), null);
            var claim = await _nonAuthoritative.RecordClaimAsync(new RecordClaimCommand(
                ProjectRef, PrincipalRef, new ClaimRef(Guid.NewGuid()), new ClaimantRef.LogicalActor(seed.Actor),
                binding, new ClaimPayload.Result("Recovered result"), [], At));
            var handoff = new HandoffRef(Guid.NewGuid());
            await _nonAuthoritative.CreateHandoffAndSelectAsync(new CreateHandoffCommand(
                ProjectRef, PrincipalRef,
                new Handoff(handoff, attempt, claim.ClaimRef, [], [], [], [], [], At)), null);
            return new(attempt, handoff, binding);
        }

        public async Task SeedIrrelevantLegacyRecordsAsync()
        {
            var projectId = ProjectRef.Value;
            var summary = Assert.Single(await new ProjectSummaryRepository(Database).AppendAsync(
                projectId,
                Guid.NewGuid(),
                [new SummaryDelta(
                    At,
                    SummaryDeltaKind.Decision,
                    "LEGACY_SUMMARY_MUST_NOT_APPEAR",
                    [new SummarySourceRef("Legacy", "summary:recovery")])],
                At));
            Assert.Equal("LEGACY_SUMMARY_MUST_NOT_APPEAR", summary.Text);

            var epochId = Guid.NewGuid();
            await new ProjectLeaderRepository(Database).CreateCurrentEpochAsync(
                new StoredProjectLeader(projectId, null, At, At),
                new StoredLeaderSessionEpoch(
                    epochId,
                    projectId,
                    "legacy-provider",
                    Guid.NewGuid(),
                    "legacy-model",
                    Guid.NewGuid(),
                    "legacy-session",
                    Path.Combine(_directory, "legacy-session"),
                    At,
                    At,
                    null,
                    null,
                    null));
            var transcript = await new LeaderMessageRepository(Database).AppendAsync(
                epochId, "user", "LEGACY_TRANSCRIPT_MUST_NOT_APPEAR", At);
            Assert.Equal("LEGACY_TRANSCRIPT_MUST_NOT_APPEAR", transcript.Text);

            var memory = new ProjectMemoryItem(
                Guid.NewGuid(),
                projectId,
                "Formal",
                "Legacy continuity",
                "LEGACY_MEMORY_MUST_NOT_APPEAR",
                "Active",
                At,
                At,
                At);
            var memories = new ProjectMemoryRepository(Database);
            await memories.AddAsync(memory, [new ProjectMemorySource("Legacy", "memory:recovery")]);
            Assert.Equal(memory, await memories.GetAsync(memory.Id));

            var activity = new ProjectActivityEvent(
                Guid.NewGuid(),
                projectId,
                "Runtime",
                "LEGACY_RUNTIME_MUST_NOT_APPEAR",
                "Legacy",
                "legacy:runtime",
                At,
                At);
            await new ProjectActivityRepository(Database).AddAsync(activity);
        }

        public async ValueTask DisposeAsync()
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
                    return;
                }
                catch (IOException) when (attempt < 19)
                {
                    await Task.Delay(25);
                }
                catch (UnauthorizedAccessException) when (attempt < 19)
                {
                    await Task.Delay(25);
                }
            }
        }
    }
}
