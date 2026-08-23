using Workbench.Core.Continuity;

namespace Workbench.Core.Tests.Continuity;

public sealed class B1ProjectionTests
{
    [Fact]
    public void Accepted_state_contains_all_authoritative_current_projection()
    {
        var f = Fixture.Create();
        var disposition = new RevisionDispositionRecord(f.Assignment.AssignmentRef, f.Revision.RevisionRef,
            AssignmentDisposition.Accepted, f.Decision.DecisionRef);
        var projection = B1Projector.Build(f.State(dispositions: [disposition]));

        Assert.Equal(f.Actor, projection.AcceptedProjectState.LogicalActors[f.Actor.LogicalActorRef]);
        Assert.Equal(f.Responsibility, projection.AcceptedProjectState.Responsibilities[f.Responsibility.ResponsibilityRef]);
        Assert.Equal(f.Assignment, projection.AcceptedProjectState.Assignments[f.Assignment.AssignmentRef]);
        Assert.Equal(f.Revision, projection.AcceptedProjectState.Revisions[f.Revision.RevisionRef]);
        Assert.Equal(disposition, projection.AcceptedProjectState.RevisionDispositions[f.Revision.RevisionRef]);
        Assert.Equal(f.Revision.RevisionRef, projection.AcceptedProjectState.CurrentEffectiveRevisionRefs[f.Assignment.AssignmentRef]);
        Assert.Contains(f.Assignment.AssignmentRef, projection.AcceptedProjectState.CurrentDelegationAssignments);
    }

    [Fact]
    public void Initial_revision_is_current_until_one_authorized_successor()
    {
        var f = Fixture.Create();
        var next = new AssignmentRevision(new RevisionRef(Guid.NewGuid()), f.Assignment.AssignmentRef,
            f.Revision.RevisionRef, new AssignmentRevisionContract("R2"), f.Decision.DecisionRef);
        var projection = B1Projector.Build(f.State(revisions: [f.Revision, next]));
        Assert.Equal(next.RevisionRef, projection.AcceptedProjectState.CurrentEffectiveRevisionRefs[f.Assignment.AssignmentRef]);
    }

    [Fact]
    public void Each_revision_has_zero_or_one_disposition()
    {
        var f = Fixture.Create();
        var first = new RevisionDispositionRecord(f.Assignment.AssignmentRef, f.Revision.RevisionRef, AssignmentDisposition.Accepted, f.Decision.DecisionRef);
        var second = first with { Disposition = AssignmentDisposition.Rejected };
        Assert.Throws<InvalidDataException>(() => B1Projector.Build(f.State(dispositions: [first, second])));
    }

    [Fact]
    public void Current_delegation_means_not_replaced()
    {
        var f = Fixture.Create();
        var replacement = f.NewAssignment();
        var replacementRevision = f.NewInitialRevision(replacement.AssignmentRef);
        var replacingDecision = f.DecisionWithDelegation(replacement, replacementRevision, f.Assignment.AssignmentRef, 2);
        var projection = B1Projector.Build(f.State(assignments: [f.Assignment, replacement], revisions: [f.Revision, replacementRevision], decisions: [f.Decision, replacingDecision]));
        Assert.DoesNotContain(f.Assignment.AssignmentRef, projection.AcceptedProjectState.CurrentDelegationAssignments);
        Assert.Contains(replacement.AssignmentRef, projection.AcceptedProjectState.CurrentDelegationAssignments);
    }

    [Fact]
    public void Effective_fulfillment_requires_current_unresolved_revision()
    {
        var f = Fixture.Create();
        var accepted = new RevisionDispositionRecord(f.Assignment.AssignmentRef, f.Revision.RevisionRef, AssignmentDisposition.Accepted, f.Decision.DecisionRef);
        var projection = B1Projector.Build(f.State(dispositions: [accepted]));
        Assert.DoesNotContain(f.Assignment.AssignmentRef, projection.EffectiveFulfillmentAssignments);
    }

    [Fact]
    public void Contributions_coexist_without_supersession()
    {
        var f = Fixture.Create();
        var a = f.Contribution("A", null);
        var x = f.Contribution("X", null);
        var projection = B1Projector.Build(f.State(decisions: [f.DecisionWithContributions(1, a, x)]));
        Assert.Equal(2, projection.AcceptedProjectState.CurrentContributions.Count);
    }

    [Fact]
    public void Explicit_supersession_removes_only_its_current_target()
    {
        var f = Fixture.Create();
        var a = f.Contribution("A", null);
        var x = f.Contribution("X", null);
        var b = f.Contribution("B", a.ContributionRef);
        var projection = B1Projector.Build(f.State(decisions: [f.DecisionWithContributions(1, a, x), f.DecisionWithContributions(2, b)]));
        Assert.Equal(["B", "X"], projection.AcceptedProjectState.CurrentContributions.Select(c => c.Statement).Order());
    }

    [Fact]
    public void Stored_attempt_can_remain_when_effective_attempt_is_null()
    {
        var f = Fixture.Create();
        var attempt = new Attempt(new AttemptRef(Guid.NewGuid()), f.Assignment.AssignmentRef, f.Revision.RevisionRef, DateTimeOffset.UtcNow);
        var disposition = new RevisionDispositionRecord(f.Assignment.AssignmentRef, f.Revision.RevisionRef, AssignmentDisposition.Accepted, f.Decision.DecisionRef);
        var state = f.State(dispositions: [disposition], attempts: [attempt], assignmentRouting: [new(f.Assignment.AssignmentRef, attempt.AttemptRef)]);
        var projection = B1Projector.Build(state);
        Assert.Equal(attempt.AttemptRef, projection.StoredAttemptSelections[f.Assignment.AssignmentRef]);
        Assert.Null(projection.EffectiveCurrentAttemptRefs[f.Assignment.AssignmentRef]);
    }

    [Fact]
    public void Effective_handoff_and_binding_require_effective_parent_attempt()
    {
        var f = Fixture.Create();
        var attempt = new Attempt(new(Guid.NewGuid()), f.Assignment.AssignmentRef, f.Revision.RevisionRef, DateTimeOffset.UtcNow);
        var handoff = new Handoff(new(Guid.NewGuid()), attempt.AttemptRef, new(Guid.NewGuid()), [], [], [], [], [], DateTimeOffset.UtcNow);
        var binding = new SessionBinding(new(Guid.NewGuid()), attempt.AttemptRef, f.Actor.LogicalActorRef, new("session:1"), DateTimeOffset.UtcNow);
        var state = f.State(attempts: [attempt], handoffs: [handoff], bindings: [binding],
            assignmentRouting: [new(f.Assignment.AssignmentRef, attempt.AttemptRef)],
            attemptRouting: [new(attempt.AttemptRef, handoff.HandoffRef, binding.SessionBindingRef)]);
        var projection = B1Projector.Build(state);
        Assert.Equal(handoff.HandoffRef, projection.EffectiveCurrentHandoffRefs[attempt.AttemptRef]);
        Assert.Equal(binding.SessionBindingRef, projection.EffectiveCurrentBindingRefs[attempt.AttemptRef]);
    }

    [Fact]
    public void CreatedAt_never_changes_decision_order()
    {
        var f = Fixture.Create();
        var a = f.Contribution("A", null);
        var b = f.Contribution("B", a.ContributionRef);
        var laterSequenceOldClock = f.DecisionWithContributionsAt(2, DateTimeOffset.MinValue, b);
        var earlierSequenceNewClock = f.DecisionWithContributionsAt(1, DateTimeOffset.MaxValue, a);
        var projection = B1Projector.Build(f.State(decisions: [laterSequenceOldClock, earlierSequenceNewClock]));
        Assert.Equal("B", Assert.Single(projection.AcceptedProjectState.CurrentContributions).Statement);
    }

    private sealed record Fixture(ProjectGovernance Governance, LogicalActor Actor, Responsibility Responsibility,
        Assignment Assignment, AssignmentRevision Revision, AuthorityDecision Decision)
    {
        public static Fixture Create()
        {
            var project = new ProjectRef(Guid.NewGuid()); var decisionRef = new AuthorityDecisionRef(Guid.NewGuid());
            var actor = new LogicalActor(new(Guid.NewGuid()), project, RoleKind.Worker, decisionRef, DateTimeOffset.UtcNow);
            var responsibility = new Responsibility(new(Guid.NewGuid()), project, new("O", "E", AuthorityBoundary.Empty), decisionRef, DateTimeOffset.UtcNow);
            var assignmentRef = new AssignmentRef(Guid.NewGuid()); var revisionRef = new RevisionRef(Guid.NewGuid());
            var assignment = new Assignment(assignmentRef, responsibility.ResponsibilityRef, actor.LogicalActorRef, revisionRef, decisionRef);
            var revision = new AssignmentRevision(revisionRef, assignmentRef, null, new("R1"), decisionRef);
            var governance = new ProjectGovernance(project, new("user:1"), B1GovernanceOrigin.Created, null, 0);
            var decision = EmptyDecision(project, decisionRef, 1);
            return new(governance, actor, responsibility, assignment, revision, decision);
        }

        public B1ProjectState State(IReadOnlyList<Assignment>? assignments = null, IReadOnlyList<AssignmentRevision>? revisions = null,
            IReadOnlyList<RevisionDispositionRecord>? dispositions = null, IReadOnlyList<Attempt>? attempts = null,
            IReadOnlyList<AuthorityDecision>? decisions = null, IReadOnlyList<AssignmentRoutingSelection>? assignmentRouting = null,
            IReadOnlyList<Handoff>? handoffs = null, IReadOnlyList<SessionBinding>? bindings = null,
            IReadOnlyList<AttemptRoutingSelection>? attemptRouting = null) =>
            new(Governance, [Actor], [Responsibility], assignments ?? [Assignment], revisions ?? [Revision], dispositions ?? [],
                attempts ?? [], bindings ?? [], [], handoffs ?? [], decisions ?? [Decision], assignmentRouting ?? [], attemptRouting ?? []);

        public Assignment NewAssignment() => new(new(Guid.NewGuid()), Responsibility.ResponsibilityRef, Actor.LogicalActorRef, new(Guid.NewGuid()), Decision.DecisionRef);
        public AssignmentRevision NewInitialRevision(AssignmentRef assignment) => new(new(Guid.NewGuid()), assignment, null, new("new"), Decision.DecisionRef);
        public AcceptedStateContribution Contribution(string text, AcceptedStateContributionRef? supersedes) =>
            new(new(Guid.NewGuid()), text, new ContributionScopeRef.Project(Governance.ProjectRef), supersedes, Decision.DecisionRef, null);
        public AuthorityDecision DecisionWithContributions(long sequence, params AcceptedStateContribution[] contributions) =>
            DecisionWithContributionsAt(sequence, DateTimeOffset.UtcNow, contributions);
        public AuthorityDecision DecisionWithContributionsAt(long sequence, DateTimeOffset createdAt, params AcceptedStateContribution[] contributions) =>
            new(new(Guid.NewGuid()), Governance.ProjectRef, sequence, new DecidingAuthorityRef.UserPrincipal(new("user:1")), [],
                null, null, null, null, null, contributions, createdAt);
        public AuthorityDecision DecisionWithDelegation(Assignment assignment, AssignmentRevision revision, AssignmentRef replaces, long sequence) =>
            new(new(Guid.NewGuid()), Governance.ProjectRef, sequence, new DecidingAuthorityRef.UserPrincipal(new("user:1")), [],
                null, null, null, null, new(assignment, revision, replaces), [], DateTimeOffset.UtcNow);
        private static AuthorityDecision EmptyDecision(ProjectRef project, AuthorityDecisionRef id, long sequence) =>
            new(id, project, sequence, new DecidingAuthorityRef.UserPrincipal(new("user:1")), [], null, null, null, null, null, [], DateTimeOffset.UtcNow);
    }
}
