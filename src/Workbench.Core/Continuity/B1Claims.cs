namespace Workbench.Core.Continuity;

public abstract record ClaimantRef
{
    private ClaimantRef() { }
    public sealed record UserPrincipal(UserPrincipalRef UserPrincipalRef) : ClaimantRef;
    public sealed record LogicalActor(LogicalActorRef LogicalActorRef) : ClaimantRef;
}

public abstract record DecidingAuthorityRef
{
    private DecidingAuthorityRef() { }
    public sealed record UserPrincipal(UserPrincipalRef UserPrincipalRef) : DecidingAuthorityRef;
    public sealed record LogicalActor(LogicalActorRef LogicalActorRef) : DecidingAuthorityRef;
}

public abstract record ContributionScopeRef
{
    private ContributionScopeRef() { }
    public sealed record Project(ProjectRef ProjectRef) : ContributionScopeRef;
    public sealed record Responsibility(ResponsibilityRef ResponsibilityRef) : ContributionScopeRef;
    public sealed record Assignment(AssignmentRef AssignmentRef) : ContributionScopeRef;
}

public abstract record ConsideredRef
{
    private ConsideredRef() { }
    public sealed record Claim(ClaimRef ClaimRef) : ConsideredRef;
    public sealed record Handoff(HandoffRef HandoffRef) : ConsideredRef;
    public sealed record Evidence(EvidenceRef EvidenceRef) : ConsideredRef;
    public sealed record EvolutionCandidate : ConsideredRef
    {
        public EvolutionCandidate(Guid candidateId)
        {
            if (candidateId == Guid.Empty)
                throw new ArgumentException("Evolution Candidate identity is required.", nameof(candidateId));
            CandidateId = candidateId;
        }

        public Guid CandidateId { get; }
    }
}

public abstract record ClaimPayload
{
    private ClaimPayload() { }

    public sealed record Result : ClaimPayload
    {
        public Result(string statement) { ArgumentException.ThrowIfNullOrWhiteSpace(statement); Statement = statement; }
        public string Statement { get; }
    }

    public sealed record Validation : ClaimPayload
    {
        public Validation(string statement) { ArgumentException.ThrowIfNullOrWhiteSpace(statement); Statement = statement; }
        public string Statement { get; }
    }

    public sealed record UnresolvedIssue : ClaimPayload
    {
        public UnresolvedIssue(string statement) { ArgumentException.ThrowIfNullOrWhiteSpace(statement); Statement = statement; }
        public string Statement { get; }
    }

    public sealed record ProposedStateContribution : ClaimPayload
    {
        public ProposedStateContribution(string statement, ContributionScopeRef scope, AcceptedStateContributionRef? ProposedSupersedes)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(statement);
            ArgumentNullException.ThrowIfNull(scope);
            Statement = statement;
            Scope = scope;
            this.ProposedSupersedes = ProposedSupersedes;
        }

        public string Statement { get; }
        public ContributionScopeRef Scope { get; }
        public AcceptedStateContributionRef? ProposedSupersedes { get; }
    }

    public sealed record ProposedAssignmentRevision : ClaimPayload
    {
        public ProposedAssignmentRevision(AssignmentRef assignmentRef, RevisionRef baseEffectiveRevisionRef, AssignmentRevisionContract proposedContract)
        {
            B1ContractValidation.Require(assignmentRef, nameof(assignmentRef));
            B1ContractValidation.Require(baseEffectiveRevisionRef, nameof(baseEffectiveRevisionRef));
            ArgumentNullException.ThrowIfNull(proposedContract);
            AssignmentRef = assignmentRef;
            BaseEffectiveRevisionRef = baseEffectiveRevisionRef;
            ProposedContract = proposedContract;
        }

        public AssignmentRef AssignmentRef { get; }
        public RevisionRef BaseEffectiveRevisionRef { get; }
        public AssignmentRevisionContract ProposedContract { get; }
    }
}

public sealed record Claim
{
    public Claim(ClaimRef claimRef, ProjectRef projectRef, ClaimantRef claimantRef,
        SessionBindingRef? sourceSessionBindingRef, ClaimPayload payload,
        IReadOnlyList<EvidenceRef> evidenceRefs, DateTimeOffset createdAt)
    {
        if (claimRef.Value == Guid.Empty) throw new ArgumentException("ClaimRef is required.", nameof(claimRef));
        B1ContractValidation.Require(projectRef, nameof(projectRef));
        ArgumentNullException.ThrowIfNull(claimantRef);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(evidenceRefs);
        if (claimantRef is ClaimantRef.UserPrincipal && sourceSessionBindingRef is not null)
            throw new ArgumentException("UserPrincipal Claims cannot carry SessionBinding provenance.", nameof(sourceSessionBindingRef));
        ClaimRef = claimRef;
        ProjectRef = projectRef;
        ClaimantRef = claimantRef;
        SourceSessionBindingRef = sourceSessionBindingRef;
        Payload = payload;
        EvidenceRefs = Array.AsReadOnly(evidenceRefs.ToArray());
        CreatedAt = createdAt;
    }

    public ClaimRef ClaimRef { get; }
    public ProjectRef ProjectRef { get; }
    public ClaimantRef ClaimantRef { get; }
    public SessionBindingRef? SourceSessionBindingRef { get; }
    public ClaimPayload Payload { get; }
    public IReadOnlyList<EvidenceRef> EvidenceRefs { get; }
    public DateTimeOffset CreatedAt { get; }
}

public sealed record Handoff
{
    public Handoff(HandoffRef handoffRef, AttemptRef attemptRef, ClaimRef resultClaimRef,
        IReadOnlyList<ClaimRef> validationClaimRefs, IReadOnlyList<ClaimRef> unresolvedIssueClaimRefs,
        IReadOnlyList<ClaimRef> proposedContributionClaimRefs, IReadOnlyList<ClaimRef> proposedAssignmentRevisionClaimRefs,
        IReadOnlyList<EvidenceRef> evidenceRefs, DateTimeOffset createdAt)
    {
        if (handoffRef.Value == Guid.Empty) throw new ArgumentException("HandoffRef is required.", nameof(handoffRef));
        B1ContractValidation.Require(attemptRef, nameof(attemptRef));
        if (resultClaimRef.Value == Guid.Empty) throw new ArgumentException("A primary Result Claim is required.", nameof(resultClaimRef));
        HandoffRef = handoffRef;
        AttemptRef = attemptRef;
        ResultClaimRef = resultClaimRef;
        ValidationClaimRefs = Copy(validationClaimRefs);
        UnresolvedIssueClaimRefs = Copy(unresolvedIssueClaimRefs);
        ProposedContributionClaimRefs = Copy(proposedContributionClaimRefs);
        ProposedAssignmentRevisionClaimRefs = Copy(proposedAssignmentRevisionClaimRefs);
        ArgumentNullException.ThrowIfNull(evidenceRefs);
        EvidenceRefs = Array.AsReadOnly(evidenceRefs.ToArray());
        CreatedAt = createdAt;
    }

    public HandoffRef HandoffRef { get; }
    public AttemptRef AttemptRef { get; }
    public ClaimRef ResultClaimRef { get; }
    public IReadOnlyList<ClaimRef> ValidationClaimRefs { get; }
    public IReadOnlyList<ClaimRef> UnresolvedIssueClaimRefs { get; }
    public IReadOnlyList<ClaimRef> ProposedContributionClaimRefs { get; }
    public IReadOnlyList<ClaimRef> ProposedAssignmentRevisionClaimRefs { get; }
    public IReadOnlyList<EvidenceRef> EvidenceRefs { get; }
    public DateTimeOffset CreatedAt { get; }

    private static IReadOnlyList<ClaimRef> Copy(IReadOnlyList<ClaimRef> refs)
    { ArgumentNullException.ThrowIfNull(refs); return Array.AsReadOnly(refs.ToArray()); }
}

public sealed record AcceptedStateContribution(
    AcceptedStateContributionRef ContributionRef,
    string Statement,
    ContributionScopeRef Scope,
    AcceptedStateContributionRef? SupersedesContributionRef,
    AuthorityDecisionRef AuthorityDecisionRef,
    ClaimRef? SourceClaimRef);
