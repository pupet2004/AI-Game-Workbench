using Workbench.App.Continuity;
using Workbench.Core.Continuity;
using Workbench.Core.Projects;
using Workbench.Storage.Continuity;
using Workbench.Storage.Database;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.Tests.Continuity;

public sealed class B1AuthorityCommandServiceTests
{
    private static readonly AuthorityBoundary Empty = AuthorityBoundary.Empty;

    [Fact]
    public async Task EstablishLogicalActor_creates_identity_only()
    {
        await using var f = await Fixture.CreateAsync();

        var decision = await f.Service.EstablishLogicalActorAsync(f.EstablishActor());
        var state = await f.LoadAsync();

        var actor = Assert.Single(state.LogicalActors);
        Assert.Equal(RoleKind.Worker, actor.RoleKind);
        Assert.Equal(decision.DecisionRef, actor.AuthorizedByDecisionRef);
        Assert.Empty(state.Responsibilities);
        Assert.Empty(state.Assignments);
        Assert.Empty(state.Revisions);
        Assert.Empty(state.RevisionDispositions);
        Assert.Empty(state.AcceptedProjectState().CurrentContributions);
    }

    [Fact]
    public async Task EstablishResponsibility_can_create_obligation_without_owner_or_assignment()
    {
        await using var f = await Fixture.CreateAsync();

        var decision = await f.Service.EstablishResponsibilityAsync(
            f.EstablishResponsibility(f.Contract("Own continuity", Empty)));
        var state = await f.LoadAsync();

        var responsibility = Assert.Single(state.Responsibilities);
        Assert.Equal("Own continuity", responsibility.Contract.Obligation);
        Assert.Equal(decision.DecisionRef, responsibility.AuthorizedByDecisionRef);
        Assert.Empty(state.LogicalActors);
        Assert.Empty(state.Assignments);
        Assert.Empty(state.Revisions);
    }

    [Fact]
    public async Task Bootstrap_can_atomically_establish_actor_responsibility_assignment_and_initial_revision()
    {
        await using var f = await Fixture.CreateAsync();

        var decision = await f.Service.EstablishResponsibilityAsync(
            f.EstablishResponsibilityWithInitialDelegation(f.Contract("Govern releases", Empty), Empty));
        var state = await f.LoadAsync();

        var actor = Assert.Single(state.LogicalActors);
        var responsibility = Assert.Single(state.Responsibilities);
        var assignment = Assert.Single(state.Assignments);
        var revision = Assert.Single(state.Revisions);
        Assert.Equal(responsibility.ResponsibilityRef, assignment.ResponsibilityRef);
        Assert.Equal(actor.LogicalActorRef, assignment.AssigneeActorRef);
        Assert.Equal(revision.RevisionRef, assignment.InitialRevisionRef);
        Assert.Null(revision.PriorRevisionRef);
        Assert.All(new[]
        {
            actor.AuthorizedByDecisionRef,
            responsibility.AuthorizedByDecisionRef,
            assignment.AuthorizedByDecisionRef,
            revision.AuthorizedByDecisionRef
        }, authorized => Assert.Equal(decision.DecisionRef, authorized));
    }

    [Fact]
    public async Task Actor_establishment_grants_no_authority()
    {
        await using var f = await Fixture.CreateAsync();
        var actorDecision = await f.Service.EstablishLogicalActorAsync(f.EstablishActor(RoleKind.Leader));
        var actorRef = actorDecision.LogicalActorEstablishmentEffect!.LogicalActor.LogicalActorRef;

        var exception = await Assert.ThrowsAsync<B1CommandException>(() =>
            f.Service.EstablishLogicalActorAsync(
                f.EstablishActor(decidingAuthority: new DecidingAuthorityRef.LogicalActor(actorRef))));

        Assert.Equal(B1FailureCode.NotAuthorized, exception.Code);
        Assert.Single((await f.LoadAsync()).LogicalActors);
    }

    [Fact]
    public async Task Prospective_assignee_and_scope_resolve_to_durable_refs()
    {
        await using var f = await Fixture.CreateAsync();
        var command = f.EstablishResponsibilityWithInitialDelegation(
            f.Contract("Own release policy", Empty),
            Empty,
            [
                f.Contribution("Responsibility rule", new ContributionScopeTarget.Responsibility.EstablishedByThisDecision()),
                f.Contribution("Assignment rule", new ContributionScopeTarget.Assignment.DelegatedByThisDecision())
            ]);

        var decision = await f.Service.EstablishResponsibilityAsync(command);
        var reloaded = Assert.Single((await f.LoadAsync()).AuthorityDecisions);
        var responsibilityRef = decision.ResponsibilityEstablishmentEffect!.Responsibility.ResponsibilityRef;
        var actorRef = decision.LogicalActorEstablishmentEffect!.LogicalActor.LogicalActorRef;
        var assignmentRef = decision.AssignmentDelegationEffect!.Assignment.AssignmentRef;

        Assert.Equal(actorRef, decision.AssignmentDelegationEffect.Assignment.AssigneeActorRef);
        Assert.Contains(reloaded.AcceptedStateContributions,
            contribution => contribution.Scope == new ContributionScopeRef.Responsibility(responsibilityRef));
        Assert.Contains(reloaded.AcceptedStateContributions,
            contribution => contribution.Scope == new ContributionScopeRef.Assignment(assignmentRef));
    }

    [Fact]
    public async Task Failure_of_one_contribution_rolls_back_every_established_identity()
    {
        await using var f = await Fixture.CreateAsync();
        var missing = new AssignmentRef(Guid.NewGuid());
        var command = f.EstablishResponsibilityWithInitialDelegation(
            f.Contract("Atomic responsibility", Empty),
            Empty,
            [f.Contribution("Invalid target", new ContributionScopeTarget.Assignment.Existing(missing))]);

        var exception = await Assert.ThrowsAsync<B1CommandException>(() =>
            f.Service.EstablishResponsibilityAsync(command));
        var state = await f.LoadAsync();

        Assert.Equal(B1FailureCode.InvalidReference, exception.Code);
        Assert.Empty(state.AuthorityDecisions);
        Assert.Empty(state.LogicalActors);
        Assert.Empty(state.Responsibilities);
        Assert.Empty(state.Assignments);
        Assert.Empty(state.Revisions);
        Assert.Equal(0, state.Governance.LastProjectCommitSequence);
    }

    [Fact]
    public async Task DelegateAssignment_creates_one_immutable_assignment_and_initial_revision()
    {
        await using var f = await Fixture.CreateAsync();
        var actor = await f.AddActorAsync();
        var responsibility = await f.AddResponsibilityAsync(f.Contract("Delegate work", Empty));

        var decision = await f.Service.DelegateAssignmentAsync(
            f.Delegate(responsibility, actor, Empty));
        var state = await f.LoadAsync();

        var assignment = Assert.Single(state.Assignments);
        var revision = Assert.Single(state.Revisions);
        Assert.Equal(decision.AssignmentDelegationEffect!.Assignment, assignment);
        Assert.Equal(responsibility, assignment.ResponsibilityRef);
        Assert.Equal(actor, assignment.AssigneeActorRef);
        Assert.Equal(revision.RevisionRef, assignment.InitialRevisionRef);
        Assert.Null(revision.PriorRevisionRef);
    }

    [Fact]
    public async Task Parallel_delegation_does_not_replace_existing_assignment()
    {
        await using var f = await Fixture.CreateAsync();
        var actor = await f.AddActorAsync();
        var responsibility = await f.AddResponsibilityAsync(f.Contract("Parallel work", Empty));

        var first = await f.Service.DelegateAssignmentAsync(f.Delegate(responsibility, actor, Empty));
        var second = await f.Service.DelegateAssignmentAsync(f.Delegate(responsibility, actor, Empty));
        var projection = B1Projector.Build(await f.LoadAsync());

        Assert.Null(first.AssignmentDelegationEffect!.ReplacesAssignmentRef);
        Assert.Null(second.AssignmentDelegationEffect!.ReplacesAssignmentRef);
        Assert.Equal(2, projection.AcceptedProjectState.CurrentDelegationAssignments.Count);
    }

    [Fact]
    public async Task Explicit_replacement_preserves_old_assignment_history()
    {
        await using var f = await Fixture.CreateAsync();
        var actor = await f.AddActorAsync();
        var responsibility = await f.AddResponsibilityAsync(f.Contract("Replace work", Empty));
        var first = await f.Service.DelegateAssignmentAsync(f.Delegate(responsibility, actor, Empty));
        var firstAssignment = first.AssignmentDelegationEffect!.Assignment.AssignmentRef;

        var replacement = await f.Service.DelegateAssignmentAsync(
            f.Delegate(responsibility, actor, Empty, replaces: firstAssignment));
        var state = await f.LoadAsync();
        var projection = B1Projector.Build(state);

        Assert.Equal(2, state.Assignments.Count);
        Assert.Contains(firstAssignment, state.Assignments.Select(value => value.AssignmentRef));
        Assert.DoesNotContain(firstAssignment, projection.AcceptedProjectState.CurrentDelegationAssignments);
        Assert.Contains(replacement.AssignmentDelegationEffect!.Assignment.AssignmentRef,
            projection.AcceptedProjectState.CurrentDelegationAssignments);
    }

    [Fact]
    public async Task Replacement_target_must_still_be_current()
    {
        await using var f = await Fixture.CreateAsync();
        var actor = await f.AddActorAsync();
        var responsibility = await f.AddResponsibilityAsync(f.Contract("Stale replacement", Empty));
        var first = await f.Service.DelegateAssignmentAsync(f.Delegate(responsibility, actor, Empty));
        var firstAssignment = first.AssignmentDelegationEffect!.Assignment.AssignmentRef;
        await f.Service.DelegateAssignmentAsync(f.Delegate(responsibility, actor, Empty, replaces: firstAssignment));

        var exception = await Assert.ThrowsAsync<B1CommandException>(() =>
            f.Service.DelegateAssignmentAsync(f.Delegate(responsibility, actor, Empty, replaces: firstAssignment)));

        Assert.Equal(B1FailureCode.StaleReplacement, exception.Code);
        Assert.Equal(2, (await f.LoadAsync()).Assignments.Count);
    }

    [Fact]
    public async Task Accepted_assignment_remains_replaceable_by_a_later_separate_decision()
    {
        await using var f = await Fixture.CreateAsync();
        var actor = await f.AddActorAsync();
        var responsibility = await f.AddResponsibilityAsync(f.Contract("Accepted replacement", Empty));
        var first = await f.Service.DelegateAssignmentAsync(f.Delegate(responsibility, actor, Empty));
        var assignment = first.AssignmentDelegationEffect!.Assignment;
        var revision = first.AssignmentDelegationEffect.InitialRevision;
        await f.CommitDispositionAsync(assignment.AssignmentRef, revision.RevisionRef, AssignmentDisposition.Accepted);

        var replacement = await f.Service.DelegateAssignmentAsync(
            f.Delegate(responsibility, actor, Empty, replaces: assignment.AssignmentRef));

        Assert.Equal(assignment.AssignmentRef, replacement.AssignmentDelegationEffect!.ReplacesAssignmentRef);
        Assert.Equal(5, replacement.ProjectCommitSequence);
    }

    [Fact]
    public async Task LogicalActor_delegate_is_responsibility_local()
    {
        await using var f = await Fixture.CreateAsync();
        var boundary = Boundary(B1AuthorityCapability.DelegateAssignment);
        var seeded = await f.AddSeededResponsibilityAsync(boundary, boundary);
        var other = await f.AddResponsibilityAsync(f.Contract("Other responsibility", boundary));

        var exception = await Assert.ThrowsAsync<B1CommandException>(() =>
            f.Service.DelegateAssignmentAsync(f.Delegate(
                other,
                seeded.Actor,
                Empty,
                decidingAuthority: new DecidingAuthorityRef.LogicalActor(seeded.Actor))));

        Assert.Equal(B1FailureCode.NotAuthorized, exception.Code);
        Assert.Single((await f.LoadAsync()).Assignments);
    }

    [Fact]
    public async Task LogicalActor_cannot_seed_new_responsibility_even_when_it_can_establish_one()
    {
        await using var f = await Fixture.CreateAsync();
        var boundary = Boundary(
            B1AuthorityCapability.EstablishResponsibility,
            B1AuthorityCapability.DelegateAssignment);
        var seeded = await f.AddSeededResponsibilityAsync(boundary, boundary);
        var actorAuthority = new DecidingAuthorityRef.LogicalActor(seeded.Actor);
        var newResponsibilityDecision = await f.Service.EstablishResponsibilityAsync(
            f.EstablishResponsibility(f.Contract("Actor-created responsibility", boundary), actorAuthority));
        var newResponsibility = newResponsibilityDecision.ResponsibilityEstablishmentEffect!.Responsibility.ResponsibilityRef;

        var exception = await Assert.ThrowsAsync<B1CommandException>(() =>
            f.Service.DelegateAssignmentAsync(f.Delegate(
                newResponsibility, seeded.Actor, Empty, decidingAuthority: actorAuthority)));

        Assert.Equal(B1FailureCode.NotAuthorized, exception.Code);
        Assert.Single((await f.LoadAsync()).Assignments);
    }

    [Fact]
    public async Task Bootstrap_seeds_each_responsibility_first_assignment()
    {
        await using var f = await Fixture.CreateAsync();
        var boundary = Boundary(
            B1AuthorityCapability.EstablishResponsibility,
            B1AuthorityCapability.DelegateAssignment);
        var seeded = await f.AddSeededResponsibilityAsync(boundary, boundary);
        var actorAuthority = new DecidingAuthorityRef.LogicalActor(seeded.Actor);
        var created = await f.Service.EstablishResponsibilityAsync(
            f.EstablishResponsibility(f.Contract("Root-seeded next", boundary), actorAuthority));
        var responsibility = created.ResponsibilityEstablishmentEffect!.Responsibility.ResponsibilityRef;

        var delegated = await f.Service.DelegateAssignmentAsync(
            f.Delegate(responsibility, seeded.Actor, Empty));

        Assert.Equal(responsibility, delegated.AssignmentDelegationEffect!.Assignment.ResponsibilityRef);
        Assert.Equal(2, (await f.LoadAsync()).Assignments.Count);
    }

    [Fact]
    public async Task New_boundary_cannot_exceed_maximum_or_decider_possession()
    {
        await using var f = await Fixture.CreateAsync();
        var delegateOnly = Boundary(B1AuthorityCapability.DelegateAssignment);
        var delegateAndDecide = Boundary(
            B1AuthorityCapability.DelegateAssignment,
            B1AuthorityCapability.DecideAssignmentDisposition);
        var maximumLimited = await f.AddResponsibilityAsync(f.Contract("Maximum limited", delegateOnly));
        var actor = await f.AddActorAsync();

        var maximumFailure = await Assert.ThrowsAsync<B1CommandException>(() =>
            f.Service.DelegateAssignmentAsync(f.Delegate(maximumLimited, actor, delegateAndDecide)));
        Assert.Equal(B1FailureCode.AuthorityAmplification, maximumFailure.Code);

        var seeded = await f.AddSeededResponsibilityAsync(delegateAndDecide, delegateOnly);
        var possessionFailure = await Assert.ThrowsAsync<B1CommandException>(() =>
            f.Service.DelegateAssignmentAsync(f.Delegate(
                seeded.Responsibility,
                seeded.Actor,
                delegateAndDecide,
                decidingAuthority: new DecidingAuthorityRef.LogicalActor(seeded.Actor))));
        Assert.Equal(B1FailureCode.AuthorityAmplification, possessionFailure.Code);
    }

    [Fact]
    public async Task Three_sequence_conflicts_reload_and_then_commit_on_the_fourth_attempt()
    {
        await using var f = await Fixture.CreateAsync();
        var time = new ConflictInjectingTimeProvider(
            Fixture.Now,
            conflictsToInject: 3,
            f.CommitUnrelatedActorAsync);

        var decision = await f.CreateService(time).EstablishLogicalActorAsync(f.EstablishActor());
        var state = await f.LoadAsync();

        Assert.Equal(4, time.ReadCount);
        Assert.Equal(4, decision.ProjectCommitSequence);
        Assert.Equal(4, state.Governance.LastProjectCommitSequence);
        Assert.Equal(4, state.AuthorityDecisions.Count);
        Assert.Contains(decision.DecisionRef, state.AuthorityDecisions.Select(value => value.DecisionRef));
    }

    [Fact]
    public async Task Fourth_sequence_conflict_exhausts_three_retry_budget_without_target_effects()
    {
        await using var f = await Fixture.CreateAsync();
        var time = new ConflictInjectingTimeProvider(
            Fixture.Now,
            conflictsToInject: 4,
            f.CommitUnrelatedActorAsync);

        var exception = await Assert.ThrowsAsync<B1CommandException>(() =>
            f.CreateService(time).EstablishLogicalActorAsync(f.EstablishActor()));
        var state = await f.LoadAsync();

        Assert.Equal(B1FailureCode.ConcurrentProjectChange, exception.Code);
        Assert.Equal(4, time.ReadCount);
        Assert.Equal(4, state.Governance.LastProjectCommitSequence);
        Assert.Equal(4, state.AuthorityDecisions.Count);
        Assert.Equal(4, state.LogicalActors.Count);
    }

    private static AuthorityBoundary Boundary(params B1AuthorityCapability[] capabilities) =>
        AuthorityBoundary.Create(capabilities);

    private sealed class Fixture : IAsyncDisposable
    {
        public static readonly DateTimeOffset Now =
            new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);
        private readonly string _directory;
        private readonly B1AuthorityRepository _repository;
        private readonly B1AuthorityEvaluator _evaluator = new();

        private Fixture(
            string directory,
            WorkbenchDatabase database,
            ProjectRef projectRef,
            UserPrincipalRef principal,
            B1AuthorityRepository repository,
            B1AuthorityCommandService service)
        {
            _directory = directory;
            Database = database;
            ProjectRef = projectRef;
            Principal = principal;
            _repository = repository;
            Service = service;
        }

        public WorkbenchDatabase Database { get; }
        public ProjectRef ProjectRef { get; }
        public UserPrincipalRef Principal { get; }
        public DecidingAuthorityRef BootstrapAuthority => new DecidingAuthorityRef.UserPrincipal(Principal);
        public B1AuthorityCommandService Service { get; }

        public B1AuthorityCommandService CreateService(TimeProvider timeProvider) =>
            new(_repository, _evaluator, timeProvider);

        public static async Task<Fixture> CreateAsync()
        {
            var directory = Path.Combine(
                Path.GetTempPath(), "AI.Game.Workbench.App.Tests", Guid.NewGuid().ToString("N"));
            var database = new WorkbenchDatabase(Path.Combine(directory, "test.db"));
            await database.InitializeAsync();
            var projectId = Guid.NewGuid();
            var principal = new UserPrincipalRef("user:b1-authority-app-test");
            await new B1ProjectGovernanceRepository(database).CreateGovernedProjectAsync(
                new CoreProject(
                    projectId,
                    "B1 Authority App Project",
                    $"C:/Projects/{projectId:N}",
                    ProjectType.Godot,
                    null,
                    Now,
                    Now),
                principal);
            var repository = new B1AuthorityRepository(database);
            var time = new MutableTimeProvider(Now);
            return new Fixture(
                directory,
                database,
                new ProjectRef(projectId),
                principal,
                repository,
                new B1AuthorityCommandService(repository, new B1AuthorityEvaluator(), time));
        }

        public EstablishLogicalActorCommand EstablishActor(
            RoleKind roleKind = RoleKind.Worker,
            DecidingAuthorityRef? decidingAuthority = null) =>
            new(ProjectRef, Principal, decidingAuthority ?? BootstrapAuthority, roleKind, [], []);

        public EstablishResponsibilityCommand EstablishResponsibility(
            ResponsibilityContract contract,
            DecidingAuthorityRef? decidingAuthority = null) =>
            new(ProjectRef, Principal, decidingAuthority ?? BootstrapAuthority,
                contract, null, null, [], []);

        public EstablishResponsibilityCommand EstablishResponsibilityWithInitialDelegation(
            ResponsibilityContract contract,
            AuthorityBoundary delegatedBoundary,
            IReadOnlyList<AcceptedContributionInstruction>? contributions = null) =>
            new(
                ProjectRef,
                Principal,
                BootstrapAuthority,
                contract,
                new AssignmentDelegationInstruction(
                    new ResponsibilityTarget.EstablishedByThisDecision(),
                    new AssignmentAssigneeTarget.EstablishedByThisDecision(),
                    new AssignmentRevisionContract("Initial work", delegatedBoundary),
                    null),
                RoleKind.Worker,
                [],
                contributions ?? []);

        public DelegateAssignmentCommand Delegate(
            ResponsibilityRef responsibility,
            LogicalActorRef actor,
            AuthorityBoundary delegatedBoundary,
            AssignmentRef? replaces = null,
            DecidingAuthorityRef? decidingAuthority = null) =>
            new(
                ProjectRef,
                Principal,
                decidingAuthority ?? BootstrapAuthority,
                new AssignmentDelegationInstruction(
                    new ResponsibilityTarget.Existing(responsibility),
                    new AssignmentAssigneeTarget.Existing(actor),
                    new AssignmentRevisionContract("Delegated work", delegatedBoundary),
                    replaces),
                null,
                [],
                []);

        public ResponsibilityContract Contract(string obligation, AuthorityBoundary maximum) =>
            new(obligation, $"{obligation} outcome", maximum);

        public AcceptedContributionInstruction Contribution(string statement, ContributionScopeTarget scope) =>
            new(statement, scope, null, null);

        public async Task<LogicalActorRef> AddActorAsync()
        {
            var decision = await Service.EstablishLogicalActorAsync(EstablishActor());
            return decision.LogicalActorEstablishmentEffect!.LogicalActor.LogicalActorRef;
        }

        public async Task<ResponsibilityRef> AddResponsibilityAsync(ResponsibilityContract contract)
        {
            var decision = await Service.EstablishResponsibilityAsync(EstablishResponsibility(contract));
            return decision.ResponsibilityEstablishmentEffect!.Responsibility.ResponsibilityRef;
        }

        public async Task<(LogicalActorRef Actor, ResponsibilityRef Responsibility, AssignmentRef Assignment)>
            AddSeededResponsibilityAsync(AuthorityBoundary maximum, AuthorityBoundary delegated)
        {
            var decision = await Service.EstablishResponsibilityAsync(
                EstablishResponsibilityWithInitialDelegation(Contract("Seeded responsibility", maximum), delegated));
            return (
                decision.LogicalActorEstablishmentEffect!.LogicalActor.LogicalActorRef,
                decision.ResponsibilityEstablishmentEffect!.Responsibility.ResponsibilityRef,
                decision.AssignmentDelegationEffect!.Assignment.AssignmentRef);
        }

        public async Task CommitDispositionAsync(
            AssignmentRef assignmentRef,
            RevisionRef revisionRef,
            AssignmentDisposition disposition)
        {
            var state = await _repository.LoadProjectStateAsync(ProjectRef);
            var command = new DecideAssignmentCommand(
                ProjectRef,
                Principal,
                BootstrapAuthority,
                new AssignmentDispositionInstruction(assignmentRef, revisionRef, disposition),
                null,
                null,
                [],
                []);
            var validated = _evaluator.Evaluate(
                state, command, new AuthorityDecisionRef(Guid.NewGuid()), Now);
            Assert.IsType<AuthorityCommitResult.Committed>(await _repository.TryCommitAsync(validated));
        }

        public async Task CommitUnrelatedActorAsync()
        {
            var state = await _repository.LoadProjectStateAsync(ProjectRef);
            var command = EstablishActor(RoleKind.Reviewer);
            var validated = _evaluator.Evaluate(
                state, command, new AuthorityDecisionRef(Guid.NewGuid()), Now);
            Assert.IsType<AuthorityCommitResult.Committed>(await _repository.TryCommitAsync(validated));
        }

        public Task<B1ProjectState> LoadAsync() => _repository.LoadProjectStateAsync(ProjectRef);

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


    private sealed class ConflictInjectingTimeProvider(
        DateTimeOffset utcNow,
        int conflictsToInject,
        Func<Task> injectConflict) : TimeProvider
    {
        private int _readCount;

        public int ReadCount => Volatile.Read(ref _readCount);

        public override DateTimeOffset GetUtcNow()
        {
            var read = Interlocked.Increment(ref _readCount);
            if (read <= conflictsToInject)
            {
                Task.Run(injectConflict).GetAwaiter().GetResult();
            }

            return utcNow.AddTicks(read);
        }
    }
}

internal static class B1AuthorityCommandServiceTestExtensions
{
    public static AcceptedProjectState AcceptedProjectState(this B1ProjectState state) =>
        B1Projector.Build(state).AcceptedProjectState;
}
