using Workbench.Core.Continuity;

namespace Workbench.Core.Tests.Continuity;

public sealed class B1AuthorityEvaluatorTests
{
    private readonly B1AuthorityEvaluator _evaluator = new();

    [Fact]
    public void Bootstrap_principal_has_root_authority_only_for_its_project()
    {
        var f = Fixture.Create();

        var validated = _evaluator.Evaluate(f.State(), f.EstablishActor(), f.NewDecisionRef(), f.Now);

        Assert.Equal(f.Project, validated.ProjectRef);
        Assert.NotNull(validated.LogicalActorEstablishmentEffect);
        AssertFailure(B1FailureCode.WrongProject, () => _evaluator.Evaluate(
            f.State(), f.EstablishActor() with { ProjectRef = new ProjectRef(Guid.NewGuid()) }, f.NewDecisionRef(), f.Now));
    }

    [Fact]
    public void Non_bootstrap_operator_cannot_submit_b1_command()
    {
        var f = Fixture.Create();
        AssertFailure(B1FailureCode.NotAuthorized, () => _evaluator.Evaluate(
            f.State(), f.EstablishActor() with { AuthenticatedOperatorRef = new UserPrincipalRef("user:other") },
            f.NewDecisionRef(), f.Now));
    }

    [Fact]
    public void Actor_decision_requires_bootstrap_manual_operator_without_borrowing_root_authority()
    {
        var f = Fixture.Create();
        var command = f.EstablishActor(new DecidingAuthorityRef.LogicalActor(f.Actor.LogicalActorRef));

        AssertFailure(B1FailureCode.NotAuthorized, () =>
            _evaluator.Evaluate(f.State(), command, f.NewDecisionRef(), f.Now));
        AssertFailure(B1FailureCode.NotAuthorized, () => _evaluator.Evaluate(
            f.State(), command with { AuthenticatedOperatorRef = new UserPrincipalRef("user:other") },
            f.NewDecisionRef(), f.Now));
    }

    [Fact]
    public void Role_kind_grants_no_authority()
    {
        var f = Fixture.Create(roleKind: RoleKind.Leader);
        AssertFailure(B1FailureCode.NotAuthorized, () => _evaluator.Evaluate(
            f.State(), f.EstablishActor(new DecidingAuthorityRef.LogicalActor(f.Actor.LogicalActorRef)),
            f.NewDecisionRef(), f.Now));
    }

    [Fact]
    public void Empty_delegated_boundary_grants_no_authority()
    {
        var f = Fixture.Create();
        AssertFailure(B1FailureCode.NotAuthorized, () => _evaluator.Evaluate(
            f.State(), f.Decide(AssignmentDisposition.Accepted, f.ActorAuthority), f.NewDecisionRef(), f.Now));
    }

    [Fact]
    public void Capability_requires_one_local_effect_source()
    {
        var f = Fixture.Create(B1AuthorityCapability.DecideAssignmentDisposition);

        var validated = _evaluator.Evaluate(
            f.State(), f.Decide(AssignmentDisposition.Accepted, f.ActorAuthority), f.NewDecisionRef(), f.Now);

        Assert.Equal(f.Assignment.AssignmentRef, validated.AssignmentDispositionEffect!.AssignmentRef);
    }

    [Fact]
    public void Capabilities_from_different_responsibilities_do_not_cross_leak()
    {
        var f = Fixture.Create(B1AuthorityCapability.DecideAssignmentDisposition);
        var other = f.AddAssignmentUnderNewResponsibility(AuthorityBoundary.Empty);
        var command = f.Decide(AssignmentDisposition.Accepted, f.ActorAuthority) with
        {
            Disposition = new(other.Assignment.AssignmentRef, other.Revision.RevisionRef, AssignmentDisposition.Accepted)
        };

        AssertFailure(B1FailureCode.NotAuthorized, () => _evaluator.Evaluate(
            other.State, command, f.NewDecisionRef(), f.Now));
    }

    [Fact]
    public void Logical_actor_cannot_seed_new_responsibility()
    {
        var f = Fixture.Create(
            B1AuthorityCapability.EstablishLogicalActor,
            B1AuthorityCapability.EstablishResponsibility,
            B1AuthorityCapability.DelegateAssignment);
        var command = f.EstablishResponsibilityWithInitialDelegation(f.ActorAuthority, AuthorityBoundary.Empty) with
        {
            InitialDelegation = new(
                new ResponsibilityTarget.EstablishedByThisDecision(),
                new AssignmentAssigneeTarget.Existing(f.Actor.LogicalActorRef),
                new AssignmentRevisionContract("First contract"), null),
            EstablishedAssigneeRoleKind = null
        };

        AssertFailure(B1FailureCode.NotAuthorized, () =>
            _evaluator.Evaluate(f.State(), command, f.NewDecisionRef(), f.Now));
    }

    [Fact]
    public void Bootstrap_can_establish_and_seed_atomically()
    {
        var f = Fixture.Create();
        var validated = _evaluator.Evaluate(
            f.State(), f.EstablishResponsibilityWithInitialDelegation(f.BootstrapAuthority, AuthorityBoundary.Empty),
            f.NewDecisionRef(), f.Now);

        Assert.NotNull(validated.ResponsibilityEstablishmentEffect);
        Assert.NotNull(validated.LogicalActorEstablishmentEffect);
        Assert.NotNull(validated.AssignmentDelegationEffect);
        Assert.Equal(
            validated.ResponsibilityEstablishmentEffect!.Responsibility.ResponsibilityRef,
            validated.AssignmentDelegationEffect!.Assignment.ResponsibilityRef);
        Assert.Equal(
            validated.LogicalActorEstablishmentEffect!.LogicalActor.LogicalActorRef,
            validated.AssignmentDelegationEffect.Assignment.AssigneeActorRef);
    }

    [Fact]
    public void New_assignment_boundary_cannot_amplify_actor_authority()
    {
        var f = Fixture.CreateWithBoundaries(
            Boundary(
                B1AuthorityCapability.DelegateAssignment,
                B1AuthorityCapability.AcceptAssignmentStateContribution),
            Boundary(B1AuthorityCapability.DelegateAssignment));
        var newBoundary = Boundary(
            B1AuthorityCapability.DelegateAssignment,
            B1AuthorityCapability.AcceptAssignmentStateContribution);

        AssertFailure(B1FailureCode.AuthorityAmplification, () => _evaluator.Evaluate(
            f.State(), f.Delegate(newBoundary, f.ActorAuthority), f.NewDecisionRef(), f.Now));
    }

    [Fact]
    public void Revision_added_capabilities_cannot_amplify_authority()
    {
        var f = Fixture.CreateWithBoundaries(
            Boundary(
                B1AuthorityCapability.ActivateAssignmentRevision,
                B1AuthorityCapability.DecideAssignmentDisposition),
            Boundary(B1AuthorityCapability.ActivateAssignmentRevision));
        var command = f.Activate(
            Boundary(B1AuthorityCapability.ActivateAssignmentRevision, B1AuthorityCapability.DecideAssignmentDisposition),
            f.ActorAuthority);

        AssertFailure(B1FailureCode.AuthorityAmplification, () =>
            _evaluator.Evaluate(f.State(), command, f.NewDecisionRef(), f.Now));
    }

    [Fact]
    public void Retained_or_removed_capabilities_are_not_new_amplification()
    {
        var f = Fixture.Create(
            B1AuthorityCapability.ActivateAssignmentRevision,
            B1AuthorityCapability.DecideAssignmentDisposition);
        var command = f.Activate(Boundary(B1AuthorityCapability.DecideAssignmentDisposition), f.ActorAuthority);

        var validated = _evaluator.Evaluate(f.State(), command, f.NewDecisionRef(), f.Now);

        Assert.Equal(
            Boundary(B1AuthorityCapability.DecideAssignmentDisposition),
            validated.RevisionActivationEffect!.Revision.Contract.DelegatedAuthorityBoundary);
    }

    [Fact]
    public void New_effects_do_not_authorize_same_decision()
    {
        var f = Fixture.Create(
            B1AuthorityCapability.EstablishResponsibility,
            B1AuthorityCapability.DelegateAssignment);
        var command = f.EstablishResponsibilityWithInitialDelegation(f.ActorAuthority, AuthorityBoundary.Empty);

        AssertFailure(B1FailureCode.NotAuthorized, () =>
            _evaluator.Evaluate(f.State(), command, f.NewDecisionRef(), f.Now));
    }

    [Fact]
    public void Every_effect_is_checked_before_draft_creation()
    {
        var f = Fixture.Create();
        var missing = new ResponsibilityRef(Guid.NewGuid());
        var command = f.EstablishActor() with
        {
            Contributions = [f.Contribution(new ContributionScopeTarget.Responsibility.Existing(missing))]
        };

        AssertFailure(B1FailureCode.InvalidReference, () =>
            _evaluator.Evaluate(f.State(), command, f.NewDecisionRef(), f.Now));
    }

    [Fact]
    public void Invalid_compound_shapes_return_INVALID_DECISION_SHAPE()
    {
        var f = Fixture.Create();
        var replacement = new AssignmentReplacementInstruction(
            f.Assignment.AssignmentRef, new AssignmentAssigneeTarget.Existing(f.Actor.LogicalActorRef), null,
            new AssignmentRevisionContract("replacement"));
        var command = f.Decide(AssignmentDisposition.Accepted, f.BootstrapAuthority) with { Replacement = replacement };

        AssertFailure(B1FailureCode.InvalidDecisionShape, () =>
            _evaluator.Evaluate(f.State(), command, f.NewDecisionRef(), f.Now));
    }

    [Fact]
    public void Contributions_require_scope_specific_capability()
    {
        var f = Fixture.Create(B1AuthorityCapability.AcceptProjectStateContribution);
        var projectCommand = f.Author(f.ActorAuthority,
            f.Contribution(new ContributionScopeTarget.Project(f.Project)));
        var responsibilityCommand = f.Author(f.ActorAuthority,
            f.Contribution(new ContributionScopeTarget.Responsibility.Existing(f.Responsibility.ResponsibilityRef)));

        Assert.Single(_evaluator.Evaluate(f.State(), projectCommand, f.NewDecisionRef(), f.Now).AcceptedStateContributions);
        AssertFailure(B1FailureCode.NotAuthorized, () =>
            _evaluator.Evaluate(f.State(), responsibilityCommand, f.NewDecisionRef(), f.Now));
    }

    [Fact]
    public void Disposition_requires_current_unresolved_revision_once()
    {
        var f = Fixture.Create();
        var stale = f.Decide(AssignmentDisposition.Accepted, f.BootstrapAuthority) with
        {
            Disposition = new(f.Assignment.AssignmentRef, new RevisionRef(Guid.NewGuid()), AssignmentDisposition.Accepted)
        };
        AssertFailure(B1FailureCode.StaleRevision, () =>
            _evaluator.Evaluate(f.State(), stale, f.NewDecisionRef(), f.Now));

        var disposition = new RevisionDispositionRecord(
            f.Assignment.AssignmentRef, f.Revision.RevisionRef, AssignmentDisposition.Rejected, f.NewDecisionRef());
        AssertFailure(B1FailureCode.AlreadyDispositioned, () => _evaluator.Evaluate(
            f.State(dispositions: [disposition]), f.Decide(AssignmentDisposition.Accepted, f.BootstrapAuthority),
            f.NewDecisionRef(), f.Now));
    }

    [Fact]
    public void Activation_eligibility_and_revision_required_combination()
    {
        var f = Fixture.Create();
        var activation = new RevisionActivationInstruction(
            f.Assignment.AssignmentRef, f.Revision.RevisionRef, new AssignmentRevisionContract("R2"), null);
        var combined = f.Decide(AssignmentDisposition.RevisionRequired, f.BootstrapAuthority) with
        {
            Activation = activation
        };

        var validated = _evaluator.Evaluate(f.State(), combined, f.NewDecisionRef(), f.Now);
        Assert.NotNull(validated.AssignmentDispositionEffect);
        Assert.NotNull(validated.RevisionActivationEffect);

        var accepted = new RevisionDispositionRecord(
            f.Assignment.AssignmentRef, f.Revision.RevisionRef, AssignmentDisposition.Accepted, f.NewDecisionRef());
        AssertFailure(B1FailureCode.AlreadyDispositioned, () => _evaluator.Evaluate(
            f.State(dispositions: [accepted]), f.Activate(AuthorityBoundary.Empty, f.BootstrapAuthority),
            f.NewDecisionRef(), f.Now));
    }

    [Fact]
    public void Replacement_matrix_and_same_assignment_restrictions()
    {
        var f = Fixture.Create();
        var replacement = new AssignmentReplacementInstruction(
            f.Assignment.AssignmentRef, new AssignmentAssigneeTarget.Existing(f.Actor.LogicalActorRef), null,
            new AssignmentRevisionContract("replacement"));
        var allowed = f.Decide(AssignmentDisposition.Rejected, f.BootstrapAuthority) with { Replacement = replacement };
        Assert.NotNull(_evaluator.Evaluate(f.State(), allowed, f.NewDecisionRef(), f.Now).AssignmentDelegationEffect);

        var other = f.AddAssignmentUnderNewResponsibility(AuthorityBoundary.Empty);
        var wrongTarget = replacement with { ReplacesAssignmentRef = other.Assignment.AssignmentRef };
        AssertFailure(B1FailureCode.InvalidDecisionShape, () => _evaluator.Evaluate(
            other.State, allowed with { Replacement = wrongTarget }, f.NewDecisionRef(), f.Now));
    }

    [Fact]
    public void Supersession_requires_current_exact_scope_target()
    {
        var f = Fixture.Create();
        var prior = f.AcceptedContribution(
            new ContributionScopeRef.Project(f.Project), f.NewDecisionRef(), "old");
        var state = f.State(decisions: [f.DecisionWith(prior)]);
        var exact = f.Contribution(new ContributionScopeTarget.Project(f.Project), prior.ContributionRef);
        Assert.Single(_evaluator.Evaluate(
            state, f.Author(f.BootstrapAuthority, exact), f.NewDecisionRef(), f.Now).AcceptedStateContributions);

        var wrongScope = f.Contribution(
            new ContributionScopeTarget.Responsibility.Existing(f.Responsibility.ResponsibilityRef),
            prior.ContributionRef);
        AssertFailure(B1FailureCode.StaleSupersession, () => _evaluator.Evaluate(
            state, f.Author(f.BootstrapAuthority, wrongScope), f.NewDecisionRef(), f.Now));
    }

    [Fact]
    public void Duplicate_supersession_target_is_invalid_decision_shape()
    {
        var f = Fixture.Create();
        var prior = f.AcceptedContribution(
            new ContributionScopeRef.Project(f.Project), f.NewDecisionRef(), "old");
        var state = f.State(decisions: [f.DecisionWith(prior)]);
        var first = f.Contribution(
            new ContributionScopeTarget.Project(f.Project), prior.ContributionRef) with { Statement = "first" };
        var second = f.Contribution(
            new ContributionScopeTarget.Project(f.Project), prior.ContributionRef) with { Statement = "second" };

        AssertFailure(B1FailureCode.InvalidDecisionShape, () => _evaluator.Evaluate(
            state, f.Author(f.BootstrapAuthority, first, second), f.NewDecisionRef(), f.Now));
    }

    [Fact]
    public void Prospective_scope_resolves_to_same_decision_identity()
    {
        var f = Fixture.Create();
        var command = f.EstablishResponsibilityWithInitialDelegation(f.BootstrapAuthority, AuthorityBoundary.Empty) with
        {
            Contributions =
            [
                f.Contribution(new ContributionScopeTarget.Responsibility.EstablishedByThisDecision()),
                f.Contribution(new ContributionScopeTarget.Assignment.DelegatedByThisDecision())
            ]
        };

        var validated = _evaluator.Evaluate(f.State(), command, f.NewDecisionRef(), f.Now);
        var responsibilityScope = Assert.IsType<ContributionScopeRef.Responsibility>(validated.AcceptedStateContributions[0].Scope);
        var assignmentScope = Assert.IsType<ContributionScopeRef.Assignment>(validated.AcceptedStateContributions[1].Scope);
        Assert.Equal(validated.ResponsibilityEstablishmentEffect!.Responsibility.ResponsibilityRef, responsibilityScope.ResponsibilityRef);
        Assert.Equal(validated.AssignmentDelegationEffect!.Assignment.AssignmentRef, assignmentScope.AssignmentRef);
    }

    [Fact]
    public void Contributions_cannot_supersede_same_decision()
    {
        var f = Fixture.Create();
        var prospectiveTarget = new AcceptedStateContributionRef(Guid.NewGuid());
        var first = f.Contribution(new ContributionScopeTarget.Project(f.Project)) with
        {
            Statement = "first",
            SupersedesContributionRef = null
        };
        var second = f.Contribution(new ContributionScopeTarget.Project(f.Project), prospectiveTarget) with
        {
            Statement = "second"
        };

        AssertFailure(B1FailureCode.StaleSupersession, () => _evaluator.Evaluate(
            f.State(), f.Author(f.BootstrapAuthority, first, second), f.NewDecisionRef(), f.Now));
    }

    [Fact]
    public void Validation_failure_returns_no_partial_draft()
    {
        var f = Fixture.Create();
        var command = f.Decide(AssignmentDisposition.Accepted, f.BootstrapAuthority) with
        {
            Contributions = [f.Contribution(new ContributionScopeTarget.Assignment.Existing(new AssignmentRef(Guid.NewGuid())))]
        };
        var before = f.State();

        AssertFailure(B1FailureCode.InvalidReference, () =>
            _evaluator.Evaluate(before, command, f.NewDecisionRef(), f.Now));
        Assert.Empty(before.RevisionDispositions);
        Assert.Empty(before.AuthorityDecisions);
    }

    [Fact]
    public void Cross_project_assignment_cannot_supply_actor_authority()
    {
        var f = Fixture.Create();
        var foreignProject = new ProjectRef(Guid.NewGuid());
        var boundary = Boundary(B1AuthorityCapability.EstablishLogicalActor);
        var responsibility = new Responsibility(
            new(Guid.NewGuid()), foreignProject, new("Foreign", "Foreign", boundary), f.NewDecisionRef(), f.Now);
        var assignmentRef = new AssignmentRef(Guid.NewGuid());
        var revisionRef = new RevisionRef(Guid.NewGuid());
        var assignment = new Assignment(
            assignmentRef, responsibility.ResponsibilityRef, f.Actor.LogicalActorRef, revisionRef, f.NewDecisionRef());
        var revision = new AssignmentRevision(
            revisionRef, assignmentRef, null, new("Foreign", boundary), f.NewDecisionRef());
        var state = f.State(
            responsibilities: [f.Responsibility, responsibility],
            assignments: [f.Assignment, assignment],
            revisions: [f.Revision, revision]);

        AssertFailure(B1FailureCode.WrongProject, () => _evaluator.Evaluate(
            state, f.EstablishActor(f.ActorAuthority), f.NewDecisionRef(), f.Now));
    }

    [Fact]
    public void Cross_project_handoff_cannot_be_considered()
    {
        var f = Fixture.Create();
        var foreignProject = new ProjectRef(Guid.NewGuid());
        var responsibility = new Responsibility(
            new(Guid.NewGuid()), foreignProject, new("Foreign", "Foreign", AuthorityBoundary.Empty),
            f.NewDecisionRef(), f.Now);
        var assignmentRef = new AssignmentRef(Guid.NewGuid());
        var revisionRef = new RevisionRef(Guid.NewGuid());
        var assignment = new Assignment(
            assignmentRef, responsibility.ResponsibilityRef, f.Actor.LogicalActorRef, revisionRef, f.NewDecisionRef());
        var revision = new AssignmentRevision(
            revisionRef, assignmentRef, null, new("Foreign"), f.NewDecisionRef());
        var attempt = new Attempt(new(Guid.NewGuid()), assignmentRef, revisionRef, f.Now);
        var handoff = new Handoff(
            new(Guid.NewGuid()), attempt.AttemptRef, new(Guid.NewGuid()), [], [], [], [], [], f.Now);
        var state = f.State(
            responsibilities: [f.Responsibility, responsibility],
            assignments: [f.Assignment, assignment], revisions: [f.Revision, revision],
            attempts: [attempt], handoffs: [handoff]);
        var command = f.EstablishActor() with
        {
            ConsideredRefs = [new ConsideredRef.Handoff(handoff.HandoffRef)]
        };

        AssertFailure(B1FailureCode.WrongProject, () =>
            _evaluator.Evaluate(state, command, f.NewDecisionRef(), f.Now));
    }

    [Fact]
    public void Validated_decision_has_no_public_constructor_or_effect_factory()
    {
        Assert.Empty(typeof(ValidatedAuthorityDecision).GetConstructors());
        Assert.DoesNotContain(typeof(ValidatedAuthorityDecision).GetMethods(), method =>
            method.IsStatic && method.IsPublic && method.GetParameters().Any(parameter => parameter.Name == "effects"));
    }

    private static AuthorityBoundary Boundary(params B1AuthorityCapability[] capabilities) =>
        AuthorityBoundary.Create(capabilities);

    private static void AssertFailure(B1FailureCode expected, Action action)
    {
        var error = Assert.Throws<B1CommandException>(action);
        Assert.Equal(expected, error.Code);
    }

    private sealed record Fixture(
        ProjectRef Project,
        UserPrincipalRef Bootstrap,
        ProjectGovernance Governance,
        LogicalActor Actor,
        Responsibility Responsibility,
        Assignment Assignment,
        AssignmentRevision Revision,
        DateTimeOffset Now)
    {
        public DecidingAuthorityRef BootstrapAuthority => new DecidingAuthorityRef.UserPrincipal(Bootstrap);
        public DecidingAuthorityRef ActorAuthority => new DecidingAuthorityRef.LogicalActor(Actor.LogicalActorRef);

        public static Fixture Create(params B1AuthorityCapability[] capabilities) =>
            Create(RoleKind.Worker, capabilities);

        public static Fixture Create(RoleKind roleKind, params B1AuthorityCapability[] capabilities)
        {
            var boundary = Boundary(capabilities);
            return CreateWithBoundaries(boundary, boundary, roleKind);
        }

        public static Fixture CreateWithBoundaries(
            AuthorityBoundary maximum,
            AuthorityBoundary delegated,
            RoleKind roleKind = RoleKind.Worker)
        {
            var project = new ProjectRef(Guid.NewGuid());
            var bootstrap = new UserPrincipalRef("user:bootstrap");
            var authorization = new AuthorityDecisionRef(Guid.NewGuid());
            var now = new DateTimeOffset(2026, 8, 22, 10, 0, 0, TimeSpan.Zero);
            var actor = new LogicalActor(new(Guid.NewGuid()), project, roleKind, authorization, now);
            var responsibility = new Responsibility(
                new(Guid.NewGuid()), project, new("Own continuity", "Continuity preserved", maximum), authorization, now);
            var assignmentRef = new AssignmentRef(Guid.NewGuid());
            var revisionRef = new RevisionRef(Guid.NewGuid());
            var assignment = new Assignment(
                assignmentRef, responsibility.ResponsibilityRef, actor.LogicalActorRef, revisionRef, authorization);
            var revision = new AssignmentRevision(
                revisionRef, assignmentRef, null, new("Perform work", delegated), authorization);
            return new(project, bootstrap, new(project, bootstrap, B1GovernanceOrigin.Created, null, 7),
                actor, responsibility, assignment, revision, now);
        }

        public B1ProjectState State(
            IReadOnlyList<LogicalActor>? actors = null,
            IReadOnlyList<Responsibility>? responsibilities = null,
            IReadOnlyList<Assignment>? assignments = null,
            IReadOnlyList<AssignmentRevision>? revisions = null,
            IReadOnlyList<RevisionDispositionRecord>? dispositions = null,
            IReadOnlyList<Attempt>? attempts = null,
            IReadOnlyList<Claim>? claims = null,
            IReadOnlyList<Handoff>? handoffs = null,
            IReadOnlyList<AuthorityDecision>? decisions = null) =>
            new(Governance, actors ?? [Actor], responsibilities ?? [Responsibility], assignments ?? [Assignment],
                revisions ?? [Revision], dispositions ?? [], attempts ?? [], [], claims ?? [], handoffs ?? [],
                decisions ?? [], [], []);

        public EstablishLogicalActorCommand EstablishActor(DecidingAuthorityRef? authority = null) =>
            new(Project, Bootstrap, authority ?? BootstrapAuthority, RoleKind.Worker, [], []);

        public EstablishResponsibilityCommand EstablishResponsibilityWithInitialDelegation(
            DecidingAuthorityRef authority, AuthorityBoundary boundary) =>
            new(Project, Bootstrap, authority,
                new("Govern releases", "Releases remain governed", boundary),
                new(new ResponsibilityTarget.EstablishedByThisDecision(),
                    new AssignmentAssigneeTarget.EstablishedByThisDecision(),
                    new AssignmentRevisionContract("First contract", boundary), null),
                RoleKind.Worker, [], []);

        public DelegateAssignmentCommand Delegate(AuthorityBoundary boundary, DecidingAuthorityRef authority) =>
            new(Project, Bootstrap, authority,
                new(new ResponsibilityTarget.Existing(Responsibility.ResponsibilityRef),
                    new AssignmentAssigneeTarget.Existing(Actor.LogicalActorRef),
                    new AssignmentRevisionContract("Delegated work", boundary), null),
                null, [], []);

        public DecideAssignmentCommand Decide(AssignmentDisposition disposition, DecidingAuthorityRef authority) =>
            new(Project, Bootstrap, authority,
                new(Assignment.AssignmentRef, Revision.RevisionRef, disposition), null, null, [], []);

        public ActivateAssignmentRevisionCommand Activate(AuthorityBoundary boundary, DecidingAuthorityRef authority) =>
            new(Project, Bootstrap, authority,
                new(Assignment.AssignmentRef, Revision.RevisionRef, new AssignmentRevisionContract("R2", boundary), null),
                [], []);

        public AuthorAcceptedStateCommand Author(
            DecidingAuthorityRef authority, params AcceptedContributionInstruction[] contributions) =>
            new(Project, Bootstrap, authority, [], contributions);

        public AcceptedContributionInstruction Contribution(
            ContributionScopeTarget scope, AcceptedStateContributionRef? supersedes = null) =>
            new("accepted statement", scope, supersedes, null);

        public AcceptedStateContribution AcceptedContribution(
            ContributionScopeRef scope, AuthorityDecisionRef decision, string statement) =>
            new(new(Guid.NewGuid()), statement, scope, null, decision, null);

        public AuthorityDecision DecisionWith(params AcceptedStateContribution[] contributions) =>
            new(NewDecisionRef(), Project, 1, BootstrapAuthority, [], null, null, null, null, null, contributions, Now);

        public AuthorityDecisionRef NewDecisionRef() => new(Guid.NewGuid());

        public (B1ProjectState State, Responsibility Responsibility, Assignment Assignment, AssignmentRevision Revision)
            AddAssignmentUnderNewResponsibility(AuthorityBoundary boundary)
        {
            var authorization = NewDecisionRef();
            var responsibility = new Responsibility(
                new(Guid.NewGuid()), Project, new("Other", "Other outcome", boundary), authorization, Now);
            var assignmentRef = new AssignmentRef(Guid.NewGuid());
            var revisionRef = new RevisionRef(Guid.NewGuid());
            var assignment = new Assignment(
                assignmentRef, responsibility.ResponsibilityRef, Actor.LogicalActorRef, revisionRef, authorization);
            var revision = new AssignmentRevision(
                revisionRef, assignmentRef, null, new("Other work", boundary), authorization);
            return (State(
                responsibilities: [Responsibility, responsibility],
                assignments: [Assignment, assignment],
                revisions: [Revision, revision]), responsibility, assignment, revision);
        }
    }
}
