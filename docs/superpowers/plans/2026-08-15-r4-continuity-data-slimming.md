# Workbench R4 - Continuity Data Slimming Implementation Plan

## Goal

Reduce duplicated persistence, copied report bodies, legacy M1.5 memory paths, and redundant Library representations while preserving Project identity, Logical Leader continuity, Assignment/Revision, Worker responsibility, Review/Authority/AskUser, provider-independent recovery, existing user data, provenance, and source traceability.

R4 is a slimming pass over schema v18. It reuses existing typed owners and existing Library Object/Timeline storage. It does not create a Result Platform, Memory Platform, Provider SPI, generic event-sourcing framework, or Truth model.

## Architecture

- The Hard Kernel owns Project, Logical Leader, Assignment/Revision, Worker responsibility, Review/Authority/AskUser, and recovery.
- A body is canonical only when the Kernel needs it to recover current authoritative state. A source locator may be nullable; source loss degrades provenance, not typed authority.
- WorkerFinalReportReceived is the planned canonical FinalReport body because LeaderReviewInputBuilder and typed review already key it through final_report_event_id. WorkerToLeaderHandoff becomes routing/lifecycle/reference metadata for new writes; historical full payloads remain readable until a safe cleanup gate.
- Worker-session authority is not guessed. R4-01 certifies exactly one existing owner: KEEP_WORKER_EXECUTIONS, KEEP_TYPED_EVENT_PROJECTION, or OTHER_EXISTING_TYPED_OWNER.
- Existing Library Objects, Timeline Nodes, and Material References are the R4 legacy Library destination. R4 does not add Truth Heads.
- Legacy Memory rows are user history. R4 may freeze new second-read writes and inventory rows, but migration/drop remains BLOCKED_BY_R5_DESTINATION until an approved R5 Summary Delta destination exists.
- Typed review tables remain authoritative. Legacy review event JSON remains compatibility/history until AskUser detail is proven recoverable without it.

## Tech Stack

C# / .NET 10, Avalonia 12, CommunityToolkit.Mvvm, Microsoft.Data.Sqlite, existing Workbench Core-Storage-Project-Runtime-App boundaries, xUnit, and the existing SQLite migration runner. Rehearsals use a byte-for-byte copy of the v18 database and never the live database.

## Global Constraints

- This planning turn may modify and commit only docs/superpowers/plans/2026-08-15-r4-continuity-data-slimming.md.
- Future implementation begins only after plan review.
- Every task has an independent review gate, test gate, stop condition, and commit. Do not create one giant cleanup commit.
- No action in this planning turn may delete user data, change schema, add a migration, or rewrite a production database. Future R4 cleanup migrations are allowed only under R4-06's eight preconditions and R4-07 seal.
- Baseline differences are a hard stop reported as BASELINE_MISMATCH.
- task_events, transcript text, and Provider state are not authority unless R4-01 certifies an existing typed event projection as the sole owner.
- R4 does not implement Summary Delta, Truth Heads, Alignment, Evolution, Truth Proposal, Library Truth UI, or a replacement R5 schema.
- Do not add generic Memory, Provider, Plugin, Result, or Candidate-Inbox abstractions.
- Retention and authority are separate decisions. All new writes are project-isolated, idempotent, and safe across a second InitializeAsync call.
- No production-copy query, migration, or deletion may target the live database.

## Baseline and Scope

Expected start: HEAD 3aa68d8d40b269be1c2755152f51bbcd3ca78b60, branch master, schema v18, clean tree.

Before each implementation task run: git rev-parse HEAD; git branch --show-current; git log -8 --oneline --decorate; git status --short; git diff --check; git diff --cached.

Known rehearsal counts (must be re-read): project_memory_items 20; project_daily_summaries 0; Library entries/proposals 0; Library Objects 2; Timeline Nodes 5; synthesis jobs 0.

## R4-00: v18 Schema and Runtime Authority Inventory

### Goal

Inventory every R4 table/body, foreign key, row count, writer, reader, and recovery test. Resolve whether project_memory_links and project_memory_topics exist in v18.

### Files

Read Migration001Initial.cs through Migration018ReviewDecisionSubject.cs, MigrationRunner.cs, AppServices.cs, WorkerSessionRouter.cs, LeaderReviewInputBuilder.cs, LeaderReviewAskUserGate.cs, all R4 repositories/coordinators, and existing certification tests. Future implementation adds only a read-only inventory test under tests/Workbench.Storage.Tests/Database/.

### Interfaces

WorkbenchDatabase.InitializeAsync; PRAGMA user_version; sqlite_schema; PRAGMA table_info; PRAGMA foreign_key_list; PRAGMA foreign_key_check; project-scoped count/identity queries; an exported inventory report containing table, writer, reader, authority role, source locator, and recovery test.

### Preconditions

Expected baseline is clean; a byte-for-byte disposable production copy exists; foreign keys are enabled; no migration runs in this task.

### Steps

1. Enumerate v18 tables/indexes and record absent project_memory_links/topics explicitly when sqlite_schema has no row.
2. Count and sample identities for leader_messages, task_events, worker_executions, worker_completion_packages, all Memory, Library, and typed review tables.
3. Map writers/readers including ScheduleMemorySynthesis, LeaderMemoryPolicyCoordinator, ProjectMemorySynthesisCoordinator, ProjectLibraryRepository.SubmitAsync, CompletionPackageRepository, and Worker routing.
4. Map each body to a consumer and recovery test; "no production caller found" is an inventory fact, never a delete reason.
5. Export the inventory as review evidence, not a new application table.

### Tests

Assert schema v18, required tables, explicit absent-table result when applicable, and empty PRAGMA foreign_key_check. Record R1 Task 4 Review and R1 Task 5 provider-independent recovery as later regression gates.

### Stop condition

Stop with BASELINE_MISMATCH, SCHEMA_INVENTORY_GAP, or ARCHITECTURE_GAP if baseline, copy, schema, or body ownership cannot be established. Do not proceed.

### Commit

test(storage): inventory v18 continuity persistence

## R4-01: Worker Execution Authority Certification

### Goal

Certify one existing authoritative Worker-session representation without losing session identity, branch/worktree, base commit, lifecycle, Assignment/Revision, or restart/recovery.

### Files

WorkerExecutionRepository.cs, WorkerExecutionModels.cs, WorkerSessionRouter.cs, WorkerRemovalService.cs, CompletionPackageRepository.cs, InteractiveSessionLauncher.cs, TaskEventRepository.cs; focused Storage Worker tests, App Worker routing tests, and the existing ProviderIndependentProjectRecoveryCertificationTests fixture.

### Interfaces

WorkerExecutionRepository CreateAsync/GetAsync/UpdateStateAsync/PersistSessionIdentityAsync/revision acknowledgement; IWorkerRoutingStore; TaskEventWorkerRoutingStore; WorkerSessionRecord; WorkerHandoff; WorkerRemoval; WorkerSessionRouter.StartAsync; StoredWorkerExecution; WorkerExecutionState.

### Preconditions

R4-00 is green. The copy has fixtures for create, resume, route, removal, completed-pending-review, interrupted/restart, and provider-unavailable reopen. R1 Task 5 is green before and after.

### Steps

1. Trace create, resume, external session identity, base commit, branch/worktree, lifecycle, restart, and Task 5 through both representations.
2. Run a TDD fixture through production WorkerSessionRouter and WorkerExecutionRepository, then compare all required fields after restart.
3. Inventory production-copy worker_executions, completion packages, WorkerSessionStarted, WorkerToLeaderHandoff, WorkerRemoved, and assignment events; record event-only, typed-only, orphaned, and conflicting rows.
4. Certify exactly KEEP_WORKER_EXECUTIONS, KEEP_TYPED_EVENT_PROJECTION, or OTHER_EXISTING_TYPED_OWNER using the same fields and tests.
5. Record demotion/retention of the non-owner. Do not merge two owners.

### Tests

Cover exact-session resume, account mismatch, handoff status, removal, base/branch/worktree, project isolation, provider-unavailable recovery, all Worker repository/routing tests, and ProviderIndependentProjectRecoveryCertificationTests.

### Stop condition

ARCHITECTURE_GAP if a required field exists only in untyped data, fixtures diverge, completion-package ownership cannot be reconciled, or no single owner passes. Do not drop worker_executions or task_events.

### Commit

test(worker): certify one execution authority

## R4-02: Canonical FinalReport and Thin Handoff

### Goal

Keep one canonical FinalReport/evidence body for new persistence while preserving historical reads, Review input, audit, callbacks, and offline recovery.

### Files

WorkerSessionRouter.cs, WorkerHandoffPayloadParser.cs, LeaderReviewInputBuilder.cs, TaskEventRepository.cs, completion-package repository/tests, Worker routing tests, LeaderReviewInputBuilderTests, LeaderReviewAuthorityRestartCertificationTests, and ProviderIndependentProjectRecoveryCertificationTests.

### Interfaces

WorkerFinalReportReceived payload (Worker session identity, report message, validation summary); WorkerHandoff.SourceEventId; handoff identity/status/kind/revision/timestamp; LeaderReviewInputBuilder.BuildAsync; AssignmentReviewStateRepository; LeaderReviewStateRepository; IWorkerRoutingStore.AppendHandoffAsync.

### Preconditions

R4-01 selected one owner. R4-00 counted historical FinalReport, Handoff, completion-package, and typed-review rows. A reader distinguishes old full handoffs from new reference handoffs without guessing a source.

### Steps

1. RED-test one matching FinalReport plus one SourceEventId binding, and reject duplicate, wrong revision/session, or missing-source bindings.
2. Define a persistence projection for new handoffs with no copied report message or validation summary. Keep in-memory callback data where required.
3. Keep LeaderReviewInputBuilder on the canonical FinalReport event and require exactly one compatible binding.
4. Read old full payloads, malformed payloads, and missing sources without replacing typed Review authority.
5. Change only new writes; do not rewrite/delete historical handoffs.
6. Inventory worker_completion_packages.final_report/evidence_json before any later cleanup.

### Tests

Old and new handoffs recover Review input; new writes contain no report duplicate; FinalReport-to-Review, audit, AskUser open/responded, source-loss degradation, routing UI, R1 Task 4, and R1 Task 5 remain green.

### Stop condition

ARCHITECTURE_GAP if new writes copy the report, historical Review input is ambiguous, completion packages are the sole evidence, or source loss changes typed authority. Retain both historical bodies.

### Commit

refactor(worker): make final report handoff reference-only

## R4-03: Legacy Library Coverage Reconciliation

### Goal

Prove coverage of every project_library_entries row by existing Library Objects, Timeline Nodes, and Material References before retirement.

### Files

ProjectLibraryRepository.cs, ProjectLibraryEvolutionRepository.cs, ProjectLibraryModels.cs, Migration011ProjectLibraryEvolution.cs only for proven RED defects; ProjectLibraryLegacyImportTests, ProjectLibraryRepositoryTests, LibraryVisualClosureTests, LibraryCategoryTimeViewTests, WorkbenchSmokeLibraryDataTests.

### Interfaces

ProjectLibraryRepository.SubmitAsync/BrowseAsync; ProjectLibraryEvolutionRepository Object/Timeline/Material Reference readers and proposal application; Migration011 ImportLegacyEntriesAsync; coverage categories FullyCovered, PartiallyCovered, Uncovered, Indeterminate.

### Preconditions

R4-00 counted entries, objects, nodes, refs, proposals, and source references. R5 Truth Heads are not a destination. No entry is deleted or hidden.

### Steps

1. Add a deterministic read-only coverage query keyed by Project and legacy entry ID.
2. Compare exact summary text, normalized identity, ordering, session, task, source reference, and Material References.
3. Emit exactly one category; malformed, conflicting, or unreadable rows are Indeterminate.
4. Reconcile counts and hashes and retain the report.
5. Route future writes through existing Object/Timeline/Material persistence only after UI, proposal, source drill-down, and legacy reads are equivalent.
6. Keep project_library_entries until R4-06; only FullyCovered rows are candidates.

### Tests

Import twice is idempotent. Fixtures cover full, partial, missing, duplicate, malformed, and cross-project rows. All Library import/UI/proposal/smoke tests remain green.

### Stop condition

LIBRARY_COVERAGE_GAP for any non-FullyCovered row, unreconciled source, or behavior change. Retain entries and old write routing.

### Commit

test(library): reconcile legacy entry coverage

## R4-04: Legacy Memory Freeze and Retention Inventory

### Goal

Stop new second-read Session Synthesis/Daily Summary writes under verified conditions while preserving existing Memory data and reads/recovery.

### Files

LeaderMemoryPolicyCoordinator.cs, ProjectMemorySynthesisCoordinator.cs, AppServices.cs, ProjectMemorySynthesisRepository.cs, ProjectMemoryRepository.cs, DailySummaryRepository.cs, Memory API/service/payload parsers, related App and Storage Memory tests.

### Interfaces

PrepareForNewBrainAsync and UpsertDailySummaryAsync; TryProcessNextAsync; RecoverRunningAsync; queue/status/application methods; ScheduleMemorySynthesis; InitializeAsync; ProjectMemoryApi; DailySummaryRepository; ProjectMemoryRepository/Service; tables project_activity_events, project_memory_items, project_memory_sources, project_memory_synthesis_jobs, project_daily_summaries, project_daily_summary_sources, and inventory-only links/topics.

### Preconditions

R4-00 has table existence, row counts, source links, job states, and consumers. Revalidate known counts from the copy. No R5 destination exists.

### Steps

1. RED-test the current New Brain second-read/synthesis behavior and identify any UI/recovery dependency.
2. Freeze new synthesis scheduling and Daily Summary writes only after proving Project reopen, Leader boot, and retained-row reads remain intact.
3. Keep RecoverRunningAsync and historical reads deterministic; a pending/running job is not a delete signal.
4. Export row-level content hashes and source locators for Memory, activity, daily summaries/sources, jobs, and any links/topics.
5. Mark existing Candidate/Formal/Learned and daily-summary migration BLOCKED_BY_R5_DESTINATION. Do not create Summary Delta or a replacement Session Summary.
6. Record retirement candidates and simplification gain without removing code/tables until R4-06.

### Tests

No new second-read job or daily-summary write after freeze; retained Memory browsing/source lookup/Project reopen/Leader boot/provider-independent recovery pass; second InitializeAsync is idempotent; Memory and R1 Task 5 tests pass.

### Stop condition

ARCHITECTURE_GAP if freeze breaks required behavior or a second model read remains reachable. Migration/drop remains BLOCKED_BY_R5_DESTINATION; retain all rows/jobs.

### Commit

refactor(memory): freeze legacy second-read writes

## R4-05: Review Detail Compatibility Narrowing

### Goal

Retain typed Review authority while reducing legacy JSON reliance only after AskUser and historical detail have a deterministic typed/source path.

### Files

LeaderReviewAskUserGate.cs, LeaderReviewInputBuilder.cs, LeaderReviewOrchestrator.cs, AssignmentReviewStateRepository.cs, LeaderReviewStateRepository.cs, legacy parser, UserResponseBinder, and existing Review/recovery certification tests.

### Interfaces

Typed decision/gate records and final_report_event_id; TryOpenAsync/RecoverAsync; UserResponseBinder; AssignmentReviewState compatibility methods; FinalReport/source lookup; legacy LeaderReviewDecisionRecorded reader.

### Preconditions

R4-02 is green. R1 Task 4 and Task 5 are green. Inventory every legacy JSON shape and every richer AskUser field. A typed/source path identifies the same decision, source, action, outcome, and question semantics without copying report bodies.

### Steps

1. Classify Summary, Issue, NextAction, ImportantNote, question marker, and source identity as reconstructible, source-locatable, or historical-only.
2. Add valid/missing/corrupt/old-payload compatibility tests; typed state works when JSON is corrupt.
3. Add only the smallest existing typed/source locator needed; no generic explanation store.
4. Keep legacy writer/data WRITE_COMPAT/READ_COMPAT until detail is reconstructed or deliberately retained.
5. Prepare, but do not execute, JSON cleanup for R4-06.

### Tests

Open/Responded AskUser survives restart/provider absence; corrupt/missing JSON falls back; rich history is unchanged; Review, Assignment/Revision, FinalReport-to-Review, R1 Task 4, and R1 Task 5 pass.

### Stop condition

REVIEW_DETAIL_COMPATIBILITY_GAP if any detail cannot be reconstructed/retained or authority/first-response binding changes. Do not delete legacy JSON.

### Commit

test(review): certify legacy detail compatibility boundary

## R4-06: Safe Migration and Schema/Data Cleanup

### Goal

Execute only individually approved, reconciled cleanup operations. "Unused" alone is never a delete reason.

### Files

One future migration/cleanup change per approved data family under src/Workbench.Storage/Migrations; corresponding repositories/readers from R4-01 through R4-05; focused per-family Storage/App tests. No giant cleanup test/commit.

### Interfaces

WorkbenchDatabase.InitializeAsync; MigrationRunner; SQLite transaction/backup/restore; PRAGMA foreign_keys; PRAGMA foreign_key_check; selected authority reader; FinalReport compatibility reader; Library coverage/import; Memory inventory/freeze; Review detail reader; row identity/content/source reports; rollback markers.

### Preconditions

Every delete/drop/body compaction has all eight recorded for the same copy and migration version:

Required labels: inventory; production-copy rehearsal; compatibility reader/import; migration destination; row reconciliation; FK validation; recovery test; rollback window.

1. Inventory: definitions, counts, identities, hashes, sources, writers/readers, orphans/conflicts.
2. Production-copy rehearsal: exact change succeeds on a byte-for-byte copy, never live.
3. Compatibility reader/import: historical rows remain readable or have an idempotent destination import.
4. Migration destination: every retained body has an identified destination; R5 Memory has none in R4 and is BLOCKED_BY_R5_DESTINATION.
5. Row reconciliation: zero Uncovered, Indeterminate, or unexplained PartiallyCovered rows.
6. FK validation: PRAGMA foreign_key_check is empty before and after.
7. Recovery test: restart, second initialization, Project reopen, provider-unavailable recovery, Review/AskUser, and relevant Worker/Library/Memory reads pass.
8. Rollback window: immutable pre-change copy and tested restore remain available; no backup deletion during the window.

### Steps

1. Process one family per commit with before/after reports.
2. Worker: retain the R4-01 owner; demote the non-owner only after historical-reference, FK, rollback, and Task 5 proof.
3. FinalReport/Handoff: retain WorkerFinalReportReceived; compact/drop duplicate handoff/completion-package bodies only after old/new readers and audit references pass.
4. Library: delete only FullyCovered entries with reconciled source refs and successful rehearsal.
5. Memory: do not migrate/drop items, sources, daily summaries, or R5-dependent tables. Retire synthesis jobs only when frozen, completed outputs reconciled, no Pending/Running recovery depends on them, and rollback is open.
6. Review: retain legacy JSON until R4-05 proves typed/source detail and audit retention.
7. Run the exact migration on fresh second initialization; abort on count, hash, FK, or recovery failure.

### Tests

Per-family zero-loss reconciliation; failure rollback at every destructive statement; empty FK check; project isolation; idempotent second initialization; R1 Task 4, R1 Task 5, Project reopen, Assignment/Revision, FinalReport-to-Review, Open/Responded AskUser, Worker recovery, and Library source lookup.

### Stop condition

Any missing precondition, non-zero FK, non-FullyCovered category, failed recovery, missing R5 destination, or expired/untested rollback is a hard stop. Leave the family intact and report the exact blocker.

### Commit

One approved family per commit, such as refactor(storage): remove reconciled duplicate handoff bodies; refactor(storage): retire fully covered legacy library rows; refactor(storage): retire completed synthesis queue. Never use one r4 cleanup everything commit.

## R4-07: Production-Copy Rehearsal and Seal

### Goal

Prove the complete sequence on a production copy and seal it without writing the live database.

### Files

Future rehearsal fixture under tests/Workbench.Storage.Tests/Database; existing ProviderIndependentProjectRecoveryCertificationTests, LeaderReviewAuthorityRestartCertificationTests, Worker routing, Memory certification, and Library reconciliation tests; immutable inventory/reconciliation/FK/restore logs.

### Interfaces

SQLite backup/restore; MigrationRunner; WorkbenchDatabase.InitializeAsync; compatibility readers/imports; typed Kernel repositories; reconciliation, FK, second-init, provider-independent reopen, and R1 gate assertions.

### Preconditions

R4-00 through R4-06 are reviewed and green. Every selected cleanup has all eight R4-06 preconditions. The copy checksum is recorded and the live path is excluded.

### Steps

1. Recreate a fresh byte-for-byte v18 copy and rerun inventory.
2. Apply reviewed tasks in order: authority, canonical body/readers, Library, Memory freeze, Review compatibility, approved cleanups.
3. Reconcile every retained/migrated body and source.
4. Assert empty PRAGMA foreign_key_check.
5. Recreate AppServices, call InitializeAsync twice, reopen without Provider/transcript, and verify Project/Leader/Assignment/Revision/Worker/Review/AskUser.
6. Run unchanged R1 Task 4 and Task 5, plus FinalReport-to-Review, AskUser, Worker, Library, and retained Memory tests.
7. Restore the pre-change copy and verify rollback; never alter live data.
8. Seal source checksum, version, reports, test output, FK output, and unresolved BLOCKED_BY_R5_DESTINATION items.

### Tests

R1 Task 4, R1 Task 5, Project reopen, Assignment/Revision, FinalReport-to-Review, Open/Responded AskUser, Worker recovery, legacy-data reconciliation, production-copy rehearsal, empty FK check, idempotent second initialization, and rollback restore.

### Stop condition

R4_SEAL_BLOCKED if live access occurred, any test/reconciliation/FK/second-init/rollback check failed, or an R5-dependent family lacks a destination. Preserve the copy.

### Commit

test(storage): seal r4 production-copy rehearsal

## Retention Matrix

Only these retention classes are permitted: KEEP_CORE, KEEP_HISTORY, KEEP_TEMPORARY, MIGRATE_THEN_DROP, DROP_WHEN_UNUSED.

| Body/table | Authority role | Current consumer | Future destination | Class | Migration required | Safe delete condition | Target |
|---|---|---|---|---|---|---|---|
| leader_messages | Leader conversation/source history, not Kernel state | Leader boot, AskUser, recovery | Existing messages and locators | KEEP_HISTORY | No R4 migration | No gate/source/history reference remains and later history policy permits | R4 retain |
| task_events | Worker routing/audit and FinalReport source, not Review authority | TaskEventRepository, routing, Review builder, assignments | Existing history and typed references | KEEP_HISTORY | Compatibility readers | All consumers have typed/source readers, audit retention and recovery pass | R4-02/R4-07 |
| WorkerFinalReportReceived body | Canonical result/evidence | Review builder, assignment transition, audit | Same event keyed by final_report_event_id | KEEP_CORE | No body migration | Never while typed Review/audit references it | R4 retain |
| WorkerToLeaderHandoff body | Routing/lifecycle/reference, not Truth | Routing/UI/callback/audit | Thin SourceEventId handoff | MIGRATE_THEN_DROP | Old full -> reference reader | New writes have no report body; old rows reconcile; audit/recovery/rollback pass | R4-02/R4-06 |
| worker_executions | Candidate Worker authority; R4-01 decides | Repository/tests, completion packages, Task 5 | R4-01-selected owner | KEEP_CORE | Only if another owner certified | Never for "no caller"; only single-owner, FK, rollback, Task 5 proof | R4-01/R4-06 |
| worker_completion_packages | Duplicate completion/evidence history | Repository/tests/audit found by inventory | Canonical FinalReport + source refs | MIGRATE_THEN_DROP | Row report/evidence reconciliation | No unique consumer; canonical/audit source recovers all rows | R4-02/R4-06 |
| project_memory_items | Legacy Candidate/Formal/Learned history | Memory API/boot/UI | R5 Summary Delta or retained history | MIGRATE_THEN_DROP | Yes; blocked in R4 | Every row/layer/status/content/source maps to approved destination | R5 then R4-06 |
| project_memory_sources | Legacy Memory provenance | Memory repository/API/UI | R5 thin Source | MIGRATE_THEN_DROP | With items | Every locator maps; no source discarded | R5 then R4-06 |
| project_memory_links | Relation if present; existence unresolved | R4-00 consumer inventory | R5 relation/source if present | KEEP_HISTORY | Inventory; migrate only if present | Existence, row mapping, R5 destination, FK/recovery proven; absent v18 means no action | R4-00/R5 |
| project_memory_topics | Topic index if present; existence unresolved | R4-00 consumer inventory | R5 topic/source if present | KEEP_HISTORY | Inventory; migrate only if present | Existence, row mapping, R5 destination, FK/recovery proven; absent v18 means no action | R4-00/R5 |
| project_memory_synthesis_jobs | Temporary second-read queue | Coordinator/startup/tests | No R4 replacement | KEEP_TEMPORARY | State/output inventory | Frozen; no Pending/Running recovery dependency; outputs reconciled; rollback open | R4-04/R4-06 |
| project_daily_summaries | Legacy mutable summary history | DailySummaryRepository, Memory API/UI, policy | R5 sparse Summary Delta | MIGRATE_THEN_DROP | Yes; blocked in R4 | Every document/revision/source maps to approved destination | R5 then R4-06 |
| project_library_entries | Legacy Library entry | BrowseAsync, Migration011, legacy UI | Existing Objects/Nodes/Material refs; R5 Truth later | MIGRATE_THEN_DROP | Per-row | FullyCovered, exact text/identity/source reconciliation, reader/rehearsal/rollback pass | R4-03/R4-06 |
| project_library_objects | Existing Library identity/overview | Evolution repository/UI/proposals/import | Same R4 table; R5 Heads later | KEEP_HISTORY | No R4 migration | No R4 delete; later R5 replacement and source proof | R4/R5 |
| project_library_timeline_nodes | Existing Library evolution/history | Evolution repository/UI/proposals/import | Same R4 table; R5 Evolution later | KEEP_HISTORY | No R4 migration | No R4 delete; later R5 source/recovery proof | R4/R5 |
| typed review state: task_review_decisions and task_review_user_gates | Authoritative Review/Authority/AskUser | Orchestrator, gate, binder, recovery | Same typed tables | KEEP_CORE | No R4 migration | Never while task/revision/audit/recovery references it | R1/R4 |
| legacy Review decision JSON | Compatibility detail/audit only | AskUser fallback and history | Typed fields + FinalReport/source | DROP_WHEN_UNUSED | Reader/detail reconciliation | All historical detail and R1 recovery pass without it; audit retention pass | R4-05/R4-06 |

## R4 Task Sequence Summary

| Task | Goal | Why now | Blocked by | Gain |
|---|---|---|---|---|
| R4-00 | Actual v18 schema/call-site inventory | Prevent unsafe assumptions | Baseline/copy | LOW |
| R4-01 | One Worker authority | Existing typed/event representations are not proven equivalent | R4-00 | MEDIUM/HIGH |
| R4-02 | Canonical FinalReport, thin Handoff | Stop new report duplication | R4-00/R4-01 | HIGH |
| R4-03 | Library row coverage | Migration011 alone is not proof | R4-00 | HIGH |
| R4-04 | Memory freeze and retention | Enforce understand-once without deleting rows | R4-00; R5 destination absent | HIGH |
| R4-05 | Review JSON compatibility narrowing | Typed authority exists, rich AskUser detail still uses JSON | R4-02/R1 gates | MEDIUM |
| R4-06 | Gated cleanup | Make deletion conditional/reversible | R4-01..05 and eight preconditions | HIGH |
| R4-07 | Production-copy seal | Validate real data and rollback | All prior tasks | LOW |

## R5 Boundary

R4 may inventory, freeze, retain read-only, reconcile, add compatibility readers, route new writes to existing canonical representations, and prepare reports. R4 may not implement Truth Heads, Summary Delta storage, Alignment, Evolution, Proposal authority, Library Truth UI, or replacement Summary/Synthesis schema.

Existing Memory items and daily summaries requiring a typed destination are BLOCKED_BY_R5_DESTINATION. Before formal R5 implementation, amend the approved Truth Governance spec to state: Summary = Leader normal cognition + Optional Summary Delta, replacing Daily Summary/session synthesis. R4 does not implement that amendment or schema.

## Safety and Recovery

Production data is read from an immutable copy. Every migration has destination, row reconciliation, FK check, second initialization, restart/provider-unavailable recovery, and rollback copy/window. R1 Task 4 Review and R1 Task 5 offline recovery are mandatory gates in R4-02, R4-05, R4-06, and R4-07. The final seal proves Project reopen, Assignment/Revision, FinalReport-to-Review, Open/Responded AskUser, Worker recovery, legacy-data no-loss, production rehearsal, empty foreign_key_check, and idempotent second initialization.

## Plan Self-Review

1. No delete-before-migration: rows remain until destination, reconciliation, recovery, and rollback pass.
2. No two authorities: R4-01 selects one Worker owner; typed review is the Review authority; FinalReport has one body.
3. No hidden R5: R4 freezes/inventories/prepares and blocks destination-dependent work.
4. No event-log authority: task_events are routing/audit/source compatibility unless R4-01 certifies a typed projection.
5. Production data is included through copy inventories, hashes, sources, and FK checks.
6. No generic Memory/Provider/Plugin system is introduced.
7. Each task is independently reviewable with its own commit and stop gate.
8. R1 Task 4 and Task 5 regression gates are explicit.
9. Every drop has the eight safe-delete preconditions and a matrix condition.
10. Simplification is concrete: thin new handoffs, reconciled Library writes, frozen second-read Memory, and only proven cleanup; uncertain/R5 data remains retained.

## Planning Deliverable

Only this plan file is created in this turn. Required commit: docs(plan): define continuity data slimming.
