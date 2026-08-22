# R5 Repository Ownership Audit

## Result

`FIRST_PASS_STATIC_OWNERSHIP_AUDIT_COMPLETE`

This report classifies current repository responsibilities under the approved R5 Boundary Reconciliation constitution. It does not authorize implementation, migration, schema change, or deletion.

The governing rule is:

> **Classify responsibility before deciding code fate.**

An audited file may contain multiple responsibilities with different ownership. `Not CORE` never means `delete`.

## Baseline and Scope

- Architecture baseline: `ebb3b2b docs(architecture): clarify r5 boundary semantics`
- Governing spec: `docs/superpowers/specs/2026-08-22-r5-boundary-reconciliation-design.md`
- Repository branch at audit start: `master`
- Code schema: v19
- Working tree at audit start: clean
- Audit type: read-only static source, migration, test-contract, and sealed-report inspection
- Production database: not opened, read, initialized, migrated, or modified
- Production code and tests: not modified
- Full runtime call graph: deferred
- Current production row counts and compatibility reachability: deferred

The first pass concentrates on the approved high-risk areas:

```text
Workbench.Runtime / CodexAgentRuntime
WorkerSessionRouter
Worker execution / workspace / branch state
LeaderReviewRuntimeAdapter and review orchestration
Permission / TaskGrant / TaskClarification / CompletionPackage
Legacy Memory / Synthesis / Daily Summary
Project Library / Truth-related structures
Leader Session / rollover / boot persistence
R5-A Summary
```

Project identity, read-only Git inspection, migration infrastructure, and composition are included only where necessary to prevent a boundary false positive.

## Audit Method

Each row answers:

```text
Current module / table / service
        ↓
Actual responsibility
        ↓
CORE / APPLICATION / ADAPTER / DELEGATED
        ↓
Mixed boundary?
        ↓
KEEP / SEPARATE / DELEGATE / FREEZE / MIGRATE / DELETE
        ↓
Reason and compatibility impact
```

Disposition words mean:

- `KEEP` — the responsibility aligns with the approved boundary and remains necessary.
- `SEPARATE` — the current unit mixes responsibilities that need independent ownership.
- `DELEGATE` — the responsibility belongs to an Agent/provider/runtime, not Workbench.
- `FREEZE` — retain for history or compatibility but add no new dependency or feature surface.
- `MIGRATE` — preserve useful semantics or data in a later explicit target mapping.
- `DELETE` — future removal candidate only after the second-round caller, data, compatibility, and migration gates pass.

No disposition in this report is an instruction to change code now.

## Architectural Gap Map

The approved target vocabulary is not yet represented directly in production code. Static search under `src/` finds no production definitions named:

```text
LogicalActor
Responsibility
SessionBinding
AcceptedProjectState
IAgentSessionGateway
ManualSessionGateway
```

This is not a request to add those types during the audit. It means current target responsibilities are distributed across older structures:

| Target responsibility | Current partial carrier | Audit implication |
|---|---|---|
| Logical Leader identity | `project_leaders` plus `current_epoch_id` | Leader identity and physical session epoch are mixed; `SEPARATE / MIGRATE`. |
| Responsibility / Assignment | `tasks`, `task_revisions`, lifecycle status | Preserve contract and lifecycle semantics; rename or reshape only after mapping. |
| SessionBinding | `leader_session_epochs`, `worker_executions`, `AgentSession`, task events | Preserve opaque external references; separate them from execution and responsibility. |
| Bounded Handoff / Claim | `WorkerHandoff`, final-report events, completion packages, review payloads | Preserve attribution and source refs; remove full-body and execution assumptions later. |
| AuthorityDecision | typed review decisions, gates, task status, user-message bindings | Current review output and authority projection are conflated; separate Claim from Decision. |
| AcceptedProjectState | partially represented by task status and Library current views | No single target authority projection exists; do not declare current prose/library rows authoritative by name. |
| Session Gateway | `IAgentRuntime` and provider adapters | Existing interface is a strong gateway precursor but mixes optional capability discovery and transcript access. |
| Manual Gateway | no implementation | The mandatory architecture acceptance scenario is not currently proven. This is a future validation need, not audit-phase implementation. |

## Responsibility Ownership Matrix

### Project and Session Connection

| Responsibility and evidence | Current owner | Classification | Mixed? | Disposition | Reason / compatibility impact |
|---|---|---|---|---|---|
| Durable Project identity and project-open flow. `src/Workbench.Core/Projects/Project.cs`; `src/Workbench.Project/Opening/ProjectOpenService.cs:30-62` | Core Project model; Project application service; storage repositories | CORE + APPLICATION + ADAPTER | Properly layered | `KEEP` | Project identity survives every Agent and is constitutional Core. Repository and opening flow are legitimate surrounding layers. |
| Read-only Git project locator. `src/Workbench.Project/Git/GitCliInspector.cs:17-53` uses only `rev-parse`, `branch --show-current`, and `status --short`. | Git inspection adapter | ADAPTER | No | `KEEP` | This discovers repository identity and optional provenance. It is not worktree, commit, merge, or Git execution ownership. The `DELEGATED` Git rule must not delete this connection adapter. |
| Provider-neutral session contract. `src/Workbench.Runtime/Runtime/IAgentRuntime.cs:6-44`; `src/Workbench.Runtime/Agents/AgentSession.cs:5-51`; `AgentEvent.cs:3-67` | Runtime abstraction | ADAPTER | Yes | `SEPARATE / MIGRATE` | Create/resume/send/observe/respond/cancel behavior is the future Session Gateway core. Model discovery, capabilities, transcript access, tool events, and schema hints are optional adapter capabilities, not universal Core requirements. Freeze transcript as contextual scene. Existing session IDs, opaque external IDs, and event/status contracts have direct tests and require compatibility mapping. |
| Connected runtime/model catalog. `src/Workbench.Runtime/Registry/AgentRuntimeRegistry.cs:6-90`; `WorkerResource.cs:6-26` | Runtime registry and Worker-resource projection | APPLICATION + ADAPTER | Yes | `SEPARATE / MIGRATE` | Runtime availability and UI/assignment selection are application concerns. The stable `kind:provider:account` resource identity is not a LogicalActor identity. |
| Codex protocol, JSON-line process, correlation, notification mapping, and session reuse. `src/Workbench.Runtime/Providers/Codex/CodexProtocolClient.cs:6-215`; `CodexAppServerProcess.cs:6-109`; `CodexRuntimeMapper.cs:19-130` | Codex provider adapter | ADAPTER | No | `KEEP` | Legitimate replaceable connection infrastructure. Codex thread IDs remain opaque SessionBinding references, never Assignment or LogicalActor IDs. |
| Codex session/turn policy literals. `CodexAgentRuntime.cs:124-153,192-203` hard-codes read-only sandbox, on-request approval, and network disabled. | Codex adapter currently choosing runtime policy | ADAPTER + DELEGATED | Yes | `DELEGATE / FREEZE` | Enforcement and semantics belong to Codex/runtime. Do not grow these literals into a Workbench sandbox or permission domain. Any later configuration remains provider-specific adapter configuration. |
| Codex permission request/response forwarding. `CodexAgentRuntime.cs:177-184,271-321,423-500`; `AgentEvent.cs:24-54` | Codex provider adapter | ADAPTER | No | `KEEP` | Request correlation and option mapping are transport. They must never decide whether permission should be granted or change Accepted Project State. Presentation and response coordination belong to Application. |
| Local Codex connection composition. `src/Workbench.App/Services/CodexRuntimeComposition.cs:7-65` | Composition root | ADAPTER | No | `KEEP` | CLI detection, environment options, and stable local account binding must remain outside Core. |
| Open native Codex conversation UI. `src/Workbench.App/Worker/InteractiveSessionLauncher.cs:5-94` | Application-facing interface with Codex/Windows implementation | ADAPTER | Yes by location | `MIGRATE` | Move provider/OS-specific implementation behind a Codex adapter. Preserve failure when no opaque external session can be resumed. |

### Worker Routing, Assignment, and Execution

| Responsibility and evidence | Current owner | Classification | Mixed? | Disposition | Reason / compatibility impact |
|---|---|---|---|---|---|
| Assignment identity and lifecycle. `src/Workbench.Core/Tasks/AssignmentReviewModels.cs:18-39`; `src/Workbench.Storage/Tasks/AssignmentReviewStateRepository.cs:106-180` | Task/Assignment models and repository | CORE + ADAPTER | Yes in repository | `KEEP / SEPARATE` | Current `TaskId` acts as Assignment identity and the bounded lifecycle is valuable. Separate state rules from SQLite/event persistence and Leader-message side effects. `Completed` must remain an Assignment status until an explicit authority model defines any Accepted Project State projection. |
| Assignment revisions and execution recommendation. `src/Workbench.Core/Tasks/TaskModels.cs:29-159`; `TaskRevisionRepository.cs` | Core task model and storage | CORE plus legacy execution fields | Yes | `SEPARATE / MIGRATE` | Goal, scope, out-of-scope, acceptance, risk, approval, and revision chain align with Assignment. Provider/model/runtime recommendations are application dispatch metadata, not durable Responsibility semantics. |
| Worker dispatch, reuse, Handoff intake, Assignment transition, and review kickoff. `src/Workbench.App/Worker/WorkerSessionRouter.cs:251-390` | `WorkerSessionRouter` | APPLICATION | Strongly | `SEPARATE` | One method performs dispatch/rebind, connection persistence, handoff capture, lifecycle transitions, review trigger, and callback delivery. Preserve the use cases but split responsibilities. It correctly sends FinalReport to Reviewing rather than directly accepting it. It currently consumes only `AgentTurnCompleted` (`:350-353`), so transient-interaction coordination is absent rather than a permission policy. |
| Session routing persistence and legacy event projection. `WorkerSessionRouter.cs:53-249` | `IWorkerRoutingStore` and `TaskEventWorkerRoutingStore` | APPLICATION + ADAPTER | Strongly | `SEPARATE / FREEZE` | Separate current SessionBinding projection from legacy event readers. Typed `worker_executions` currently outrank stale event payloads; preserve that compatibility rule until migration. FinalReport handoff persistence intentionally keeps a source event pointer instead of duplicating report text (`:101-113,236-248`). |
| Worker handoff JSON intake. `src/Workbench.App/Worker/WorkerHandoffPayloadParser.cs:5-24` | Application parser | APPLICATION | No | `FREEZE / MIGRATE` | The current envelope has only kind/message/validation. Migrate later to bounded Claim/Handoff/refs without treating successful parsing as truth or authority. |
| Worker removal coordination. `src/Workbench.App/Worker/WorkerRemovalService.cs:12-42` | Application service | APPLICATION | Yes | `SEPARATE` | Best-effort provider stop and local binding visibility/removal are separate responsibilities. Static evidence suggests `WorkerRemoved` filters legacy events but not typed execution rows (`WorkerSessionRouter.cs:88-98,136-163`); validate in round two before changing anything. |
| Worker execution identity and lifecycle. `src/Workbench.Core/Workers/WorkerExecutionModels.cs:19-86`; `WorkerExecutionRepository.cs:8-13,19-63,94-130,190-195` | Core WorkerExecution model and storage | CORE reference fragments + APPLICATION/ADAPTER + DELEGATED | Strongly | `SEPARATE / MIGRATE / FREEZE` | Preserve Assignment/revision and opaque session/provenance references. WorkspaceCreating/Created, RuntimeStarting, BaseCommit, target/worker branch, worktree path, and matching-worktree enforcement are Agent execution ownership. Freeze the current table for compatibility; later migrate only the SessionBinding/Attempt/provenance subset and delegate the execution-specific remainder. |
| One-active-worker-execution constraint. `src/Workbench.Storage/Migrations/Migration007LeaderWorkerDelegation.cs:11,17` | `worker_executions` table/index | Application-era execution control | Yes | `FREEZE / MIGRATE` | One active execution per Project conflicts with multiple Assignment-scoped Worker actors and should not become a target invariant. Existing rows and typed recovery paths must be mapped before retirement. |
| Task event history and compatibility payloads. `src/Workbench.Storage/Workers/TaskEventRepository.cs:3-4`; review migrations 17/18 consume legacy events. | Append-only event repository | ADAPTER / compatibility history | Yes when queried as current state | `FREEZE / SEPARATE` | Retain attribution, chronology, and backfill inputs. Do not treat JSON events as current authority or extend them as the new domain model. |
| Completion report and evidence package. `src/Workbench.Storage/Workers/CompletionPackageRepository.cs:3-4`; Migration007 `:16` | Execution-owned package table | Claim/Handoff candidate + ADAPTER + DELEGATED evidence warehouse | Yes | `MIGRATE / FREEZE / DELETE` | Preserve Assignment/revision, source session, report Claim, and bounded EvidenceRefs. Do not migrate unrestricted full report/log/evidence JSON as Project truth. The unique execution relationship does not directly fit multiple Attempt/SessionBinding semantics. Delete only after data and caller gates. |
| Workbench permission taxonomy and persistent grants. `src/Workbench.Core/Workers/PermissionModels.cs:17-93`; `PermissionRepository.cs`; `TaskGrantRepository.cs`; Migration007 `:13-14` | Core permission models and storage | DELEGATED | No for policy ownership | `FREEZE / DELETE` | Merge, push, worktree, commit, build/test, credential, install, and system-setting policy belong to Agent/runtime. Static search shows repositories have no App/runtime callers, but actual rows and compatibility must be checked before deletion. Provider forwarding remains separate and retained. Applied Migration007 must remain historical. |
| Task clarification records. `src/Workbench.Core/Workers/TaskClarificationModels.cs:3-35`; `TaskClarificationRepository.cs`; Migration007 `:15` | Clarification model and storage | CORE + APPLICATION/ADAPTER | Strongly | `SEPARATE / MIGRATE / FREEZE` | Provider clarification is transient interaction. Goal/scope/acceptance/architecture/product changes require Revision or Authority. Current tests explicitly allow clarification without revision, so freeze the legacy path and migrate material records with provenance. |

### Review, Claim, and Authority

| Responsibility and evidence | Current owner | Classification | Mixed? | Disposition | Reason / compatibility impact |
|---|---|---|---|---|---|
| Build review input from Assignment revision, FinalReport event, and canonical Handoff. `src/Workbench.App/Leader/LeaderReviewInputBuilder.cs:41-121` | Application builder | APPLICATION + compatibility ADAPTER | Yes | `KEEP / SEPARATE / FREEZE` | Bounded input assembly and source binding are valid. Freeze the legacy fallback that matches duplicated message/validation text (`:74-105`); future inputs should reference stable Claim/Handoff IDs. |
| Send review request and validate structured result. `src/Workbench.App/Leader/LeaderReviewRuntimeAdapter.cs:24-86`; `LeaderReviewPayloadParser.cs:22-49` | Review runtime adapter | ADAPTER + APPLICATION | Yes | `SEPARATE / MIGRATE` | Session communication and envelope validation can become normal gateway/handoff flow. Agent output must enter as ReviewClaim, not be named an AuthorityDecision merely because schema validation passed. |
| Review method and depth strategy. `LeaderReviewRuntimeAdapter.cs:89-146` prescribes REPORT_FIRST, escalation depth, PASS/FIX/CONTINUE/ASK_USER, and action levels. | Workbench review prompt | DELEGATED | No | `DELEGATE` | Review execution strategy belongs to the reviewing Agent. Workbench may route a result-oriented review responsibility and bounded input, but must not grow a parallel review engine. |
| Review orchestration and persistence. `src/Workbench.App/Leader/LeaderReviewOrchestrator.cs:47-116` | Orchestrator, runtime registry, typed/legacy repositories | CORE + APPLICATION + ADAPTER + DELEGATED Claim source | Strongly | `SEPARATE / MIGRATE` | Preserve idempotent subject lookup, recovery, source attribution, and authority routing. Separate Agent ReviewClaim from AuthorityDecision. The current code calls the Leader runtime, resolves authority mode, inserts a typed decision, then writes a legacy event in separate operations (`:61-104`), creating both semantic and transactional coupling. |
| Typed review decision/gate history. `src/Workbench.Storage/Reviews/LeaderReviewStateRepository.cs:10-75`; Migrations014-018 | Review state repository and tables | CORE candidate + ADAPTER/history | Yes | `MIGRATE / FREEZE` | Subject, outcome, authority snapshot, resolution, and timestamps are valuable. Current records lack explicit LogicalActor/Responsibility/authority basis/evidence-reference chain and must not automatically be treated as target AuthorityDecisions. Preserve migrations and legacy readers. |
| Legacy review event projection. `src/Workbench.Storage/Tasks/AssignmentReviewStateRepository.cs:27-103` | Task event compatibility facade | ADAPTER/history | Yes when used as authority | `FREEZE / SEPARATE` | JSON `LIKE` lookup and duplicate typed/event persistence are historical compatibility, not target authority. Keep until review history and backfill reachability are proven safe. |
| Auto-complete after Agent review PASS. `src/Workbench.App/Leader/LeaderReviewAutoProceedExecutor.cs:29-56` | AutoProceed application service | APPLICATION driven by DELEGATED Claim | Yes | `FREEZE / MIGRATE` | Current code can transition Reviewing to Completed from stored review outcome/resolution. Under R5, external output is a Claim; an attributable AuthorityDecision must be a separate gate before accepted state changes. |
| AskUser gate and user-response binding. `LeaderReviewAskUserGate.cs:31-66`; `LeaderReviewUserResponseBinder.cs:21-38`; `AssignmentReviewStateRepository.cs:183-215` | Application routing plus Leader-message/session persistence | APPLICATION + ADAPTER + CORE decision candidate | Strongly | `KEEP / SEPARATE / MIGRATE` | Keep recovery, question routing, and response traceability. A transcript message ID is a locator, not the AuthorityDecision itself. Migrate user response into an attributable decision while retaining message refs as context. |

### Continuity, Leader Sessions, Library, and Summary

| Responsibility and evidence | Current owner | Classification | Mixed? | Disposition | Reason / compatibility impact |
|---|---|---|---|---|---|
| R5-A SummaryDelta semantic envelope. `src/Workbench.Storage/Memory/ProjectSummaryModels.cs:3-116` | Summary models currently located in Storage | APPLICATION contract | Minor location mixing | `KEEP / SEPARATE` | Correctly non-authoritative: it has no Accepted State or Authority fields. Move semantic contract only if later layering requires it; do not redesign or recompute entries. |
| R5-A append-only persistence and bounded query. `ProjectSummaryRepository.cs:17-158`; Migration019 `:31-56` | Summary repository/tables | ADAPTER | No | `KEEP` | `(result_id, delta_ordinal)` idempotency, payload conflict rejection, and deterministic bounded query implement the sealed R5-A contract. Source refs are locators, not proof. |
| R5-A Summary outbox and recovery. `Migration019ProjectSummary.cs:17-27`; `LeaderSummaryRecoveryService.cs:19-73`; `LeaderDraftProposalBuilder.cs:98-139,347-362` | Leader-message outbox, recovery coordinator, structured sidecar parser | APPLICATION + ADAPTER; admission cognition DELEGATED | Yes | `KEEP / SEPARATE` | Recovery mechanically applies already-produced deltas and never calls a runtime. Keep the sealed path. Clarify retention between transcript body and outbox payload later; do not infer Summary from Accepted State. |
| Persistent Leader identity and physical epochs. `src/Workbench.Storage/Leaders/StoredProjectLeader.cs:3-7`; `StoredLeaderSessionEpoch.cs:3-14`; `ProjectLeaderRepository.cs:171-244` | Leader/epoch schema and repository | CORE candidate + APPLICATION/ADAPTER | Strongly | `SEPARATE / MIGRATE` | `current_epoch_id` makes physical session epoch carry Leader identity. Preserve atomic rollover/history, but map durable Leader Responsibility and LogicalActor separately from replaceable SessionBindings. |
| Leader rollover orchestration and semantic handoff generation. `src/Workbench.App/Leader/LeaderSessionRolloverService.cs:54-213` | Application service using runtime, messages, and repositories | APPLICATION + ADAPTER + DELEGATED cognition | Strongly | `SEPARATE / DELEGATE / FREEZE` | Keep connection rollover coordination. Delegate handoff cognition to Agent and store only bounded Claim/Handoff. Freeze the fallback that reconstructs continuity from full messages. Current successor validation inherits provider/account/model/workdir, conflicting with provider replacement as binding-only change. |
| Session rotation policy. `LeaderSessionRotationStateService.cs:29-48`; `WorkbenchSettingsRepository.cs:12-39`; `ProjectSettingsRepository.cs:10-46` | Policy evaluation and settings persistence | APPLICATION + ADAPTER | Properly separable | `KEEP / SEPARATE` | Connectivity lifecycle may remain configurable. It must not own responsibility, authority, or truth. |
| Legacy Leader boot context and continuity-material plans. `LeaderBootContextBuilder.cs:18-95`; `ProjectContinuityMaterialService.cs:25-207`; `LeaderEpochContinuityRepository.cs` | Boot assembler, catalog/resolver, persisted selections | APPLICATION + ADAPTER | Strongly | `FREEZE / MIGRATE` | Old paths can inject Formal/Learned memory, Daily Summary, prior handoff, raw conversation, and Library material. They are not automatically authoritative. Migrate to thin boot from current responsibility, accepted state, unresolved authority, and bounded refs; raw replay remains explicit drill-down only. |
| Leader transcript and Summary outbox sharing one message table. `LeaderMessageRepository.cs:53-214`; `StoredLeaderMessage.cs:3-36` | Transcript persistence and recovery outbox | ADAPTER | Yes | `SEPARATE / FREEZE` | Transcript is contextual scene. Preserve source/navigation and pending Summary recovery, but do not use raw messages as default state reconstruction or authority. |
| Legacy M1.5 Memory. `ProjectMemoryRepository.cs:11-174`; `ProjectMemoryService.cs`; Migration004 `:12-16` | Formal/Learned/Candidate memory and activity persistence | Legacy ADAPTER/APPLICATION | Yes | `FREEZE / MIGRATE / DELETE` | Existing write/certify/synthesis-apply APIs conflict with the approved frozen-read-only boundary. Do not delete data. A separate Legacy Memory Migration Audit must decide whether any row maps to Claim, Summary, Authority history, or nothing. Delete only after that audit. |
| Legacy Memory synthesis. `ProjectMemorySynthesisRepository.cs:8-174`; `LeaderMemoryPolicyCoordinator.cs:23-36`; Migration005 `:17-30` | Frozen job storage and disabled coordinator | Legacy ADAPTER/APPLICATION | Yes | `FREEZE / DELETE` | Coordinator and running recovery are no-op, but queue/claim/write APIs remain callable. Prevent new dependencies; remove only after caller and data gates. Pending historical jobs must never be executed. |
| Legacy Daily Summary. `DailySummaryRepository.cs:7-133`; `IProjectMemoryApi.cs:7-26`; Migration009 `:14-42` | Mutable daily document, source rows, API | Legacy APPLICATION + ADAPTER | Yes | `FREEZE / SEPARATE / MIGRATE / DELETE` | It is not R5-A Summary. Do not convert mutable daily documents into append-only SummaryDelta automatically. Preserve compatibility reads until actual data and consumers are audited. |
| Project Library Objects/Timeline/MaterialRefs. `ProjectLibraryEvolutionModels.cs:3-107`; `ProjectLibraryEvolutionRepository.cs:95-230`; Migration011 `:19-69` | Current overview, evolution-like timeline, material refs, proposal state | CORE candidate + APPLICATION + ADAPTER | Strongly | `SEPARATE / FREEZE / MIGRATE` | This is not yet Accepted Project State: overview is mutable prose, timeline nodes are editable, and no complete Claim→AuthorityDecision provenance chain exists. Preserve user data and proposal confirmation behavior, but freeze expansion and map only through a future Truth/authority migration. |
| Library proposal workflow. `ProjectLibraryProposalService.cs:26-215` | Pending/accept/edit/reject application flow | APPLICATION | Yes with persistence placement | `KEEP / MIGRATE` | User confirmation is valuable, but proposal status `Accepted` is not a full AuthorityDecision. Preserve decision inputs and source refs while adding explicit attribution only in a later phase. |
| Legacy Library entry import. `ProjectLibraryRepository.cs:11-57`; Migration011 `:71-113` | Legacy entry persistence and runtime import into evolution tables | ADAPTER / compatibility | Yes | `FREEZE / DELETE` | Stop expanding the automatic import surface. Keep historical migration and compatibility reads. Remove runtime re-import only after second-round coverage and row reconciliation. |

## Mixed-Boundary Hotspots

The following files must not receive a single coarse KEEP or DELETE judgment:

### `WorkerSessionRouter`

```text
Session dispatch / rebind       → APPLICATION
Codex/runtime lookup            → APPLICATION over ADAPTER
Session and legacy persistence  → ADAPTER
Handoff / Claim intake          → APPLICATION
Assignment transitions         → CORE rule coordinated by APPLICATION
Review trigger                  → APPLICATION
Execution/worktree assumptions  → DELEGATED residue
```

Judgment: `SEPARATE`.

### `WorkerExecution`

```text
Assignment / revision refs      → CORE candidate
Opaque external session refs    → SessionBinding candidate
Provider/account/model metadata → ADAPTER / navigation
Runtime/workspace states        → DELEGATED residue
BaseCommit/branches/worktree    → DELEGATED
```

Judgment: `SEPARATE`, migrate the bounded connection/provenance subset, freeze the legacy row until data reconciliation.

### Leader review pipeline

```text
Review responsibility routing   → APPLICATION
Runtime communication/schema    → ADAPTER
Review method and depth          → DELEGATED
Agent review output              → Claim
Authority resolution             → CORE candidate
Task status projection           → CORE/APPLICATION
Legacy event duplication         → ADAPTER / history
```

Judgment: `SEPARATE / MIGRATE`; freeze AutoProceed until Claim and AuthorityDecision are distinct.

### Persistent Leader and rollover

```text
Logical Leader responsibility    → CORE candidate
Current epoch pointer             → mixed identity/connectivity
Session create/resume             → ADAPTER
Rollover coordination             → APPLICATION
Semantic handoff cognition        → DELEGATED
Transcript fallback               → legacy contextual scene
```

Judgment: `SEPARATE / MIGRATE`; retain atomic rollover and history.

### Project Library

```text
Potential accepted current state  → CORE candidate
Mutable overview text              → non-authoritative compatibility
Timeline/evolution history         → CORE candidate with missing authority chain
Proposal confirmation              → APPLICATION
SQL and legacy import              → ADAPTER
```

Judgment: `SEPARATE / FREEZE / MIGRATE`; never relabel current text rows as Accepted Project State without provenance and authority audit.

## Static Data and Compatibility Risk Register

This register identifies schema surfaces only. It does not assert current production row counts.

| Schema group | Static-visible data | Current judgment | Required gate before migration/removal |
|---|---|---|---|
| `projects`, `project_layouts` | Project identity, roots, layout | `KEEP` | Preserve IDs and project ownership. |
| `project_leaders`, `leader_session_epochs`, `leader_messages` | Leader pointer, provider/session epochs, handoffs, transcript, R5-A outbox columns | `SEPARATE / MIGRATE / FREEZE` | Map LogicalActor/Responsibility separately from SessionBinding; preserve history and pending Summary recovery. |
| `tasks`, `task_revisions` | Assignment contract, status, revision chain, execution recommendation | `KEEP / SEPARATE / MIGRATE` | Decide target Assignment status and move provider/profile metadata out of Core semantics. |
| `worker_executions` | revision, Git/worktree/branch, provider/session refs, runtime state | `FREEZE / MIGRATE / DELETE` | Query actual rows/callers; map only Attempt/SessionBinding/provenance; certify legacy recovery replacement. |
| `task_events` | routing, FinalReport, Handoff, review, gate compatibility JSON | `FREEZE / KEEP_HISTORY` | Identify migration/backfill readers and historical source refs before any payload retirement. |
| `permission_requests`, `task_grants` | Workbench capability policy decisions and grants | `FREEZE / DELETE` | Confirm rows and callers; decide whether history needs non-authoritative provenance export. Keep Migration007. |
| `task_clarifications` | material and transient questions mixed together | `FREEZE / MIGRATE / DELETE` | Classify actual rows into transient interaction versus Revision/Authority history. |
| `worker_completion_packages` | final report and evidence JSON per execution | `FREEZE / MIGRATE / DELETE` | Map bounded Claim/Handoff/EvidenceRefs; prevent full-log migration. |
| `task_review_decisions`, `task_review_user_gates` | typed review outcome, authority snapshot, subject, question/response locators | `FREEZE / MIGRATE` | Reconstruct Claim versus AuthorityDecision attribution and preserve migrations 14-18. |
| `project_memory_items`, `project_memory_sources`, `project_activity_events` | legacy Formal/Learned/Candidate memory and activity | `FROZEN_READ_ONLY` | Separate Legacy Memory Migration Audit with actual row/provenance inspection. |
| `project_memory_synthesis_jobs` | pending/running/completed legacy jobs | `FROZEN_READ_ONLY` | Confirm job states and callers; never execute pending historical jobs. |
| `project_daily_summaries`, `project_daily_summary_sources`, memory preferences | mutable daily documents and old continuity settings | `FROZEN_READ_ONLY` | Inspect actual rows and remaining UI/API use; never auto-convert to R5-A Summary. |
| `project_library_entries` | legacy Library rows | `FREEZE / DELETE` runtime surface | Reconcile coverage against current Library objects before removing compatibility readers/import. |
| Library objects, timeline, material refs, proposals | mutable current prose, history, refs, user proposal statuses | `FREEZE / MIGRATE` | Independent Truth/authority/provenance audit; no text-only automatic conversion. |
| `leader_epoch_continuity_plans` and selections | legacy boot material selection, possibly raw transcript/daily/handoff refs | `FREEZE / MIGRATE / DELETE` | Inspect active historical plans and consumption semantics before replacing with thin boot. |
| `project_summary_entries`, `project_summary_source_refs`, Leader-message outbox columns | R5-A append-only Summary and crash recovery | `KEEP` | Preserve idempotency and non-authoritative semantics; later define outbox retention without recomputation. |

Applied migrations are compatibility history. `MigrationRunner.cs:49-112` still upgrades through Worker, Review, and R5-A schemas. Even when a current runtime surface later qualifies for deletion, existing migrations must not be rewritten or removed; retirement requires a new forward migration after data reconciliation.

The sealed R4 report recorded production counts at an earlier time. Those numbers are historical evidence only and were not refreshed in this audit. They must not be used as current deletion proof.

## High-Confidence Ownership Decisions

### KEEP

- Project identity and project-open continuity.
- Read-only Git repository inspection as an Adapter, not Git execution.
- Codex protocol/process/session mapping and transient permission forwarding.
- Assignment identity, contract revisions, and bounded lifecycle semantics.
- Bounded Handoff/Claim attribution and source references.
- Review/gate history needed for attribution and compatibility.
- Atomic session rollover mechanics, once separated from identity and semantic synthesis.
- R5-A SummaryDelta models, mechanical append-only persistence, bounded query, and crash recovery.

### SEPARATE

- `IAgentRuntime` minimum gateway operations from optional discovery/transcript capabilities.
- `WorkerSessionRouter` dispatch, binding, handoff, lifecycle, review trigger, and persistence.
- `WorkerExecution` Assignment/session fragments from execution/worktree/Git state.
- ReviewClaim generation from AuthorityDecision recording and Accepted State projection.
- Persistent Leader responsibility from physical session epochs.
- Transcript persistence from Summary outbox recovery.
- Project Library current state, timeline, proposals, and SQL/legacy import responsibilities.

### DELEGATE

- Worktree, branch, commit, merge, build, test, sandbox, network, and execution strategy.
- Runtime permission policy, enforcement, and semantics.
- Review execution method/depth and evidence-gathering strategy.
- Semantic handoff cognition; Workbench only validates and routes bounded output.

### FREEZE

- Legacy task-event JSON authority/routing projections and content-equality fallbacks.
- Workbench permission/grant taxonomy and repositories.
- Standalone clarification path that bypasses Revision/Authority.
- Completion-package full report/evidence warehouse.
- AutoProceed from Agent review output.
- Legacy Memory, synthesis, Daily Summary, continuity-material boot, and runtime Library import.
- Current Project Library/Evolution expansion until Truth authority mapping is explicit.

### MIGRATE

- Current Task/revision semantics to explicit Assignment terminology.
- Useful WorkerExecution session/provenance subset to Attempt/SessionBinding.
- Worker reports, completion packages, and review outputs to bounded Claim/Handoff/EvidenceRef records.
- Typed review/gate history to explicit ReviewClaim and attributable AuthorityDecision representation.
- Leader identity/epoch storage to LogicalActor/Responsibility/Assignment plus replaceable SessionBinding.
- Library proposals/current/history only through an explicit authority/provenance design.
- Legacy material only after row-level classification in a separate migration audit.

### DELETE

No module or table is authorized for deletion by this first pass.

Conditional future candidates include execution-only WorkerExecution fields/models, Workbench permission/grant policy, full CompletionPackage bodies, dead legacy synthesis write/claim APIs, mutable Daily surfaces, runtime legacy Library re-import, transcript-based boot fallback, and custom review-strategy code. Each remains blocked by second-round caller, data, migration, and compatibility evidence.

## Deferred Second-Round Questions

The next audit must compare this matrix against actual use rather than redesign the architecture. It should answer:

1. Which production tables and rows currently exist, and which migrations/backfills still read them?
2. Which direct and indirect callers remain for every `FREEZE` or conditional `DELETE` candidate?
3. Does typed Worker removal have a durable SessionBinding removal path, or does `WorkerRemoved` affect only legacy event views?
4. Is `IAgentRuntime.GetTranscriptAsync` used anywhere as authority or state reconstruction rather than explicit contextual drill-down?
5. Can Manual operation produce the same Assignment → Claim/Handoff → Authority flow without a provider account, runtime registry, Codex parser, or transcript?
6. Which WorkerExecution fields are used for continuity versus merely enforcing worktree/Git/runtime assumptions?
7. Can current `Completed` be separated into Assignment fulfillment status and Accepted Project State projection without losing historical meaning?
8. Which review rows represent Agent ReviewClaims, which reflect user/Leader Authority Decisions, and is the attribution sufficient to distinguish them?
9. Are Permission, Grant, Clarification, or CompletionPackage rows present, and do any current compatibility/UI paths read them?
10. Which legacy Memory/Daily/Synthesis rows and APIs remain reachable, and what provenance would be lost by freezing or deleting them?
11. Which Library entries have already been imported, partially covered, or left unmatched against Objects/Timeline/MaterialRefs?
12. Do active Leader continuity plans contain raw conversation, Daily, handoff, or legacy memory selections, and what happens if their sources disappear?
13. Can Leader rollover replace provider/account/model while preserving the same LogicalActor and Responsibility, or do current inheritance constraints block rebinding?
14. What outbox retention is safe after R5-A Summary persistence while preserving idempotent recovery?

The second round may refine dispositions, but it must not silently implement them.

## Safety and Scope Seal

- Production code changed: `NO`
- Tests changed: `NO`
- Schema or migration changed: `NO`
- Production database accessed: `NO`
- Ownership Matrix produced: `YES`, at responsibility level
- Complete call graph claimed: `NO`
- Current production data reconciliation claimed: `NO`
- Implementation plan produced: `NO`
- Deletion authorized: `NO`

Final first-pass judgment:

> **The repository contains a viable session-routing and continuity substrate, but its target responsibilities are still distributed across execution-era identities, transcript-era continuity, and review-era authority shortcuts. Preserve the substrate, separate mixed ownership, delegate Agent capabilities, freeze legacy surfaces, and defer every destructive action until the second-round usage and data audit.**
