# R5 Call Graph + Persistence Data Audit

Date: 2026-08-22
Baseline: `master` at `4d5a2a17485998b44eac703078ee648d93265aae`
Governing decision: `docs/superpowers/specs/2026-08-22-r5-boundary-reconciliation-design.md`
Prior audit: `docs/superpowers/reports/2026-08-22-r5-repository-ownership-audit.md`

## 1. Executive Summary

This audit validates the first ownership audit against the current production call graph and a safely copied production database. It does not authorize implementation, migration, freezing, or deletion.

The principal findings are:

1. `IAgentRuntime`, `WorkerSessionRouter`, `WorkerExecution`, Leader epoch persistence, and the review pipeline are mixed-boundary implementations. Their application/continuity responsibilities are active even where their execution or provider assumptions belong in `DELEGATED` or `ADAPTER`. The correct disposition remains `SEPARATE`, not whole-module deletion.
2. Current Leader identity is materially conflated with a physical provider session. `project_leaders.current_epoch_id` is the durable logical anchor, while the epoch also stores provider, account, model, runtime session, and working directory. Provider/model/workdir equality is enforced during rollover.
3. The current review path treats an Agent-generated review payload as a persisted “decision.” `AutoProceed` can then complete an Assignment through mechanical policy resolution without an attributable `AuthorityDecision`. No production `AcceptedProjectState` model or projection was observed.
4. Several execution-taxonomy repositories have no observed production consumer and no production-copy rows: `PermissionRequest`, `TaskGrant`, `TaskClarification`, `WorkerCompletionPackage`, and their tables. This is evidence for future reconciliation, not deletion authorization; migrations, tests, public repository surfaces, and unresolved semantic splits remain blockers.
5. Legacy Memory is not empty and is not semantically equivalent to R5-A Summary. The production copy contains 20 memory items, 28 source links, and 16 synthesis jobs. Its normal UI/production writer path is no longer observed, but its provenance and history require explicit treatment.
6. Leader continuity is live historical state: 20 epochs and 39 messages remain, with three active current epochs. The rollover/boot path still reads epochs, messages, and optional continuity selections. It cannot be retired before logical actor/session-binding semantics have a durable replacement.
7. The production copy is schema v11. A second disposable copy migrated through the real `WorkbenchDatabase.InitializeAsync` chain to v19 successfully, with `quick_check=ok`, zero foreign-key violations, and preserved historical row counts. The new typed review and R5-A Summary tables contain zero rows on this copy.
8. Real `task_events` contain three legacy `WorkerSessionStarted` and three legacy `WorkerToLeaderHandoff` payloads. The latter use the old shape and are still reachable through compatibility readers. They are not hypothetical migration debris.

The data divides the broad label “legacy” into three different states:

```text
No observed production consumer / empty shell
!=
Historical compatibility with real rows
!=
Older implementation still carrying continuity
```

## 2. Audit Method / Safety

### 2.1 Static call/use method

The priority areas were traced across `src/**` at the baseline from composition roots through direct calls, constructor injection, interface implementations, event/result handling, repository composition, UI bindings, startup/recovery entry points, serialization payloads, and migration/backfill readers. For every `NO_PRODUCTION_CONSUMER_OBSERVED` finding, the search included its concrete type, interface, constructor/composition registration, and public method call sites in `src/**`; declarations and tests were excluded from production-consumer counts. Tests were recorded only as evidence of supported behavior. No prioritized service was classified as unused from a single text search.

The primary production composition root registers Runtime/Worker at `src/Workbench.App/Services/AppServices.cs:149-166,202-242`, continuity/Memory/Library/Summary at `src/Workbench.App/Services/AppServices.cs:167-188,245-250`, and review services at `src/Workbench.App/Services/AppServices.cs:189-200,232-239`. The main project-open recovery entry is `src/Workbench.App/ViewModels/MainWindowViewModel.cs:112-116`.

### 2.2 Live database safety

Live database: `%LOCALAPPDATA%\AI Game Workbench\workbench.db`

- Live file existed, length 421,888 bytes, last-write UTC `2026-08-14T05:11:33.5943754Z`.
- No live `-wal` or `-shm` file was present.
- A read handle with exclusive sharing was acquired before copying; no repository or SQLite initialization was run against the live path.
- `LIVE_HASH_BEFORE`: `DDB96E3E0973FB8F9E6CF44FA2775EB0F8452A9A3671FF4939EB917806BAC848`.
- Untouched v11 copy: `C:\Users\pupet\AppData\Local\Temp\r5-call-data-audit-5c192087c9aa4fb49285efe5e65ffe96\workbench-untouched.db`.
- Migrated working copy: `C:\Users\pupet\AppData\Local\Temp\r5-call-data-audit-5c192087c9aa4fb49285efe5e65ffe96\workbench-v19-audit.db`.
- The untouched copy hash exactly matched `LIVE_HASH_BEFORE`.
- Only the second copy was passed to the real migration chain. Its pre-migration hash matched the untouched copy; its post-migration hash was `751DAF3BF3D74ECE1AF637DC71A2E36293EBC97ACBDA4CFB38414C29F99AE9A4`.
- Database inspection used read-only immutable SQLite connections. Transcript bodies, project paths, memory text, and JSON message content were not emitted.

`LIVE_HASH_AFTER`: `DDB96E3E0973FB8F9E6CF44FA2775EB0F8452A9A3671FF4939EB917806BAC848` (exact match).

The validated disposable Temp directory, both database copies, and the temporary migration runner were deleted after final inspection.

## 3. Current Call Graph Findings

### 3.1 Runtime and session gateway surface

| Responsibility | Production graph | Observed state | Boundary / semantic result |
|---|---|---|---|
| `IAgentRuntime` | Interface at `src/Workbench.Runtime/Runtime/IAgentRuntime.cs:6-44`; constructed at `src/Workbench.App/Services/CodexRuntimeComposition.cs:16-32` and registered at `src/Workbench.App/Services/AppServices.cs:149-166,202-242`; create/resume/send/stop are called by Leader, Worker, rollover, and review paths. | `MIXED` | Minimal create/resume/send/respond/stop is an `ADAPTER` Session Gateway `SEMANTIC_MATCH`. Discovery, transcript, and provider capabilities are optional `PARTIAL_MATCH` surfaces. Retirement `BLOCKED`. |
| `CodexAgentRuntime` and protocol stack | The only production `IAgentRuntime` implementation; its send loop maps protocol events at `src/Workbench.Runtime/Providers/Codex/CodexAgentRuntime.cs:162-269`, consumed by Leader UI, Worker routing, rollover, and review. | `ACTIVE_ADAPTER` | Codex thread ID can be a future opaque SessionBinding locator, but the current stored session model remains conflated. Keep the provider adapter; do not make Codex semantics a domain prerequisite. |
| Runtime sandbox/approval literals | `CodexAgentRuntime` selects read-only sandbox and approval settings during create/resume/turn setup at `src/Workbench.Runtime/Providers/Codex/CodexAgentRuntime.cs:124-153,192-203`. | `ACTIVE_ADAPTER`, with delegated policy leakage | Transporting provider options is adapter work; owning runtime permission policy is `DELEGATED`. No target domain mapping. |
| Runtime registry/resources | Registry is composed at `src/Workbench.App/Services/AppServices.cs:166,232`; Leader model/resource reads occur at `src/Workbench.App/ViewModels/Panes/LeaderPaneViewModel.cs:276,588-602`; resources create profiles at `src/Workbench.Runtime/Registry/WorkerResource.cs:15-26`. | `ACTIVE_APPLICATION` + `ADAPTER` | Dispatch metadata is a `PARTIAL_MATCH` for Assignment routing, not LogicalActor identity. |
| Transcript read | Work pane selection calls `GetTranscriptAsync` at `src/Workbench.App/ViewModels/Panes/WorkPaneViewModel.cs:33-39`; Codex reads/maps turns at `src/Workbench.Runtime/Providers/Codex/CodexAgentRuntime.cs:366-383`. No production recovery or authority consumer was observed in the declared `src/**` search. | `ACTIVE_ADAPTER` | Optional scene/navigation capability; transcript is not authoritative state. |
| Interactive Codex launcher | Work pane invokes it at `src/Workbench.App/ViewModels/Panes/WorkPaneViewModel.cs:40-55`; Codex/Windows Terminal launch is at `src/Workbench.App/Worker/InteractiveSessionLauncher.cs:35-93`. | `ACTIVE_ADAPTER` | Provider-specific convenience adapter, not a core workflow. |

### 3.2 Worker routing and execution

```text
Leader draft confirmation (`LeaderPaneViewModel.cs:1014-1029`)
  -> WorkerSessionRouter.StartAsync
  -> runtime Create/Resume (`WorkerSessionRouter.cs:263-330`)
  -> worker_executions + legacy WorkerSessionStarted event (`WorkerSessionRouter.cs:275-306`)
  -> runtime Send (`WorkerSessionRouter.cs:330-350`)
  -> AgentTurnCompleted final text (`WorkerSessionRouter.cs:350-353`)
  -> bounded handoff parser (`WorkerSessionRouter.cs:357-371`)
  -> task lifecycle + task_events
  -> review callback + Leader transcript callback (`WorkerSessionRouter.cs:372-380`)
```

`WorkerSessionRouter` is `MIXED`:

- Assignment dispatch, session reuse/rebinding, handoff routing, and recovery are `APPLICATION`.
- Codex event/session mapping and legacy payload persistence are `ADAPTER`.
- workspace/worktree/branch/base-commit state and runtime strategy are `DELEGATED` leakage.
- revision identity and bounded result provenance are partial domain candidates.

The typed `worker_executions` path is active production code even though the inspected production copy contains no rows. `src/Workbench.App/Worker/WorkerSessionRouter.cs:77-99,120-134` reads typed executions first and falls back to legacy task events. `src/Workbench.Core/Workers/WorkerExecutionModels.cs:19-118` combines Assignment revision, provider profile, runtime session, branch, base commit, and worktree identity; `src/Workbench.Storage/Workers/WorkerExecutionRepository.cs:19-64,94-101,190-195` persists and constrains it. This confirms the prior `SEPARATE / MIGRATE / FREEZE` classification.

Two material asymmetries were observed:

1. Worker removal appends a legacy `WorkerRemoved` event at `src/Workbench.App/Worker/WorkerRemovalService.cs:12-42`, but the typed card path at `src/Workbench.App/Worker/WorkerSessionRouter.cs:88-98` does not consult that event. A typed execution with `AgentSessionId` can therefore remain visible because there is no typed close/removal write.
2. Leader transient approval requests are held/answered at `src/Workbench.App/ViewModels/Panes/LeaderPaneViewModel.cs:560-565,804-840`; Worker routing consumes only completion events at `src/Workbench.App/Worker/WorkerSessionRouter.cs:350-353` and has no equivalent approval/clarification route. The dormant `PermissionRepository` is not used for this live provider interaction.

The Worker handoff parser produces `{ Kind, Message, ValidationSummary }` at `src/Workbench.App/Worker/WorkerSessionRouter.cs:350-371`; the review input compatibility reader is at `src/Workbench.App/Leader/LeaderReviewInputBuilder.cs:66-105`. This is a `PARTIAL_MATCH` for bounded Claim/Handoff, but it has no explicit ChangedRefs, EvidenceRefs, result Claim identity, or authority. Parse success does not establish truth.

### 3.3 Leader continuity and rollover

```text
project_leaders.current_epoch_id
  -> leader_session_epochs
  -> external provider/account/model/session/workdir
  -> restore AgentSession + selected model
```

Current durable Leader identity is `(ProjectId, CurrentEpochId)` in `src/Workbench.Storage/Leaders/StoredProjectLeader.cs:3-7`, not a LogicalActor/Responsibility object. `src/Workbench.Storage/Leaders/StoredLeaderSessionEpoch.cs:3-17` simultaneously stores physical provider-session details. Restore reconstructs `AgentSession` at `src/Workbench.App/ViewModels/Leader/ProjectLeaderSessionManager.cs:236-258`; rollover inheritance and persistence predicates are at `src/Workbench.App/Leader/LeaderSessionRolloverService.cs:71-88` and `src/Workbench.Storage/Leaders/ProjectLeaderRepository.cs:203-230`. Session binding is therefore not currently replaceable independently of logical continuity.

Active readers/writers include:

- `ProjectLeaderSessionManager` creates/restores epochs and writes user/assistant messages at `src/Workbench.App/ViewModels/Leader/ProjectLeaderSessionManager.cs:64-91,99-167`.
- `ProjectLeaderRepository` atomically creates/switches the current epoch and performs rollover/selection writes at `src/Workbench.Storage/Leaders/ProjectLeaderRepository.cs:94-169,171-304`.
- Leader history UI reads archived epochs/messages at `src/Workbench.App/ViewModels/Leader/LeaderEpochHistoryViewModel.cs:54-55,93-99`.
- `LeaderBootContextBuilder` reads current epoch and persisted continuity plans at `src/Workbench.App/Leader/LeaderBootContextBuilder.cs:54-61`.
- `LeaderSummaryRecoveryService` reads assistant-message R5-A outbox metadata at `src/Workbench.App/Leader/LeaderSummaryRecoveryService.cs:37-55`.

The normally injected `LeaderMemoryPolicyCoordinator` returns no semantic selection and freezes legacy synthesis at `src/Workbench.App/Memory/LeaderMemoryPolicyCoordinator.cs:23-35`; the production caller passes policy-managed state at `src/Workbench.App/ViewModels/Panes/LeaderPaneViewModel.cs:722-784`. Consequently, the runtime-prompt/transcript-fallback branch at `src/Workbench.App/Leader/LeaderSessionRolloverService.cs:54-68,138-198` has `NO_PRODUCTION_CONSUMER_OBSERVED` under the current `src/**` composition, although it remains public/tested and historical continuity plans are still readable.

An observed continuity loss window remains: after first boot delivery, `MarkBootContextDeliveredAsync` clears referenced archived handoff material and deletes successor selections at `src/Workbench.Storage/Leaders/LeaderSessionEpochRepository.cs:149-194`. The UI marks boot context delivered on the first runtime event at `src/Workbench.App/ViewModels/Panes/LeaderPaneViewModel.cs:548-551`. A failure after that event but before a successful business turn can consume the one-time selection without completed recovery. This is evidence that the old epoch mechanism still carries continuity and that its semantics are not yet a safe SessionBinding replacement.

### 3.4 Memory, Daily Summary, Library, and R5-A

| Area | Writer -> store -> reader/consumer | Observed state | Mapping |
|---|---|---|---|
| Legacy Memory | Writer APIs are at `src/Workbench.Storage/Memory/ProjectMemoryService.cs:9-25` and repository writes at `src/Workbench.Storage/Memory/ProjectMemoryRepository.cs:11-77,135-182`. It is constructed/exposed at `src/Workbench.App/Services/AppServices.cs:175,216` but not injected by `src/Workbench.App/ViewModels/MainWindowViewModel.cs:82-117`. | Normal business writer/reader: `NO_PRODUCTION_CONSUMER_OBSERVED` in the declared `src/**` search; stored rows are real. | Legacy Memory -> R5-A Summary: `NO_MATCH`. Authority/certification and mutable layer semantics differ from sparse non-authoritative continuity deltas. |
| Memory synthesis | Queue/claim/return APIs remain at `src/Workbench.Storage/Memory/ProjectMemorySynthesisRepository.cs:16-141`; startup calls its no-op recovery at `src/Workbench.App/Services/AppServices.cs:249` and `ProjectMemorySynthesisRepository.cs:143-148`. | Queue/claim/apply: `NO_PRODUCTION_CONSUMER_OBSERVED` in `src/**`; 16 rows exist. | `NO_MATCH / UNRESOLVED`; preserve provenance until rows/states are reconciled. |
| Daily Summary | Writer is `src/Workbench.Storage/Memory/DailySummaryRepository.cs:57-105`; API exposure is `src/Workbench.App/Memory/ProjectMemoryApi.cs:14-16`; selected continuity reads are `src/Workbench.App/Memory/ProjectContinuityMaterialService.cs:25-35,155-162`. | `FROZEN_READ_ONLY` with conditional compatibility reader; no current normal writer caller found in `src/**`; zero rows. | Daily -> R5-A Summary: `NO_MATCH` (mutable daily CAS document versus append-only sparse delta). |
| Legacy Library entries | Old repository write/import/read is `src/Workbench.Storage/Memory/ProjectLibraryRepository.cs:11-48`; it is constructed but not injected into the main UI. Migration 011 reads/imports legacy rows at `src/Workbench.Storage/Migrations/Migration011ProjectLibraryEvolution.cs:19-149,222-267`. | Runtime surface: `NO_PRODUCTION_CONSUMER_OBSERVED` in `src/**`; migration compatibility remains. | Evolution objects/timeline/refs are a `PARTIAL_MATCH`; coverage still needs row-level proof. |
| Evolution Library | UI readers are `src/Workbench.App/ViewModels/Panes/LibraryPaneViewModel.cs:194-233,363-430`; repository CRUD/ownership is `src/Workbench.Storage/Memory/ProjectLibraryEvolutionRepository.cs:19-305`; proposal write/accept flow is `src/Workbench.Storage/Memory/ProjectLibraryProposalService.cs:26-66,153-208`. | `ACTIVE_APPLICATION` + `ADAPTER` | User Accept/Edit/Reject is an explicit application confirmation boundary and a `PARTIAL_MATCH` for future authority governance; it is not a complete AuthorityDecision or AcceptedProjectState projection. |
| R5-A Summary | Admission is `src/Workbench.App/Leader/LeaderDraftProposalBuilder.cs:48-113,152-190,347-362`; live write is `src/Workbench.App/ViewModels/Panes/LeaderPaneViewModel.cs:640-687`; repository append/query is `src/Workbench.Storage/Memory/ProjectSummaryRepository.cs:17-165,179-231`; recovery is `src/Workbench.App/Leader/LeaderSummaryRecoveryService.cs:32-73`. | Writer/recovery: `ACTIVE_CANONICAL`; query/UI/boot consumer: `NO_PRODUCTION_CONSUMER_OBSERVED` in `src/**`. | `KEEP`. Non-authoritative continuity view, not Accepted State and not a recomputed projection. |

### 3.5 Review and authority call graph

```text
Worker FinalReport/Handoff Claim
  -> tasks: Working -> Reviewing
  -> LeaderReviewInputBuilder (`LeaderReviewInputBuilder.cs:41-121`)
  -> LeaderReviewRuntimeAdapter prompt (`LeaderReviewRuntimeAdapter.cs:24-146`)
  -> Agent review JSON (ReviewClaim)
  -> parser + authority-mode policy resolver
  -> task_review_decisions (`LeaderReviewStateRepository.cs:60-114`)
  -> legacy task event (`AssignmentReviewStateRepository.cs:27-71`)
  -> AutoProceed OR AskUser gate
```

| Responsibility | Observed state / boundary | Authority finding |
|---|---|---|
| Review runtime adapter and prompt | Called only by the orchestrator at `src/Workbench.App/Leader/LeaderReviewRuntimeAdapter.cs:24-146`; `ACTIVE_ADAPTER`, while review strategy/prompt cognition is `DELEGATED`. | Agent output is the source ReviewClaim. Approval/tool events are treated as review failure at `LeaderReviewRuntimeAdapter.cs:43-50` rather than routed transient interactions. |
| Review input builder | `src/Workbench.App/Leader/LeaderReviewInputBuilder.cs:41-121`; `ACTIVE_APPLICATION` + compatibility adapter. | Reads current revision plus final-report/handoff events; canonical binding uses source event ID and legacy binding compares content at lines 66-105. |
| Review payload parser | Production callee at `src/Workbench.App/Leader/LeaderReviewPayloadParser.cs:22-50`; `ACTIVE_ADAPTER`. | The type name says “decision,” but the parsed object is only a ReviewClaim: no actor, responsibility, evidence chain, or authority attribution. |
| Authority mode/resolver | Mode selection is `src/Workbench.Core/Leaders/LeaderAuthorityMode.cs:10-13`; resolution is `src/Workbench.Core/Leaders/LeaderAuthorityResolver.cs:14-35`; settings are read by `src/Workbench.App/Leader/LeaderReviewOrchestrator.cs:83-86`. | `AutoProceed/Notify/AskUser` is a mechanical routing result, not an attributable AuthorityDecision. Settings save methods had no source caller in the declared `src/**` search. |
| Typed review state | Orchestrator writes at `src/Workbench.App/Leader/LeaderReviewOrchestrator.cs:80-104`; stored shape is `src/Workbench.Storage/Reviews/LeaderReviewStateRepository.cs:60-114`; `ACTIVE_APPLICATION`/history adapter. | Stores Claim outcome plus policy snapshot; no LogicalActor, authority holder, Claim ref, or EvidenceRef. |
| Legacy review task events | JSON read/write is `src/Workbench.Storage/Tasks/AssignmentReviewStateRepository.cs:27-71`; migrations/backfills are `src/Workbench.Storage/Migrations/Migration017TypedLeaderReviewBackfill.cs:13-46` and `Migration018ReviewDecisionSubject.cs:17-72`. | `COMPATIBILITY_ONLY` for current typed recovery; JSON/content-equality history is not authoritative state. |
| AutoProceed | `src/Workbench.App/Leader/LeaderReviewAutoProceedExecutor.cs:27-56`; `ACTIVE_APPLICATION`, driven by delegated Agent Claim. | A matching Pass ReviewClaim and stored AutoProceed resolution writes `tasks.status=Completed`. This bypasses attributable Authority. |
| AskUser gate and binder | Gate writes at `src/Workbench.App/Leader/LeaderReviewAskUserGate.cs:31-66`; binding is `src/Workbench.App/Leader/LeaderReviewUserResponseBinder.cs:21-41`; UI source is `src/Workbench.App/ViewModels/Panes/LeaderPaneViewModel.cs:472-480,534-540`. | Opens a user-decision state and binds the first user message, but does not create an AuthorityDecision or accepted-state projection. |

The path also contains a separate attribution mismatch: `src/Workbench.App/Leader/LeaderDraftProposalBuilder.cs:364-400` persists an Agent-created draft task revision with `ApprovedBy=User`; `src/Workbench.App/ViewModels/Panes/LeaderPaneViewModel.cs:1014-1037` confirms worker start rather than approving the already-stored revision.

No production model, writer, reader, or consumer representing `AcceptedProjectState` was observed in the reviewed paths. `tasks.status=Completed` is an Assignment lifecycle state, not Accepted Project State.

## 4. Persistence Writer/Reader Matrix

| Store | Current writer(s) | Current reader(s) | Semantic consumer | Use state / asymmetry |
|---|---|---|---|---|
| `project_leaders`, `leader_session_epochs` | Leader session manager; rollover transaction | restore, rotation, history, boot | continuity and provider-session restoration | `MIXED`; logical identity and physical binding conflated. |
| `leader_messages` | Leader user/assistant turns; R5-A outbox metadata | current/history UI, selected transcript continuity, Summary recovery | contextual transcript plus durable outbox | `MIXED`; transcript non-authoritative, outbox active. |
| continuity plans/selections | rollover only when policy yields a selection | boot/recovery | bounded next-epoch context | Current normal writer branch absent; historical reader active. |
| `tasks`, `task_revisions` | draft builder, confirmation/lifecycle, router, review gate/auto-proceed | UI, router, review input/recovery | Assignment lifecycle and revision | `ACTIVE_CANONICAL`; approval attribution mismatch remains. |
| `task_events` | router, review compatibility, user-response trace | router fallback, review input, migrations/backfills | chronology/provenance/compatibility | `MIXED`; real legacy rows exist; JSON history is not authority. |
| `worker_executions` | Worker router | Worker router/cards/recovery | typed execution/session projection | `ACTIVE_APPLICATION` code, zero copied rows; worktree/Git fields leak delegated execution. |
| completion packages | Repository API only | Repository API only | no observed production semantic consumer | `NO_PRODUCTION_CONSUMER_OBSERVED`; zero rows; migration/FK surface remains. |
| permission requests | Repository API only | Repository API only | no live provider-interaction consumer | `NO_PRODUCTION_CONSUMER_OBSERVED`; zero rows. Live Leader approval uses runtime events instead. |
| task grants | Repository API only | Repository API only | no observed production consumer | `NO_PRODUCTION_CONSUMER_OBSERVED`; zero rows. |
| task clarifications | Repository API only | Repository API only | no observed production consumer | `NO_PRODUCTION_CONSUMER_OBSERVED`; zero rows; transient vs material semantics unresolved. |
| typed review decisions/gates | orchestrator, gate, response binder | auto-proceed, gate, binder, recovery | review routing and Assignment state | `ACTIVE_APPLICATION` code; zero copied rows; Claim/Authority conflation. |
| Legacy Memory/items/sources | Public service/repository writers remain | public repository readers; no current main-window consumer | historical memory/provenance | Frozen call surface, non-empty data. Writer API without observed current business caller. |
| synthesis jobs | queue API; no current caller observed | status API; no current UI; startup no-op recovery | historical job state | 16 rows despite dormant runtime queue. |
| Daily Summary | repository writer, no current caller observed | conditional continuity reader | selected recovery material | Reader without current writer; zero rows. |
| legacy Library entries | old repository writer, no current UI caller | Migration 011/import/coverage | compatibility import | zero rows; migration reader persists. |
| Library objects/timeline/refs/proposals | proposal service and evolution repository | Library UI and continuity material reader | governed Library | `ACTIVE_APPLICATION`/`ADAPTER`; objects/timeline/refs non-empty, proposals empty. |
| R5-A Summary entries/refs | Leader result outbox + recovery | query repository, but no production query/UI/boot caller observed | non-authoritative continuity | Active writer with no observed semantic reader; zero rows in copied historical DB. |
| workbench/project settings | repository save APIs | authority/rotation effective-settings services | routing policy | One global rotation setting row; project settings empty. Authority-mode save has no observed production source caller. |

## 5. Production-Copy Data Inventory

### 5.1 Untouched schema-v11 copy

All requested tables that exist in v11 were counted. `quick_check=ok`; `pragma foreign_key_check` returned no violations.

| Table | Rows | Material shape |
|---|---:|---|
| `projects` | 4 | One project has no `project_leaders` row. |
| `project_leaders` | 3 | All current epoch references valid and project-consistent. |
| `leader_session_epochs` | 20 | 17 ended, 3 active; all provider `codex`; all have external session IDs; 15 boot-delivered, 5 not delivered. |
| `leader_messages` | 39 | 22 user, 17 assistant; no orphan epoch references. Bodies not inspected. |
| `leader_epoch_continuity_plans` | 0 | Empty. |
| `leader_epoch_continuity_selections` | 0 | Empty. |
| `tasks` | 5 | All `Draft`; all current revision references valid. |
| `task_revisions` | 23 | Historical revisions exist. |
| `task_events` | 6 | Three `WorkerSessionStarted`, three `WorkerToLeaderHandoff`; all `execution_id` null. |
| `task_grants` | 0 | Empty. |
| `task_clarifications` | 0 | Empty. |
| `worker_executions` | 0 | Empty. |
| `worker_completion_packages` | 0 | Empty. |
| `permission_requests` | 0 | Empty. |
| `project_memory_items` | 20 | Layers: Candidate 12, Formal 6, Learned 2. States: Active 12, Superseded 6, Rejected 2. |
| `project_memory_sources` | 28 | Source kinds: Manual 13, LeaderMessage 8, LeaderEpoch 7; zero orphan references; every memory item has a source. |
| `project_memory_synthesis_jobs` | 16 | Pending 13, Completed 3. |
| `project_memory_preferences` | 0 | Empty. |
| `project_daily_summaries` | 0 | Empty. |
| `project_daily_summary_sources` | 0 | Empty. |
| `project_library_entries` | 0 | Empty legacy table. |
| `project_library_objects` | 2 | Categories: Design 1, Engineering 1; current overview present on both. |
| `project_library_timeline_nodes` | 5 | All object references valid. |
| `project_library_material_refs` | 2 | All node references valid. |
| `project_library_proposals` | 0 | Empty. |
| `project_settings` | 0 | Empty. |
| `workbench_settings` | 1 | `leader_session_rotation_policy=ManualOnly`. |

The six task-event payloads are valid JSON. The three Worker-start payloads use the legacy session snapshot shape. The three handoff payloads contain only the old fields `CreatedAt`, `Message`, `ProjectId`, `Status`, `TaskId`, `WorkerLabel`, and `WorkerSessionId`; they do not contain the newer source-event/revision binding. These are real historical rows parseable by the Worker-routing compatibility reader at `src/Workbench.App/Worker/WorkerSessionRouter.cs:77-118`. Their presence does **not** prove the review-input fallback is currently reachable: all copied tasks are Draft, while `LeaderReviewInputBuilder` requires Reviewing plus FinalReport/current-revision conditions at `src/Workbench.App/Leader/LeaderReviewInputBuilder.cs:41-105`.

### 5.2 Disposable v11 -> v19 migration

The real storage initializer migrated only the second disposable copy. Results:

- schema version: 19
- `quick_check`: `ok`
- foreign-key violations: 0
- all non-empty historical table counts listed above were preserved
- `task_review_decisions`: 0
- `task_review_user_gates`: 0
- `project_summary_entries`: 0
- `project_summary_source_refs`: 0
- R5-A outbox metadata on the 39 migrated `leader_messages`: zero `result_id`, zero payload, zero persisted timestamp

No historical task event qualified for a typed review backfill. This confirms that the current review/summary schema is supported by migration while the inspected production history predates actual use of those rows.

## 6. Legacy -> Target Semantic Mapping

| Current artifact | Target candidate | Result | Reason |
|---|---|---|---|
| Leader project row / epoch | LogicalActor + Responsibility + SessionBinding | `PARTIAL_MATCH` | Project-level durable pointer exists, but provider session and logical identity are conflated. |
| WorkerExecution assignment/revision fields | Assignment + Attempt provenance | `PARTIAL_MATCH` | Revision identity is useful; worktree/Git/runtime strategy is delegated and session identity is not separately bound. |
| External Agent session ID | SessionBinding locator | `PARTIAL_MATCH` | It can become an opaque locator, but current persistence does not permit binding-only replacement independent of responsibility/provider metadata. |
| Worker final report/handoff | Claim + bounded Handoff + EvidenceRefs | `PARTIAL_MATCH` | Current payload is bounded but lacks explicit Claim/evidence/ref semantics. |
| Completion package | Claim/Handoff/EvidenceRefs | `PARTIAL_MATCH` | Repository stores package material, but full execution package is not accepted project truth and has no observed production consumer. |
| Runtime provider approval | TransientInteraction | `SEMANTIC_MATCH` | Leader forwarding exists; Worker forwarding is missing. Policy/enforcement stays delegated. |
| Task clarification | TransientInteraction or Assignment Revision/Authority | `UNRESOLVED` | Current model does not split transient questions from material scope/authority changes. |
| Task grant taxonomy | Assignment/Revision authority | `NO_MATCH / UNRESOLVED` | Current capability grant is an execution taxonomy with no observed production consumer. |
| Parsed review JSON | ReviewClaim | `SEMANTIC_MATCH` after reinterpretation | It is attributable only to an external runtime result, not an AuthorityDecision. |
| Typed review decision row | ReviewClaim + policy snapshot + future AuthorityDecision | `PARTIAL_MATCH` | Current row conflates these concepts and lacks actor/evidence attribution. |
| `tasks.status=Completed` | Assignment fulfillment state | `SEMANTIC_MATCH` at Assignment scope only | It is not Accepted Project State. |
| Legacy Memory | R5-A Summary | `NO_MATCH` | Certified/mutable memory layers and sparse non-authoritative Summary deltas have different semantics. |
| Daily Summary | R5-A Summary | `NO_MATCH` | Mutable daily document and append-only continuity entries differ. |
| Legacy Library entry | object/timeline/material refs | `PARTIAL_MATCH` | Migration preserves content/source refs but infers date and does not prove all view semantics. |
| R5-A Summary | non-authoritative continuity view | `SEMANTIC_MATCH` | Append-only source locators/provenance refs and crash recovery; neither Claim verification nor AuthorityDecision attribution. |

## 7. Session Identity Findings

1. **Logical Leader identity today:** effectively `project_leaders` plus its current epoch pointer. There is no durable LogicalActor or Responsibility object.
2. **Physical provider session today:** `leader_session_epochs.external_session_id` plus provider/account/model/agent/workdir fields; Worker uses `worker_executions.agent_session_id` or legacy task-event session snapshots.
3. **Conflation:** Leader rollover requires inherited provider/account/model/workdir; WorkerExecution combines Assignment revision, session, runtime profile, branch, worktree, and Git base identity.
4. **What survives rollover:** archived epoch rows/messages, successor epoch, current pointer, and any not-yet-consumed continuity selections.
5. **What can be lost:** physical runtime continuity if the external session is unavailable; semantic handoff selection after first-event delivery marking; unpersisted provider scene; Worker typed-removal state because no typed close path exists.
6. **Required reinterpretation, not schema design:** `project_leaders` is closest to LogicalActor anchoring; epoch/session IDs are SessionBinding candidates; task/revision is the Assignment/Revision candidate; WorkerExecution should be split between Assignment attempt provenance and delegated execution state.

Session rebinding is not presently a connectivity-only operation. That makes epoch/WorkerExecution retirement `BLOCKED` until the invariant can be represented and historical continuity mapped.

## 8. Authority Findings

| Current path | Source Claim | Current decision maker/route | Persisted result | R5 finding |
|---|---|---|---|---|
| Worker completion | Agent final report/handoff | lifecycle transition | task events + Reviewing status | Correctly treated as review input, not accepted state. |
| Agent review Pass/Fix/Continue/AskUser | external Leader runtime payload | mechanical resolver using authority-mode settings | typed “decision” + legacy event | ReviewClaim is mislabeled/conflated with a decision; no attributable authority actor/evidence. |
| AutoProceed | Pass ReviewClaim | resolver says AutoProceed | task Completed + completion event | Violates authority invariant: Agent output can cause completion without attributable AuthorityDecision. |
| AskUser | AskUser ReviewClaim | gate coordinator | NeedsUserDecision + message locator + typed gate | Escalation is valid application behavior, but response binding does not close authority. |
| Agent draft proposal | Agent structured result | draft builder | revision `ApprovedBy=User` | Attribution mismatch: user has not yet approved the stored revision. |
| Library proposal | Agent proposal Claim | explicit user Accept/Edit/Reject | applied object/timeline/ref changes + proposal state | Positive application confirmation boundary, but only a `PARTIAL_MATCH`: it is not a complete AuthorityDecision/AcceptedProjectState projection. |

Other findings:

- Fix/Continue does not create a revision-required record or lifecycle transition; the Assignment remains Reviewing.
- `NeedsUserDecision -> Reviewing` exists in lifecycle rules but no reviewed production owner was found for authority closure.
- Typed review state is written before the legacy review event in separate transactions; typed success plus legacy failure leaves recoverable policy state without its expected event identity.
- AskUser writes assignment/event/message before typed gate creation; partial failure can leave an escalation without its typed recovery owner.
- Authority-mode settings are policy snapshots, not actor attribution.

## 9. Delegated Execution Leakage

| Location | Continuity/application responsibility to retain | Delegated responsibility to separate |
|---|---|---|
| `IAgentRuntime` / Codex adapter | session create/resume/send/observe/respond/cancel transport | Workbench-owned sandbox/permission policy and provider execution strategy |
| `WorkerSessionRouter` | Assignment dispatch, bounded handoff routing, session rebinding/recovery | execution scheduling assumptions and provider-specific completion strategy |
| `WorkerExecution` | Assignment revision, Attempt/session/provenance identity | base commit, worktree, branches, workspace lifecycle, Git matching/verification |
| Leader review runtime adapter | route ReviewClaim and transient interactions | review cognition, prompt strategy, execution depth/method |
| Permission/Grant taxonomy | transient interaction forwarding or material Revision/Authority routing | runtime capability policy/enforcement |
| Interactive session launcher | optional provider connection convenience | Codex CLI/runtime UX as a product invariant |

No surrounding module is a deletion candidate merely because it contains delegated behavior. These are `SEPARATE` findings.

## 10. Manual Gateway Current Blockers

Scenario certification: create Assignment -> no Agent API -> human works externally -> bounded result/refs -> Leader/User accepts/rejects -> recover tomorrow -> next Agent takes over.

| Exact current blocker | Classification |
|---|---|
| No durable LogicalActor/Responsibility/SessionBinding distinction; no attributable AuthorityDecision or AcceptedProjectState projection. | `DOMAIN_MISSING` |
| No application service/UI admits a manual bounded Claim/Handoff and references without an Agent completion event. | `APPLICATION_MISSING` |
| Worker start requires an `ExecutionProfile`, parses provider account identity, resolves a runtime, and immediately Create/Resume/Sends. | `ADAPTER_ASSUMPTION` |
| Handoff production only follows `AgentTurnCompleted` plus the Worker JSON parser. | `ADAPTER_ASSUMPTION` |
| Review orchestration requires a persisted live Leader epoch and runtime adapter. There is no provider-independent user/Leader authority action over a manual Claim. | `APPLICATION_MISSING` + `ADAPTER_ASSUMPTION` |
| Review input requires legacy FinalReport and exactly one compatible Worker handoff task event. | `ADAPTER_ASSUMPTION` |
| Provider/account/model/runtime values are embedded in Assignment revision/execution metadata; Manual work would need fabricated adapter values. | `ADAPTER_ASSUMPTION` |
| WorkerExecution assumes worktree/branch/base-commit execution identity. | `DELEGATED_LEAK` |
| AutoProceed/review prompt construction makes delegated Agent cognition part of completion routing. | `DELEGATED_LEAK` + `DOMAIN_MISSING` |
| User response binding records a locator but cannot persist acceptance/rejection authority and accepted-state effects. | `APPLICATION_MISSING` |

The scenario does not currently pass. The blockers are architectural gaps and adapter assumptions, not proof that Workbench should implement Git, worktrees, tests, sandbox, or Agent execution itself.

## 11. Retirement Confidence Matrix

Confidence evaluates whether later retirement is discussable, not authorized.

| Candidate / prior disposition | Target direction | Confidence | Concrete blockers |
|---|---|---:|---|
| Minimal `IAgentRuntime` split | `SEPARATE` Session Gateway from optional/provider surfaces | `BLOCKED` | All live Leader/Worker/review sessions depend on the interface; target gateway and interaction routing are not implemented. |
| Codex adapter | retain as `ADAPTER` | `BLOCKED` | Only current production runtime. |
| Workbench runtime policy ownership | `DELEGATE` | `MEDIUM` | Active adapter literals and behavior compatibility; transport options still needed. |
| WorkerSessionRouter | `SEPARATE` | `BLOCKED` | Active dispatch/recovery/handoff production root; Manual Gateway and provider-neutral binding absent. |
| WorkerExecution Git/worktree fields | `DELEGATE / MIGRATE` | `LOW` | Active typed code path, historical/event compatibility, no target Attempt/SessionBinding persistence, typed removal incomplete. Zero rows alone is insufficient. |
| PermissionRequest taxonomy | later removal/delegation candidate | `MEDIUM` | No production consumer observed and zero rows, but schema migration, tests, public repository, and transient-interaction boundary must be reconciled. |
| TaskGrant taxonomy | later removal/delegation candidate | `MEDIUM` | No production consumer observed and zero rows; authority replacement semantics and migration history unresolved. |
| TaskClarification taxonomy | `SEPARATE / MIGRATE` | `LOW` | Transient versus material scope/authority questions are conflated; no replacement routing. |
| CompletionPackage | `MIGRATE` useful Claim/evidence subset | `LOW` | Zero rows/no consumer observed, but FK/migration surface and target Handoff/Evidence semantics are not designed. |
| Legacy Worker task-event session/handoff shapes | `FREEZE / MIGRATE` | `LOW` | Six real rows and active compatibility readers/migrations. |
| Leader epoch/session model | `SEPARATE / MIGRATE` | `BLOCKED` | 20 real epochs, active restore/history/boot paths, and no LogicalActor/Responsibility/SessionBinding destination. |
| Runtime rollover prompt/transcript fallback branch | later freeze/removal candidate | `MEDIUM` | No current composition caller observed, but public/tested path and compatibility behavior remain; continuity replacement not settled. |
| Continuity plans/selections | `FREEZE / MIGRATE` | `LOW` | Historical boot reader remains even though current copy has zero rows; one-time delivery semantics carry recovery behavior. |
| Legacy Memory | `FREEZE`, eventual explicit archival/migration decision | `BLOCKED` | 20 items/28 sources, distinct semantics, provenance, no valid Summary equivalence. |
| Memory synthesis runtime | freeze; later runtime-surface removal candidate | `LOW` | 16 real jobs including 13 Pending; migration/public APIs/tests and row-state meaning require resolution. |
| Daily Summary | `FREEZE` | `LOW` | Empty copy but historical continuity reader and distinct semantics remain. |
| Legacy Library runtime API | freeze; later removal candidate | `MEDIUM` | Empty legacy table and no current UI caller, but Migration 011/import coverage/malformed history remain compatibility obligations. |
| Library evolution model | retain | `BLOCKED` for retirement | Active UI, proposal governance, material and continuity readers, real rows. |
| R5-A Summary | retain | `BLOCKED` for retirement | Active canonical writer/outbox recovery; non-authoritative semantics are correct even though copied history and production semantic readers are empty. |
| Legacy review JSON/content fallback | `FREEZE / MIGRATE` | `LOW` | Real old handoff events, migrations 17/18, compatibility input builder. |
| AutoProceed | `FREEZE / MIGRATE` authority semantics | `BLOCKED` | Active completion path; no attributable AuthorityDecision/Accepted State replacement. |
| Typed review tables | `SEPARATE / MIGRATE` | `LOW` | Active production recovery contracts despite zero copied rows; ReviewClaim and authority policy are conflated. |
| `ApprovedBy=User` draft attribution | `MIGRATE` semantics | `LOW` | Active draft path; no explicit approval decision model. |

No candidate receives `HIGH` retirement confidence. Empty tables are supporting evidence, never sufficient proof.

## 12. Non-authorizing Dependency Observations

These are dependency constraints, not an implementation plan:

1. Resolve Claim versus attributable AuthorityDecision and Accepted Project State semantics before changing AutoProceed, review-state retention, or completion meaning.
2. Establish durable LogicalActor/Responsibility/Assignment/SessionBinding interpretation before retiring Leader epochs or WorkerExecution session fields.
3. Establish provider-neutral bounded manual Claim/Handoff admission before making Agent runtime optional in Assignment routing.
4. Split transient interaction forwarding from material Assignment Revision/Authority before reconciling Permission, Grant, and Clarification taxonomies.
5. Preserve task-event compatibility until real legacy Worker start/handoff rows no longer require their readers and forward migrations.
6. Resolve Legacy Memory/synthesis row semantics and provenance independently of R5-A Summary; never use Summary as an automatic migration target.
7. Prove legacy Library coverage and preserve import compatibility before removing the legacy runtime surface.
8. Separate delegated Git/worktree/review-execution responsibilities only after their remaining continuity/application fields have an explicit owner.

## 13. Explicitly Unresolved Questions

- Which actor or authority holder may accept Assignment results, and what accepted project facts are projected from that decision?
- Is Assignment completion distinct from acceptance in every workflow, including AutoProceed and user-confirmed review?
- How should existing `project_leaders` map to LogicalActor and long-lived Responsibility when one project lacks a Leader row?
- Which epoch/message history must remain navigable after SessionBinding migration, especially after one-time continuity selection consumption?
- Does any external or future composition consume the public Legacy Memory, synthesis, Daily, preference, Permission, Grant, Clarification, or CompletionPackage APIs outside the main UI root?
- How should the 13 Pending synthesis jobs be archived or interpreted without reviving frozen Agent cognition?
- Which TaskClarification categories are transient interactions, and which require Assignment Revision or Authority?
- What bounded evidence/ref shape replaces CompletionPackage and old Worker handoff payloads without making full Agent output authoritative?
- Which Library legacy import edge cases exist in production populations not represented by the inspected empty legacy table?
- Should R5-A Summary gain a user-visible/query consumer, or remain a durable recovery substrate until a later application design? This is not a retention question.

## 14. Safety / Verification

- Baseline matched required branch, commit prefix, and clean tree.
- Static audit covered all priority areas and transitive review/settings/migration/recovery dependencies discovered during tracing.
- Live DB was never initialized, migrated, checkpointed, renamed, or written through a repository.
- One untouched v11 copy was kept throughout historical inspection; a separate copy alone was migrated via the real v19 chain.
- Both inspected copies passed `quick_check`; the migrated copy had zero foreign-key violations.
- Full transcript/memory/library bodies and project paths were not placed in this report.
- Production code modified: **NO**.
- Tests modified: **NO**.
- Schema/migrations modified: **NO**.
- Implementation plan created: **NO**.
- Deletion/migration/freeze action authorized: **NO**.

Final safety result: live hash unchanged; disposable directory deleted; independent review `PASS — Critical 0, Important 0, Minor 0`. Final Git diff and commit are recorded in the task result.
