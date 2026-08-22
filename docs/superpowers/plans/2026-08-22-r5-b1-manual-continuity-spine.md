# R5-B1 Manual Continuity Spine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the approved R5-B1 durable Manual continuity spine so a Project can preserve responsibility, accept bounded human work through Claims/Handoffs, commit attributable authority decisions, and recover deterministic accepted state without any Agent session or transcript.

**Architecture:** Add a parallel `Continuity` slice across Core, Storage, and App. Core owns immutable B1 identities, closed unions, projections, and pure authority validation; Storage adds an additive SQLite v20 schema, bootstrap/adoption persistence, immutable histories, routing CAS, and one atomic validated-decision commit boundary; App exposes the eight non-authoritative commands and exactly six named authority commands. Legacy tables remain independently readable and are never converted, back-written, or treated as B1 authority.

**Tech Stack:** C# / .NET 10 / xUnit 2.9 / Microsoft.Data.Sqlite 10.0 / existing `Workbench.Core`, `Workbench.Storage`, `Workbench.App`, and `AppServices` composition

**Spec:** `docs/superpowers/specs/2026-08-22-r5-b1-manual-continuity-spine-design.md`

## Global Constraints

- The approved spec and `docs/superpowers/specs/2026-08-22-r5-boundary-reconciliation-design.md` are authoritative. Do not reopen architecture while executing this plan.
- Use genuine TDD: no production code before a focused test demonstrates a new missing behavior. Existing green characterization tests are regression evidence, never RED evidence.
- Add B1 alongside Legacy. Do not reinterpret, migrate, backfill, dual-write, freeze, delete, or cut over `project_leaders`, epochs, Tasks, WorkerExecution, review, AutoProceed, CompletionPackage, Memory, Daily Summary, Library, task events, or R5-A Summary.
- `Responsibility` has no owner field. `Assignment.AssigneeActorRef`, Revision contracts, Attempt revision binding, SessionBinding ownership, Claims, Handoffs, Decisions, and accepted contributions are immutable.
- SessionBinding is optional and connectivity-only. No Provider, model, runtime, thread, worktree, Git, test, sandbox, permission-policy, or execution-status field enters B1 Core.
- Claims/Handoffs remain non-authoritative. `AcceptedProjectState` is rebuilt only from authoritative records and has no write method or canonical materialized table.
- B1 has exactly eight capabilities and exactly six public authority commands. Do not add wildcards, denies, role templates, arbitrary resource paths, generic ACL/RBAC, public `effects[]`, a generic decision builder, or a seventh authority command.
- Accepted contribution scope is exactly `Project | Responsibility | Assignment`. Supersession is explicit, `0..1`, exact-scope, current-target-only, and atomic with the containing Decision.
- Routing selections are stored Application refs. Effective refs are deterministic projections. CAS compares the stored ref, including when the effective projection is null.
- Authority is bootstrap-rooted or effective Assignment-derived, capability/locality checked per effect, non-amplifying, and evaluated only from the pre-commit Project state.
- Every Responsibility's first Assignment is bootstrap-seeded. Prospective refs resolve only within one named command and never supply same-Decision authority.
- Keep R5-A Summary append-only and non-authoritative. No B1 code queries, writes, recomputes, or injects Summary.
- Do not add an Agent Gateway, Manual Gateway, provider adapter, execution abstraction, UI, or automatic Claim verification in R5-B1.
- Tests and certification use `TemporaryDatabase` or another unique disposable path. Do not open, hash, copy, initialize, migrate, checkpoint, or otherwise touch `%LOCALAPPDATA%\AI Game Workbench\workbench.db`.
- Migration 020 is a forward-only additive implementation decision. Never edit migrations 001–019.

## File / Responsibility Map

### Create

- `src/Workbench.Core/Continuity/B1References.cs` — validated closed identity/reference value types and external locators.
- `src/Workbench.Core/Continuity/B1Contracts.cs` — closed enums, authority boundaries, Responsibility/Revision contracts, and durable identity records.
- `src/Workbench.Core/Continuity/B1Claims.cs` — immutable Claim payload union, EvidenceRef use, Handoff, claimant and considered-ref unions.
- `src/Workbench.Core/Continuity/B1Commands.cs` — eight non-authoritative command records, six authority command records, prospective targets, and contribution instructions.
- `src/Workbench.Core/Continuity/B1ProjectState.cs` — complete immutable authoritative/routing state supplied to validation and projection.
- `src/Workbench.Core/Continuity/B1Projector.cs` — deterministic current Revision, delegation, accepted-state, and effective-routing projections.
- `src/Workbench.Core/Continuity/B1AuthorityEvaluator.cs` — six overloads that validate named commands and produce non-constructible validated commits.
- `src/Workbench.Storage/Migrations/Migration020ManualContinuitySpine.cs` — additive B1 schema plus mechanically captured pre-B1 Project provenance.
- `src/Workbench.Storage/Continuity/B1ProjectGovernanceRepository.cs` — atomic new governed Project creation and one-time Legacy Project adoption.
- `src/Workbench.Storage/Continuity/B1RoutingRepository.cs` — immutable Attempt/SessionBinding persistence and stored-ref CAS.
- `src/Workbench.Storage/Continuity/B1ClaimHandoffRepository.cs` — immutable Claim/Handoff persistence and typed-reference validation.
- `src/Workbench.Storage/Continuity/B1AuthorityRepository.cs` — state loading plus ProjectCommitSequence-CAS atomic Decision/effect persistence.
- `src/Workbench.App/Continuity/B1NonAuthoritativeCommandService.cs` — the eight named non-authoritative commands and three narrow create-and-select conveniences.
- `src/Workbench.App/Continuity/B1AuthorityCommandService.cs` — exactly six public authority command methods; no public arbitrary Decision submission.
- `src/Workbench.App/Continuity/B1ProjectionService.cs` — durable reload and deterministic accepted/routing query composition.
- `tests/Workbench.Core.Tests/Continuity/B1IdentityContractTests.cs` — identity, contract, immutability, and closed-enum tests.
- `tests/Workbench.Core.Tests/Continuity/B1ClaimHandoffContractTests.cs` — Claim/Handoff union, attribution, and provenance tests.
- `tests/Workbench.Core.Tests/Continuity/B1ProjectionTests.cs` — deterministic current/effective projection tests.
- `tests/Workbench.Core.Tests/Continuity/B1AuthorityEvaluatorTests.cs` — command-shape, capability, locality, amplification, stale, and combination tests.
- `tests/Workbench.Storage.Tests/Database/ManualContinuityMigrationTests.cs` — v20 schema, pre-B1 provenance, additive preservation, and negative mapping certification.
- `tests/Workbench.Storage.Tests/Continuity/B1ProjectGovernanceRepositoryTests.cs` — new Project bootstrap and one-time Legacy adoption tests.
- `tests/Workbench.Storage.Tests/Continuity/B1ContinuityStorageFixture.cs` — disposable governed B1 fixture shared by Storage tests; raw seed SQL is removed when the authority repository lands.
- `tests/Workbench.Storage.Tests/Continuity/B1RoutingRepositoryTests.cs` — Attempt/Binding history and stored selection CAS tests.
- `tests/Workbench.Storage.Tests/Continuity/B1ClaimHandoffRepositoryTests.cs` — immutable Claim/Handoff round-trip and cross-Project/typed-ref tests.
- `tests/Workbench.Storage.Tests/Continuity/B1AuthorityRepositoryTests.cs` — sequence, atomicity, rollback, and concurrency tests.
- `tests/Workbench.App.Tests/Continuity/B1NonAuthoritativeCommandServiceTests.cs` — eight-command Application validation and routing orchestration.
- `tests/Workbench.App.Tests/Continuity/B1AuthorityCommandServiceTests.cs` — six-command integration and authority semantics.
- `tests/Workbench.App.Tests/Continuity/B1ProjectionRecoveryTests.cs` — restart/rebuild and stored/effective read tests.
- `tests/Workbench.App.Tests/Continuity/B1ManualContinuityCertificationTests.cs` — required zero-Session end-to-end Manual proof.
- `tests/Workbench.App.Tests/Continuity/B1LegacyCoexistenceCertificationTests.cs` — explicit one-way Legacy bridge and no automatic mappings.

### Modify

- `src/Workbench.Storage/Database/MigrationRunner.cs` — register `Migration020ManualContinuitySpine` after v19.
- `src/Workbench.App/Services/AppServices.cs` — construct/expose only the B1 repositories and Application services; no UI or runtime wiring.
- `tests/Workbench.Storage.Tests/Database/WorkbenchDatabaseTests.cs` — change the latest expected schema version from 19 to 20 and retain historical preservation coverage.

## Implementation Landing Decisions

1. All B1-owned IDs are distinct validated `readonly record struct` wrappers over `Guid`; `UserPrincipalRef`, `ExternalSessionRef`, and `EvidenceRef` are validated external string locators and are not Project-local B1 entity IDs.
2. Core unions are closed through abstract records with private constructors and nested sealed cases. Callers cannot invent a fourth contribution scope, a third claimant kind, or an arbitrary command target.
3. Storage serializes only evidence-locator arrays and authority-boundary enum arrays as canonical JSON. Closed Claim discriminants and every B1 identity-bearing relationship use explicit columns and foreign keys. JSON never contains provider transcripts, evidence bodies, B1 entity references, or execution metadata.
4. Every B1 entity table contains `project_id`; B1-to-B1 references use composite ownership constraints wherever SQLite can enforce them. External refs are never forced into Project-local foreign keys.
5. `b1_project_governance.last_commit_sequence` is the optimistic Project authority version. `B1AuthorityEvaluator` validates a snapshot; `B1AuthorityRepository.TryCommitAsync` first CAS-increments that sequence in one SQLite transaction and then persists the whole validated Decision. Sequence conflict causes reload/revalidation, while target-specific stale guards still fail the whole command.
6. The only general-looking persistence artifact is `ValidatedAuthorityDecision`; its constructor is internal to Core and only the six evaluator overloads can create it. UI/App callers receive no public Decision/effect builder.
7. Routing pointers live in dedicated routing tables so immutable Assignment/Attempt rows remain immutable. Effective routing is never stored.
8. No AcceptedProjectState table is added. `B1Projector` rebuilds it from Decision-owned identities/effects and non-superseded contributions in ProjectCommitSequence order.

---

### Task 1: Define Durable B1 References, Contracts, And Identities

**Files:**
- Create: `src/Workbench.Core/Continuity/B1References.cs`
- Create: `src/Workbench.Core/Continuity/B1Contracts.cs`
- Test: `tests/Workbench.Core.Tests/Continuity/B1IdentityContractTests.cs`

**Interfaces:**
- Consumes: existing `Guid`, `DateTimeOffset`, immutable C# records, and `Workbench.Core.Projects.Project` IDs.
- Produces: `ProjectRef`, `LogicalActorRef`, `ResponsibilityRef`, `AssignmentRef`, `RevisionRef`, `AttemptRef`, `SessionBindingRef`, `ClaimRef`, `HandoffRef`, `AuthorityDecisionRef`, `AcceptedStateContributionRef`, `UserPrincipalRef`, `ExternalSessionRef`, `EvidenceRef`, `B1GovernanceOrigin`, `RoleKind`, `B1AuthorityCapability`, `AssignmentDisposition`, `AuthorityBoundary`, `ProjectGovernance`, `LogicalActor`, `ResponsibilityContract`, `Responsibility`, `AssignmentRevisionContract`, `AssignmentRevision`, `Assignment`, `Attempt`, and `SessionBinding`.

- [ ] **Step 1: Write focused failing contract tests.** Add named tests `B1_owned_refs_reject_empty_guid_and_remain_distinct`, `External_refs_reject_blank_locator`, `RoleKind_and_capability_sets_are_closed`, `Responsibility_has_no_owner_property`, `Assignment_assignee_and_initial_revision_are_constructor_only`, `Attempt_requires_one_assignment_and_revision`, `SessionBinding_contains_no_provider_or_execution_state`, and `Delegated_boundary_defaults_empty_and_must_be_subset_of_maximum`.

  ```csharp
  var maximum = AuthorityBoundary.Create([
      B1AuthorityCapability.DelegateAssignment,
      B1AuthorityCapability.AcceptAssignmentStateContribution]);
  var revision = new AssignmentRevisionContract("Implement bounded work", AuthorityBoundary.Empty);
  Assert.True(revision.DelegatedAuthorityBoundary.IsSubsetOf(maximum));
  Assert.DoesNotContain(typeof(Responsibility).GetProperties(), p => p.Name.Contains("Owner"));
  ```

- [ ] **Step 2: Run RED.**

  ```powershell
  dotnet test tests/Workbench.Core.Tests/Workbench.Core.Tests.csproj --filter FullyQualifiedName~B1IdentityContractTests
  ```

  Expected RED: compile failure because `Workbench.Core.Continuity` and its closed value types do not exist. Existing Core tests must remain green when run separately.

- [ ] **Step 3: Implement the minimum immutable contracts.** Each B1-owned ref rejects `Guid.Empty`; each external ref rejects blank text without interpreting its format. Use exactly these enums:

  ```csharp
  public enum B1GovernanceOrigin { Created, Adopted }

  public enum RoleKind { Leader, Worker, Reviewer }

  public enum B1AuthorityCapability
  {
      EstablishLogicalActor,
      EstablishResponsibility,
      DelegateAssignment,
      DecideAssignmentDisposition,
      ActivateAssignmentRevision,
      AcceptProjectStateContribution,
      AcceptResponsibilityStateContribution,
      AcceptAssignmentStateContribution
  }

  public enum AssignmentDisposition { Accepted, Rejected, RevisionRequired }
  ```

  `AuthorityBoundary` owns a read-only set, rejects undefined enum values, removes duplicates, exposes `Contains`, `Except`, and `IsSubsetOf`, and has `AuthorityBoundary.Empty`. `ResponsibilityContract` contains only `Obligation`, `ExpectedOutcome`, and `MaximumDelegableAuthorityBoundary`. `AssignmentRevisionContract` contains only `WorkContract` and `DelegatedAuthorityBoundary`. Entity records exactly preserve the approved identities; no lifecycle/status/provider/runtime/Git fields are added.

- [ ] **Step 4: Run focused GREEN and the Core regression suite.**

  ```powershell
  dotnet test tests/Workbench.Core.Tests/Workbench.Core.Tests.csproj --filter FullyQualifiedName~B1IdentityContractTests
  dotnet test tests/Workbench.Core.Tests/Workbench.Core.Tests.csproj
  ```

- [ ] **Step 5: Commit independently.**

  ```powershell
  git add src/Workbench.Core/Continuity/B1References.cs src/Workbench.Core/Continuity/B1Contracts.cs tests/Workbench.Core.Tests/Continuity/B1IdentityContractTests.cs
  git commit -m "feat(core): define r5 b1 durable identities"
  ```

### Task 2: Define Immutable Claims, Handoffs, Scopes, And Command Shapes

**Files:**
- Create: `src/Workbench.Core/Continuity/B1Claims.cs`
- Create: `src/Workbench.Core/Continuity/B1Commands.cs`
- Test: `tests/Workbench.Core.Tests/Continuity/B1ClaimHandoffContractTests.cs`

**Interfaces:**
- Consumes: Task 1 refs/contracts/entities.
- Produces: closed `ClaimantRef`, `DecidingAuthorityRef`, `ClaimPayload`, `ContributionScopeRef`, `ConsideredRef`, `Claim`, `Handoff`, `AcceptedStateContribution`, `AuthorityDecision`, `ResponsibilityTarget`, `AssignmentAssigneeTarget`, `ContributionScopeTarget`, `AcceptedContributionInstruction`, eight non-authoritative command records, six authority command records, and `B1FailureCode`/`B1CommandException`.

- [ ] **Step 1: Write failing union and shape tests.** Add `Claimant_is_user_or_actor_only`, `Claim_payload_has_exactly_five_closed_cases`, `User_claim_cannot_carry_session_binding`, `Contribution_scope_is_explicit_and_closed`, `Handoff_has_one_primary_result_and_no_status`, `Primary_result_contract_targets_assignee`, `Considered_ref_distinguishes_claim_handoff_and_evidence`, `Prospective_targets_exist_only_in_commands`, `Eight_non_authoritative_commands_are_named`, and `Six_authority_commands_are_named_without_effect_array`.

  ```csharp
  ClaimPayload payload = new ClaimPayload.ProposedStateContribution(
      "Session is replaceable",
      new ContributionScopeRef.Project(projectRef),
      ProposedSupersedes: null);
  Assert.IsType<ClaimPayload.ProposedStateContribution>(payload);
  Assert.DoesNotContain(typeof(AuthorAcceptedStateCommand).GetProperties(), p => p.Name == "Effects");
  ```

- [ ] **Step 2: Run RED.**

  ```powershell
  dotnet test tests/Workbench.Core.Tests/Workbench.Core.Tests.csproj --filter FullyQualifiedName~B1ClaimHandoffContractTests
  ```

  Expected RED: compile failure for the missing Claim/Handoff unions and command records.

- [ ] **Step 3: Implement the closed records.** Use private-base/nested-sealed cases for:

  ```csharp
  ClaimantRef = UserPrincipal(UserPrincipalRef) | LogicalActor(LogicalActorRef)
  DecidingAuthorityRef = UserPrincipal(UserPrincipalRef) | LogicalActor(LogicalActorRef)
  ContributionScopeRef = Project(ProjectRef) | Responsibility(ResponsibilityRef) | Assignment(AssignmentRef)
  ConsideredRef = Claim(ClaimRef) | Handoff(HandoffRef) | Evidence(EvidenceRef)
  ClaimPayload = Result | Validation | UnresolvedIssue | ProposedStateContribution | ProposedAssignmentRevision
  ```

  `Claim` contains `ClaimRef`, `ProjectRef`, `ClaimantRef`, optional `SourceSessionBindingRef`, one payload, `IReadOnlyList<EvidenceRef>`, and `CreatedAt`. `Handoff` contains `HandoffRef`, `AttemptRef`, required `ResultClaimRef`, typed Claim-ref lists, EvidenceRefs, and `CreatedAt`; it has no state field. `AcceptedStateContribution` contains only statement, explicit scope, optional supersedes, owning Decision ref, optional proposal source, and its own ref.

  Use this closed failure enum consistently across Core, Storage, and App; `B1CommandException.Code` exposes one value and preserves no partial-result payload:

  ```csharp
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
  ```

  Define exactly these non-authoritative command records: `CreateAttemptCommand`, `SelectCurrentAttemptCommand`, `RecordClaimCommand`, `CreateHandoffCommand`, `SelectContinuationHandoffCommand`, `CreateSessionBindingCommand`, `SelectCurrentSessionBindingCommand`, `ClearCurrentSessionBindingCommand`. Define exactly these authority command records: `EstablishLogicalActorCommand`, `EstablishResponsibilityCommand`, `DelegateAssignmentCommand`, `DecideAssignmentCommand`, `ActivateAssignmentRevisionCommand`, `AuthorAcceptedStateCommand`. Every Application command carries a command-time `AuthenticatedOperatorRef`; in B1 it must equal the Project bootstrap `UserPrincipalRef`. That command-time operator is never persisted as Claimant or DecidingAuthority metadata. Each authority command separately carries its `DecidingAuthorityRef` and fixed effect shape; none accepts `IReadOnlyList<object>`, `Effects`, an arbitrary target string, or a custom scope.

- [ ] **Step 4: Run focused GREEN and Tasks 1–2 together.**

  ```powershell
  dotnet test tests/Workbench.Core.Tests/Workbench.Core.Tests.csproj --filter "FullyQualifiedName~B1IdentityContractTests|FullyQualifiedName~B1ClaimHandoffContractTests"
  ```

- [ ] **Step 5: Commit independently.**

  ```powershell
  git add src/Workbench.Core/Continuity/B1Claims.cs src/Workbench.Core/Continuity/B1Commands.cs tests/Workbench.Core.Tests/Continuity/B1ClaimHandoffContractTests.cs
  git commit -m "feat(core): define r5 b1 claims and commands"
  ```

### Task 3: Implement Deterministic Current, Accepted, And Routing Projections

**Files:**
- Create: `src/Workbench.Core/Continuity/B1ProjectState.cs`
- Create: `src/Workbench.Core/Continuity/B1Projector.cs`
- Test: `tests/Workbench.Core.Tests/Continuity/B1ProjectionTests.cs`

**Interfaces:**
- Consumes: Tasks 1–2 immutable records and stored selections.
- Produces: `B1ProjectState`, `RevisionDispositionRecord`, `AssignmentRoutingSelection`, `AttemptRoutingSelection`, `AcceptedProjectState`, `B1ProjectProjection`, and `B1Projector.Build(B1ProjectState)`.

  ```csharp
  public static class B1Projector
  {
      public static B1ProjectProjection Build(B1ProjectState state);
  }

  public sealed record B1ProjectProjection(
      ProjectRef ProjectRef,
      IReadOnlyDictionary<AssignmentRef, RevisionRef> CurrentEffectiveRevisionRefs,
      IReadOnlySet<AssignmentRef> CurrentDelegationAssignments,
      IReadOnlySet<AssignmentRef> EffectiveFulfillmentAssignments,
      IReadOnlyDictionary<AssignmentRef, AttemptRef?> StoredAttemptSelections,
      IReadOnlyDictionary<AssignmentRef, AttemptRef?> EffectiveCurrentAttemptRefs,
      IReadOnlyDictionary<AttemptRef, HandoffRef?> StoredHandoffSelections,
      IReadOnlyDictionary<AttemptRef, HandoffRef?> EffectiveCurrentHandoffRefs,
      IReadOnlyDictionary<AttemptRef, SessionBindingRef?> StoredBindingSelections,
      IReadOnlyDictionary<AttemptRef, SessionBindingRef?> EffectiveCurrentBindingRefs,
      AcceptedProjectState AcceptedProjectState);
  ```

- [ ] **Step 1: Write failing projection tests.** Add `Initial_revision_is_current_until_one_authorized_successor`, `Each_revision_has_zero_or_one_disposition`, `Current_delegation_means_not_replaced`, `Effective_fulfillment_requires_current_unresolved_revision`, `Contributions_coexist_without_supersession`, `Explicit_supersession_removes_only_its_current_target`, `Stored_attempt_can_remain_when_effective_attempt_is_null`, `Effective_handoff_and_binding_require_effective_parent_attempt`, and `CreatedAt_never_changes_decision_order`.

  ```csharp
  var projection = B1Projector.Build(state);
  Assert.Equal(attemptRef, projection.StoredAttemptSelections[assignmentRef]);
  Assert.Null(projection.EffectiveCurrentAttemptRefs[assignmentRef]);
  Assert.DoesNotContain(oldContribution, projection.AcceptedProjectState.CurrentContributions);
  Assert.Contains(newContribution, projection.AcceptedProjectState.CurrentContributions);
  ```

- [ ] **Step 2: Run RED.**

  ```powershell
  dotnet test tests/Workbench.Core.Tests/Workbench.Core.Tests.csproj --filter FullyQualifiedName~B1ProjectionTests
  ```

  Expected RED: compile failure because no B1 state aggregate or projector exists.

- [ ] **Step 3: Implement a pure projector only.** `B1ProjectState` is immutable input loaded from authoritative/history tables. `B1Projector.Build`:

  - orders Decisions by `ProjectCommitSequence`, never `CreatedAt`;
  - identifies the unique Revision-chain leaf per Assignment as current;
  - treats missing current-Revision disposition as query-only `Unresolved`;
  - computes current delegations from explicit replacement edges only;
  - computes effective fulfillment from current delegation plus unresolved current Revision;
  - removes a contribution from current state only when one later successfully committed contribution explicitly supersedes it;
  - keeps stored routing refs unchanged and derives effective refs through all parent validity predicates;
  - exposes no mutation or AcceptedProjectState writer.

- [ ] **Step 4: Run focused GREEN and all Core tests.**

  ```powershell
  dotnet test tests/Workbench.Core.Tests/Workbench.Core.Tests.csproj --filter FullyQualifiedName~B1ProjectionTests
  dotnet test tests/Workbench.Core.Tests/Workbench.Core.Tests.csproj
  ```

- [ ] **Step 5: Commit independently.**

  ```powershell
  git add src/Workbench.Core/Continuity/B1ProjectState.cs src/Workbench.Core/Continuity/B1Projector.cs tests/Workbench.Core.Tests/Continuity/B1ProjectionTests.cs
  git commit -m "feat(core): project r5 b1 current state"
  ```

### Task 4: Implement Pure Named-Command Authority Validation

**Files:**
- Create: `src/Workbench.Core/Continuity/B1AuthorityEvaluator.cs`
- Test: `tests/Workbench.Core.Tests/Continuity/B1AuthorityEvaluatorTests.cs`

**Interfaces:**
- Consumes: Tasks 1–3 commands, state, and projections.
- Produces: six overloads on `B1AuthorityEvaluator`, plus `ValidatedAuthorityDecision` whose constructor is not callable by App/UI code.

  ```csharp
  public sealed class B1AuthorityEvaluator
  {
      public ValidatedAuthorityDecision Evaluate(B1ProjectState state, EstablishLogicalActorCommand command, AuthorityDecisionRef id, DateTimeOffset createdAt);
      public ValidatedAuthorityDecision Evaluate(B1ProjectState state, EstablishResponsibilityCommand command, AuthorityDecisionRef id, DateTimeOffset createdAt);
      public ValidatedAuthorityDecision Evaluate(B1ProjectState state, DelegateAssignmentCommand command, AuthorityDecisionRef id, DateTimeOffset createdAt);
      public ValidatedAuthorityDecision Evaluate(B1ProjectState state, DecideAssignmentCommand command, AuthorityDecisionRef id, DateTimeOffset createdAt);
      public ValidatedAuthorityDecision Evaluate(B1ProjectState state, ActivateAssignmentRevisionCommand command, AuthorityDecisionRef id, DateTimeOffset createdAt);
      public ValidatedAuthorityDecision Evaluate(B1ProjectState state, AuthorAcceptedStateCommand command, AuthorityDecisionRef id, DateTimeOffset createdAt);
  }
  ```

- [ ] **Step 1: Write failing policy tests.** Cover `Bootstrap_principal_has_root_authority_only_for_its_project`, `Non_bootstrap_operator_cannot_submit_b1_command`, `Actor_decision_requires_bootstrap_manual_operator_without_borrowing_root_authority`, `Role_kind_grants_no_authority`, `Empty_delegated_boundary_grants_no_authority`, `Capability_requires_one_local_effect_source`, `Capabilities_from_different_responsibilities_do_not_cross_leak`, `Logical_actor_cannot_seed_new_responsibility`, `Bootstrap_can_establish_and_seed_atomically`, `New_assignment_boundary_cannot_amplify_actor_authority`, `Revision_added_capabilities_cannot_amplify_authority`, `Retained_or_removed_capabilities_are_not_new_amplification`, `New_effects_do_not_authorize_same_decision`, `Every_effect_is_checked_before_draft_creation`, `Invalid_compound_shapes_return_INVALID_DECISION_SHAPE`, and `Contributions_require_scope_specific_capability`.

- [ ] **Step 2: Add failing stale/combination tests.** Cover one-disposition, expected-current Revision, activation eligibility, `RevisionRequired + activation`, replacement matrix, same-Assignment restrictions, current supersession target, exact scope, prospective scope resolution, no intra-Decision supersession, and all-or-nothing validation.

- [ ] **Step 3: Run RED.**

  ```powershell
  dotnet test tests/Workbench.Core.Tests/Workbench.Core.Tests.csproj --filter FullyQualifiedName~B1AuthorityEvaluatorTests
  ```

  Expected RED: compile failure because the evaluator and validated commit type do not exist.

- [ ] **Step 4: Implement validation in the mandated order.** Validate fixed command shape, same Project, existing/prospective refs, pre-commit stale guards, capability/locality per effect, and no-amplification before creating `ValidatedAuthorityDecision`. It carries `ExpectedProjectCommitSequence = state.Governance.LastProjectCommitSequence`, resolved durable IDs for prospective objects, considered refs, and fully resolved effects. It contains at least one effect. Its constructor is `internal`; do not add a public static factory accepting effects.

  First require `command.AuthenticatedOperatorRef == state.Governance.BootstrapPrincipalRef`. Bootstrap capability checks then use Project root authority and maximum-boundary subset checks. LogicalActor checks require at least one current, unresolved, local Assignment source; the bootstrap operator does not lend root authority to an Actor-attributed Decision. Project-level capabilities still require explicit delegation. A newly created Actor, Responsibility, Assignment, or Revision never participates in the current Decision's authority calculation.

- [ ] **Step 5: Run focused GREEN and all Core tests.**

  ```powershell
  dotnet test tests/Workbench.Core.Tests/Workbench.Core.Tests.csproj --filter FullyQualifiedName~B1AuthorityEvaluatorTests
  dotnet test tests/Workbench.Core.Tests/Workbench.Core.Tests.csproj
  ```

- [ ] **Step 6: Commit independently.**

  ```powershell
  git add src/Workbench.Core/Continuity/B1AuthorityEvaluator.cs tests/Workbench.Core.Tests/Continuity/B1AuthorityEvaluatorTests.cs
  git commit -m "feat(core): validate r5 b1 authority commands"
  ```

### Task 5: Add Additive Migration 020 And Mechanical Legacy Provenance

**Files:**
- Create: `src/Workbench.Storage/Migrations/Migration020ManualContinuitySpine.cs`
- Modify: `src/Workbench.Storage/Database/MigrationRunner.cs`
- Modify: `tests/Workbench.Storage.Tests/Database/WorkbenchDatabaseTests.cs`
- Test: `tests/Workbench.Storage.Tests/Database/ManualContinuityMigrationTests.cs`

**Interfaces:**
- Consumes: the existing v19 migration chain, `MigrationRunner.ApplyAsync`, `HistoricalMigrationTestDatabase`, and `TemporaryDatabase`.
- Produces: schema version 20 and only the additive `b1_*` landing zone below.

- [ ] **Step 1: Write migration RED tests.** Add `Migration020_sets_user_version_and_creates_exact_b1_tables`, `Migration020_marks_only_projects_existing_at_upgrade_as_pre_b1`, `Post_migration_project_insert_is_not_legacy_eligible`, `Migration020_does_not_synthesize_any_b1_domain_or_authority_rows`, `Migration020_preserves_v19_schema_and_rows`, `B1_owned_foreign_keys_reject_cross_project_identity`, and `B1_tables_enforce_single_revision_disposition_and_single_superseder`.

  Build a synthetic v19 database with two Projects plus representative epoch, Task/revision/event, review, Memory, Library, and R5-A Summary rows. Capture their counts and bounded values before migration; do not read the live database.

- [ ] **Step 2: Run RED.**

  ```powershell
  dotnet test tests/Workbench.Storage.Tests/Workbench.Storage.Tests.csproj --filter "FullyQualifiedName~ManualContinuityMigrationTests|FullyQualifiedName~WorkbenchDatabaseTests.Latest_migrations_set_user_version"
  ```

  Expected RED: latest version is still 19 and no `b1_*` objects exist.

- [ ] **Step 3: Implement `Migration020ManualContinuitySpine.Version = 20`.** Register it immediately after v19 through the normal transaction helper. Create these tables; all ID/timestamp columns use the repository's existing `TEXT` round-trip conventions:

  ```text
  b1_legacy_project_origins(
      project_id PK/FK projects,
      source_schema_version CHECK = 19)

  b1_project_governance(
      project_id PK/FK projects,
      bootstrap_user_principal NOT NULL,
      origin CHECK Created|Adopted,
      adopted_at NULL iff Created,
      last_commit_sequence >= 0)

  b1_logical_actors(id, project_id, role_kind, authorized_by_decision_id, created_at)
  b1_responsibilities(id, project_id, obligation, expected_outcome,
      maximum_authority_json, authorized_by_decision_id, created_at)
  b1_assignments(id, project_id, responsibility_id, assignee_actor_id,
      replaces_assignment_id?, authorized_by_decision_id)
  b1_revisions(id, project_id, assignment_id, prior_revision_id?, work_contract,
      delegated_authority_json, authorized_by_decision_id)
  b1_revision_dispositions(revision_id PK, project_id, assignment_id,
      disposition CHECK Accepted|Rejected|RevisionRequired, authority_decision_id)
  b1_attempts(id, project_id, assignment_id, effective_revision_id, created_at)
  b1_session_bindings(id, project_id, attempt_id, logical_actor_id,
      external_session_ref, created_at)
  b1_assignment_routing(assignment_id PK, project_id, selected_attempt_id?)
  b1_attempt_routing(attempt_id PK, project_id,
      selected_handoff_id?, selected_session_binding_id?)
  b1_claims(id, project_id, claimant_kind,
      claimant_user_principal?, claimant_actor_id?, source_binding_id?,
      kind CHECK five approved kinds,
      statement?,
      proposed_scope_kind?, proposed_scope_project_id?,
      proposed_scope_responsibility_id?, proposed_scope_assignment_id?,
      proposed_supersedes_contribution_id?,
      proposed_assignment_id?, base_revision_id?, proposed_work_contract?,
      proposed_delegated_authority_json?,
      evidence_refs_json, created_at)
  b1_handoffs(id, project_id, attempt_id, result_claim_id,
      evidence_refs_json, created_at)
  b1_handoff_claim_refs(handoff_id, project_id, role_kind,
      ordinal >= 0, claim_id, PK(handoff_id, role_kind, ordinal))
  b1_authority_decisions(id, project_id, project_commit_sequence,
      command_kind CHECK six approved commands, deciding_authority_kind,
      deciding_user_principal?, deciding_actor_id?, created_at,
      UNIQUE(project_id, project_commit_sequence))
  b1_decision_considered_refs(decision_id, project_id, ordinal,
      ref_kind CHECK Claim|Handoff|Evidence,
      claim_id?, handoff_id?, evidence_ref?,
      PK(decision_id, ordinal))
  b1_accepted_state_contributions(id, project_id, statement,
      scope_kind CHECK Project|Responsibility|Assignment,
      scope_project_id?, scope_responsibility_id?, scope_assignment_id?,
      supersedes_contribution_id? UNIQUE,
      authority_decision_id, source_claim_id?)
  ```

  Add `UNIQUE(id, project_id)` parent keys and composite child foreign keys for every B1-owned relationship. Add CHECK constraints for exact Claim payload columns by kind, exact scope discriminants, claimant/decider/considered-ref discriminants, initial-versus-successor Revision shape, nonblank contract/statement/locator text, and canonical non-null JSON arrays. Add one partial unique index for one initial Revision per Assignment and one unique prior-Revision edge so Revision history cannot branch. Add indexes for Project history order, Assignment/Revision reads, routing, Claims/Handoffs, current delegation replacement, and contribution supersession.

  Every B1-owned entity table's `project_id` references `b1_project_governance(project_id)`; the origin and governance tables alone reference Legacy `projects`. At migration time only, insert every existing `projects.id` into `b1_legacy_project_origins` with `source_schema_version = 19`. Do not insert governance, Actor, Responsibility, Assignment, Revision, Claim, Handoff, Decision, disposition, routing, or contribution rows. Later Project inserts receive no origin row automatically.

- [ ] **Step 4: Run focused GREEN and full Storage migration tests.**

  ```powershell
  dotnet test tests/Workbench.Storage.Tests/Workbench.Storage.Tests.csproj --filter "FullyQualifiedName~ManualContinuityMigrationTests|FullyQualifiedName~WorkbenchDatabaseTests|FullyQualifiedName~ProjectSummaryMigrationTests"
  ```

  Require v20, `PRAGMA quick_check = ok`, zero `pragma_foreign_key_check` rows, unchanged v19 bounded snapshot, and empty B1 histories except the mechanical origin rows.

- [ ] **Step 5: Commit independently.**

  ```powershell
  git add src/Workbench.Storage/Migrations/Migration020ManualContinuitySpine.cs src/Workbench.Storage/Database/MigrationRunner.cs tests/Workbench.Storage.Tests/Database/ManualContinuityMigrationTests.cs tests/Workbench.Storage.Tests/Database/WorkbenchDatabaseTests.cs
  git commit -m "feat(storage): add r5 b1 persistence schema"
  ```

### Task 6: Implement New-Project Bootstrap And One-Time Legacy Adoption

**Files:**
- Create: `src/Workbench.Storage/Continuity/B1ProjectGovernanceRepository.cs`
- Test: `tests/Workbench.Storage.Tests/Continuity/B1ProjectGovernanceRepositoryTests.cs`

**Interfaces:**
- Consumes: `WorkbenchDatabase`, existing `Project` storage columns, Task 1 `ProjectGovernance`, and v20 origin/governance tables.
- Produces:

  ```csharp
  public sealed class B1ProjectGovernanceRepository(WorkbenchDatabase database)
  {
      public Task<ProjectGovernance> CreateGovernedProjectAsync(
          Workbench.Core.Projects.Project project,
          UserPrincipalRef bootstrapPrincipal,
          CancellationToken cancellationToken = default);

      public Task<ProjectGovernance> AdoptLegacyProjectAsync(
          ProjectRef projectRef,
          UserPrincipalRef authenticatedPrincipal,
          DateTimeOffset adoptedAt,
          CancellationToken cancellationToken = default);

      public Task<ProjectGovernance?> GetAsync(
          ProjectRef projectRef,
          CancellationToken cancellationToken = default);
  }
  ```

- [ ] **Step 1: Write failing bootstrap/adoption tests.** Add `CreateGovernedProject_inserts_project_and_root_atomically`, `New_project_requires_nonblank_user_principal`, `New_project_has_created_origin_and_zero_commit_sequence`, `Eligible_pre_b1_project_can_be_adopted_once`, `Null_governance_without_origin_is_not_adoptable`, `Adoption_fails_when_any_b1_governance_history_exists`, `Adoption_creates_no_decision_claim_or_accepted_state`, and `Adoption_failure_writes_nothing`.

- [ ] **Step 2: Run RED.**

  ```powershell
  dotnet test tests/Workbench.Storage.Tests/Workbench.Storage.Tests.csproj --filter FullyQualifiedName~B1ProjectGovernanceRepositoryTests
  ```

  Expected RED: repository type and atomic root operations are absent.

- [ ] **Step 3: Implement the two root operations.** `CreateGovernedProjectAsync` inserts the existing seven Project fields and the `Created` governance row in one transaction; it does not create an origin row or any B1 domain identity. `AdoptLegacyProjectAsync` verifies an origin row, absent governance, and zero rows across B1 Actor/Responsibility/Assignment/Decision histories inside one transaction, then inserts only an `Adopted` governance row. The authenticated principal is written as the bootstrap principal; legacy Leader/session/settings values are never consulted.

  Duplicate creation/adoption throws `B1CommandException(B1FailureCode.GovernanceAlreadyExists)`; missing mechanical origin, post-B1 null governance, or any adoption precondition failure throws `B1CommandException(B1FailureCode.LegacyProjectNotEligible)`. Do not return a partially populated governance record. Leave `ProjectRepository.UpsertAsync` unchanged: it is Legacy Project persistence and is not silently upgraded into a B1 creation path.

- [ ] **Step 4: Run focused GREEN and Project repository regressions.**

  ```powershell
  dotnet test tests/Workbench.Storage.Tests/Workbench.Storage.Tests.csproj --filter "FullyQualifiedName~B1ProjectGovernanceRepositoryTests|FullyQualifiedName~ProjectRepositoryTests"
  ```

- [ ] **Step 5: Commit independently.**

  ```powershell
  git add src/Workbench.Storage/Continuity/B1ProjectGovernanceRepository.cs tests/Workbench.Storage.Tests/Continuity/B1ProjectGovernanceRepositoryTests.cs
  git commit -m "feat(storage): bootstrap r5 b1 project authority"
  ```

### Task 7: Implement Attempt And SessionBinding Commands With Stored-Ref CAS

**Files:**
- Create: `src/Workbench.Storage/Continuity/B1RoutingRepository.cs`
- Create: `src/Workbench.App/Continuity/B1NonAuthoritativeCommandService.cs`
- Create: `tests/Workbench.Storage.Tests/Continuity/B1ContinuityStorageFixture.cs`
- Test: `tests/Workbench.Storage.Tests/Continuity/B1RoutingRepositoryTests.cs`
- Test: `tests/Workbench.App.Tests/Continuity/B1NonAuthoritativeCommandServiceTests.cs`

**Interfaces:**
- Consumes: governed Projects, Assignment/Revision records later created by authority commits, Task 2 routing commands, and Task 3 projections.
- Produces the Attempt/SessionBinding subset of `B1NonAuthoritativeCommandService`:

  ```csharp
  public Task<Attempt> CreateAttemptAsync(CreateAttemptCommand command, CancellationToken ct = default);
  public Task SelectCurrentAttemptAsync(SelectCurrentAttemptCommand command, CancellationToken ct = default);
  public Task<SessionBinding> CreateSessionBindingAsync(CreateSessionBindingCommand command, CancellationToken ct = default);
  public Task SelectCurrentSessionBindingAsync(SelectCurrentSessionBindingCommand command, CancellationToken ct = default);
  public Task ClearCurrentSessionBindingAsync(ClearCurrentSessionBindingCommand command, CancellationToken ct = default);
  public Task<Attempt> CreateAttemptAndSelectAsync(CreateAttemptCommand create, AttemptRef? expectedStored, CancellationToken ct = default);
  public Task<SessionBinding> CreateSessionBindingAndSelectAsync(CreateSessionBindingCommand create, SessionBindingRef? expectedStored, CancellationToken ct = default);
  ```

- [ ] **Step 1: Write storage-level RED tests.** Seed valid authority-created Assignment/Revision rows through `B1ContinuityStorageFixture`; until Task 9 exists, that test-only fixture may use explicit v20 INSERTs, but no production unrestricted insert API is allowed. Add `Attempt_binds_current_unresolved_revision_immutably`, `Attempt_creation_rejects_replaced_assignment`, `Attempt_creation_rejects_dispositioned_or_stale_revision`, `SessionBinding_requires_attempt_assignee_actor`, `Manual_attempt_allows_zero_bindings`, `Same_external_locator_can_have_multiple_bindings`, `Same_external_locator_can_bind_different_projects_without_merging`, `Selection_uses_expected_stored_ref_not_effective_ref`, `Concurrent_attempt_selection_has_one_winner`, `Clear_binding_does_not_mutate_binding`, and `Create_and_select_rolls_back_both_on_stale_CAS`.

- [ ] **Step 2: Write Application-level RED tests.** Add `Service_exposes_attempt_and_binding_commands_without_authority_decision`, `CreateAttemptAndSelect_is_one_routing_transaction`, `CreateSessionBindingAndSelect_is_one_routing_transaction`, `Explicit_clear_uses_stored_expected_binding`, and `Routing_commands_do_not_call_authority_repository`. Use concrete temporary repositories; do not introduce repository mocks as a new production abstraction.

- [ ] **Step 3: Run RED.**

  ```powershell
  dotnet test tests/Workbench.Storage.Tests/Workbench.Storage.Tests.csproj --filter FullyQualifiedName~B1RoutingRepositoryTests
  dotnet test tests/Workbench.App.Tests/Workbench.App.Tests.csproj --filter FullyQualifiedName~B1NonAuthoritativeCommandServiceTests
  ```

  Expected RED: routing repository/service and B1 routing rows do not exist in production code.

- [ ] **Step 4: Implement immutable creation and CAS.** Repository inserts use SQL ownership/current-state predicates inside the write transaction; a pre-read alone is insufficient. Creating an Attempt also creates its empty `b1_attempt_routing` row in the same transaction. Selection updates match both the containing identity and nullable expected stored ref. Use explicit null-safe predicates rather than `COALESCE` sentinel IDs. On a stale expected ref throw `B1CommandException(B1FailureCode.StaleRoutingSelection)` and commit no create-and-select row. Creating or selecting never changes disposition, authority, accepted state, or external runtime lifecycle.

  `CreateAttemptAsync` requires current delegation, exact current Revision, and no disposition. `CreateSessionBindingAsync` requires Binding actor = immutable Assignment assignee through the Attempt chain. It stores only opaque `ExternalSessionRef`; no Provider/model/runtime fields are added.

- [ ] **Step 5: Run focused GREEN and WorkerExecution regressions.**

  ```powershell
  dotnet test tests/Workbench.Storage.Tests/Workbench.Storage.Tests.csproj --filter "FullyQualifiedName~B1RoutingRepositoryTests|FullyQualifiedName~WorkerExecutionRepositoryTests|FullyQualifiedName~RevisionAckTests"
  dotnet test tests/Workbench.App.Tests/Workbench.App.Tests.csproj --filter FullyQualifiedName~B1NonAuthoritativeCommandServiceTests
  ```

- [ ] **Step 6: Commit independently.**

  ```powershell
  git add src/Workbench.Storage/Continuity/B1RoutingRepository.cs src/Workbench.App/Continuity/B1NonAuthoritativeCommandService.cs tests/Workbench.Storage.Tests/Continuity/B1ContinuityStorageFixture.cs tests/Workbench.Storage.Tests/Continuity/B1RoutingRepositoryTests.cs tests/Workbench.App.Tests/Continuity/B1NonAuthoritativeCommandServiceTests.cs
  git commit -m "feat(app): route r5 b1 attempts and bindings"
  ```

### Task 8: Implement Attributable Claim And Immutable Handoff Commands

**Files:**
- Create: `src/Workbench.Storage/Continuity/B1ClaimHandoffRepository.cs`
- Modify: `src/Workbench.App/Continuity/B1NonAuthoritativeCommandService.cs`
- Test: `tests/Workbench.Storage.Tests/Continuity/B1ClaimHandoffRepositoryTests.cs`
- Modify: `tests/Workbench.App.Tests/Continuity/B1NonAuthoritativeCommandServiceTests.cs`

**Interfaces:**
- Consumes: Task 2 Claim/Handoff records and commands, Task 6 governance, and Task 7 Attempt/Binding history.
- Produces the remaining non-authoritative methods:

  ```csharp
  public Task<Claim> RecordClaimAsync(RecordClaimCommand command, CancellationToken ct = default);
  public Task<Handoff> CreateHandoffAsync(CreateHandoffCommand command, CancellationToken ct = default);
  public Task SelectContinuationHandoffAsync(SelectContinuationHandoffCommand command, CancellationToken ct = default);
  public Task<Handoff> CreateHandoffAndSelectAsync(CreateHandoffCommand create, HandoffRef? expectedStored, CancellationToken ct = default);
  ```

- [ ] **Step 1: Write failing Claim tests.** Add `Claim_round_trips_each_closed_payload_without_status`, `User_claim_requires_bootstrap_principal_and_null_session`, `LogicalActor_claim_is_project_local`, `LogicalActor_session_provenance_must_belong_to_same_actor`, `Manual_actor_claim_allows_null_session`, `Evidence_ref_is_provenance_not_claimant_or_authority`, and `Cross_project_b1_refs_fail_while_external_evidence_locator_is_allowed`.

- [ ] **Step 2: Write failing Handoff tests.** Add `Handoff_requires_typed_same_project_claim_refs`, `Primary_result_must_be_assignee_claim`, `Primary_result_session_binding_must_belong_to_attempt`, `Non_primary_claim_may_have_another_valid_project_claimant`, `Packaged_revision_proposal_matches_attempt_assignment_and_revision`, `Independent_revision_proposal_can_target_other_same_project_assignment`, `Handoff_has_no_completion_or_acceptance_state`, `Continuation_selection_uses_stored_CAS`, and `Create_handoff_and_select_is_atomic`.

- [ ] **Step 3: Add Application-level RED tests.** Add `RecordClaim_preserves_actor_claimant_when_bootstrap_user_operates_manually`, `CreateHandoff_does_not_create_decision_or_accepted_state`, `SelectContinuationHandoff_uses_expected_stored_ref`, `CreateHandoffAndSelect_rolls_back_on_stale_selection`, and `Public_non_authoritative_surface_contains_only_eight_commands_and_three_conveniences`.

- [ ] **Step 4: Run RED.**

  ```powershell
  dotnet test tests/Workbench.Storage.Tests/Workbench.Storage.Tests.csproj --filter FullyQualifiedName~B1ClaimHandoffRepositoryTests
  dotnet test tests/Workbench.App.Tests/Workbench.App.Tests.csproj --filter FullyQualifiedName~B1NonAuthoritativeCommandServiceTests
  ```

  Expected RED: Claim/Handoff repository and service methods are missing.

- [ ] **Step 5: Implement bounded persistence.** Map the five closed payload shapes to the v20 discriminated Claim columns and serialize only EvidenceRef locator arrays; reject unknown kind/column combinations or unknown evidence JSON on read instead of accepting extensions. Store Handoff primary Result directly and other Claim refs in `b1_handoff_claim_refs` with fixed role kinds and stable ordinals. Validate every referenced Claim kind and ownership inside the create transaction. Never copy Claim statements into Handoff rows.

  `RecordClaimCommand` carries the authenticated Manual operator separately from `ClaimantRef` only at command time. For an actor Claim the operator must be the Project bootstrap user and the Claimant remains the selected LogicalActor. Do not persist `SubmittedByPrincipalRef`. Handoff selection remains routing only and uses the same stored nullable CAS rule as Attempt selection.

- [ ] **Step 6: Run focused GREEN and routing tests.**

  ```powershell
  dotnet test tests/Workbench.Storage.Tests/Workbench.Storage.Tests.csproj --filter "FullyQualifiedName~B1ClaimHandoffRepositoryTests|FullyQualifiedName~B1RoutingRepositoryTests"
  dotnet test tests/Workbench.App.Tests/Workbench.App.Tests.csproj --filter FullyQualifiedName~B1NonAuthoritativeCommandServiceTests
  ```

- [ ] **Step 7: Commit independently.**

  ```powershell
  git add src/Workbench.Storage/Continuity/B1ClaimHandoffRepository.cs src/Workbench.App/Continuity/B1NonAuthoritativeCommandService.cs tests/Workbench.Storage.Tests/Continuity/B1ClaimHandoffRepositoryTests.cs tests/Workbench.App.Tests/Continuity/B1NonAuthoritativeCommandServiceTests.cs
  git commit -m "feat(app): record r5 b1 claims and handoffs"
  ```

### Task 9: Persist Validated Authority Decisions Atomically

**Files:**
- Create: `src/Workbench.Storage/Continuity/B1AuthorityRepository.cs`
- Test: `tests/Workbench.Storage.Tests/Continuity/B1AuthorityRepositoryTests.cs`

**Interfaces:**
- Consumes: `B1ProjectState`, `ValidatedAuthorityDecision`, v20 tables, and `B1Projector`.
- Produces:

  ```csharp
  public sealed class B1AuthorityRepository(WorkbenchDatabase database)
  {
      public Task<B1ProjectState> LoadProjectStateAsync(ProjectRef projectRef, CancellationToken ct = default);
      public Task<AuthorityCommitResult> TryCommitAsync(ValidatedAuthorityDecision decision, CancellationToken ct = default);
  }

  public enum AuthorityCommitResult { Committed, ProjectSequenceConflict }
  ```

- [ ] **Step 1: Write failing round-trip and ordering tests.** Use `B1AuthorityEvaluator` to obtain validated commits from governed snapshots. Add `Decision_commit_assigns_next_project_sequence`, `Successful_decisions_reload_in_sequence_order`, `CreatedAt_does_not_override_commit_sequence`, `Structural_effects_and_initial_revision_commit_together`, `Disposition_activation_replacement_and_contributions_round_trip`, and `Considered_refs_and_proposal_source_round_trip_without_becoming_evidence_or_authority`.

- [ ] **Step 2: Write failing atomicity/concurrency tests.** Add `Wrong_expected_sequence_commits_zero_rows`, `Constraint_failure_rolls_back_decision_and_every_effect`, `Concurrent_same_sequence_commits_one_winner`, `Failed_decision_consumes_no_successful_sequence`, `Supersession_unique_target_has_one_winner`, `Replacement_unique_target_has_one_winner`, and `Decision_with_zero_effects_cannot_reach_repository`.

- [ ] **Step 3: Run RED.**

  ```powershell
  dotnet test tests/Workbench.Storage.Tests/Workbench.Storage.Tests.csproj --filter FullyQualifiedName~B1AuthorityRepositoryTests
  ```

  Expected RED: the state loader and atomic authority commit adapter do not exist.

- [ ] **Step 4: Implement state load and one transaction boundary.** `LoadProjectStateAsync` reads only B1 governance/domain/routing tables and reconstructs closed records; it does not read Legacy, Runtime, transcript, Memory, Library, review, task_events, or Summary tables. Reject corrupt/unknown enum/union JSON rather than guessing.

  `TryCommitAsync` begins one SQLite transaction, executes:

  ```sql
  UPDATE b1_project_governance
  SET last_commit_sequence = last_commit_sequence + 1
  WHERE project_id = $projectId
    AND last_commit_sequence = $expectedSequence;
  ```

  If the affected count is zero, roll back and return `ProjectSequenceConflict`. Otherwise insert the Decision at `$expectedSequence + 1`, considered refs, all resolved structural effects, initial/later Revisions, the empty routing row for each new Assignment, disposition, replacement edge, and contributions/supersession. Any insert or invariant failure rolls back the sequence update and every effect. Do not catch a constraint error and continue with a subset.

- [ ] **Step 5: Run focused GREEN and migration tests.**

  ```powershell
  dotnet test tests/Workbench.Storage.Tests/Workbench.Storage.Tests.csproj --filter "FullyQualifiedName~B1AuthorityRepositoryTests|FullyQualifiedName~ManualContinuityMigrationTests"
  ```

- [ ] **Step 6: Remove the shared fixture's temporary seed SQL.** Change `B1ContinuityStorageFixture` to create governed B1 state through evaluated/committed authority commands. Re-run `B1RoutingRepositoryTests` and `B1ClaimHandoffRepositoryTests`; no production test seam or unrestricted insert API is added.

- [ ] **Step 7: Commit independently.**

  ```powershell
  git add src/Workbench.Storage/Continuity/B1AuthorityRepository.cs tests/Workbench.Storage.Tests/Continuity/B1AuthorityRepositoryTests.cs tests/Workbench.Storage.Tests/Continuity/B1ContinuityStorageFixture.cs tests/Workbench.Storage.Tests/Continuity/B1RoutingRepositoryTests.cs tests/Workbench.Storage.Tests/Continuity/B1ClaimHandoffRepositoryTests.cs
  git commit -m "feat(storage): commit r5 b1 authority atomically"
  ```

### Task 10: Expose Establishment And Delegation Through Three Named Authority Commands

**Files:**
- Create: `src/Workbench.App/Continuity/B1AuthorityCommandService.cs`
- Test: `tests/Workbench.App.Tests/Continuity/B1AuthorityCommandServiceTests.cs`

**Interfaces:**
- Consumes: `B1AuthorityRepository.LoadProjectStateAsync`/`TryCommitAsync`, `B1AuthorityEvaluator`, and `TimeProvider`.
- Produces the first three public methods; Task 11 adds the remaining three and no other public authority method:

  ```csharp
  public Task<AuthorityDecision> EstablishLogicalActorAsync(EstablishLogicalActorCommand command, CancellationToken ct = default);
  public Task<AuthorityDecision> EstablishResponsibilityAsync(EstablishResponsibilityCommand command, CancellationToken ct = default);
  public Task<AuthorityDecision> DelegateAssignmentAsync(DelegateAssignmentCommand command, CancellationToken ct = default);
  ```

- [ ] **Step 1: Write failing establishment tests.** Add `EstablishLogicalActor_creates_identity_only`, `EstablishResponsibility_can_create_obligation_without_owner_or_assignment`, `Bootstrap_can_atomically_establish_actor_responsibility_assignment_and_initial_revision`, `Actor_establishment_grants_no_authority`, `Prospective_assignee_and_scope_resolve_to_durable_refs`, and `Failure_of_one_contribution_rolls_back_every_established_identity`.

- [ ] **Step 2: Write failing delegation/locality tests.** Add `DelegateAssignment_creates_one_immutable_assignment_and_initial_revision`, `Parallel_delegation_does_not_replace_existing_assignment`, `Explicit_replacement_preserves_old_assignment_history`, `Replacement_target_must_still_be_current`, `Accepted_assignment_remains_replaceable_by_a_later_separate_decision`, `LogicalActor_delegate_is_responsibility_local`, `LogicalActor_cannot_seed_new_responsibility_even_when_it_can_establish_one`, `Bootstrap_seeds_each_responsibility_first_assignment`, and `New_boundary_cannot_exceed_maximum_or_decider_possession`.

- [ ] **Step 3: Run RED.**

  ```powershell
  dotnet test tests/Workbench.App.Tests/Workbench.App.Tests.csproj --filter FullyQualifiedName~B1AuthorityCommandServiceTests
  ```

  Expected RED: named authority Application service is absent.

- [ ] **Step 4: Implement a private optimistic execute loop, not a public builder.** Each public method loads state, calls only its matching evaluator overload, and tries the validated commit. On `ProjectSequenceConflict`, reload and re-evaluate so unrelated concurrent authority commits may proceed; target conflicts then fail through fresh stale validation. Cap retry count at three and throw `B1CommandException(B1FailureCode.ConcurrentProjectChange)` without effects if contention persists. The same public command is never decomposed into multiple Decisions.

  The service may have a private generic helper constrained to evaluator delegates. It must not expose `ExecuteAsync`, `SubmitDecisionAsync`, `CommitEffectsAsync`, `AuthorityDecisionBuilder`, or raw effect collections publicly.

- [ ] **Step 5: Run focused GREEN plus Core authority and Storage atomicity suites.**

  ```powershell
  dotnet test tests/Workbench.App.Tests/Workbench.App.Tests.csproj --filter FullyQualifiedName~B1AuthorityCommandServiceTests
  dotnet test tests/Workbench.Core.Tests/Workbench.Core.Tests.csproj --filter FullyQualifiedName~B1AuthorityEvaluatorTests
  dotnet test tests/Workbench.Storage.Tests/Workbench.Storage.Tests.csproj --filter FullyQualifiedName~B1AuthorityRepositoryTests
  ```

- [ ] **Step 6: Commit independently.**

  ```powershell
  git add src/Workbench.App/Continuity/B1AuthorityCommandService.cs tests/Workbench.App.Tests/Continuity/B1AuthorityCommandServiceTests.cs
  git commit -m "feat(app): establish and delegate r5 b1 authority"
  ```

### Task 11: Complete The Six-Command Matrix And Accepted-State Effects

**Files:**
- Modify: `src/Workbench.App/Continuity/B1AuthorityCommandService.cs`
- Modify: `tests/Workbench.App.Tests/Continuity/B1AuthorityCommandServiceTests.cs`

**Interfaces:**
- Consumes: Task 10 service and all Task 4 evaluator overloads.
- Produces the final three methods, leaving the complete public authority surface at exactly six:

  ```csharp
  public Task<AuthorityDecision> DecideAssignmentAsync(DecideAssignmentCommand command, CancellationToken ct = default);
  public Task<AuthorityDecision> ActivateAssignmentRevisionAsync(ActivateAssignmentRevisionCommand command, CancellationToken ct = default);
  public Task<AuthorityDecision> AuthorAcceptedStateAsync(AuthorAcceptedStateCommand command, CancellationToken ct = default);
  ```

- [ ] **Step 1: Write failing disposition/activation tests.** Add `DecideAssignment_accepts_current_unresolved_revision_once`, `Stale_or_already_dispositioned_revision_rolls_back_whole_decision`, `RevisionRequired_can_atomically_activate_replacement_revision`, `RevisionRequired_can_receive_later_activation_only`, `Accepted_or_rejected_cannot_activate_revision`, `Revision_activation_source_must_be_proposed_revision_for_same_assignment`, `Stale_revision_proposal_remains_considered_history_but_new_contract_targets_current_revision`, `RevisionRequired_or_rejected_can_replace_same_assignment`, `Accepted_plus_replacement_is_invalid`, `Disposition_parallel_delegation_or_other_assignment_replacement_is_invalid`, `Historical_old_revision_claim_can_support_project_contribution_without_old_disposition`, and `Activation_creates_no_attempt_or_execution_state`.

- [ ] **Step 2: Write failing accepted-state/supersession tests.** Add `AuthorAcceptedState_requires_one_or_more_contributions`, `Authority_can_adopt_modify_or_author_without_claim`, `Source_claim_must_be_same_project_proposed_state_claim`, `Proposal_scope_or_proposed_supersession_never_applies_automatically`, `Rejected_or_revision_required_decision_may_still_author_explicit_contributions`, `Contributions_default_to_coexistence`, `Supersession_requires_current_exact_scope_target`, `Contributions_in_same_decision_cannot_supersede_each_other`, `Concurrent_supersession_has_one_complete_winner`, `Prospective_scope_resolves_only_for_identity_created_by_same_command`, and `Contribution_failure_rolls_back_disposition_activation_and_replacement`.

- [ ] **Step 3: Run RED.**

  ```powershell
  dotnet test tests/Workbench.App.Tests/Workbench.App.Tests.csproj --filter FullyQualifiedName~B1AuthorityCommandServiceTests
  ```

  Expected RED: the final three methods are absent while Task 10 establishment/delegation tests remain green.

- [ ] **Step 4: Add only the three named methods.** Reuse Task 10's private load/evaluate/commit retry. Persist `Unresolved` nowhere. `DecideAssignment` always has one disposition and at most one activation or same-Assignment replacement, never both. `AuthorAcceptedState` creates no Claim automatically. Contribution statements are Authority-authored immutable values; proposal Claim payloads remain unchanged.

- [ ] **Step 5: Run focused GREEN and assert the public surface.** Add a reflection assertion that the only public instance methods declared by `B1AuthorityCommandService` are the six named async commands plus inherited `object` methods excluded from the check.

  ```powershell
  dotnet test tests/Workbench.App.Tests/Workbench.App.Tests.csproj --filter FullyQualifiedName~B1AuthorityCommandServiceTests
  dotnet test tests/Workbench.Core.Tests/Workbench.Core.Tests.csproj --filter "FullyQualifiedName~B1AuthorityEvaluatorTests|FullyQualifiedName~B1ProjectionTests"
  dotnet test tests/Workbench.Storage.Tests/Workbench.Storage.Tests.csproj --filter FullyQualifiedName~B1AuthorityRepositoryTests
  ```

- [ ] **Step 6: Commit independently.**

  ```powershell
  git add src/Workbench.App/Continuity/B1AuthorityCommandService.cs tests/Workbench.App.Tests/Continuity/B1AuthorityCommandServiceTests.cs
  git commit -m "feat(app): complete r5 b1 authority commands"
  ```

### Task 12: Compose Durable Read And Recovery Projections

**Files:**
- Create: `src/Workbench.App/Continuity/B1ProjectionService.cs`
- Test: `tests/Workbench.App.Tests/Continuity/B1ProjectionRecoveryTests.cs`

**Interfaces:**
- Consumes: `B1AuthorityRepository.LoadProjectStateAsync` and `B1Projector.Build`.
- Produces:

  ```csharp
  public sealed class B1ProjectionService(
      B1AuthorityRepository authorityRepository)
  {
      public Task<B1ProjectProjection> GetProjectProjectionAsync(ProjectRef projectRef, CancellationToken ct = default);
      public Task<AcceptedProjectState> GetAcceptedProjectStateAsync(ProjectRef projectRef, CancellationToken ct = default);
  }
  ```

- [ ] **Step 1: Write failing restart/rebuild tests.** Add `Restart_recovers_bootstrap_identities_contracts_and_decisions`, `Current_revision_and_single_disposition_rebuild_from_history`, `Current_delegation_and_effective_fulfillment_have_distinct_results`, `Accepted_state_rebuilds_only_non_superseded_contributions`, `Deleting_no_cache_is_required_for_same_projection`, `Stored_and_effective_routing_refs_both_survive_restart`, `Disposition_invalidates_effective_route_without_clearing_stored_refs`, and `Projection_reads_no_summary_transcript_runtime_or_legacy_state`.

- [ ] **Step 2: Run RED.**

  ```powershell
  dotnet test tests/Workbench.App.Tests/Workbench.App.Tests.csproj --filter FullyQualifiedName~B1ProjectionRecoveryTests
  ```

  Expected RED: the Application projection service and restart query surface do not exist.

- [ ] **Step 3: Implement read composition only.** Load complete B1 state, call the pure projector, and return immutable results. Do not add a write-through cache, accepted-state table, Summary query, transcript fallback, latest-timestamp shortcut, or Legacy merge. Expose stored refs alongside effective refs so clients can submit correct future CAS commands.

- [ ] **Step 4: Run focused GREEN and broader continuity regressions.**

  ```powershell
  dotnet test tests/Workbench.App.Tests/Workbench.App.Tests.csproj --filter "FullyQualifiedName~B1ProjectionRecoveryTests|FullyQualifiedName~TruthGovernanceR5ACertificationTests|FullyQualifiedName~ProviderIndependentProjectRecoveryCertificationTests"
  ```

- [ ] **Step 5: Commit independently.**

  ```powershell
  git add src/Workbench.App/Continuity/B1ProjectionService.cs tests/Workbench.App.Tests/Continuity/B1ProjectionRecoveryTests.cs
  git commit -m "feat(app): recover r5 b1 accepted state"
  ```

### Task 13: Wire AppServices And Certify The Zero-Session Manual Vertical Slice

**Files:**
- Modify: `src/Workbench.App/Services/AppServices.cs`
- Test: `tests/Workbench.App.Tests/Continuity/B1ManualContinuityCertificationTests.cs`

**Interfaces:**
- Consumes: Tasks 6–12 concrete repositories/services and existing `AppServices.CreateForDatabasePath`/`InitializeAsync`.
- Produces `AppServices` properties `B1ProjectGovernance`, `B1NonAuthoritativeCommands`, `B1AuthorityCommands`, and `B1Projections`. Repository instances may also be exposed only where existing composition/testing conventions require them.

- [ ] **Step 1: Write the full failing Manual certification.** In a disposable DB and an empty `AgentRuntimeRegistry`, execute:

  ```text
  CreateGovernedProject(P1, U1)
  EstablishResponsibility(
      establish Worker W1,
      establish Responsibility R1,
      delegate Assignment A1 to W1 with initial Revision R1.1)
  CreateAttempt(T1 under A1/R1.1)
  SelectCurrentAttempt(null -> T1)
  RecordClaim(Result C1 by W1, SourceSessionBindingRef = null)
  RecordClaim(Validation C2 by W1, SourceSessionBindingRef = null)
  RecordClaim(ProposedStateContribution C3 by W1, SourceSessionBindingRef = null)
  CreateHandoff(H1 with primary C1)
  SelectContinuationHandoff(null -> H1)
  DecideAssignment(Accepted A1/R1.1, considered H1/C1, accepted contribution sourced from C3)
  dispose services
  recreate services on the same disposable DB
  recover B1ProjectProjection and AcceptedProjectState
  DelegateAssignment in a later separate Decision to continue R1
  ```

  Every Application command in this scenario has `AuthenticatedOperatorRef = U1`. The primary Claim and Decision attribution remain respectively W1 and the selected `DecidingAuthorityRef`; the operator field is not persisted as a replacement identity. Assert throughout: `SessionBindings = 0`; before Decision H1 changes no accepted state; after Decision stored T1/H1 refs remain while all effective continuation refs are null; primary Result claimant is W1; Decision authority is U1; accepted contribution points to the Decision and optional C3; later delegation is not compressed into the Accepted Decision.

- [ ] **Step 2: Add negative infrastructure assertions.** Query only the disposable DB and assert zero `leader_session_epochs`, `leader_messages`, `project_summary_entries`, `project_summary_source_refs`, `worker_executions`, `task_events`, `task_review_decisions`, and `worker_completion_packages`. Assert `RuntimeRegistry.Runtimes` and sent-request count remain zero. Do not create a fake transcript, Summary, Provider, runtime, Git/worktree, or review execution object to satisfy the flow.

- [ ] **Step 3: Run RED.**

  ```powershell
  dotnet test tests/Workbench.App.Tests/Workbench.App.Tests.csproj --filter FullyQualifiedName~B1ManualContinuityCertificationTests
  ```

  Expected RED: `AppServices` does not expose the B1 spine and the end-to-end flow cannot be composed.

- [ ] **Step 4: Add composition only.** Construct one instance of each B1 repository/service from the existing `WorkbenchDatabase` and `TimeProvider`. Do not pass RuntimeRegistry, Leader services, WorkerSessionRouter, review adapters, Summary repository, or Legacy repositories into a B1 service. `InitializeAsync` continues normal database migration and existing R5-A recovery; B1 recovery is query-based and needs no startup job.

- [ ] **Step 5: Run focused GREEN and all App continuity certifications.**

  ```powershell
  dotnet test tests/Workbench.App.Tests/Workbench.App.Tests.csproj --filter "FullyQualifiedName~B1ManualContinuityCertificationTests|FullyQualifiedName~B1ProjectionRecoveryTests|FullyQualifiedName~TruthGovernanceR5ACertificationTests|FullyQualifiedName~ProviderIndependentProjectRecoveryCertificationTests"
  ```

- [ ] **Step 6: Commit independently.**

  ```powershell
  git add src/Workbench.App/Services/AppServices.cs tests/Workbench.App.Tests/Continuity/B1ManualContinuityCertificationTests.cs
  git commit -m "test(cert): prove r5 b1 manual continuity"
  ```

### Task 14: Certify Parallel Legacy Readability And Explicit One-Way Crossing

**Files:**
- Test: `tests/Workbench.App.Tests/Continuity/B1LegacyCoexistenceCertificationTests.cs`

**Interfaces:**
- Consumes: v19 synthetic historical fixtures, Migration020, existing Legacy repositories/services, the B1 root/non-authoritative/authority services, and B1 projections.
- Produces no new production API; this task is a negative coexistence gate.

- [ ] **Step 1: Record baseline Legacy characterization before adding new assertions.** Run existing Leader persistence/epoch, Task/revision, Worker routing, review/AutoProceed, Memory, Daily Summary, Library, and R5-A Summary suites. Record them as already-green regression evidence, not RED.

  ```powershell
  dotnet test tests/Workbench.Storage.Tests/Workbench.Storage.Tests.csproj --filter "FullyQualifiedName~LeaderPersistenceRepositoryTests|FullyQualifiedName~TaskLifecycleTests|FullyQualifiedName~WorkerExecutionRepositoryTests|FullyQualifiedName~ProjectMemoryFoundationTests|FullyQualifiedName~DailySummaryRepositoryTests|FullyQualifiedName~ProjectSummaryRepositoryTests"
  dotnet test tests/Workbench.App.Tests/Workbench.App.Tests.csproj --filter "FullyQualifiedName~WorkerSessionRoutingTests|FullyQualifiedName~LeaderReviewAutoProceedExecutorTests|FullyQualifiedName~TruthGovernanceR5ACertificationTests"
  ```

- [ ] **Step 2: Write new failing coexistence certification.** Create a v19 disposable fixture containing a Legacy Project/Leader epoch/message, Task/revision/event, Worker completion-shaped event, typed review/AutoProceed state, Memory/Synthesis, Daily Summary, Library, and R5-A Summary. Migrate the disposable fixture to v20 and add tests:

  - `Migration_creates_only_legacy_origin_and_no_b1_identity_or_authority`;
  - `Legacy_leader_epoch_never_becomes_actor_or_session_binding`;
  - `Legacy_task_revision_never_becomes_responsibility_assignment_or_b1_revision`;
  - `Legacy_review_or_autoproceed_never_becomes_authority_decision`;
  - `Legacy_completion_event_never_becomes_claim_or_handoff`;
  - `Memory_daily_library_and_summary_never_become_accepted_state`;
  - `Explicit_adoption_sets_only_bootstrap_root`;
  - `Explicit_current_time_claim_can_reference_legacy_evidence_locator`;
  - `Named_authority_command_is_required_before_any_accepted_contribution`;
  - `Legacy_rows_remain_byte_for_byte_equivalent_in_bounded_columns_after_b1_actions`.

- [ ] **Step 3: Run RED.**

  ```powershell
  dotnet test tests/Workbench.App.Tests/Workbench.App.Tests.csproj --filter FullyQualifiedName~B1LegacyCoexistenceCertificationTests
  ```

  Expected RED: the new certification class and explicit B1 composition do not yet exist at the pre-task checkpoint; no existing green Legacy test may be relabeled as RED.

- [ ] **Step 4: Add only test fixtures/assertions needed to prove coexistence.** Use the production migration and named B1 commands. Do not add compatibility mappers, background import, historical backdating, deduplication, Legacy back-write, cutover flag, or retirement behavior. A Legacy locator may enter `EvidenceRef` or considered context only through explicit current-time recording. AutoProceed may continue its Legacy behavior during coexistence, but B1 Decision and accepted-state counts must remain unchanged.

- [ ] **Step 5: Run focused GREEN and both baseline commands again.** Require the new one-way-boundary tests and every pre-task Legacy characterization suite to pass unchanged.

- [ ] **Step 6: Commit independently.**

  ```powershell
  git add tests/Workbench.App.Tests/Continuity/B1LegacyCoexistenceCertificationTests.cs
  git commit -m "test(cert): seal r5 b1 legacy coexistence"
  ```

## Final Verification

- [ ] Run every focused command at its task's RED and GREEN checkpoints. Preserve the command output in the implementation task record.
- [ ] Run all five test projects and the solution build from the repository root:

  ```powershell
  dotnet test tests/Workbench.Core.Tests/Workbench.Core.Tests.csproj
  dotnet test tests/Workbench.Storage.Tests/Workbench.Storage.Tests.csproj
  dotnet test tests/Workbench.Project.Tests/Workbench.Project.Tests.csproj
  dotnet test tests/Workbench.Runtime.Tests/Workbench.Runtime.Tests.csproj
  dotnet test tests/Workbench.App.Tests/Workbench.App.Tests.csproj
  dotnet build AI.Game.Workbench.sln
  git diff --check
  ```

- [ ] Run a fresh temporary-database seal, never the live DB:

  ```text
  create unique TemporaryDatabase
  initialize v19 synthetic Legacy fixture
  migrate fixture through WorkbenchDatabase.InitializeAsync to v20
  require PRAGMA quick_check = ok
  require PRAGMA foreign_key_check returns zero rows
  require bounded Legacy snapshot unchanged
  require only mechanical b1_legacy_project_origins rows exist before explicit adoption
  execute Manual certification through restart
  rebuild AcceptedProjectState twice and require structural equality
  dispose TemporaryDatabase and verify its directory is deleted
  ```

- [ ] Verify the implementation diff contains no changes to migrations 001–019 and no changes to Legacy production models/services except `AppServices` composition and `MigrationRunner` registration explicitly listed in this plan.
- [ ] Verify no Gateway, provider, runtime, transcript, Summary, Git/worktree, review-execution, generic ACL, generic Decision builder, automatic Legacy converter, AcceptedProjectState writer, or UI dependency was added.
- [ ] Verify `%LOCALAPPDATA%\AI Game Workbench\workbench.db` was never opened, copied, hashed, initialized, migrated, or written during implementation/certification.

## Spec Coverage Matrix

| Approved spec section | Implementation tasks |
|---|---|
| 4 — Project root, bootstrap, one-time adoption, no circular authority | 1, 5, 6, 10, 13, 14 |
| 5 — UserPrincipal, LogicalActor, Responsibility, Assignment, current delegation, root seeding | 1, 3, 4, 9, 10 |
| 6 — immutable Revision, revision-bound Attempt, stale history | 1, 3, 4, 7, 9, 11 |
| 7 — optional Actor-owned SessionBinding and connectivity-only rebinding | 1, 7, 12, 13 |
| 8 — closed Claim payload, EvidenceRef provenance, bounded Handoff, assignee primary Result | 2, 8, 13, 14 |
| 9 — stored routing selections, effective projections, expected-old CAS | 3, 7, 8, 12, 13 |
| 10 — eight capabilities, fixed locality, maximum/subset, no amplification | 1, 4, 10, 11 |
| 11 — attributable bounded compound Decision, considered refs, validate-before-persist, commit order | 2, 4, 9, 10, 11 |
| 12 — one disposition, Revision activation eligibility, replacement matrix | 3, 4, 9, 11 |
| 13 — proposal/authority separation, closed scope, prospective refs, supersession, AcceptedProjectState | 2, 3, 4, 9, 11, 12 |
| 14 — six authority and eight non-authoritative named command surfaces | 2, 7, 8, 10, 11 |
| 15 — complete zero-Session Manual continuity and next-day recovery | 12, 13 |
| 16 — parallel Legacy readability and explicit one-way crossing | 5, 6, 14 |
| 17 — atomicity, stale/concurrent guards, stored-ref CAS, ProjectCommitSequence | 4, 7, 8, 9, 10, 11 |
| 18 — durable reads and deterministic rebuild | 3, 9, 12, 13 |
| 19 — identity, authority, decision, routing, Manual, and Legacy acceptance checks | 1–14, with end-to-end gates in 13–14 |
| 20 — parallel landing zone without legacy retirement or delegated execution ownership | 5, 13, 14 and Final Verification |

## Plan Self-Review Checklist

- [x] All approved spec sections 4–20 map to at least one implementation task; no semantic section depends only on prose.
- [x] All referenced types and method names originate in an earlier task or the same task. Later tasks use `ProjectRef`, `RevisionRef`, `B1ProjectState`, `ValidatedAuthorityDecision`, and service method names exactly as introduced.
- [x] Every task has a named focused RED, expected failure reason, minimal GREEN work, focused GREEN command, broader regression command where relevant, and one reviewable commit.
- [x] Existing green characterization suites are explicitly labeled baseline/regression evidence and never counted as failing TDD evidence.
- [x] Public authority surface is exactly six named methods. Public non-authoritative surface contains the approved eight commands plus only the three approved create-and-select conveniences.
- [x] Durable objects match the spec: no Responsibility owner, mutable assignee, mutable Revision, Session state, Claim status, Handoff status, persisted Unresolved disposition, open contribution scope, or independent AcceptedProjectState writer.
- [x] Authority checks are per-effect, capability/locality aware, maximum/subset constrained, non-amplifying, pre-commit only, root-seeded, and unable to use prospective identities for same-Decision authority.
- [x] Decision persistence increments ProjectCommitSequence and writes every effect in one SQLite transaction; any failure rolls back the sequence and all effects.
- [x] Supersession and delegation replacement use uniqueness plus Project sequence concurrency so one conflicting Decision wins and the other fails in full.
- [x] Routing CAS compares stored nullable refs. Effective-null projection never clears stored refs or assigns execution status.
- [x] Claims/Handoffs retain attribution and bounded locators only; EvidenceRef does not verify truth; primary Handoff Result belongs to immutable assignee Actor.
- [x] Migration020 is additive, marks only Projects present at upgrade as pre-B1, synthesizes no B1 authority/history, and leaves migrations 001–019 untouched.
- [x] Legacy records remain independently readable and unchanged. All Legacy-to-B1 crossings are explicit, attributable, current-time, and one-way.
- [x] Manual certification succeeds with zero SessionBindings, Agent API calls, transcript rows, Summary rows, Provider/runtime objects, Git/worktree data, and review execution.
- [x] The plan adds no Gateway, execution engine, generic ACL, generic public Decision/effect builder, automatic Legacy migration, UI, or retirement work.
- [x] Planning and later implementation safety never require access to the live database; all schema/data evidence comes from disposable fixtures.

## Planning Gate

This document authorizes no implementation by itself. After the planning commit, stop for implementation-plan review. Begin Task 1 only after the user explicitly approves this plan and selects the required execution workflow.
