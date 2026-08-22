namespace Workbench.Core.Continuity;

public enum B1FailureCode
{
    InvalidDecisionShape,
    InvalidReference,
    WrongProject,
    NotAuthorized,
    AuthorityAmplification,
    StaleRevision,
    AlreadyDispositioned,
    StaleReplacement,
    StaleSupersession,
    StaleRoutingSelection,
    ConcurrentProjectChange,
    LegacyProjectNotEligible,
    GovernanceAlreadyExists
}

public sealed class B1CommandException(B1FailureCode code, string message) : InvalidOperationException(message)
{
    public B1FailureCode Code { get; } = code;
}

public abstract record ResponsibilityTarget
{
    private ResponsibilityTarget() { }
    public sealed record Existing(ResponsibilityRef ResponsibilityRef) : ResponsibilityTarget;
    public sealed record EstablishedByThisDecision : ResponsibilityTarget;
}

public abstract record AssignmentAssigneeTarget
{
    private AssignmentAssigneeTarget() { }
    public sealed record Existing(LogicalActorRef LogicalActorRef) : AssignmentAssigneeTarget;
    public sealed record EstablishedByThisDecision : AssignmentAssigneeTarget;
}

public abstract record ContributionScopeTarget
{
    private ContributionScopeTarget() { }
    public sealed record Project(ProjectRef ProjectRef) : ContributionScopeTarget;

    public abstract record Responsibility : ContributionScopeTarget
    {
        private Responsibility() { }
        public sealed record Existing(ResponsibilityRef ResponsibilityRef) : Responsibility;
        public sealed record EstablishedByThisDecision : Responsibility;
    }

    public abstract record Assignment : ContributionScopeTarget
    {
        private Assignment() { }
        public sealed record Existing(AssignmentRef AssignmentRef) : Assignment;
        public sealed record DelegatedByThisDecision : Assignment;
    }
}

public sealed record AcceptedContributionInstruction(
    string Statement,
    ContributionScopeTarget Scope,
    AcceptedStateContributionRef? SupersedesContributionRef,
    ClaimRef? SourceClaimRef);

public sealed record AssignmentDispositionInstruction(
    AssignmentRef AssignmentRef,
    RevisionRef EffectiveRevisionRef,
    AssignmentDisposition Disposition);

public sealed record RevisionActivationInstruction(
    AssignmentRef AssignmentRef,
    RevisionRef ExpectedCurrentRevisionRef,
    AssignmentRevisionContract NewRevisionContract,
    ClaimRef? SourceClaimRef);

public sealed record AssignmentDelegationInstruction(
    ResponsibilityTarget ResponsibilityTarget,
    AssignmentAssigneeTarget AssigneeTarget,
    AssignmentRevisionContract InitialRevisionContract,
    AssignmentRef? ReplacesAssignmentRef);

public sealed record AssignmentReplacementInstruction(
    AssignmentRef ReplacesAssignmentRef,
    AssignmentAssigneeTarget AssigneeTarget,
    RoleKind? EstablishedAssigneeRoleKind,
    AssignmentRevisionContract InitialRevisionContract);

public sealed record CreateAttemptCommand(
    ProjectRef ProjectRef, UserPrincipalRef AuthenticatedOperatorRef, AttemptRef AttemptRef,
    AssignmentRef AssignmentRef, RevisionRef EffectiveRevisionRef, DateTimeOffset CreatedAt);

public sealed record SelectCurrentAttemptCommand(
    ProjectRef ProjectRef, UserPrincipalRef AuthenticatedOperatorRef, AssignmentRef AssignmentRef,
    AttemptRef? ExpectedStoredAttemptRef, AttemptRef? SelectedAttemptRef);

public sealed record RecordClaimCommand(
    ProjectRef ProjectRef, UserPrincipalRef AuthenticatedOperatorRef, ClaimRef ClaimRef,
    ClaimantRef ClaimantRef, SessionBindingRef? SourceSessionBindingRef, ClaimPayload Payload,
    IReadOnlyList<EvidenceRef> EvidenceRefs, DateTimeOffset CreatedAt);

public sealed record CreateHandoffCommand(
    ProjectRef ProjectRef, UserPrincipalRef AuthenticatedOperatorRef, Handoff Handoff);

public sealed record SelectContinuationHandoffCommand(
    ProjectRef ProjectRef, UserPrincipalRef AuthenticatedOperatorRef, AttemptRef AttemptRef,
    HandoffRef? ExpectedStoredHandoffRef, HandoffRef? SelectedHandoffRef);

public sealed record CreateSessionBindingCommand(
    ProjectRef ProjectRef, UserPrincipalRef AuthenticatedOperatorRef, SessionBinding SessionBinding);

public sealed record SelectCurrentSessionBindingCommand(
    ProjectRef ProjectRef, UserPrincipalRef AuthenticatedOperatorRef, AttemptRef AttemptRef,
    SessionBindingRef? ExpectedStoredSessionBindingRef, SessionBindingRef SelectedSessionBindingRef);

public sealed record ClearCurrentSessionBindingCommand(
    ProjectRef ProjectRef, UserPrincipalRef AuthenticatedOperatorRef, AttemptRef AttemptRef,
    SessionBindingRef? ExpectedStoredSessionBindingRef);

public sealed record EstablishLogicalActorCommand(
    ProjectRef ProjectRef, UserPrincipalRef AuthenticatedOperatorRef, DecidingAuthorityRef DecidingAuthorityRef,
    RoleKind RoleKind, IReadOnlyList<ConsideredRef> ConsideredRefs,
    IReadOnlyList<AcceptedContributionInstruction> Contributions);

public sealed record EstablishResponsibilityCommand(
    ProjectRef ProjectRef, UserPrincipalRef AuthenticatedOperatorRef, DecidingAuthorityRef DecidingAuthorityRef,
    ResponsibilityContract Contract, AssignmentDelegationInstruction? InitialDelegation,
    RoleKind? EstablishedAssigneeRoleKind, IReadOnlyList<ConsideredRef> ConsideredRefs,
    IReadOnlyList<AcceptedContributionInstruction> Contributions);

public sealed record DelegateAssignmentCommand(
    ProjectRef ProjectRef, UserPrincipalRef AuthenticatedOperatorRef, DecidingAuthorityRef DecidingAuthorityRef,
    AssignmentDelegationInstruction Delegation, RoleKind? EstablishedAssigneeRoleKind,
    IReadOnlyList<ConsideredRef> ConsideredRefs, IReadOnlyList<AcceptedContributionInstruction> Contributions);

public sealed record DecideAssignmentCommand(
    ProjectRef ProjectRef, UserPrincipalRef AuthenticatedOperatorRef, DecidingAuthorityRef DecidingAuthorityRef,
    AssignmentDispositionInstruction Disposition, RevisionActivationInstruction? Activation,
    AssignmentReplacementInstruction? Replacement, IReadOnlyList<ConsideredRef> ConsideredRefs,
    IReadOnlyList<AcceptedContributionInstruction> Contributions);

public sealed record ActivateAssignmentRevisionCommand(
    ProjectRef ProjectRef, UserPrincipalRef AuthenticatedOperatorRef, DecidingAuthorityRef DecidingAuthorityRef,
    RevisionActivationInstruction Activation, IReadOnlyList<ConsideredRef> ConsideredRefs,
    IReadOnlyList<AcceptedContributionInstruction> Contributions);

public sealed record AuthorAcceptedStateCommand(
    ProjectRef ProjectRef, UserPrincipalRef AuthenticatedOperatorRef, DecidingAuthorityRef DecidingAuthorityRef,
    IReadOnlyList<ConsideredRef> ConsideredRefs, IReadOnlyList<AcceptedContributionInstruction> Contributions);

public sealed record LogicalActorEstablishmentEffect(LogicalActor LogicalActor);
public sealed record ResponsibilityEstablishmentEffect(Responsibility Responsibility);
public sealed record AssignmentDispositionEffect(
    AssignmentRef AssignmentRef, RevisionRef EffectiveRevisionRef, AssignmentDisposition Disposition);
public sealed record RevisionActivationEffect(AssignmentRevision Revision, ClaimRef? SourceClaimRef);
public sealed record AssignmentDelegationEffect(
    Assignment Assignment, AssignmentRevision InitialRevision, AssignmentRef? ReplacesAssignmentRef);

public sealed record AuthorityDecision
{
    public AuthorityDecision(
        AuthorityDecisionRef decisionRef, ProjectRef projectRef, long projectCommitSequence,
        DecidingAuthorityRef decidingAuthorityRef, IReadOnlyList<ConsideredRef> consideredRefs,
        LogicalActorEstablishmentEffect? logicalActorEstablishmentEffect,
        ResponsibilityEstablishmentEffect? responsibilityEstablishmentEffect,
        AssignmentDispositionEffect? assignmentDispositionEffect,
        RevisionActivationEffect? revisionActivationEffect,
        AssignmentDelegationEffect? assignmentDelegationEffect,
        IReadOnlyList<AcceptedStateContribution> acceptedStateContributions, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(decidingAuthorityRef);
        ArgumentNullException.ThrowIfNull(consideredRefs);
        ArgumentNullException.ThrowIfNull(acceptedStateContributions);
        DecisionRef = decisionRef;
        ProjectRef = projectRef;
        ProjectCommitSequence = projectCommitSequence;
        DecidingAuthorityRef = decidingAuthorityRef;
        ConsideredRefs = Array.AsReadOnly(consideredRefs.ToArray());
        LogicalActorEstablishmentEffect = logicalActorEstablishmentEffect;
        ResponsibilityEstablishmentEffect = responsibilityEstablishmentEffect;
        AssignmentDispositionEffect = assignmentDispositionEffect;
        RevisionActivationEffect = revisionActivationEffect;
        AssignmentDelegationEffect = assignmentDelegationEffect;
        AcceptedStateContributions = Array.AsReadOnly(acceptedStateContributions.ToArray());
        CreatedAt = createdAt;
    }

    public AuthorityDecisionRef DecisionRef { get; }
    public ProjectRef ProjectRef { get; }
    public long ProjectCommitSequence { get; }
    public DecidingAuthorityRef DecidingAuthorityRef { get; }
    public IReadOnlyList<ConsideredRef> ConsideredRefs { get; }
    public LogicalActorEstablishmentEffect? LogicalActorEstablishmentEffect { get; }
    public ResponsibilityEstablishmentEffect? ResponsibilityEstablishmentEffect { get; }
    public AssignmentDispositionEffect? AssignmentDispositionEffect { get; }
    public RevisionActivationEffect? RevisionActivationEffect { get; }
    public AssignmentDelegationEffect? AssignmentDelegationEffect { get; }
    public IReadOnlyList<AcceptedStateContribution> AcceptedStateContributions { get; }
    public DateTimeOffset CreatedAt { get; }
}
