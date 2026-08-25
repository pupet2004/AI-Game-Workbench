# Product Slice 1 — Manual Project Experience Plan

Status: **Phase 8 completed; Manual Project Slice sealed**  
Date: **2026-08-25**

## 1. Position

This plan builds the missing human-operation layer on top of the sealed foundations:

```text
R5-B1 Project World Kernel
        +
Library Projection Boundary
        ↓
Product Slice 1 — Manual Project Experience
```

R5-B1 already defines what the Project accepts. The Library Projection Boundary already defines how that accepted reality is browsed safely. This plan defines how a person enters that world, performs one bounded piece of work, makes one attributable decision, and returns later without losing continuity.

This is not R6, Agent Gateway work, or a new authority model.

## 2. Product outcome

The Alpha is successful when one Windows user can complete this journey without an Agent, Provider, Runtime, Git, Session, Summary, or cloud service:

```text
Create / Open Project
        ↓
Project World Setup
        ↓
State-first Project Explorer
        ↓
Begin Manual Work
        ↓
Record Handoff
        ↓
Review and Decide
        ↓
Accepted State + Library Projection
        ↓
Close application
        ↓
Reopen and continue
```

The product proof is not that every B1 object has a screen. The proof is that the user can understand and change a Project without treating chat history as the Project itself.

## 3. Target user and boundary

Target user:

> A single Windows user managing a long-running local game or software Project. Git is optional. Manual participation comes first.

Required runtime boundary:

- Windows desktop application;
- local SQLite persistence;
- existing B1 services and commands;
- existing Library projection services;
- no external runtime dependency for the Manual path.

The Manual path must work with:

- empty Agent runtime registry;
- no Provider account;
- no Codex CLI;
- no Git installation;
- no SessionBinding;
- no cloud identity.

## 4. Non-negotiable invariants

1. `UserPrincipalRef` and `LogicalActorRef` remain separate.
2. `RoleKind` describes an Actor; it grants no authority.
3. Only a persisted named B1 authority command changes `AcceptedProjectState`.
4. Attempt creation, Handoff recording, and routing changes do not change accepted state.
5. Handoff is an input to Decision, not Decision itself.
6. Legacy adoption creates only the bootstrap root; it does not infer B1 history.
7. Library is a read/projection surface and never writes B1 state.
8. Summary, Handoff, Claim, Evidence, and Legacy records retain their own labels.
9. Failed atomic actions leave zero partial domain effects.
10. Every continuation uses persisted effective routing; no latest-by-timestamp guessing.
11. The UI coordinates named use cases and does not expose a generic Claim/Decision/effect builder.
12. The first screen emphasizes current accepted Project reality, not Activity, Provider, or Runtime.

## 5. Existing foundations to reuse

Do not redesign or duplicate:

- `B1AuthorityCommandService` and existing named authority commands;
- `B1NonAuthoritativeCommandService` and existing Attempt/Claim/Handoff operations;
- `B1ProjectionService` and `AcceptedProjectState`;
- `B1ProjectGovernanceRepository` and explicit Legacy adoption;
- `LibraryAcceptedStateReader`;
- `LibraryProjectionContractService`;
- `ProjectLibraryProposalService`;
- existing Legacy `WorkspaceViewModel` and Library data;
- sealed R5-B1 and Library Projection design records.

## 6. Phase sequence

### Phase 0 — Product journey audit — **COMPLETED**

Read-only audit of the current application surface.

Confirm:

- current Home / Project opening route;
- which B1 commands already have application-safe wrappers;
- which existing ViewModels can be reused and which must remain Legacy-only;
- current Library Overview entry point;
- current Project Home and Workspace navigation seams;
- missing UI states for initialization, Manual Work, Handoff, and Decision.

Deliverable:

- a short implementation map;
- a list of existing services to reuse;
- a list of missing seams;
- no code changes.

Audit findings:

#### Existing entry path

```text
HomeViewModel
  ↓ ProjectOpenService.OpenAsync
  ↓ ProjectRepository.UpsertAsync + default layout
  ↓ MainWindowViewModel.ShowWorkspace
  ↓ Legacy WorkspaceViewModel
     ├── LeaderPane
     ├── WorkPane
     └── LibraryPane
```

`ProjectOpenService.OpenAsync` currently mutates the local Project catalog before any B1 governance decision. `MainWindowViewModel` always routes an opened Project to the Legacy-style `WorkspaceViewModel`; there is no mechanical B1/Legacy route decision at the UI boundary.

#### Existing B1 capabilities available for reuse

- `AppServices.B1ProjectGovernance` — governed creation and explicit Legacy adoption;
- `AppServices.B1AuthorityCommands` — named authority commands including responsibility establishment and assignment decisions;
- `AppServices.B1NonAuthoritativeCommands` — Attempt, routing, Claim, and Handoff operations;
- `AppServices.B1Projections` — deterministic Project projection and effective routing;
- `B1ProjectGovernanceRepository.GetAsync` — governance presence and origin;
- `LibraryAcceptedStateReader` — read-only Accepted State to Library mapping;
- `LibraryProjectionContractService` and `ProjectLibraryProposalService` — explicit Library projection proposal flow;
- existing `WorkspaceViewModel.LibraryPane` — current Library browse surface, including B1 Accepted State Overview and Legacy labels.

#### Missing product seams

1. No read-only Project World entry classification service exists at Home level.
2. No local UserPrincipal provider is connected to Project creation/adoption.
3. Project creation and Legacy adoption are not separate explicit product confirmations.
4. No atomic initialization preview/confirm UI exists for Actor + Responsibility + Assignment + Revision.
5. No Project World Explorer ViewModel exists; B1 state is currently shown only inside the Legacy Workspace Library pane.
6. No Manual Work surface calls `CreateAttemptAndSelect` from user intent.
7. No bounded Handoff Composer maps user fields to typed Claims + Handoff atomically.
8. No guided Decision Composer presents Submitted-as versus Deciding-as or reloads the post-decision world.
9. Existing Library projection is browseable, but the Manual Decision flow is not connected to it as a user journey.
10. Existing Legacy Leader/Worker workspace must remain available as a separately labeled compatibility path.

#### Boundary conclusion

The architecture and persistence seams are present. The missing work is orchestration and presentation, not a new domain model. Phase 1 should therefore add mechanical entry classification first, without changing B1 tables, authority semantics, or the existing Legacy workspace behavior.

Gate: Phase 0 reported. Phase 1 implemented and verified; awaiting approval before Phase 2.

### Phase 1 — Project Home and automatic entry routing

Make Project Home the only project entry surface for the Manual path.

User intents:

- `Create Project`;
- `Open Local Project`.

Mechanical routing:

```text
governed B1 project → Project World
eligible Legacy project → explicit adoption confirmation
unknown local folder → governed Project creation confirmation
invalid / corrupt / unavailable → diagnosis, no mutation
```

Requirements:

- detection remains read-only;
- UI never asks the user to choose “B1” or “Legacy” as an architecture era;
- Legacy data is described as retained context, never imported truth;
- adoption and governed creation require explicit confirmation;
- no Agent / Provider / Runtime appears on Project Home.

Acceptance:

- new folder inspection creates no Project row before confirmation;
- opening a Legacy project creates no B1 rows before adoption;
- invalid state does not auto-repair or auto-adopt;
- existing Legacy workspace remains reachable through an explicit Legacy Context route.

Implementation record:

- Added read-only `B1GovernanceEntryFacts` inspection for project, governance, Legacy origin, and B1 history presence.
- Added `ProjectWorldEntryStatusService` with explicit classifications: `ProjectWorldReady`, `LegacySetupRequired`, `UnmanagedProjectUnavailable`, `CorruptProjectUnavailable`, and `PathUnavailable`.
- Project Home cards now display the mechanically detected entry status without creating or adopting anything.
- Existing Legacy workspace routing remains unchanged; Phase 1 only adds status visibility at the Home boundary.

Verification:

- `ProjectWorldEntryStatusServiceTests`: 5 passed.
- Combined entry-status and Home regression tests: 16 passed.
- Full `Workbench.App.Tests`: 458 passed.
- Full `Workbench.Storage.Tests`: 302 passed.
- `dotnet build AI.Game.Workbench.sln --no-restore`: succeeded with 0 warnings and 0 errors.
- `git diff --check`: clean (only existing line-ending normalization warnings from Git).

Gate: Phase 1 reported. Awaiting user approval before Phase 2.

### Phase 2 — Guided atomic Project World initialization

Implement the first-world setup experience using one fixed application use case.

User sees:

1. Who is acting in this Project?
2. What responsibility exists?
3. What is the first bounded assignment?
4. Review the effects.
5. Confirm once.

One confirmation calls the existing named authority command and creates:

- one LogicalActor;
- one Responsibility;
- one Assignment;
- one initial immutable Revision;
- one AuthorityDecision.

It must not create:

- a fake Session;
- an Attempt;
- a Claim;
- a Handoff;
- a Summary;
- an automatic Library fact.

Acceptance:

- preview creates zero persisted rows;
- confirmation is atomic;
- failure leaves zero partial effects;
- repeated initialization is rejected or safely reported;
- UI shows UserPrincipal and acting LogicalActor separately;
- RoleKind remains descriptive only.

Implementation record:

- Added explicit governance establishment for an already-registered empty Project candidate, separate from Legacy adoption.
- Added local Windows `UserPrincipal` resolution; it is never treated as a `LogicalActor`.
- Added `ProjectWorldInitializationService` with a non-persistent preview and a commit path that calls the existing named `EstablishResponsibility` authority command.
- The single authority command establishes the LogicalActor, Responsibility, Assignment, initial Revision, and AuthorityDecision atomically.
- Added `ProjectWorldSetupViewModel` and `ProjectWorldSetupView` with explicit governance/adoption, guided fields, effect preview, and one confirmation action.
- Existing unmanaged/Legacy workspace compatibility remains unchanged until the dedicated Project Home create/adopt confirmation flow is expanded; only eligible Legacy setup and already-governed empty worlds enter this setup surface.

Verification:

- Initialization service tests: 2 passed.
- Governance repository tests: 15 passed.
- Full `Workbench.App.Tests`: 460 passed.
- Full `Workbench.Storage.Tests`: 304 passed.
- `dotnet build AI.Game.Workbench.sln --no-restore`: succeeded with 0 warnings and 0 errors.
- `git diff --check`: clean (only existing line-ending normalization warnings from Git).

Gate: Phase 2 reported. Awaiting user approval before Phase 3.

### Phase 3 — State-first Project World Explorer

Build the first usable Project World screen around current accepted reality.

Default Overview sections:

- `CURRENT ACCEPTED STATE`;
- `NEEDS ATTENTION` as an application projection only;
- `ACTIVE WORK`;
- `RECENT DECISIONS`;
- `LEGACY CONTEXT`, only when applicable.

Navigation:

```text
Overview
Governance
Work
Decisions
History / Legacy Context
Library
```

Requirements:

- empty accepted state remains honestly empty;
- Assignment is not shown as execution status;
- Decision history uses Project commit order;
- Legacy context is visually and semantically separate;
- Library remains the primary object/history browsing surface;
- Summary, Handoff, and Claim are trace expansions, not the default truth view.

Acceptance:

- a newly initialized Project tells the user what exists and what has not yet been established;
- active Assignment shows Actor, Responsibility, Revision, and continuation selection;
- no fabricated “Project exists” contribution is displayed.

Implementation record:

- Added a read-only `ProjectWorldExplorerViewModel` and view as the first screen for governed B1 Projects.
- The Explorer loads persisted B1 state and the read-only Library Accepted State mapping; it does not write Library or authority data.
- `CURRENT ACCEPTED STATE` remains honestly empty when no accepted contributions exist.
- `ACTIVE WORK` shows Actor, Responsibility, effective Revision, and explicit continuation selection without presenting runtime execution status.
- `RECENT DECISIONS` is ordered by persisted Project commit sequence and describes effects rather than rendering a chat timeline.
- `NEEDS ATTENTION` is explicitly labeled as an application projection; it does not invent a domain `Pending` status.
- Adopted Legacy context is shown as a separate labeled area and never merged into the B1 truth chain.
- Existing unmanaged/Legacy compatibility Workspace routing remains unchanged.

Verification:

- Explorer semantic tests: 2 passed.
- Full `Workbench.App.Tests`: 462 passed.
- Full `Workbench.Storage.Tests`: 304 passed.
- `dotnet build AI.Game.Workbench.sln --no-restore`: succeeded with 0 warnings and 0 errors.
- `git diff --check`: clean (only existing line-ending normalization warnings from Git).

Gate: Phase 3 reported. Awaiting user approval before Phase 4.

### Phase 4 — Explicit Begin / Continue Manual Work

Add the first human work surface.

Entry actions:

- `Begin Manual Work` when no effective Attempt exists;
- `Continue Manual Work` when persisted effective routing identifies an Attempt.

Manual Work screen shows:

- signed-in UserPrincipal;
- acting LogicalActor;
- Responsibility;
- Assignment contract;
- effective Revision;
- selected Attempt;
- selected continuation Handoff, if any.

Rules:

- use `CreateAttemptAndSelect` semantics;
- never select the newest Attempt by timestamp;
- create no SessionBinding, Claim, Handoff, Decision, or runtime session;
- disposition invalidation must prevent unsafe continuation.

Acceptance:

- beginning work changes only Attempt/routing state;
- restart returns to the same explicit continuation;
- zero runtime and zero SessionBinding rows remain true.

Implementation record:

- Added `ManualWorkViewModel` and a dedicated Manual Work surface reachable from each Explorer Assignment.
- `Begin Manual Work` uses `CreateAttemptAndSelect` with the persisted expected routing selection.
- `Continue Manual Work` reloads the explicitly selected Attempt; it never chooses the newest Attempt by timestamp and never creates a second Attempt when a selection already exists.
- The screen shows UserPrincipal, acting Actor, Responsibility, Assignment, effective Revision, and current Attempt.
- The path creates no Runtime session, SessionBinding, Claim, Handoff, or AuthorityDecision.
- Revision/disposition validity remains enforced by the existing B1 routing repository.

Verification:

- Manual Work behavior tests: 2 passed.
- Full `Workbench.App.Tests`: 464 passed.
- Full `Workbench.Storage.Tests`: 304 passed.
- `dotnet build AI.Game.Workbench.sln --no-restore`: succeeded with 0 warnings and 0 errors.
- `git diff --check`: clean (only existing line-ending normalization warnings from Git).

Gate: Phase 4 reported. Awaiting user approval before Phase 5.

### Phase 5 — Guided bounded Handoff Composer

Expose one product action: `Record Handoff`.

Fixed semantic fields:

- Primary Result;
- Validations;
- Unresolved Issues;
- Proposed Project Changes;
- optional Proposed Assignment Revision;
- Evidence References.

The application maps these fields to typed Claims and one Handoff. The user never creates arbitrary Claim types.

Atomic submission:

```text
validate Project / Actor / Assignment / Revision / Attempt
        ↓
create typed Claims
        ↓
create Handoff
        ↓
update explicit continuation selection
```

It must not:

- change AcceptedProjectState;
- complete Assignment;
- close Attempt;
- verify Claims;
- create a Decision;
- infer acceptance from “done” wording.

Acceptance:

- Claim attribution uses the immutable Assignment assignee Actor;
- UserPrincipal remains the operator;
- stale continuation produces zero partial effects;
- Handoff can be followed by a separate Review & Decide action.

Implementation record:

- Added a bounded `Record Handoff` Composer to the Manual Work surface.
- Fixed fields map to typed `Result`, `Validation`, `UnresolvedIssue`, `ProposedStateContribution`, and optional `ProposedAssignmentRevision` Claims.
- Added atomic `RecordGuidedHandoffAndSelect` persistence: Claims, Handoff, and explicit continuation selection share one SQLite transaction.
- Claim attribution is always the immutable Assignment assignee Actor; the local UserPrincipal remains only the authenticated operator.
- Evidence is stored as EvidenceRef provenance and is never promoted to Accepted State.
- The existing `B1NonAuthoritativeCommandService` public command surface remains unchanged; guided handoff uses a separate application command service.

Verification:

- Guided Handoff Composer test: 1 passed.
- Non-authoritative command surface regression plus Composer tests: 11 passed.
- Full `Workbench.App.Tests`: 465 passed.
- Full `Workbench.Storage.Tests`: 304 passed.
- `dotnet build AI.Game.Workbench.sln --no-restore`: succeeded with 0 warnings and 0 errors.
- `git diff --check`: clean (only existing line-ending normalization warnings from Git).

Gate: Phase 5 reported. Awaiting user approval before Phase 6.

### Phase 6 — Guided Decision and post-decision loop

Expose one product action: `Review Work Result` or `Decide Assignment`.

The screen distinguishes:

```text
Submitted as: LogicalActor
Deciding as: UserPrincipal
```

Supported outcomes:

- Accepted;
- Rejected;
- Revision Required.

Proposed contributions support:

- Ignore;
- Adopt verbatim;
- Edit and establish, preserving the original Claim reference.

Preview:

- is in-memory;
- shows exact authority effects;
- is not a temporary Decision row;
- commits through the existing named authority command only.

After commit:

```text
reload B1 projection
→ update Overview
→ update Library projection proposal / view
→ preserve stored routing history
→ follow effective continuation rules
```

Acceptance:

- no UI action is named “Accept Handoff”;
- rejected work is not deleted;
- disposition and accepted contributions remain separate effects;
- stale revision, source, or authority failures leave zero partial effects;
- Accepted State changes only through the persisted AuthorityDecision.

Implementation record:

- Added `GuidedDecisionService` with in-memory preview and named-command commit.
- Added `GuidedDecisionViewModel` and `GuidedDecisionView`.
- Added `Review Work Result` routing from Manual Work to the guided decision flow.
- Kept `B1NonAuthoritativeCommandService` public command surface unchanged; guided handoff/decision orchestration remains in dedicated services.
- Added coverage for non-persistent preview and atomic confirm behavior.

Verification:

- Guided decision test: 1 passed.
- Full `Workbench.App.Tests`: 466 passed.
- Full `Workbench.Storage.Tests`: 304 passed.
- `dotnet build AI.Game.Workbench.sln --no-restore`: succeeded with 0 warnings and 0 errors.
- `git diff --check`: clean; only existing Git line-ending normalization warnings were reported.

Gate: Phase 6 reported. Awaiting user approval before Phase 7.

### Phase 7 — End-to-end Manual Project certification

Certification-only phase. No new production API unless a prior phase exposes a proven gap.

Required scenarios:

#### New Project

```text
Create
→ governed root
→ atomic initialization
→ Explorer
→ Begin Work
→ Record Handoff
→ verify Accepted State unchanged
→ Decide
→ Library Projection
→ close
→ reopen
→ same state and continuation
```

#### Legacy Project

```text
Open existing Legacy project
→ inspect
→ explicit adoption
→ verify bootstrap root only
→ initialize current B1 world
→ Legacy remains unchanged and labeled
→ continue Manual flow
```

Certification must verify:

- empty runtime registry;
- zero SessionBindings;
- no Provider, Codex, Git, terminal, sandbox, or Summary dependency;
- no automatic Legacy-to-B1 inference;
- same AcceptedProjectState after restart;
- Library labels and projection references remain stable;
- Library failure cannot mutate B1.

Implementation record:

- Added New Project end-to-end certification covering governed creation, atomic initialization, Explorer, Manual Work, Handoff, unchanged Accepted State before Decision, Decision, explicit Library projection, close/reopen, and persisted continuation behavior.
- Added certification assertions for empty runtime registry, zero SessionBindings, zero runtime-created side effects, stable Library provenance, and the rule that an Accepted revision no longer appears as effective work after restart while its history remains preserved.
- Reused the existing Legacy coexistence certification to verify explicit adoption, bootstrap-root-only behavior, Legacy immutability, and no automatic Legacy-to-current inference.
- Added the explicit Home `Create Project` route; ordinary `Open local project folder` remains compatible with the existing Legacy Workspace while new projects enter guided setup.
- Added a user-facing Library Update composer in Project Overview that creates and applies a provenance-validated Library proposal for an accepted contribution.

Verification:

- New Project certification: passed, including the user-facing Library Update composer.
- Legacy coexistence certification suite: passed.
- Full `Workbench.App.Tests` before Phase 8: 467 passed.

### Phase 8 — Product usability review and seal — **COMPLETED**

No new architecture. Review only:

- first-launch wording;
- whether the user understands “acting as” versus “signed in as”;
- whether the first Overview explains an empty Project clearly;
- whether Begin Work, Record Handoff, and Decide are discoverable;
- whether error messages explain “nothing was changed” after atomic failure;
- keyboard reachability and primary action accessibility;
- 1280×720 layout does not clip the core journey.

Seal only when the journey is understandable without exposing internal table names or requiring the user to know B1 terminology.

Implementation record:

- Reworded Project Home, setup, overview, Manual Work, and Decision surfaces around user-visible actions rather than internal B1 terms.
- Added explicit `Signed in as`, `Acting as`, `Submitted as`, and `Deciding as` labels so operator identity and acting identity remain distinguishable in the UI.
- Added empty-state guidance, discoverable primary actions, pre-confirmation wording, and atomic-failure messages that explain that nothing was changed.
- Added Phase 8 usability regression coverage for first-launch wording, core action discoverability, identity labels, preview-before-confirm behavior, internal-term leakage, and the 1280×720 minimum window.
- Performed manual Windows visual review at 1280×720 and confirmed the Project Home and existing Workspace surfaces render without clipping or blocking the core journey.

Verification:

- `Phase8UsabilityReviewTests`: 5 passed.
- Full `Workbench.App.Tests`: 472 passed.
- `dotnet build AI.Game.Workbench.sln --no-restore`: succeeded with 0 warnings and 0 errors.
- User visual review: approved on 2026-08-25.

Gate: Phase 8 sealed. Product Slice 1 Manual Project Experience is complete within the stated non-goals.

## 7. Explicit non-goals for this plan

- Agent / Worker / Codex / ACP adapters;
- Provider or model routing;
- Summary Journal generation or cleanup;
- automatic memory extraction;
- attachment ingestion or rich document/image preview;
- Git/worktree/terminal/sandbox/test execution;
- multi-user, cloud sync, or IAM;
- generic workflow engine;
- generic Claim or Decision builder;
- automatic Legacy migration;
- installer / self-contained packaging;
- R6 architecture design;
- causal ledger or expanded accountability platform.

## 8. Verification strategy

Each Phase follows:

```text
focused failing test or explicit audit
        ↓
minimal implementation
        ↓
focused verification
        ↓
full relevant suite
        ↓
report to user
        ↓
approval before next Phase
```

Minimum final verification:

```powershell
dotnet build AI.Game.Workbench.sln --no-restore
dotnet test AI.Game.Workbench.sln --no-restore
git diff --check
```

The plan is complete when the Manual journey works end to end and the user can return the next day knowing:

> “这个项目现在是什么、刚刚发生了什么、我接下来可以从哪里继续。”

## 9. Governing records

- [Product Slice 1 — Manual Project World Design](../specs/2026-08-24-product-slice-1-manual-project-world-design.md)
- [R5-B1 Baseline](../../architecture/r5-b1-baseline.md)
- [Workbench Global Architecture](../../architecture/workbench-global-architecture.md)
- [Library Projection Boundary Design](../specs/2026-08-25-library-projection-boundary-design.md)
