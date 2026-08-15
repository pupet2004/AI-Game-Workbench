# R1 Kernel Persistence Boundary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Follow TDD: establish RED before implementation, make the smallest GREEN change, verify, then commit.

**Goal:** Replace fragile JSON-event-based Leader Review / Authority / AskUser recovery with a small typed local persistence projection that survives provider failure and application restart, while preserving all current E1/E2A/E2B behavior and safely upgrading the observed production database path from v11 through v12/v13 to v14.

**Architecture:** Keep existing Project identity, Logical Leader, `tasks` / `task_revisions`, authority settings, Worker routing events, Daily Summary, Library, and provider/runtime boundaries unchanged. Add only the minimum typed review state needed for authoritative recovery: an immutable review-decision projection plus a mutable AskUser gate/reply binding projection. Existing `task_events` remain available as routing/audit compatibility data during R1, but review/authority recovery stops treating serialized JSON and SQL `LIKE` matching as the authority source. Do not build Truth Heads, Summary ingestion, Provider SPI, runtime replacement, result deduplication, or legacy deletion in R1.

**Tech Stack:** C# / .NET 10 / Avalonia 12 / CommunityToolkit.Mvvm / Microsoft.Data.Sqlite / existing Workbench Core–Storage–Project–Runtime–App boundaries.

**Approved behavioral contract:** `docs/superpowers/specs/2026-08-15-truth-governance-v1-design.md`

**Expected starting baseline:** `d84bd374f997c6f30af89f8c472861673351f10f` on `master`, clean. Treat actual Git output as authoritative and stop if the baseline differs unexpectedly.

---

## Global Constraints

- Implement **R1 only**.
- Do not implement Summary Journal V1 storage changes, Intent/Implemented Heads, Alignment, Truth Delta, Evolution promotion, or Truth Proposal redesign yet. Those belong to R5.
- Do not implement Provider SPI, Git/Terminal providerization, CodingAgentRunner, or runtime replacement. Those belong to R2/R3.
- Do not deduplicate Final Report / Handoff / Leader transcript bodies, delete M1.5 memory, delete v7 worker tables, or physically drop legacy Library/event data. Those belong to R4 or later.
- Do not introduce a generic event-sourcing framework.
- Do not add another parallel Task/Worker persistence model.
- Preserve `task_events` for current Worker routing/audit compatibility; only remove its role as the authoritative Review/AskUser recovery source.
- Preserve existing E1/E2A/E2B user-visible semantics exactly.
- Never run migration experiments against the live production database. Rehearsals use a byte-for-byte copy in a disposable location.
- Existing external Agent/runtime availability must not be required to reopen a Project and recover Review/Authority/AskUser state.
- Reuse existing enums/domain types whenever they already exist. Do not create duplicate `LeaderReviewOutcome`, action-level, or authority-mode concepts merely to support persistence.
- If the legacy E1/E2 event identities cannot be deterministically reconstructed from existing rows, stop with `ARCHITECTURE_GAP`; do not invent IDs or guess relationships.
- Every migration/backfill write must be idempotent and project-isolated.

---

## Target R1 Persistence Shape

Use the smallest typed projection that satisfies recovery. Exact SQL naming may be adjusted only to match existing repository conventions; do not expand the model without a failing test that requires it.

### `task_review_decisions`

Purpose: immutable authoritative record of the Leader review decision and authority resolution already produced by the current Review flow.

Conceptual columns:

```text
review_decision_id      TEXT PRIMARY KEY
project_id              TEXT NOT NULL
task_id                 TEXT NOT NULL
revision_id             TEXT NOT NULL
source_event_id          TEXT NULL UNIQUE
outcome                 TEXT NOT NULL
action_level            TEXT NOT NULL
authority_mode          TEXT NOT NULL
authority_resolution    TEXT NOT NULL
created_at              TEXT NOT NULL
```

Constraints:

- foreign keys to Project / Task / Revision where existing schema permits;
- exact enum/check values must match the current Core contracts;
- preserve the existing E2 review-decision identity if one already exists; if the existing `ReviewDecisionEventId` is already the stable identity, use it instead of inventing a second ID;
- one persisted immutable decision per stable decision identity;
- insertion is idempotent.

### `task_review_user_gates`

Purpose: typed AskUser gate plus first effective reply binding.

Conceptual columns:

```text
review_decision_id      TEXT PRIMARY KEY
project_id              TEXT NOT NULL
task_id                 TEXT NOT NULL
revision_id             TEXT NOT NULL
question_message_id     TEXT NOT NULL
user_message_id         TEXT NULL
opened_at               TEXT NOT NULL
responded_at            TEXT NULL
state                   TEXT NOT NULL   -- Open / Responded
```

Constraints:

- `review_decision_id` references the typed decision;
- project/task/revision identity is stored explicitly for bounded recovery and project isolation;
- the user message body remains in `leader_messages`; this table stores references only;
- `TryBindFirstUserResponse` must be a guarded conditional update (`Open` and `user_message_id IS NULL`) with affected-row checking;
- a second ordinary user message must never replace the first binding;
- when multiple open gates exist, higher-level routing returns “ambiguous / no bind”; the repository must not silently choose the newest gate.

No third general-purpose review-event table is introduced in R1.

---

## Task 0: Seal the Approved Documents and Reconfirm Baseline

**Outcome:** The approved design and this R1 plan are present in-repo before implementation begins, and actual Git/schema state is reconfirmed.

**Files:**
- Add `docs/superpowers/specs/2026-08-15-truth-governance-v1-design.md`.
- Add `docs/superpowers/plans/2026-08-15-r1-kernel-persistence-boundary.md`.
- No production code changes.

- [ ] Run:

```powershell
git rev-parse HEAD
git branch --show-current
git log -8 --oneline --decorate
git status --short
git diff --check
git diff --cached
```

- [ ] Confirm the actual baseline. Expected only: `d84bd374f997c6f30af89f8c472861673351f10f`, `master`, clean. If different, report the exact difference and stop before code work unless it is only the documentation files being intentionally added.
- [ ] Inspect current migration registration and latest schema:

```powershell
rg -n "Migration012|Migration013|user_version|LeaderReview|NeedsUserDecision|LeaderReviewUserResponseReceived|GetBoundUserMessageIdAsync|TaskEventWorkerRoutingStore" src tests
```

- [ ] Copy the approved spec and this plan into the paths above.
- [ ] Ensure the spec status says `Approved for implementation planning`, not `pending user review`.
- [ ] `git diff --check`.
- [ ] Commit:

```text
docs(truth): seal governance design and r1 plan
```

**Stop condition:** documentation committed; no source/schema behavior changed.

---

## Task 1: Establish the v11 → v13 Safety Baseline Before Adding v14

**Outcome:** Existing Migration012/013 are proven against a disposable copy of the observed production-style v11 database before R1 adds another migration.

**Files:**
- Modify `tests/Workbench.Storage.Tests/Database/WorkbenchDatabaseTests.cs`.
- Add a focused fixture/helper under `tests/Workbench.Storage.Tests/Database/` only if current helpers cannot open/copy a real SQLite fixture safely.
- Do not modify Migration012/013 unless the new RED test proves a real defect.

- [ ] Create a disposable test fixture representing the relevant v11 shape and data: Projects, Tasks, TaskRevisions, TaskEvents, Leader/Settings rows, Daily/Library rows. Prefer a sanitized copy/fixture generated from schema rather than checking personal production content into Git.
- [ ] Write a failing chained-upgrade test that starts at `PRAGMA user_version = 11`, runs the normal migration runner, and asserts:
  - target reaches v13 before v14 exists in this task;
  - Project/Task/Revision/Event row counts are preserved;
  - Migration012 Task status rebuild preserves current task identities and foreign-key integrity;
  - Migration013 adds `leader_authority_mode` without losing existing settings;
  - `PRAGMA foreign_key_check` returns no rows.
- [ ] Run RED and capture why it fails if current test coverage is absent rather than because product code is wrong:

```powershell
dotnet test tests/Workbench.Storage.Tests/Workbench.Storage.Tests.csproj --filter "FullyQualifiedName~WorkbenchDatabase"
```

- [ ] Add only the fixture/test support necessary to make the existing v11→v13 path reproducibly testable. Do not “fix” Migration012/013 if they already pass.
- [ ] GREEN:

```powershell
dotnet test tests/Workbench.Storage.Tests/Workbench.Storage.Tests.csproj --filter "FullyQualifiedName~WorkbenchDatabase"
```

- [ ] `git diff --check`.
- [ ] Commit:

```text
test(storage): pin v11 review-state migration baseline
```

**Stop condition:** existing v11→v13 upgrade is covered and green. Any discovered Migration012/013 defect is reported separately before continuing.

---

## Task 2: Add Typed Review Persistence Contracts and Migration014

**Outcome:** v14 can persist immutable typed review decisions and AskUser gate/reply bindings without touching Summary/Library Truth.

**Files:**
- Reuse the existing Core file(s) containing `LeaderReviewOutcome`, `LeaderReviewActionLevel`, `LeaderAuthorityMode`, and authority resolution if present.
- If no suitable persistence-neutral record file exists, add `src/Workbench.Core/Review/LeaderReviewPersistenceModels.cs`.
- Add `src/Workbench.Storage/Migrations/Migration014TypedLeaderReviewState.cs`.
- Modify `src/Workbench.Storage/Database/MigrationRunner.cs`.
- Modify `tests/Workbench.Storage.Tests/Database/WorkbenchDatabaseTests.cs`.
- Add `tests/Workbench.Storage.Tests/Review/LeaderReviewStateRepositoryTests.cs` later in Task 3, not yet.

- [ ] First locate and reuse current review domain types:

```powershell
rg -n "enum .*LeaderReview|LeaderReviewOutcome|LeaderReviewActionLevel|LeaderAuthorityMode|AuthorityResolution" src/Workbench.Core src/Workbench.App
```

- [ ] Write RED migration tests asserting v14 creates exactly the two typed tables plus required indexes/foreign keys/checks, with no new Summary/Library/Provider tables.
- [ ] Write RED tests for:
  - review decision identity uniqueness;
  - Project/Task/Revision isolation;
  - gate one-to-one with decision;
  - nullable first-response binding;
  - state values restricted to Open/Responded;
  - user-message body is not duplicated into the gate table.
- [ ] Run RED:

```powershell
dotnet test tests/Workbench.Storage.Tests/Workbench.Storage.Tests.csproj --filter "FullyQualifiedName~WorkbenchDatabase|FullyQualifiedName~TypedLeaderReview"
```

- [ ] Implement `Migration014TypedLeaderReviewState` using existing SQLite migration conventions.
- [ ] Register Migration014 immediately after Migration013.
- [ ] Add only minimal persistence-neutral records if the repository needs typed return values. Reuse existing enums; do not clone review business rules.
- [ ] GREEN the focused migration tests, then all Storage tests:

```powershell
dotnet test tests/Workbench.Storage.Tests/Workbench.Storage.Tests.csproj
```

- [ ] `git diff --check`.
- [ ] Commit:

```text
feat(review): add typed review state schema
```

**Stop condition:** fresh and v13 databases reach v14 with the two-table typed projection; no current App read/write path is rewired yet.

---

## Task 3: Backfill Legacy E1/E2 State and Add the Typed Repository

**Outcome:** Existing review decisions, AskUser gates, and E2B reply bindings can be deterministically reconstructed from current legacy `task_events` into v14, and new code has a typed repository API.

**Files:**
- Modify `src/Workbench.Storage/Migrations/Migration014TypedLeaderReviewState.cs`.
- Add `src/Workbench.Storage/Review/LeaderReviewStateRepository.cs` (use `Reviews/` instead if that is the established local naming convention; choose one and keep it focused).
- Add `tests/Workbench.Storage.Tests/Review/LeaderReviewStateRepositoryTests.cs`.
- Modify `tests/Workbench.Storage.Tests/Database/WorkbenchDatabaseTests.cs`.

**Repository contract:**

```csharp
Task InsertDecisionIfAbsentAsync(...);
Task<PersistedLeaderReviewDecision?> GetDecisionAsync(...);
Task OpenUserGateIfAbsentAsync(...);
Task<IReadOnlyList<LeaderReviewUserGate>> GetOpenUserGatesAsync(ProjectId projectId, ...);
Task<bool> TryBindFirstUserResponseAsync(...);
Task<Guid?> GetBoundUserMessageIdAsync(ProjectId projectId, TaskId taskId, Guid reviewDecisionId, ...);
```

Use actual project value-object types and existing cancellation-token conventions.

- [ ] Discover the exact legacy event kinds and payload properties currently written by E1/E2A/E2B:

```powershell
rg -n "LeaderReviewUserResponseReceived|ReviewDecision|NeedsUserDecision|AskUser|payload_json|GetBoundUserMessageIdAsync" src tests
```

- [ ] Write RED migration/backfill fixtures for:
  - one PASS/AutoProceed review decision;
  - one AskUser review decision plus open gate;
  - one E2B gate with first user-message binding;
  - unrelated Worker routing events remaining untouched;
  - multiple Projects with colliding-looking IDs proving isolation.
- [ ] Write a RED malformed-authoritative-event test. If a known review/AskUser legacy event cannot be deterministically parsed, Migration014 must fail atomically and leave `user_version` at 13; it must not guess or partially backfill.
- [ ] Write RED repository tests:
  - decision insert is idempotent;
  - gate open is idempotent;
  - first response binds exactly once;
  - replay of the same response is harmless;
  - a second different message cannot rebind;
  - open gates are returned without a “pick newest” policy;
  - project/task mismatch cannot read or mutate another Project’s gate.
- [ ] Run RED:

```powershell
dotnet test tests/Workbench.Storage.Tests/Workbench.Storage.Tests.csproj --filter "FullyQualifiedName~LeaderReviewState|FullyQualifiedName~WorkbenchDatabase"
```

- [ ] Implement deterministic backfill for only the exact known current review event kinds. Preserve legacy rows.
- [ ] Prefer the existing stable review-decision/event identity. Do not manufacture a second identity if the current `ReviewDecisionEventId` already serves this purpose.
- [ ] Implement repository methods with parameterized SQL and existing transaction conventions.
- [ ] Implement `TryBindFirstUserResponseAsync` as a guarded conditional update and check affected rows. Do not use read-then-write without a CAS condition.
- [ ] GREEN focused tests, then all Storage tests.
- [ ] Assert no typed recovery query contains `LIKE '%<guid>%'` or equivalent serialized-payload matching.
- [ ] `git diff --check`.
- [ ] Commit:

```text
feat(review): persist typed review decisions and gates
```

**Stop condition:** v14 backfill + repository are independently correct; legacy events still exist and App behavior is still unchanged.

---

## Task 4: Rewire E1/E2A/E2B to Typed Authority Without Changing Behavior

**Outcome:** Current Leader Review flows write/read typed review state as the authoritative source; `task_events` may remain thin compatibility/audit output, but SQL/JSON scanning is no longer required for Review/AskUser recovery.

**Files to inspect/modify, using actual current ownership after `rg`:**
- `src/Workbench.App/Services/AppServices.cs`.
- Current Leader Review orchestration/service files found by:
  ```powershell
  rg -n "ReviewDecision|AutoProceed|NeedsUserDecision|LeaderReviewUserResponseReceived|GetBoundUserMessageIdAsync" src/Workbench.App
  ```
- Current `TaskEventWorkerRoutingStore` implementation only where review-specific read/write responsibilities must be removed or delegated.
- `src/Workbench.App/ViewModels/Leader/ProjectLeaderSessionManager.cs` only if E2B binding currently lives there.
- Relevant existing App review tests; add `tests/Workbench.App.Tests/Review/LeaderReviewTypedPersistenceTests.cs` only if there is no focused existing test file.

- [ ] Write RED App tests that pin existing behavior while requiring the typed repository:
  - E1 PASS/AutoProceed writes one typed decision and preserves the current Task transition/notification behavior.
  - E2A AskUser writes one typed decision plus one open typed gate.
  - E2B binds the first effective user message by IDs only; user body remains solely in `leader_messages`.
  - E2B leaves Task in `NeedsUserDecision`.
  - replay is idempotent.
  - a second ordinary message is not attached.
  - exactly one open gate permits fallback binding.
  - multiple open gates produce no binding/no guess.
- [ ] Run RED against the focused App tests.
- [ ] Inject `LeaderReviewStateRepository` through existing App composition.
- [ ] Rewire current decision persistence/recovery to the typed repository.
- [ ] Preserve thin legacy `task_events` writes only if another current consumer still requires them; mark them compatibility/audit, not authority.
- [ ] Remove review/AskUser authoritative reads that search `payload_json`/serialized GUIDs with SQL `LIKE`.
- [ ] Do not modify Worker session routing events unrelated to review.
- [ ] GREEN focused App tests, then Storage + App suites:

```powershell
dotnet test tests/Workbench.Storage.Tests/Workbench.Storage.Tests.csproj
dotnet test tests/Workbench.App.Tests/Workbench.App.Tests.csproj
```

- [ ] `git diff --check`.
- [ ] Commit:

```text
refactor(review): recover from typed review state
```

**Stop condition:** E1/E2A/E2B semantics are unchanged, but typed rows are the authority source for Review/AskUser recovery.

---

## Task 5: Prove Provider-Independent Restart Recovery

**Outcome:** A Project can reopen and recover current Review/Authority/AskUser state from the local Continuity Kernel even when the Agent runtime is unavailable.

**Files:**
- Add/modify focused App integration tests under `tests/Workbench.App.Tests/Review/`.
- Modify `tests/Workbench.App.Tests/AppTestContext.cs` only if a runtime-unavailable fake is needed.
- Reuse existing fake runtime support; do not modify real provider code.

- [ ] Write RED integration tests for restart/reopen after:
  - persisted review decision before downstream UI refresh;
  - open AskUser gate;
  - E2B first-response binding;
  - authority mode loaded from the existing global/project settings path;
  - task status from Migration012 state.
- [ ] Configure the fake runtime to throw/unavailable and prove Project-open/recovery does not call it for typed Review/Authority state.
- [ ] Assert recovery returns:
  - Project identity;
  - Task + current Revision identity;
  - persisted review decision;
  - authority resolution/mode;
  - open/responded gate state;
  - bound user-message reference when present.
- [ ] Assert no raw Agent transcript is required.
- [ ] Assert no duplicate Leader message, gate, or decision is created merely by reopening.
- [ ] Run RED, implement only the missing recovery wiring, then GREEN.
- [ ] Run the complete affected test projects:

```powershell
dotnet test tests/Workbench.Core.Tests/Workbench.Core.Tests.csproj
dotnet test tests/Workbench.Storage.Tests/Workbench.Storage.Tests.csproj
dotnet test tests/Workbench.Project.Tests/Workbench.Project.Tests.csproj
dotnet test tests/Workbench.Runtime.Tests/Workbench.Runtime.Tests.csproj
dotnet test tests/Workbench.App.Tests/Workbench.App.Tests.csproj
```

- [ ] `git diff --check`.
- [ ] Commit:

```text
test(review): verify offline review recovery
```

**Stop condition:** provider-offline local recovery passes; no R2 Provider SPI work is introduced.

---

## Task 6: Production-Copy v11 → v14 Rehearsal and R1 Seal

**Outcome:** The actual observed production schema/data shape upgrades safely on a disposable copy, all regressions pass, and R1 can be sealed without touching the live database or deleting compatibility data.

**Files:**
- No production code unless rehearsal exposes a reproducible defect already covered by a RED test.
- Add a small test/rehearsal note under `docs/superpowers/reports/` only if the repository already uses this reports directory; otherwise include evidence in the final Worker report rather than creating a new documentation category.

- [ ] Locate the production DB through existing Workbench configuration/runtime conventions. **Do not open it for migration in write mode.**
- [ ] Close Workbench or otherwise ensure a consistent copy, then copy the DB to a disposable rehearsal path.
- [ ] Record before-state:
  - file hash/size if convenient;
  - `PRAGMA user_version` = observed v11;
  - row counts for all 29 known tables that exist at v11;
  - key counts for Projects, Tasks, TaskRevisions, TaskEvents, Daily Summary, Library Objects/Timeline/Material, Leader epochs/messages, legacy memory;
  - `PRAGMA foreign_key_check`.
- [ ] Run the real application migration runner against **the copy only**, upgrading sequentially v11→v12→v13→v14.
- [ ] Record after-state:
  - `user_version = 14`;
  - preserved legacy row counts/content where no migration intentionally transforms schema;
  - Task identities/current revisions intact after Migration012 rebuild;
  - authority settings present after Migration013;
  - typed review/gate rows equal the deterministic projection of existing review-related events;
  - unrelated task events unchanged;
  - `PRAGMA foreign_key_check` empty.
- [ ] Re-run the same migration/open operation against the already-v14 rehearsal copy and confirm idempotent no-op behavior.
- [ ] Run solution verification:

```powershell
dotnet restore
dotnet build AI.Game.Workbench.sln --no-restore
dotnet test AI.Game.Workbench.sln --no-build
git diff --check
git status --short
```

- [ ] If an executable smoke is useful, use only `C:\Users\pupet\Desktop\WorkbenchSmoke` or a fresh disposable Project. Do not touch `玉牌劫` or `立围`.
- [ ] Confirm:
  - 0 build warnings/errors;
  - all test projects PASS;
  - main repo tree clean after final commit;
  - no legacy table/event physical deletion;
  - no Provider/runtime refactor;
  - no Truth Governance R5 implementation leaked into scope.
- [ ] Final commit only if rehearsal required a tested fix; otherwise no empty “seal” commit is needed.

**R1 final result must report one of:**

```text
PASS
BASIC_PASS_MANUAL_PENDING
ARCHITECTURE_GAP
FAIL
```

---

## Acceptance Checklist

R1 is complete only when all are true:

- [ ] Approved Truth Governance spec and R1 plan are committed.
- [ ] v11→v13 baseline migration is pinned by automated coverage.
- [ ] source schema is v14 with exactly the minimum typed Review/AskUser projection required by R1.
- [ ] existing v11 production-copy rehearsal reaches v14 without lost Project/Task/Revision/Event/Leader/Daily/Library data.
- [ ] Review decision authority no longer depends on `task_events.payload_json` + SQL `LIKE` serialized-GUID queries.
- [ ] `task_events` remains available for current Worker routing/audit compatibility.
- [ ] E1 PASS/AutoProceed semantics are unchanged.
- [ ] E2A AskUser semantics are unchanged.
- [ ] E2B preserves singleton fallback, ambiguity/no-guess, first-response-only, idempotency, body-in-`leader_messages`, and `NeedsUserDecision`.
- [ ] typed Review/Authority/AskUser state is recoverable with Agent runtime unavailable.
- [ ] Migration/backfill is atomic, idempotent, and Project-isolated.
- [ ] no Summary/Library Truth Head, Provider SPI, CodingAgentRunner, result dedup, or legacy deletion work is included.
- [ ] full solution build/tests and `git diff --check` pass.
- [ ] live production DB was never used as a migration scratch target.

---

## Final Worker Report Format

```text
【结果】
PASS / BASIC_PASS_MANUAL_PENDING / ARCHITECTURE_GAP / FAIL

【Baseline】
start HEAD
branch
source schema before
rehearsal DB user_version before

【Documents】
design spec commit/path
R1 plan commit/path

【Migration】
Migration014 shape
v11→v14 chained test
production-copy rehearsal before/after
foreign_key_check

【Typed Review Projection】
decision table
gate/binding table
legacy event backfill
idempotency / isolation

【E1 / E2A / E2B Compatibility】
PASS/FAIL per behavior
any compatibility task_events still written and why

【Offline Recovery】
provider unavailable test
recovered Project/Task/Revision/Review/Authority/Gate state

【Removed Fragility】
which JSON/LIKE authoritative reads were removed
remaining task_events responsibilities

【Tests】
Core
Storage
Project
Runtime
App
solution build
solution test
git diff --check

【Scope Check】
confirm no R2/R3/R4/R5 work
confirm no legacy physical deletion
confirm live DB untouched

【Git】
final HEAD
commits created
git status --short
```

---

## After R1

Do **not** automatically continue into the next phase inside the same Worker task.

Return the R1 report to the Leader for audit. Only after R1 is sealed should the next separate plan be chosen:

```text
R2 — Provider SPI
R3 — CodingAgentRunner parity/replacement spike
R4 — canonical result/material references + data slimming
R5 — Truth-governed Summary/Library loop
```

This ordering preserves the approved architecture: first make local authority/recovery trustworthy, then make capabilities replaceable, then remove duplication, then place Truth Governance on top of the smaller kernel.
