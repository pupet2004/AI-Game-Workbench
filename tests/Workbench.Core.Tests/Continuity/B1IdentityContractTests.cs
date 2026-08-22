using Workbench.Core.Continuity;

namespace Workbench.Core.Tests.Continuity;

public sealed class B1IdentityContractTests
{
    [Fact]
    public void B1_owned_refs_reject_empty_guid_and_remain_distinct()
    {
        Assert.Throws<ArgumentException>(() => new ProjectRef(Guid.Empty));
        Assert.Throws<ArgumentException>(() => new LogicalActorRef(Guid.Empty));
        Assert.Throws<ArgumentException>(() => new ResponsibilityRef(Guid.Empty));
        Assert.Throws<ArgumentException>(() => new AssignmentRef(Guid.Empty));
        Assert.Throws<ArgumentException>(() => new RevisionRef(Guid.Empty));
        Assert.Throws<ArgumentException>(() => new AttemptRef(Guid.Empty));
        Assert.Throws<ArgumentException>(() => new SessionBindingRef(Guid.Empty));
        Assert.Throws<ArgumentException>(() => new ClaimRef(Guid.Empty));
        Assert.Throws<ArgumentException>(() => new HandoffRef(Guid.Empty));
        Assert.Throws<ArgumentException>(() => new AuthorityDecisionRef(Guid.Empty));
        Assert.Throws<ArgumentException>(() => new AcceptedStateContributionRef(Guid.Empty));

        var value = Guid.NewGuid();
        Assert.Equal(value, new ProjectRef(value).Value);
        Assert.NotEqual(typeof(ProjectRef), typeof(AssignmentRef));
        Assert.NotEqual(typeof(AssignmentRef), typeof(RevisionRef));
    }

    [Fact]
    public void External_refs_reject_blank_locator()
    {
        Assert.Throws<ArgumentException>(() => new UserPrincipalRef(" "));
        Assert.Throws<ArgumentException>(() => new ExternalSessionRef("\t"));
        Assert.Throws<ArgumentException>(() => new EvidenceRef(string.Empty));

        Assert.Equal("user:42", new UserPrincipalRef("user:42").Value);
        Assert.Equal("codex:thread/abc", new ExternalSessionRef("codex:thread/abc").Value);
        Assert.Equal("legacy:task/17", new EvidenceRef("legacy:task/17").Value);
    }

    [Fact]
    public void RoleKind_and_capability_sets_are_closed()
    {
        Assert.Equal(["Leader", "Worker", "Reviewer"], Enum.GetNames<RoleKind>());
        Assert.Equal(
            [
                "EstablishLogicalActor",
                "EstablishResponsibility",
                "DelegateAssignment",
                "DecideAssignmentDisposition",
                "ActivateAssignmentRevision",
                "AcceptProjectStateContribution",
                "AcceptResponsibilityStateContribution",
                "AcceptAssignmentStateContribution"
            ],
            Enum.GetNames<B1AuthorityCapability>());
        Assert.Equal(["Accepted", "Rejected", "RevisionRequired"], Enum.GetNames<AssignmentDisposition>());
        Assert.Equal(["Created", "Adopted"], Enum.GetNames<B1GovernanceOrigin>());

        Assert.Throws<ArgumentOutOfRangeException>(() => AuthorityBoundary.Create([(B1AuthorityCapability)999]));
    }

    [Fact]
    public void Responsibility_has_no_owner_property()
    {
        var responsibility = new Responsibility(
            NewResponsibilityRef(),
            NewProjectRef(),
            new ResponsibilityContract("Preserve continuity", "Recover accepted state", AuthorityBoundary.Empty),
            NewDecisionRef(),
            DateTimeOffset.UtcNow);

        Assert.DoesNotContain(
            typeof(Responsibility).GetProperties(),
            property => property.Name.Contains("Owner", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Preserve continuity", responsibility.Contract.Obligation);
        Assert.Equal("Recover accepted state", responsibility.Contract.ExpectedOutcome);
    }

    [Fact]
    public void Assignment_assignee_and_initial_revision_are_constructor_only()
    {
        var assignee = NewActorRef();
        var initialRevision = NewRevisionRef();
        var assignment = new Assignment(
            NewAssignmentRef(),
            NewResponsibilityRef(),
            assignee,
            initialRevision,
            NewDecisionRef());

        Assert.Equal(assignee, assignment.AssigneeActorRef);
        Assert.Equal(initialRevision, assignment.InitialRevisionRef);
        Assert.Null(typeof(Assignment).GetProperty(nameof(Assignment.AssigneeActorRef))!.SetMethod);
        Assert.Null(typeof(Assignment).GetProperty(nameof(Assignment.InitialRevisionRef))!.SetMethod);
    }

    [Fact]
    public void Attempt_requires_one_assignment_and_revision()
    {
        var attemptRef = NewAttemptRef();
        var assignmentRef = NewAssignmentRef();
        var revisionRef = NewRevisionRef();
        var createdAt = DateTimeOffset.UtcNow;
        var attempt = new Attempt(attemptRef, assignmentRef, revisionRef, createdAt);

        Assert.Equal(assignmentRef, attempt.AssignmentRef);
        Assert.Equal(revisionRef, attempt.EffectiveRevisionRef);
        Assert.Throws<ArgumentException>(() => new Attempt(attemptRef, default, revisionRef, createdAt));
        Assert.Throws<ArgumentException>(() => new Attempt(attemptRef, assignmentRef, default, createdAt));
    }

    [Fact]
    public void SessionBinding_contains_no_provider_or_execution_state()
    {
        var binding = new SessionBinding(
            NewBindingRef(),
            NewAttemptRef(),
            NewActorRef(),
            new ExternalSessionRef("opaque:session/42"),
            DateTimeOffset.UtcNow);

        Assert.Equal("opaque:session/42", binding.ExternalSessionRef.Value);
        var forbiddenFragments = new[] { "Provider", "Model", "Runtime", "Worktree", "Git", "Status", "Active", "Closed" };
        Assert.DoesNotContain(
            typeof(SessionBinding).GetProperties(),
            property => forbiddenFragments.Any(fragment => property.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Delegated_boundary_defaults_empty_and_must_be_subset_of_maximum()
    {
        var maximum = AuthorityBoundary.Create(
            [
                B1AuthorityCapability.DelegateAssignment,
                B1AuthorityCapability.AcceptAssignmentStateContribution,
                B1AuthorityCapability.DelegateAssignment
            ]);
        var defaultRevision = new AssignmentRevisionContract("Implement bounded work");
        var delegated = AuthorityBoundary.Create([B1AuthorityCapability.DelegateAssignment]);
        var revision = new AssignmentRevisionContract("Implement bounded work", delegated);

        Assert.Equal(AuthorityBoundary.Empty, defaultRevision.DelegatedAuthorityBoundary);
        Assert.True(revision.DelegatedAuthorityBoundary.IsSubsetOf(maximum));
        Assert.True(maximum.Contains(B1AuthorityCapability.AcceptAssignmentStateContribution));
        Assert.Equal(
            AuthorityBoundary.Create([B1AuthorityCapability.AcceptAssignmentStateContribution]),
            maximum.Except(delegated));
        Assert.Equal(2, maximum.Capabilities.Count);
        Assert.Throws<NotSupportedException>(() =>
            ((ICollection<B1AuthorityCapability>)maximum.Capabilities).Add(B1AuthorityCapability.EstablishLogicalActor));
    }

    [Fact]
    public void Project_governance_and_durable_entities_preserve_only_approved_identity_fields()
    {
        var projectRef = NewProjectRef();
        var bootstrap = new UserPrincipalRef("user:bootstrap");
        var adoptedAt = DateTimeOffset.UtcNow;
        var governance = new ProjectGovernance(projectRef, bootstrap, B1GovernanceOrigin.Adopted, adoptedAt, 7);
        var actorRef = NewActorRef();
        var decisionRef = NewDecisionRef();
        var actor = new LogicalActor(actorRef, projectRef, RoleKind.Worker, decisionRef, adoptedAt);
        var responsibilityRef = NewResponsibilityRef();
        var responsibility = new Responsibility(
            responsibilityRef,
            projectRef,
            new ResponsibilityContract("Maintain release", "Releases remain reproducible", AuthorityBoundary.Empty),
            decisionRef,
            adoptedAt);
        var assignmentRef = NewAssignmentRef();
        var revisionRef = NewRevisionRef();
        var revision = new AssignmentRevision(
            revisionRef,
            assignmentRef,
            priorRevisionRef: null,
            new AssignmentRevisionContract("Prepare release"),
            decisionRef);

        Assert.Equal(bootstrap, governance.BootstrapPrincipalRef);
        Assert.Equal(7, governance.LastProjectCommitSequence);
        Assert.Equal(actorRef, actor.LogicalActorRef);
        Assert.Equal(projectRef, responsibility.ProjectRef);
        Assert.Null(revision.PriorRevisionRef);
        Assert.Equal("Prepare release", revision.Contract.WorkContract);
        Assert.Throws<ArgumentException>(() =>
            new ProjectGovernance(projectRef, bootstrap, B1GovernanceOrigin.Created, adoptedAt, 0));
    }

    private static ProjectRef NewProjectRef() => new(Guid.NewGuid());
    private static LogicalActorRef NewActorRef() => new(Guid.NewGuid());
    private static ResponsibilityRef NewResponsibilityRef() => new(Guid.NewGuid());
    private static AssignmentRef NewAssignmentRef() => new(Guid.NewGuid());
    private static RevisionRef NewRevisionRef() => new(Guid.NewGuid());
    private static AttemptRef NewAttemptRef() => new(Guid.NewGuid());
    private static SessionBindingRef NewBindingRef() => new(Guid.NewGuid());
    private static AuthorityDecisionRef NewDecisionRef() => new(Guid.NewGuid());
}
