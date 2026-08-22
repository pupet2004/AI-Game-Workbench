namespace Workbench.Core.Continuity;

public sealed record RevisionDispositionRecord(AssignmentRef AssignmentRef, RevisionRef RevisionRef,
    AssignmentDisposition Disposition, AuthorityDecisionRef AuthorityDecisionRef);

public sealed record AssignmentRoutingSelection(AssignmentRef AssignmentRef, AttemptRef? SelectedAttemptRef);

public sealed record AttemptRoutingSelection(AttemptRef AttemptRef, HandoffRef? SelectedHandoffRef,
    SessionBindingRef? SelectedSessionBindingRef);

public sealed record B1ProjectState(
    ProjectGovernance Governance,
    IReadOnlyList<LogicalActor> LogicalActors,
    IReadOnlyList<Responsibility> Responsibilities,
    IReadOnlyList<Assignment> Assignments,
    IReadOnlyList<AssignmentRevision> Revisions,
    IReadOnlyList<RevisionDispositionRecord> RevisionDispositions,
    IReadOnlyList<Attempt> Attempts,
    IReadOnlyList<SessionBinding> SessionBindings,
    IReadOnlyList<Claim> Claims,
    IReadOnlyList<Handoff> Handoffs,
    IReadOnlyList<AuthorityDecision> AuthorityDecisions,
    IReadOnlyList<AssignmentRoutingSelection> AssignmentRoutingSelections,
    IReadOnlyList<AttemptRoutingSelection> AttemptRoutingSelections);

public sealed record AcceptedProjectState(
    ProjectRef ProjectRef,
    IReadOnlyDictionary<LogicalActorRef, LogicalActor> LogicalActors,
    IReadOnlyDictionary<ResponsibilityRef, Responsibility> Responsibilities,
    IReadOnlyDictionary<AssignmentRef, Assignment> Assignments,
    IReadOnlyDictionary<RevisionRef, AssignmentRevision> Revisions,
    IReadOnlyDictionary<RevisionRef, RevisionDispositionRecord> RevisionDispositions,
    IReadOnlyDictionary<AssignmentRef, RevisionRef> CurrentEffectiveRevisionRefs,
    IReadOnlySet<AssignmentRef> CurrentDelegationAssignments,
    IReadOnlyList<AcceptedStateContribution> CurrentContributions);

public sealed record B1ProjectProjection(
    ProjectRef ProjectRef,
    AcceptedProjectState AcceptedProjectState,
    IReadOnlySet<AssignmentRef> EffectiveFulfillmentAssignments,
    IReadOnlyDictionary<AssignmentRef, AttemptRef?> StoredAttemptSelections,
    IReadOnlyDictionary<AssignmentRef, AttemptRef?> EffectiveCurrentAttemptRefs,
    IReadOnlyDictionary<AttemptRef, HandoffRef?> StoredHandoffSelections,
    IReadOnlyDictionary<AttemptRef, HandoffRef?> EffectiveCurrentHandoffRefs,
    IReadOnlyDictionary<AttemptRef, SessionBindingRef?> StoredBindingSelections,
    IReadOnlyDictionary<AttemptRef, SessionBindingRef?> EffectiveCurrentBindingRefs);
