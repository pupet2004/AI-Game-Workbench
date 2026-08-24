# Product Slice 1 — Manual Project World Alpha Design Specification

Status: **Approved product design; implementation authorized**

Date: **2026-08-24**

Governing architecture:

- [R5-B1 Manual Continuity Spine](./2026-08-22-r5-b1-manual-continuity-spine-design.md)
- [R5-B1 Baseline](../../architecture/r5-b1-baseline.md)
- [Workbench Global Architecture](../../architecture/workbench-global-architecture.md)

## 1. Purpose

Product Slice 1 makes the sealed R5-B1 Manual Continuity Spine usable as the primary product path for the first time.

The Alpha must prove that one Windows user can create or explicitly adopt a local Project, establish a Project World, perform bounded Manual work, record a non-authoritative Handoff, make an attributable Decision, restart the application, and recover the same Accepted Project State without an Agent, Session, Provider, runtime, Git, transcript, or Summary.

This Slice does not redesign R5-B1. It presents and coordinates the existing named B1 Application commands through bounded product use cases.

## 2. Target User and Product Boundary

The Alpha target is:

> A single Windows user managing a long-running local game or software Project. Git is optional. Manual participation is first. Codex is not required.

The minimum package contains:

- the Workbench desktop application;
- local SQLite persistence;
- the Project World domain and Application services;
- the Manual Project World UI.

The core Manual journey must not require:

- Node.js, Codex CLI, Claude, ACP, or another Provider;
- Git, a worktree, a terminal, tests, or a sandbox;
- a live Agent runtime;
- cloud identity, multi-user collaboration, or cloud sync.

## 3. Product Invariants

1. `UserPrincipalRef` is the authenticated local operator; `LogicalActorRef` is the durable Project identity acting within the Project. They are never collapsed.
2. RoleKind is descriptive and grants no authority.
3. Accepted Project State changes only through a persisted `AuthorityDecision` produced by an existing named B1 authority command.
4. Creating an Attempt, recording Claims, recording a Handoff, or changing routing never changes Accepted Project State.
5. Legacy records remain readable but never automatically create a B1 Actor, Responsibility, Assignment, Revision, Claim, Decision, or accepted contribution.
6. A new Project receives its governance root at Project creation. An eligible pre-B1 Project receives it only through explicit one-time adoption.
7. Guided UI may coordinate a sealed command shape; it may not expose a generic effect builder or directly write B1 tables.
8. Failed initialization, Handoff recording, and Decision submission leave zero partial effects.
9. Manual operation produces zero `SessionBinding` rows.
10. The current Legacy Leader/Worker/Library workspace remains independently available during coexistence; this Slice does not reinterpret it as B1.

## 4. Stable Local UserPrincipal

Product Slice 1 uses one stable external local Windows principal. The Application obtains it through `ILocalUserPrincipalProvider`.

The persisted reference is derived from the current Windows user SID and is namespaced as `windows:<sid>`. The display name is presentation-only and is never used as identity. The provider does not create a B1 entity and does not grant authority by itself; it supplies the pre-existing external principal required by Project creation or eligible Legacy adoption.

Tests use an injected deterministic principal. Production never substitutes Provider, model, Session, runtime, or Workbench identity.

## 5. Project Home and Mechanical Entry Detection

Project Home is the only Project entry surface. It presents two user intents:

- **Create Project**
- **Open Local Project**

It never asks the user to choose an architecture era.

Every known Project receives one mechanical entry classification:

| Entry state | Mechanical facts | Product route |
|---|---|---|
| `ProjectWorldReady` | governance root exists and the Project state can be loaded | Project World Explorer |
| `LegacySetupRequired` | mechanical pre-B1 origin exists, no governance root, and no B1 governance history | explicit Legacy adoption confirmation |
| `NewSetupRequired` | a newly inspected folder is not yet a persisted Workbench Project | new governed Project creation followed by guided initialization |
| `UnmanagedProjectUnavailable` | a persisted post-B1 Project has no governance root and is not an eligible pre-B1 Project | diagnosis; no inferred adoption |
| `CorruptProjectUnavailable` | contradictory governance/history or unreadable Project World state | diagnosis; no automatic repair |
| `PathUnavailable` | local folder is missing | unavailable recent item |

Detection is read-only. Merely opening Project Home or selecting a known Project creates no governance and performs no adoption.

Opening an unknown local folder first produces an in-memory inspection result. It must not call the Legacy `ProjectRepository.UpsertAsync` path before the user confirms governed Project creation.

## 6. New Project and Legacy Adoption

### 6.1 New Project

For an unknown local folder, confirmation atomically creates:

- the durable Project identity;
- the B1 bootstrap governance root using the current local UserPrincipal;
- the default presentation layout required by the application.

It does not create Actor, Responsibility, Assignment, Revision, Claim, Handoff, Decision, contribution, Attempt, or SessionBinding.

### 6.2 Eligible Legacy Project

The adoption screen states that previous Workbench data remains unchanged. The user must explicitly confirm adoption.

Adoption performs only the sealed one-time root operation:

```text
Project.BootstrapAuthorityPrincipalRef = current UserPrincipal
Project.B1GovernanceAdoptedAt = now
```

It produces no B1 history and does not map Legacy Leader, Task, review, Memory, Summary, Library, Session, or completion records.

## 7. Guided Atomic Initialization

After new Project creation or Legacy adoption, the UI asks:

1. **Which Project role will act manually?** One closed RoleKind.
2. **What long-term responsibility exists?** Obligation and expected outcome.
3. **What is the first bounded assignment?** Initial work contract.
4. **What authority is delegated?** Alpha defaults to an empty delegated authority boundary.

The preview explains that one Decision will establish:

- one LogicalActor;
- one Responsibility;
- one Assignment delegated to that Actor;
- one initial immutable Revision.

Preview is an in-memory Application read model, not a draft Decision or persisted domain object. Confirmation calls the existing `EstablishResponsibilityCommand` shape with establishment plus initial delegation and `EstablishedAssigneeRoleKind`.

R5-B1 does not define a mutable Actor name. The Alpha therefore presents an Actor by RoleKind plus a shortened immutable Actor reference after establishment. It does not add a parallel alias store or smuggle a display name into authority or accepted state.

Any validation, authority, concurrency, or persistence failure produces zero effects.

## 8. State-first Project World Explorer

After initialization, the primary Project screen is not the Legacy chat workspace. Its Overview reads one B1 Project snapshot and presents:

1. **What the Project currently accepts** — current accepted contributions, with scope, Decision attribution, and optional source Claim reference.
2. **Needs attention** — Application-derived unresolved inputs relevant to current Assignments. `Unresolved` is never persisted as a disposition or Claim status.
3. **Active work** — effective fulfillment Assignments, assignee Actor, current Revision, and explicit continuation routing. Assignment is not shown as running execution.
4. **Recent Decisions** — attributable Decision history in Project commit order.

Navigation for the Alpha is:

- Overview
- Governance
- Work
- Decisions
- Legacy Context, only when Legacy data exists

Legacy Context is visually and semantically separate from Project World history.

An initialized Project with no accepted contributions honestly displays that no accepted Project statements exist yet. The application never fabricates a contribution to make the screen look populated.

## 9. First Manual Work Loop

An unresolved current Assignment offers **Begin Manual Work** when no effective current Attempt exists and **Continue Manual Work** when an explicit effective Attempt selection exists.

Beginning work calls `CreateAttemptAndSelectAsync` for the Assignment's current effective Revision. It creates no SessionBinding, Claim, Handoff, execution status, or authority effect.

The Manual Work screen displays:

- signed-in UserPrincipal;
- acting LogicalActor;
- Responsibility and Assignment contract;
- effective Revision;
- selected Attempt;
- current selected continuation Handoff, if any.

The Application never selects the latest Attempt by timestamp. Stored/effective routing from the B1 projection governs continuation.

## 10. Guided Bounded Handoff Composer

The user records one semantic Handoff action rather than manually creating database objects.

The fixed Alpha composer contains:

- required Primary Result → one `ResultClaim`;
- zero or more Validations → `ValidationClaim` values;
- zero or more Unresolved Issues → `UnresolvedIssue` Claims;
- zero or more Proposed Project Changes → `ProposedStateContribution` Claims with explicit closed scope;
- optional Proposed Assignment Revision → one `ProposedAssignmentRevision` Claim;
- zero or more EvidenceRef locators.

All primary Assignment result attribution is mechanically assigned to the Assignment's immutable assignee LogicalActor. The physical UserPrincipal is the operator, not the Claimant.

One named Application transaction validates all input and atomically:

- records the typed Claims;
- records one immutable Handoff referencing them;
- updates the Attempt's stored continuation Handoff using expected-old semantics.

The transaction creates no Decision, accepted contribution, completion state, Attempt status, or automatic verification.

## 11. Guided Manual Decision Composer

The Decision flow begins from the current continuation Handoff but creates an independent AuthorityDecision.

The screen distinguishes:

- **Submitted as:** the Assignment assignee LogicalActor;
- **Deciding as:** the bootstrap UserPrincipal.

The Alpha supports the existing `DecideAssignmentCommand` shape:

- `Accepted`, `Rejected`, or `RevisionRequired` for the current Revision;
- optional accepted contributions selected from proposed contribution Claims;
- adopt proposal verbatim or author an edited authority-owned statement while retaining `SourceClaimRef`;
- optional Revision activation only where the sealed command matrix permits it.

The preview is an in-memory representation of the exact effects. It is not persisted. Confirmation validates the current Assignment, Revision, authority, sources, and supersession targets and submits one atomic named command.

No UI action is named “Accept Handoff.” Handoff remains an immutable considered input.

## 12. Post-Decision Continuation

After a successful Decision, the Application reloads the persisted B1 projection and returns to the Project World Overview.

The Overview must show:

- accepted contributions added or superseded by the Decision;
- the persisted Revision disposition;
- effective Attempt/Handoff continuation becoming null when the sealed projection invalidates it;
- stored Attempt/Handoff routing retained in history;
- the Decision in Project commit order.

The Application performs no post-commit repair or direct projection mutation.

## 13. Packaging and Accessibility

The Alpha provides a self-contained Windows x64 publish profile. The package must start and complete the Manual acceptance journey on a machine without a separately installed .NET runtime, Node.js, Codex CLI, or Git.

All core controls are keyboard reachable and have meaningful accessible names. The 1400×850 default window and a 1280×720 working area must not clip primary navigation or actions.

## 14. Explicit Non-goals

Product Slice 1 does not add:

- Codex/Claude/ACP/Codeg Agent participation adapters;
- an Authority Packet for Agent startup;
- Provider/model/account selection;
- attachment ingestion or rich document/image preview;
- Summary Journal consumption or automatic Summary creation;
- configurable transcript cleanup or Leader rotation thresholds;
- a generic Claim builder, Decision builder, workflow engine, ACL, Truth engine, or causal ledger;
- Git/worktree/test/sandbox/terminal/merge execution;
- Legacy migration, deletion, or automatic semantic conversion;
- multiple users, organizations, cloud sync, or bootstrap authority transfer.

These remain later Product Slices. This Slice must not create speculative hooks or extensibility frameworks for them.

## 15. Acceptance Scenarios

### New Project

```text
Launch -> Project Home -> Create Project -> choose local folder
-> confirm governed Project creation -> guided initialization preview
-> one atomic Decision -> Project World Overview
-> begin Manual Attempt -> record typed Handoff
-> verify Accepted State unchanged -> review and decide
-> reload projection -> restart -> same Accepted State and routing history
```

### Eligible Legacy Project

```text
Launch -> Project Home -> Open known pre-B1 Project
-> Setup required + Legacy data available -> explicit adoption
-> zero B1 history created by adoption -> guided initialization
-> Legacy remains readable and unchanged -> new B1 world starts now
```

### Zero-dependency Manual certification

Both flows run with:

```text
Runtime registry empty
SessionBindings = 0
Leader epochs/messages = 0
Worker executions/events = 0
Summary rows = 0
Git unavailable
```

and still recover the same Accepted Project State after restart.

## 16. Consequences

This Slice changes the product center without deleting the Legacy workspace. Project Home routes B1-ready Projects to Project World and preserves an explicit Legacy Context boundary. Later Agent work must enter through the same Attempt/SessionBinding/Claim/Handoff semantics; if an Agent adapter requires a new Accepted State writer or makes recovery depend on Session history, it is rejected.
