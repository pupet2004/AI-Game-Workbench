# R5-B1 Manual Continuity Spine — Design Specification

Status: **Architecture approved; written specification pending review**
Date: 2026-08-22
Product: AI Game Workbench

Governing architecture:

- `docs/superpowers/specs/2026-08-22-r5-boundary-reconciliation-design.md`

Evidence informing this design:

- `docs/superpowers/reports/2026-08-22-r5-repository-ownership-audit.md`
- `docs/superpowers/reports/2026-08-22-r5-call-data-audit.md`

This document defines the narrow R5-B1 domain and application boundary. It does not define a database schema, migration sequence, production type layout, UI, Agent Gateway, or implementation plan.

## 1. Decision Summary

R5-B1 establishes the minimum durable spine through which a Project can preserve responsibility, admit bounded work performed without any Agent API, make attributable authority decisions, project accepted state, and recover later without replaying a transcript.

The vertical slice is:

```text
Project
  ↓
LogicalActor
  ↓
Responsibility
  ↓
Assignment
  ├─ Revision
  ├─ Attempt
  └─ SessionBinding?        optional; absent in Manual mode
        ↓
Claim + bounded Handoff
        ↓
AuthorityDecision
        ↓
AcceptedProjectState
```

The architecture proof is Manual continuity:

> **A Project remains governable and recoverable when SessionBindings, transcripts, Summary, Agent APIs, runtimes, Git, worktrees, and execution infrastructure are all absent.**

R5-B1 is not a Manual UI feature and does not implement an Agent Gateway. Manual operation is the acceptance scenario used to prove that the Kernel does not depend on execution.

## 2. Governing Principles

R5-B1 inherits all eight invariants from the R5 Boundary Reconciliation decision. The following rules are load-bearing within this slice:

1. Durable identity and responsibility never derive from a physical session.
2. Responsibility, Assignment, Revision, Attempt, and SessionBinding are different identities with different lifetimes.
3. External or human work reports enter as immutable, attributable Claims and Handoffs.
4. A Claim or selected Handoff is never authoritative merely because it is recent, structured, validated, reviewed, or selected for continuation.
5. Only a valid AuthorityDecision can create authoritative effects.
6. AcceptedProjectState is a deterministic current projection of authoritative records and has no independent write API.
7. Summary is a non-authoritative continuity aid and is not required to be mechanically recomputable from AcceptedProjectState.
8. Rebinding changes connectivity only.
9. Every authoritative effect is validated against the Project state that existed before its Decision commits.
10. No identity, capability, or prospective effect created by a Decision may authorize another effect in that same Decision.
11. A failed bounded compound Decision persists none of its effects.
12. Legacy records remain legacy records unless an explicit current-time B1 action references or adopts their bounded meaning.

## 3. Scope

R5-B1 designs:

- Project authority bootstrap and one-time pre-B1 Project adoption;
- durable LogicalActor identity and closed RoleKind;
- immutable Responsibility contracts;
- immutable Assignment delegation and explicit replacement;
- immutable Revision contracts and Revision-bound Attempts;
- optional Attempt-bound SessionBinding history and current connectivity selection;
- immutable, attributable Claims;
- immutable bounded Handoffs and continuation selection;
- bounded compound AuthorityDecisions;
- closed accepted contribution scopes and explicit supersession;
- deterministic authoritative and Application routing projections;
- a closed capability set and non-amplifying Assignment-derived authority;
- a closed set of named authority Application commands;
- the Manual end-to-end recovery acceptance scenario;
- parallel legacy readability with an explicit one-way bridge into B1.

R5-B1 deliberately does not design:

- Codex, Claude, ACP, Codeg, or other Agent Session Gateways;
- a general ACL, RBAC, policy, workflow, or transaction language;
- execution scheduling, worktrees, Git, tests, builds, merges, review execution, sandboxing, or runtime permission policy;
- automatic Claim verification or automatic promotion of Agent output;
- arbitrary Project truth objects or arbitrary contribution scopes;
- Actor rename, role change, retirement, merge, or cross-Project identity;
- Responsibility revision, completion, retirement, replacement, hierarchy, or lifecycle;
- Project membership, multi-user ownership, proxy grants, joint authority, or bootstrap authority transfer;
- legacy data migration, automatic semantic conversion, schema removal, or production cutover;
- a schema, migration number, API/class layout, UI, implementation plan, or production patch.

## 4. Identity and Root of Trust

### 4.1 Project

A Project is the durable boundary within which all B1 identities, authority, Claims, Decisions, and projections have meaning.

For a new B1 Project, the root fact is conceptually:

```text
Project
├─ ProjectRef
├─ BootstrapAuthorityPrincipalRef   // UserPrincipalRef, required
└─ CreatedAt
```

Project creation is not an AuthorityDecision. It is the operation that establishes the root of trust from which the first AuthorityDecision becomes possible.

The bootstrap principal:

- must be a pre-existing authenticated `UserPrincipalRef`;
- is Project-scoped;
- is the only UserPrincipal guaranteed by B1 to act as Manual operator, UserPrincipal Claimant, and root DecidingAuthority;
- is not a LogicalActor, Leader role, Session, Provider, model, runtime, Workbench, or accepted contribution;
- cannot be transferred, revoked, or replaced in B1.

Bootstrap authority is not inferred from a Claim or accepted contribution. A statement such as `"User U owns Project P"` cannot bootstrap the authority required to accept that statement.

### 4.2 One-Time Adoption of a Pre-B1 Project

A Project that mechanically predates B1 may establish its root of trust through the compatibility operation:

```text
AdoptLegacyProjectIntoB1
├─ ProjectRef
└─ AuthenticatedUserPrincipalRef
```

This is not an AuthorityDecision and is not a seventh authority Application command. It is the legacy equivalent of establishing bootstrap authority at new Project creation.

Adoption is legal only when all of the following are true:

```text
Project has mechanically provable pre-B1 provenance
AND BootstrapAuthorityPrincipalRef is absent
AND no B1 LogicalActor exists
AND no B1 Responsibility exists
AND no B1 Assignment exists
AND no B1 AuthorityDecision exists
```

Equivalently:

> **Adoption is legal only while the Project has no B1 governance history whatsoever.**

A null bootstrap field is not sufficient evidence that a Project is legacy. A post-B1 Project with a missing bootstrap principal is corrupt and is not eligible for adoption merely because the field is null.

Successful adoption atomically establishes:

```text
Project.BootstrapAuthorityPrincipalRef = authenticated principal
Project.B1GovernanceAdoptedAt = successful adoption time
```

`B1GovernanceAdoptedAt` is bootstrap provenance, not a Project lifecycle status. Adoption creates no Actor, Responsibility, Assignment, Claim, AuthorityDecision, accepted contribution, or legacy mapping. It is allowed exactly once.

### 4.3 No Circular Authorization

The general root rule is:

> **Authority may be extended by later Project governance, but it may never be bootstrapped from an entity or effect created by the same Decision.**

All effect authority is evaluated against the pre-commit Project state. Prospective references allow one Decision to create related objects atomically; they never allow those objects to authorize that Decision.

## 5. Durable Domain Identities

All B1-owned durable domain entity references are Project-local. A referenced LogicalActor, Responsibility, Assignment, Revision, Attempt, SessionBinding, Claim, Handoff, contribution, or AuthorityDecision must belong to the same Project as the containing operation.

`UserPrincipalRef`, `ExternalSessionRef`, and `EvidenceRef` refer to identities or locators outside the B1-owned Project entity set. They are governed by their explicit bootstrap, provenance, ownership, and evidence constraints rather than by pretending that the external identity itself is Project-local.

### 5.1 UserPrincipal and LogicalActor

`UserPrincipal` is a real external product identity that exists before B1 Project governance.

`LogicalActor` is an immutable, authority-established Project identity that may later receive Assignment delegations, originate attributable Claims, and exercise explicitly delegated authority.

```text
LogicalActorEstablishmentEffect
└─ RoleKind
```

Successful commit creates:

```text
LogicalActor
├─ LogicalActorRef
├─ ProjectRef
├─ RoleKind
├─ AuthorizedByDecisionRef
└─ CreatedAt
```

`RoleKind` is a closed enum:

```text
Leader
Worker
Reviewer
```

RoleKind is immutable in B1 and has no authority semantics. `Leader` does not mean root authority; `Reviewer` does not mean review acceptance authority; `Worker` does not forbid authority. Capability validation, not RoleKind, determines whether an Actor may decide an effect.

Actor establishment creates identity only. It does not create Responsibility, Assignment, Attempt, SessionBinding, Claim, execution, or authority. An Actor may exist without current work and may later receive multiple explicit Assignments.

Provider, model, runtime, account, work directory, thread, and session data never become LogicalActor identity.

### 5.2 Responsibility

A Responsibility is an immutable, authority-established Project obligation.

```text
ResponsibilityContract
├─ Obligation
├─ ExpectedOutcome
└─ MaximumDelegableAuthorityBoundary
```

```text
ResponsibilityEstablishmentEffect
└─ Contract
```

Successful commit creates:

```text
Responsibility
├─ ResponsibilityRef
├─ ProjectRef
├─ Contract
├─ AuthorizedByDecisionRef
└─ CreatedAt
```

Responsibility does not store an owner. It may exist with zero Assignments. It may later have multiple parallel Assignments delegating all or part of its fulfillment. B1 therefore does not define a singular `CurrentOwner`.

The `MaximumDelegableAuthorityBoundary` is the upper bound of authority that may later be included in an Assignment Revision under this Responsibility. It cannot authorize its own establishment and cannot authorize any other effect in the Decision that creates it.

B1 does not mutate, revise, close, replace, or retire a Responsibility contract.

### 5.3 Assignment

An Assignment is an immutable concrete delegation of a Responsibility to exactly one LogicalActor.

```text
Assignment
├─ AssignmentRef
├─ ResponsibilityRef
├─ AssigneeActorRef          // immutable
├─ InitialRevisionRef
└─ AuthorizedByDecisionRef
```

Changing the assignee never rewrites an Assignment. Responsibility transfer creates a new Assignment and may explicitly replace one current delegation.

```text
AssignmentDelegationEffect
├─ ResponsibilityTarget
│  ├─ Existing(ResponsibilityRef)
│  └─ EstablishedByThisDecision
├─ AssigneeTarget
│  ├─ Existing(LogicalActorRef)
│  └─ EstablishedByThisDecision
├─ InitialRevisionContract
└─ ReplacesAssignmentRef?
```

Successful delegation atomically creates:

```text
Assignment
+ Initial immutable Revision
+ current delegation projection update
```

The new assignee must belong to the same Project. Replacing an Assignment changes the current delegation projection only. It does not rewrite the old Assignment, its assignee, Revisions, Attempts, SessionBindings, Claims, Handoffs, routing selections, or dispositions.

`ReplacesAssignmentRef = null` means an additional parallel delegation. Workbench never infers replacement from similar text, timing, assignee, or Responsibility.

When `ReplacesAssignmentRef` is present, the target must:

- belong to the same Project;
- belong to the exact same Responsibility targeted by the new delegation;
- still be a `CurrentDelegationAssignment` in the pre-commit projection.

A stale or already-replaced target invalidates the complete AuthorityDecision. Replacement does not require the target Revision to remain Unresolved; an Accepted, Rejected, or RevisionRequired Assignment may still be replaced later while it remains an unreplaced current delegation.

### 5.4 Current Delegation and Effective Fulfillment

These are distinct query meanings:

```text
CurrentDelegationAssignment
= an Assignment that has not been explicitly replaced
```

```text
EffectiveFulfillmentAssignment
= CurrentDelegationAssignment
  whose CurrentEffectiveRevision is Unresolved
```

An Accepted Assignment that has not been replaced remains a current delegation and a valid future replacement target. It is not an effective fulfillment Assignment, does not supply Actor authority, and has no effective continuation.

A Responsibility may have zero or more current delegations and zero or more effective fulfillment Assignments.

### 5.5 Root-Seeded Delegation

Every Responsibility's first Assignment is seeded by the bootstrap UserPrincipal.

A LogicalActor-derived `DelegateAssignment` capability is permanently local to the Responsibility that supplies that capability. An Actor under Responsibility R1 may establish a new Responsibility R2 if it holds the Project-level `EstablishResponsibility` capability, but it cannot seed R2 merely because it created R2.

```text
Actor authority source = Assignment under R1
DelegateAssignment locality = R1 only
```

The following combined Decision is therefore valid only for the bootstrap UserPrincipal:

```text
Establish R2
+ create first Assignment under R2
```

Prospective references do not expand authority locality.

## 6. Revision and Attempt

### 6.1 Revision

A Revision is an immutable, authorized Assignment contract.

```text
AssignmentRevisionContract
├─ WorkContract
└─ DelegatedAuthorityBoundary
```

Every Assignment is created with exactly one initial Revision in the same Decision and transaction. There is no Assignment without a contract.

Initial and later activated contracts use the same Revision identity:

```text
Revision
├─ RevisionRef
├─ AssignmentRef
├─ PriorRevisionRef?          // null iff this is the initial Revision
├─ Contract
└─ AuthorizedByDecisionRef
```

For the initial Revision:

```text
PriorRevisionRef = null
AuthorizedByDecisionRef = Assignment creation Decision
```

B1 does not define a separate `InitialRevision` object kind.

Subsequent Revision proposals are Claims, not pending Revision entities:

```text
ProposedAssignmentRevision Claim payload
├─ AssignmentRef
├─ BaseEffectiveRevisionRef
└─ ProposedContract
```

R5-B1 has no Draft or Pending Revision entity. A new Revision comes into existence only through successful authority-owned activation:

```text
RevisionActivationEffect
├─ AssignmentRef
├─ ExpectedCurrentRevisionRef
├─ NewRevisionContract
└─ SourceClaimRef?
```

Successful activation creates:

```text
Revision
├─ RevisionRef
├─ AssignmentRef
├─ PriorRevisionRef            // pre-commit CurrentEffectiveRevisionRef
├─ Contract
└─ AuthorizedByDecisionRef
```

and changes the deterministic CurrentEffectiveRevision projection. Activation does not create an Attempt, SessionBinding, Handoff, Claim, or execution state.

A stale proposal remains valid attributable history. It may be considered by a later Decision, but it cannot be activated verbatim when its base no longer matches the pre-commit current Revision. The deciding Authority must author a contract against the actual current Revision.

### 6.2 Attempt

An Attempt is one immutable effort under exactly one Assignment Revision.

```text
Attempt
├─ AttemptRef
├─ AssignmentRef
├─ EffectiveRevisionRef       // immutable
└─ CreatedAt
```

Attempt creation requires:

```text
Assignment is a CurrentDelegationAssignment
AND EffectiveRevisionRef = Assignment.CurrentEffectiveRevisionRef
AND current Revision disposition = Unresolved
```

A material Revision never mutates an existing Attempt:

```text
R1 → A1 / A2
R2 → A3
```

Changing Session does not change Attempt or Revision. Retrying the same contract may create a new Attempt, but merely rebinding a Session is not automatically a retry.

Claims and Handoffs from an older Revision remain attributable history and may be considered by later Decisions. An older Revision is no longer a valid target for a current Assignment disposition, but its history does not become invalid.

## 7. SessionBinding and Connectivity

SessionBinding is optional in B1. Manual Attempts may have no SessionBinding at any time.

```text
SessionBinding
├─ SessionBindingRef
├─ AttemptRef
├─ LogicalActorRef
├─ ExternalSessionRef          // opaque locator
└─ CreatedAt
```

The following ownership invariants are mechanical:

```text
Binding.Attempt.Assignment.AssigneeActorRef
== Binding.LogicalActorRef
```

and all referenced objects belong to the same Project.

SessionBindings are immutable connectivity/provenance history. A Binding has no `active`, `closed`, `current`, `failed`, `dead`, `accepted`, or execution status.

Provider, model, account, runtime, thread ID, and other connection metadata may be needed by an Adapter to resolve `ExternalSessionRef`; none of them replaces LogicalActor identity.

The same external session locator may appear in multiple SessionBindings. Each Binding records one Workbench-specific use of that external Session by one LogicalActor in one Attempt. Reusing the locator does not merge Actors, Attempts, Assignments, or Projects.

Historical Bindings remain owned by their original Actor and Attempt. They are never reinterpreted after Assignment replacement.

The hard rule is:

> **Rebinding changes connectivity only. It never changes LogicalActor, Responsibility, Assignment, Revision, Attempt, Claim, Handoff, authority, or AcceptedProjectState.**

Switching or clearing the selected Binding does not imply that the external runtime was cancelled or closed. External lifecycle remains an Adapter concern.

## 8. Claim, Evidence, and Handoff

### 8.1 Claim

A Claim is one immutable, attributable, non-authoritative statement envelope.

```text
Claim
├─ ClaimId
├─ ProjectRef
├─ ClaimantRef
├─ SourceSessionBindingRef?    // provenance only
├─ Kind
├─ Payload
├─ EvidenceRefs[]              // 0..N; provenance only
└─ CreatedAt
```

Claimant is a closed union:

```text
ClaimantRef
├─ UserPrincipal(UserPrincipalRef)
└─ LogicalActor(LogicalActorRef)
```

Claimant answers who is responsible for the statement. Session, Provider, model, runtime, external tool, and Workbench itself are never Claimants.

R5-B1 Claim kinds are closed:

```text
Result
Validation
UnresolvedIssue
ProposedStateContribution
ProposedAssignmentRevision
```

The minimum payload meanings are:

```text
Result(statement)
Validation(statement)
UnresolvedIssue(statement)
ProposedStateContribution(statement, scope, proposed_supersedes?)
ProposedAssignmentRevision(AssignmentRef, BaseEffectiveRevisionRef, ProposedContract)
```

A ProposedStateContribution scope and any proposed supersession reference must belong to the Claim's Project. A ProposedAssignmentRevision must reference an existing same-Project Assignment and one of its Revisions. The base or proposed supersession target may later become stale; staleness does not rewrite or invalidate the Claim, but Authority cannot apply the stale proposal without satisfying the actual pre-commit guards.

Claims have no Accepted, Rejected, Disputed, Current, or truth status. Recording, parsing, validating the shape of, or selecting a Claim does not make it authoritative.

`EvidenceRefs` allows a standalone Claim to retain bounded direct evidence provenance. Evidence presence does not verify the Claim, grant authority, or promote it into AcceptedProjectState.

Workbench and Application code may mechanically persist a Claim, but they do not become its Claimant. External tool and CI results are EvidenceRefs or provenance; a responsibility-bearing UserPrincipal or LogicalActor must make the corresponding Claim.

Session provenance is constrained as follows:

```text
UserPrincipal Claim
→ SourceSessionBindingRef must be null
```

```text
LogicalActor Claim with SourceSessionBindingRef
→ Binding.ProjectRef = Claim.ProjectRef
→ Binding.LogicalActorRef = Claimant LogicalActorRef
```

A LogicalActor Claim may omit SourceSessionBindingRef, as required by Manual operation.

### 8.2 EvidenceRef

EvidenceRef is a compact locator or provenance reference. It does not copy an evidence warehouse into the Kernel, does not become a Claimant, and does not prove the referenced statement true.

EvidenceRefs may point to current external artifacts or readable legacy records. Their eventual retention and resolver semantics are separate persistence and compatibility concerns.

### 8.3 Handoff

An Attempt may produce zero or more immutable Handoffs.

Conceptually:

```text
Handoff
├─ HandoffRef
├─ AttemptRef
├─ ResultClaimRef                  // required primary Result
├─ ValidationClaimRefs[]
├─ UnresolvedIssueClaimRefs[]
├─ ProposedContributionClaimRefs[]
├─ ProposedAssignmentRevisionClaimRefs[]
├─ EvidenceRefs[]
└─ CreatedAt
```

Assignment, Project, and effective Revision identity are derived mechanically through Attempt. A submitter cannot supply contradictory copies of those identities.

Handoff references Claims and never copies a second mutable version of their payloads. It has no `current`, `final`, `accepted`, `rejected`, `failed`, `completed`, or execution status.

Every referenced Claim must belong to the same Project, and every typed Claim reference must point to the corresponding closed Claim kind. B1 does not accept arbitrary Handoff payload extensions.

Each `ProposedAssignmentRevisionClaimRef` included in a Handoff is additionally Attempt-bounded:

```text
Proposal.AssignmentRef
= Handoff.Attempt.AssignmentRef

AND

Proposal.BaseEffectiveRevisionRef
= Handoff.Attempt.EffectiveRevisionRef
```

An independent ProposedAssignmentRevision Claim may target any valid same-Project Assignment and Revision. The stricter identity match applies when the proposal is packaged as part of a particular Attempt's Handoff.

For the primary ResultClaim:

```text
ResultClaim.ProjectRef = Handoff.ProjectRef
ResultClaim.Kind = Result
ResultClaim.ClaimantRef
= LogicalActor(Handoff.Attempt.Assignment.AssigneeActorRef)
```

If that ResultClaim has a SourceSessionBindingRef:

```text
Binding.AttemptRef = Handoff.AttemptRef
```

Only the primary ResultClaim is required to be attributable to the assignee. Other Claims referenced by the Handoff may come from another valid Project claimant, such as a Reviewer Actor or the bootstrap UserPrincipal.

In Manual operation, the authenticated bootstrap user may explicitly operate the application on behalf of the Assignment's LogicalActor. The physical operator remains the user, while the Project Claimant for the primary Result is the assignee Actor. B1 does not add a `SubmittedByPrincipalRef` identity layer.

## 9. Application Routing State

Attempts, Handoffs, and SessionBindings are immutable histories. Application chooses at most one selected route at each level:

```text
Assignment.SelectedAttemptRef?

Attempt.SelectedContinuationHandoffRef?

Attempt.SelectedSessionBindingRef?
```

These are durable selected references, not authority, truth, execution status, or append-only histories of every pointer change.

Selection rules are uniform:

1. Selection is explicit; it is never inferred from latest `CreatedAt`.
2. The selected object must belong to its containing Assignment or Attempt.
3. Creation does not automatically select an object.
4. A named create-and-select Application command may atomically do both.
5. Selection uses expected-old compare-and-swap semantics.
6. Expected-old compares the stored selected reference, not an effective current projection.
7. Concurrent clients starting from the same stored selection permit at most one successful change.
8. Clearing a selection is explicit through the applicable nullable-selection or named clear command and does not mutate or delete the selected object.
9. Selection never grants authority or changes AcceptedProjectState.

The effective recovery chain is a query projection:

```text
Assignment
↓ EffectiveCurrentAttemptRef

Attempt
├─ EffectiveCurrentHandoffRef?
└─ EffectiveCurrentSessionBindingRef?
```

`EffectiveCurrentAttemptRef` returns the stored selected Attempt only if:

```text
Assignment remains a CurrentDelegationAssignment
AND selected Attempt belongs to Assignment
AND Attempt.EffectiveRevisionRef = Assignment.CurrentEffectiveRevisionRef
AND current Revision disposition = Unresolved
```

Otherwise it returns null without rewriting `SelectedAttemptRef`.

Handoff and SessionBinding effective queries first require their parent Attempt to be effective and then require the stored selected object to belong to that Attempt.

Assignment replacement, Revision activation, or any authoritative disposition mechanically invalidates the old effective continuation chain. It does not mark unselected or historical Attempts stopped, failed, abandoned, completed, or cancelled.

Because stored and effective references may differ, Application recovery must expose the stored reference or an equivalent selection version needed for a later CAS update.

## 10. Authority Capability Model

### 10.1 Closed Capability Set

B1 has exactly eight authority capabilities:

```text
B1AuthorityCapability
├─ EstablishLogicalActor
├─ EstablishResponsibility
├─ DelegateAssignment
├─ DecideAssignmentDisposition
├─ ActivateAssignmentRevision
├─ AcceptProjectStateContribution
├─ AcceptResponsibilityStateContribution
└─ AcceptAssignmentStateContribution
```

There is no ninth `ReplaceDelegation` or `SupersedeContribution` capability. Replacement uses `DelegateAssignment`. Explicit contribution supersession uses the accept capability for the exact contribution scope.

B1 has no wildcard, deny rule, role template, hierarchy, arbitrary resource expression, or generic action-plus-resource language.

### 10.2 Fixed Capability Locality

Capability locality is fixed:

| Capability | Target locality |
|---|---|
| `EstablishLogicalActor` | Project |
| `EstablishResponsibility` | Project |
| `AcceptProjectStateContribution` | Project |
| `DelegateAssignment` | Authority-source Responsibility |
| `DecideAssignmentDisposition` | Assignment under authority-source Responsibility |
| `ActivateAssignmentRevision` | Assignment under authority-source Responsibility |
| `AcceptResponsibilityStateContribution` | Authority-source Responsibility |
| `AcceptAssignmentStateContribution` | Assignment under authority-source Responsibility |

Project-level capabilities must appear explicitly in both the Responsibility maximum boundary and the effective Assignment Revision boundary. RoleKind never provides them.

### 10.3 Maximum and Explicit Subset

```text
AssignmentRevision.DelegatedAuthorityBoundary
⊆ Responsibility.MaximumDelegableAuthorityBoundary
```

The Assignment boundary defaults to the empty set. Being assigned work does not automatically grant any governance authority.

### 10.4 Effective LogicalActor Authority

A LogicalActor has a capability for an effect only when at least one pre-commit Assignment satisfies all of the following:

```text
Assignment.AssigneeActorRef = LogicalActorRef
AND Assignment is a CurrentDelegationAssignment
AND Assignment.CurrentEffectiveRevision is Unresolved
AND Revision.DelegatedAuthorityBoundary contains required capability
AND fixed capability locality contains the effect target
```

Capabilities from multiple effective Assignments may form a mechanical union, but each effect must identify at least one valid authority source satisfying both capability and locality. B1 has no deny or priority resolution between sources.

Historical authority loss does not invalidate a Decision that was valid when committed.

### 10.5 No Authority Amplification

For no-amplification checks, capability possession is locality-aware rather than a plain union. For a target Responsibility R:

```text
LogicalActor can exercise capability C for target R iff:

if C is Project-level:
    at least one effective pre-commit authority-source Assignment
    grants C at its fixed Project locality

if C is Responsibility-local:
    at least one effective pre-commit authority-source Assignment
    under R grants C
```

A capability obtained under another Responsibility cannot leak into the target Responsibility merely because the Actor's capabilities can be displayed as a union.

For the bootstrap UserPrincipal, both new Assignment delegation and Revision activation use the root rule:

```text
NewBoundary
⊆ TargetResponsibility.MaximumDelegableAuthorityBoundary
```

Bootstrap does not use Assignment-derived effective capabilities.

For a LogicalActor creating a new Assignment under target Responsibility R:

```text
for every capability C in NewAssignment.DelegatedAuthorityBoundary:
    C is exercisable by Decider for target R
    in the pre-commit state

AND

NewAssignment.DelegatedAuthorityBoundary
⊆ R.MaximumDelegableAuthorityBoundary
```

For a LogicalActor activating a Revision under target Responsibility R:

```text
AddedCapabilities
= NewRevision.DelegatedAuthorityBoundary
  - PriorRevision.DelegatedAuthorityBoundary

for every capability C in AddedCapabilities:
    C is exercisable by Decider for target R
    in the pre-commit state

AND

NewRevision.DelegatedAuthorityBoundary
⊆ R.MaximumDelegableAuthorityBoundary
```

Retaining capabilities already present in the prior Revision does not require the Decider to hold them. Removing capabilities is permitted when the Decider is otherwise authorized to activate the Revision. No LogicalActor may use `DelegateAssignment` or `ActivateAssignmentRevision` to manufacture authority it did not already possess.

## 11. AuthorityDecision

### 11.1 Identity and Order

```text
AuthorityDecision
├─ DecisionId
├─ ProjectRef
├─ ProjectCommitSequence
├─ DecidingAuthorityRef
├─ ConsideredRefs[]
├─ LogicalActorEstablishmentEffect?       // 0..1
├─ ResponsibilityEstablishmentEffect?    // 0..1
├─ AssignmentDispositionEffect?          // 0..1
├─ RevisionActivationEffect?              // 0..1
├─ AssignmentDelegationEffect?            // 0..1
├─ AcceptedStateContributions[]           // 0..N
└─ CreatedAt
```

Every Decision contains at least one authoritative effect. Empty Decisions are invalid.

`ProjectCommitSequence` is assigned by successful commit, is unique and strictly ordered within the Project, and is durably recoverable. It is the authoritative order for current-state interpretation. The sequence need not be mathematically gap-free. Failed Decisions never become members of the successful authoritative sequence.

`CreatedAt` is display and provenance time only. It does not determine precedence.

### 11.2 Deciding Authority

```text
DecidingAuthorityRef
├─ UserPrincipal(UserPrincipalRef)
└─ LogicalActor(LogicalActorRef)
```

In B1:

- a UserPrincipal is a valid DecidingAuthority only when it equals the Project bootstrap principal;
- a LogicalActor is valid only through effective pre-commit Assignment-derived authority;
- Session, Provider, model, runtime, external tool, and Workbench are never DecidingAuthorities.

B1 likewise defines no Project eligibility for another UserPrincipal to act as Manual operator or UserPrincipal Claimant. Implementations must not infer additional membership or proxy rights from legacy records, UI access, or Session identity.

The Manual Application may allow the authenticated bootstrap user to explicitly operate on behalf of a LogicalActor. The Decision is attributed to the Actor and validated against that Actor's authority, not the operator's root authority.

B1 has no Agent Gateway or authenticated Agent authority control plane. Agent text such as `"approved"` remains a Claim. A future direct Agent authority protocol requires a separate architecture decision and may not reinterpret ordinary output as a Decision.

### 11.3 Considered References

```text
ConsideredRef
├─ ClaimRef
├─ HandoffRef
└─ EvidenceRef
```

`ConsideredRefs` are optional immutable provenance/context references. They do not prove a statement true, grant authority, or become effects.

The minimum authoritative chain is:

```text
AcceptedStateContribution
← AuthorityDecision
← DecidingAuthority
```

When a Decision responds to prior work, `ConsideredRefs` preserves the bounded context it considered. A direct authority-authored assertion does not require a fabricated Claim.

`source_claim_ref` on an accepted contribution has a different meaning: it identifies a proposal from which that particular assertion was materially derived. It does not replace `ConsideredRefs` and is not an evidence field.

When present, an accepted contribution's `SourceClaimRef` must reference a same-Project `ProposedStateContribution` Claim. The Authority still authors the accepted statement, scope, and explicit supersession effect; the proposal's scope or proposed supersession never applies automatically.

### 11.4 Validate Before Persisting

The conceptual Decision transaction is:

```text
validate closed command shape
↓
resolve existing and prospective identities
↓
validate same Project and referential shape
↓
validate pre-commit current state and stale guards
↓
validate every capability and locality
↓
validate non-amplification
↓
all pass
↓
assign ProjectCommitSequence
↓
commit the complete Decision and all effects atomically
```

No effect is semantically persisted before all validation succeeds. Any shape, stale-state, authority, locality, scope, supersession, or amplification failure commits zero effects.

## 12. Assignment Disposition and Revision Activation

### 12.1 Disposition

```text
AssignmentDispositionEffect
├─ AssignmentRef
├─ EffectiveRevisionRef
└─ Accepted | Rejected | RevisionRequired
```

`Unresolved` is an Application query result meaning that the current Revision has no disposition. It is not an AuthorityDecision effect or stored disposition.

An Assignment disposition is valid only when:

```text
Effect.Assignment.Project = Decision.Project
AND Effect.EffectiveRevisionRef
    = Assignment.CurrentEffectiveRevisionRef
AND current Revision disposition = Unresolved
```

Each Revision may receive exactly one disposition:

```text
Unresolved
├─→ Accepted
├─→ Rejected
└─→ RevisionRequired
```

A stale Revision or already-dispositioned Revision invalidates the complete bounded compound Decision.

Historical Claims and Handoffs from an older Revision remain valid considered context. They may support a later Project-level contribution even when the older Revision can no longer receive a current Assignment disposition.

### 12.2 Activation Eligibility

Activation is permitted according to the pre-commit current Revision disposition:

```text
Unresolved       → activation-only allowed
RevisionRequired → later activation-only allowed
Accepted         → activation forbidden
Rejected         → activation forbidden
```

`RevisionRequired + activation` in one Decision is allowed only when the pre-commit current Revision is `Unresolved`. The complete transaction records `RevisionRequired` for the old Revision and activates its replacement.

When `RevisionActivationEffect.SourceClaimRef` is present, it must reference a same-Project `ProposedAssignmentRevision` Claim for the same Assignment. Its base may be stale history, but the activated contract and expected-current guard are always authored and validated against the actual pre-commit current Revision.

`Accepted + activation` and `Rejected + activation` are forbidden both in the same Decision and later for that Assignment. Continuing responsibility after Accepted or Rejected requires a separate Assignment delegation rather than reopening the accepted or rejected Assignment through Revision activation.

### 12.3 Replacement Combination Matrix

R5-B1 permits:

| Decision combination | Allowed |
|---|---:|
| Delegation with `Replaces = null` | Yes |
| Delegation with `Replaces = A1` | Yes |
| `Rejected(A1)` + replace A1 | Yes |
| `RevisionRequired(A1)` + replace A1 | Yes |
| `Accepted(A1)` + replace A1 | No |
| disposition A1 + parallel non-replacing Assignment | No |
| disposition A1 + replace another Assignment | No |
| disposition + activation + replacement | No |

When disposition and replacement coexist:

```text
Disposition.AssignmentRef
= Delegation.ReplacesAssignmentRef
```

Both operate against the same pre-commit Assignment and Revision snapshot. Any stale Revision, stale replacement target, invalid assignee, capability failure, or contribution failure invalidates the entire Decision.

An Accepted Assignment may be replaced by a later separate Decision because it remains a current delegation until explicitly replaced. This later replacement does not revise the Accepted disposition.

## 13. Accepted State Contributions

### 13.1 Proposal and Authority Separation

A Claim may propose a state contribution, but the proposal remains immutable and non-authoritative.

AuthorityDecision may:

- adopt a proposal statement verbatim;
- author a modified assertion while retaining the source Claim unchanged;
- author a new bounded assertion without any prior Claim.

AuthorityDecision never edits a Claim. It creates a new assertion for which the DecidingAuthority accepts responsibility.

```text
AcceptedStateContribution
├─ ContributionRef
├─ Statement
├─ Scope
├─ SupersedesContributionRef?       // 0..1
├─ AuthorityDecisionRef             // required
└─ SourceClaimRef?                   // optional proposal provenance
```

The contribution contains state semantics and attribution only. It does not contain general-purpose status, confidence, rationale, evidence arrays, arbitrary metadata, or a truth-object payload.

One AuthorityDecision may produce zero or more accepted contributions. Accepting an Assignment with zero Project-state contributions is valid. A Decision containing only contributions must contain at least one.

### 13.2 Closed Contribution Scope

Every accepted contribution has exactly one explicit closed scope:

```text
ContributionScopeRef
├─ Project(ProjectRef)
├─ Responsibility(ResponsibilityRef)
└─ Assignment(AssignmentRef)
```

Project scope must be explicit; null has no legal meaning.

Scope is a non-hierarchical identity boundary. It does not imply containment, inheritance, priority, specificity, or automatic conflict resolution. Responsibility-scoped contributions do not propagate to Assignments. Assignment-scoped contributions do not override Responsibility- or Project-scoped contributions. Application may compose multiple scopes for reading, but that read composition is not an authoritative inference.

R5-B1 has no custom, string, ProjectObject, module, file, feature, or Library-object scope. Adding another scope kind requires a new architecture decision.

### 13.3 Closed Prospective Scope References

Command-time contribution targets may refer to the unique Responsibility or Assignment created by the same Decision:

```text
ContributionScopeTarget
├─ Project(ProjectRef)
├─ Responsibility
│  ├─ Existing(ResponsibilityRef)
│  └─ EstablishedByThisDecision
└─ Assignment
   ├─ Existing(AssignmentRef)
   └─ DelegatedByThisDecision
```

These forms exist only in the command model. Successful commit resolves them to durable concrete references. They are never persisted as temporary IDs, aliases, effect indexes, or general intra-transaction paths.

`EstablishedByThisDecision` and `DelegatedByThisDecision` are legal only when the corresponding unique effect exists. A contribution scoped to a newly created identity cannot supersede another contribution because no current contribution can yet exist at that scope.

A proposal Claim cannot fabricate the identity of an object that does not yet exist. The Authority may form a contribution at the prospective scope during the Decision and retain the proposal through `SourceClaimRef`.

Prospective identities never authorize effects in their own Decision.

### 13.4 Explicit Supersession

Accepted contributions coexist by default. Textual contradiction, later timestamp, narrower wording, or a more specific-looking scope does not remove an existing contribution.

Supersession is explicit:

```text
NewContribution.SupersedesContributionRef
= one current existing contribution
```

All rules are mechanical:

1. `supersedes` is `0..1`.
2. The target must exist and still belong to the current projection at pre-commit validation.
3. New and old contributions must belong to the same Project.
4. New and old scopes must have exactly the same discriminant and referenced identity.
5. The Authority must have the capability to author at that exact scope; the same scope capability governs entry of the new assertion and retirement of the old one.
6. Contributions created in the same Decision cannot supersede one another.
7. Two concurrent Decisions superseding the same current target permit only the first successful commit; the stale Decision fails in full.
8. Supersession and every other effect in the Decision commit atomically.

If current contributions are A and X at the same scope and B supersedes only A, the result is X and B. Workbench does not infer whether B contradicts X.

History is append-only:

```text
A ← B ← C

History = A, B, C
Current = C
```

Restoring an earlier meaning creates a new accepted contribution. It never resurrects or rewrites an old row.

### 13.5 AcceptedProjectState

AuthorityDecision, its effects, and its accepted contributions are authoritative records.

AcceptedProjectState is their deterministic current projection:

```text
AuthorityDecision history
├─ established LogicalActor identities
├─ established Responsibility identities and contracts
├─ established Assignment and Revision identities
├─ Revision dispositions
├─ CurrentEffectiveRevision
├─ CurrentDelegation projection
└─ current non-superseded accepted contributions
        ↓
AcceptedProjectState
```

AcceptedProjectState has no independent write API. An implementation may materialize a cache, but deleting and rebuilding that cache from authoritative records must produce the same result.

Summary is not such a cache or projection. It remains a separately produced, non-authoritative continuity view.

## 14. Closed Authority Application Command Matrix

Callers do not submit arbitrary `effects[]`. UI, Gateway, Adapter, and ordinary Application services may invoke only named Application commands with fixed shapes.

Every successful authority command creates exactly one AuthorityDecision.

### 14.1 Six Authority Commands

| Command | Fixed effect shape |
|---|---|
| `EstablishLogicalActor` | exactly one Actor establishment; contributions allowed |
| `EstablishResponsibility` | exactly one Responsibility establishment; optional initial delegation and optional assignee Actor establishment; contributions allowed |
| `DelegateAssignment` | exactly one delegation targeting an existing Responsibility; optional assignee Actor establishment; contributions allowed |
| `DecideAssignment` | exactly one disposition; optional activation or same-Assignment replacement, never both; contributions allowed |
| `ActivateAssignmentRevision` | exactly one activation; contributions allowed |
| `AuthorAcceptedState` | one or more contributions; no other effects |

#### EstablishLogicalActor

```text
LogicalActorEstablishmentEffect = 1
all other structural/Assignment effects = 0
AcceptedStateContributions = 0..N
```

#### EstablishResponsibility

Allowed forms:

```text
Responsibility establishment only
```

or:

```text
Responsibility establishment
+ initial delegation targeting EstablishedByThisDecision
+ optional Actor establishment used by that delegation
```

For initial delegation, `ReplacesAssignmentRef` must be null. Capability and locality validation make this compound form bootstrap-only.

#### DelegateAssignment

```text
AssignmentDelegationEffect = 1
ResponsibilityTarget = Existing
LogicalActorEstablishmentEffect = 0..1
AssignmentDispositionEffect = 0
RevisionActivationEffect = 0
```

It covers parallel delegation, pure replacement, and establishment of a new assignee Actor followed by delegation or replacement.

#### DecideAssignment

```text
AssignmentDispositionEffect = 1
```

It may additionally contain exactly one of:

- Revision activation, only with `RevisionRequired`;
- replacement of the same Assignment, only with `Rejected` or `RevisionRequired`.

Replacement may establish a new assignee Actor. It may not establish a new Responsibility.

#### ActivateAssignmentRevision

```text
RevisionActivationEffect = 1
all establishment, disposition, and delegation effects = 0
AcceptedStateContributions = 0..N
```

#### AuthorAcceptedState

```text
AcceptedStateContributions = 1..N
all other effects = 0
```

### 14.2 Contributions Do Not Relax Command Shape

The first five commands may include zero or more contributions. Every contribution is independently validated for capability, locality, scope, Project identity, source, and supersession. A contribution failure invalidates the complete command.

For example, `DecideAssignment + replacement` may scope a contribution to `Assignment(DelegatedByThisDecision)`. The Accepted branch has no replacement and therefore has no prospective new Assignment scope.

Shape validity never implies authority. A structurally valid `EstablishResponsibility + initial delegation` command still fails for a LogicalActor because root-seeded locality forbids it.

### 14.3 Invalid Shapes

Unsupported combinations fail before effect persistence with the semantic result:

```text
INVALID_DECISION_SHAPE
```

The command is not split, downgraded, partially executed, or converted into multiple Decisions.

### 14.4 Non-Authoritative Application Commands

The following are non-authoritative Application commands:

```text
CreateAttempt
SelectCurrentAttempt
RecordClaim
CreateHandoff
SelectContinuationHandoff
CreateSessionBinding
SelectCurrentSessionBinding
ClearCurrentSessionBinding
```

`SelectCurrentAttempt` and `SelectContinuationHandoff` accept a nullable new selected reference, so an explicit null performs their clear operation under the same stored expected-old CAS rule. Session connectivity uses the explicit `ClearCurrentSessionBinding` command. No selection is cleared merely because its effective projection becomes null.

Named atomic conveniences may include:

```text
CreateAttemptAndSelect
CreateHandoffAndSelect
CreateSessionBindingAndSelect
```

Their transactionality prevents half-completed Application routing state. It never creates AuthorityDecision, accepted contribution, Assignment disposition, contract change, delegation, authority, or inferred completion.

## 15. Manual Continuity Acceptance

Manual continuity is a mandatory architecture certification.

### 15.1 Bootstrap and Establish the Spine

A new Project P1 is created with bootstrap UserPrincipal U1, or an eligible pre-B1 Project is explicitly adopted by U1.

U1 submits one valid `EstablishResponsibility` command that may atomically:

```text
establish Worker Actor W1
+ establish Responsibility R1
+ create initial Assignment A1 → W1
+ create initial Revision R1.1
```

All establishment, delegation, authority-boundary, Project, and prospective-reference checks pass before anything persists.

### 15.2 Begin Manual Work

Application creates Attempt T1 under A1/R1.1 and explicitly selects it using stored expected-old semantics.

```text
SessionBindings(T1) = []
SelectedSessionBindingRef = null
```

This is a fully valid Attempt.

### 15.3 Submit Claims and Handoff

The human works outside Workbench. In Manual Application, U1 explicitly acts on behalf of assignee Actor W1 and records:

```text
C1 = Result Claim by W1
C2 = Validation Claim by W1
C3 = ProposedStateContribution Claim by W1
```

All SourceSessionBindingRefs are null.

Application creates immutable Handoff H1 under T1, with C1 as its primary ResultClaim, and explicitly selects H1 for continuation. H1 and its Claims remain non-authoritative.

Pre-decision state is:

```text
A1/R1.1 disposition = Unresolved
EffectiveCurrentAttemptRef = T1
EffectiveCurrentHandoffRef = H1
AcceptedProjectState unchanged by H1
```

### 15.4 Decide

U1 submits `DecideAssignment` as the bootstrap DecidingAuthority. The Decision references H1 and/or C1 in `ConsideredRefs`, applies `Accepted` to A1/R1.1, and may author zero or more accepted contributions with optional proposal source C3.

Validation occurs against the pre-commit state. Successful commit atomically records the disposition and contributions.

After commit:

```text
A1 remains a CurrentDelegationAssignment until replaced
A1 is not an EffectiveFulfillmentAssignment
EffectiveCurrentAttemptRef = null
EffectiveCurrentHandoffRef = null
EffectiveCurrentSessionBindingRef = null
```

Stored T1/H1 selections, immutable Claims, Handoff, Attempt, and all provenance remain intact.

### 15.5 Recover the Next Day

Recovery reads durable identities, contracts, Decisions, projections, Claims, Handoffs, and selected routing references. It can answer:

```text
Who held the responsibility?
Under which contract?
Which Attempt and Handoff reported work?
Who claimed the primary result?
Which Authority decided the disposition?
Which bounded contributions are currently accepted?
Is any continuation route currently effective?
```

Recovery does not require:

```text
SessionBinding
Agent API
transcript
Summary
Provider
runtime
Git/worktree
review execution
```

If Responsibility R1 needs further work, a later separate Decision may replace A1 with A2. `Accepted(A1) + replace A1` is not compressed into the original acceptance Decision. A future Agent Adapter may create a SessionBinding for a new effective Attempt and translate external output into the same Claims and Handoffs; it must not require a Kernel change.

### 15.6 Certification Result

R5-B1 passes Manual continuity only if the complete responsibility-to-recovery chain remains correct with:

```text
SessionBindings = 0
Transcript = 0
Summary = 0
Agent API = 0
Git/worktree/runtime = 0
```

Failure indicates that delegated execution or provider identity leaked into the B1 Kernel.

## 16. Parallel Legacy Readability and Explicit One-Way Bridge

R5-B1 does not automatically reinterpret existing epoch, Task, review, execution, Memory, Summary, or Library data.

The compatibility relationship is:

```text
Legacy records
├─ remain independently readable
├─ retain original semantics
└─ may be referenced as context or EvidenceRef

        explicit current-time RecordClaim
        or named B1 authority command
                         ↓
B1 Claim / EvidenceRef / AuthorityDecision
                         ↓
B1 AcceptedProjectState
```

The bridge is explicit, attributable, current-time, and one-way.

### 16.1 Prohibited Automatic Mappings

No compatibility component may synthesize:

```text
Legacy Leader or project_leaders row → LogicalActor or bootstrap authority
Leader epoch → LogicalActor or SessionBinding
Task/revision → Responsibility, Assignment, or B1 Revision
Review output or AutoProceed → AuthorityDecision
Completed status → AcceptedProjectState
CompletionPackage/task_event → Handoff
Memory/Synthesis → accepted contribution or R5-A Summary
Summary → AuthorityDecision or AcceptedProjectState
Library current prose → accepted contribution
```

Semantic similarity is not identity or authority.

### 16.2 Allowed Compatibility Uses

| Legacy area | B1-compatible use |
|---|---|
| Leader epoch/session | independently readable context or EvidenceRef |
| Task/revision | historical locator or considered context |
| Review/AutoProceed | legacy Claim/context only after explicit attributable recording; never a Decision |
| CompletionPackage/task_events | Claim or Evidence source after explicit recording |
| Memory/Synthesis | recovery context with original semantics |
| Daily Summary/R5-A Summary | non-authoritative continuity view |
| Project Library | material/evidence reference, not automatic accepted state |

Legacy and B1 information may be displayed side by side. Application must not merge them into one unlabeled current status.

### 16.3 Compatibility Safety

1. B1 never back-writes or rewrites Legacy records.
2. Legacy writers may remain active during coexistence; their output does not automatically enter B1.
3. Explicitly recorded B1 Claims and Decisions use their actual current creation and commit order; they are not backdated into fabricated historical authority.
4. The same Legacy locator may be considered more than once; Workbench does not infer deduplication or semantic equivalence.
5. Legacy source unavailability does not change B1 AcceptedProjectState. It may make an EvidenceRef unavailable.
6. Later Legacy retirement requires proof that B1 references remain resolvable or a separately approved bounded evidence-preservation design.
7. This design authorizes no migration, freeze, deletion, cutover, schema removal, or dual-write implementation.

## 17. Failure and Concurrency Semantics

### 17.1 Bounded Compound Decision Atomicity

All Decision effects are all-or-nothing. Examples of failures that invalidate the complete Decision include:

- invalid command shape;
- wrong Project ownership;
- missing or ambiguous prospective target;
- stale current Revision;
- already-dispositioned Revision;
- stale replacement target;
- stale supersession target;
- invalid exact contribution scope;
- insufficient capability or wrong locality;
- delegated authority amplification;
- invalid assignee or Claim provenance;
- any contribution failure.

There is no best-effort effect subset and no compensation-based domain meaning.

### 17.2 Stale Revision

If an Assignment disposition or activation does not match the required pre-commit current Revision, the complete Decision fails:

```text
STALE_REVISION
↓
0 dispositions
0 Revisions
0 delegations
0 contributions
0 supersessions
```

### 17.3 Concurrent Supersession

Two Decisions that both observe contribution A as current and attempt to supersede it permit at most one successful commit. The loser fails in full after A is no longer current.

### 17.4 Concurrent Delegation Replacement

Two Decisions that both attempt to replace the same current delegation permit at most one successful commit. A replaced target cannot be replaced again as though it were still current.

### 17.5 Routing CAS

Routing updates compare the stored selected reference. If two clients start from the same stored reference, at most one update succeeds. Effective null caused by a disposition, Revision activation, or replacement does not erase the stored expected-old reference.

### 17.6 Commit Ordering

Successful AuthorityDecisions have one durable Project-local total order through `ProjectCommitSequence`. Wall-clock timestamps, Agent event timestamps, legacy event time, and `CreatedAt` never override this order.

## 18. Read and Recovery Projections

Application may compose deterministic read models, but composition does not create authority.

Required B1 queries must be able to recover:

- bootstrap authority and adoption provenance;
- all LogicalActors and immutable RoleKinds;
- Responsibility contracts and maximum authority boundaries;
- current and historical Assignment delegations;
- CurrentDelegationAssignments and EffectiveFulfillmentAssignments;
- Assignment Revision history, CurrentEffectiveRevision, and single dispositions;
- Attempt history and stored/effective routing selections;
- SessionBinding history without requiring external Session availability;
- immutable Claims, Handoffs, EvidenceRefs, and considered-reference chains;
- AuthorityDecision history in ProjectCommitSequence order;
- current non-superseded accepted contributions;
- deterministic AcceptedProjectState.

Application may also show Summary or readable Legacy context. Neither can affect the result of the authoritative projections.

Deleting any materialized authoritative projection cache and rebuilding it from authoritative records must produce the same result. Deleting Summary does not require rebuilding it from AcceptedProjectState and does not change authoritative results.

## 19. Architecture Acceptance Checks

The written and later implementation designs must certify at least the following cases.

### Identity and Ownership

- Session replacement does not change Actor, Responsibility, Assignment, Revision, Attempt, Claims, authority, or accepted state.
- Assignment assignee cannot be mutated.
- Historical SessionBinding remains attached to its original Actor and Attempt.
- A primary Handoff ResultClaim is attributable to the immutable Assignment assignee.
- Cross-Project references fail except for explicitly external EvidenceRefs.

### Authority

- RoleKind grants no authority.
- A normal Worker Assignment with an empty delegated boundary grants no authority.
- Actor authority disappears when its source Assignment is replaced or its current Revision receives a disposition.
- A Decision remains historically valid after its DecidingAuthority later loses effective authority.
- New effects cannot authorize the Decision that creates them.
- LogicalActor delegation and Revision activation cannot amplify authority.
- A LogicalActor cannot seed a new Responsibility's first Assignment.
- Bootstrap principal can establish and seed a Responsibility atomically.

### Decisions and State

- Every Revision receives at most one disposition.
- `Unresolved` is derived and never persisted as a disposition.
- Accepted or Rejected Assignments cannot be reopened through Revision activation.
- Invalid compound effects fail atomically.
- Accepted contributions require an owning AuthorityDecision.
- Proposal Claims remain unchanged when Authority authors a different assertion.
- Contributions coexist unless explicit exact-scope supersession occurs.
- Concurrent supersession and replacement have one winner.
- AcceptedProjectState has no independent writer.

### Routing

- Latest timestamp never selects Attempt, Handoff, or SessionBinding.
- Stored and effective routing references can differ.
- CAS compares stored selection.
- Replacement, activation, and disposition invalidate effective continuation without mutating history.
- Unselected Attempts do not acquire inferred failure or abandonment status.

### Manual Continuity

- Complete Manual flow succeeds with zero SessionBindings.
- Recovery succeeds with no transcript or Summary.
- Handoff selection remains non-authoritative.
- User decision may accept zero or more bounded contributions.
- A future Agent can enter through Claim/Handoff translation without changing Kernel semantics.

### Legacy Compatibility

- Pre-B1 adoption is explicitly user-rooted and allowed only with no B1 governance history.
- Null bootstrap on a post-B1 Project is corruption, not adoption eligibility.
- Legacy approved/completed/AutoProceed state never automatically becomes B1 authority.
- Legacy and B1 views remain labeled and independently interpretable.
- No retirement or migration is authorized by this design.

## 20. Consequences for Later Design and Repository Work

R5-B1 supplies the first real semantic landing zone for responsibilities currently mixed into Leader epochs, Tasks, WorkerExecution, review state, task events, CompletionPackage, and runtime routing. It does not declare those existing structures equivalent to B1 and does not authorize their removal.

Later implementation design must preserve these boundaries:

- Legacy Leader epoch identity may inform explicit compatibility work but cannot become a LogicalActor automatically.
- WorkerExecution may contain future Attempt, Assignment, and Session provenance candidates, but Git/worktree/runtime state remains delegated.
- Worker completion and review payloads are Claim/Handoff candidates, not AuthorityDecisions.
- AutoProceed cannot be reinterpreted as attributable B1 authority.
- Existing Task completion remains legacy Task-level completion state until an explicit reconciliation design maps it.
- R5-A Summary remains the approved append-only non-authoritative continuity path and is not recomputed from AcceptedProjectState.
- Legacy Memory and Daily Summary retain distinct semantics and are not automatic Summary or accepted-state migration targets.
- Project Library material may be referenced as context/evidence but does not become accepted state by name.

The dependency order expressed here is architectural, not an implementation plan:

1. New B1 semantics must be independently valid before legacy retirement is discussable.
2. Manual continuity must pass before any Agent Gateway is treated as a B1 requirement.
3. AuthorityDecision and AcceptedProjectState must exist before review/AutoProceed semantics can be reconciled.
4. LogicalActor, Assignment, Attempt, and SessionBinding must have real durable meaning before Leader epoch or WorkerExecution continuity is migrated.
5. Explicit compatibility bridges must exist before any legacy reader or referenced data can be retired.

## 21. Final Boundary

R5-B1 is complete when the following statement is true in the domain and Application model:

> **A bootstrap user can establish durable Project actors and responsibilities, delegate a bounded Assignment, admit Manual work as attributable Claims and an immutable Handoff, make one atomic authority decision, recover deterministic accepted state later without Session or transcript continuity, and continue through another explicit delegation.**

Nothing in this design makes Workbench an execution engine, Agent runtime, Truth platform, general workflow engine, or ACL system.

No implementation work is authorized until this written specification has completed user review and a separate implementation plan has been explicitly approved.
