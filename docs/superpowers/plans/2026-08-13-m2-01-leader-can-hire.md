# M2-01 Leader Can Hire Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the approved M2-01 contract so a user can review a Leader-created Draft Task, explicitly start one isolated Worker, resume the same Worker session after interruption, and receive a `CompletedPendingReview` package containing the Worker Final Report and Workbench-collected deterministic Git evidence.

**Architecture:** Keep the five existing project boundaries. Put immutable task/execution contracts in `Workbench.Core`, durable records and repositories in `Workbench.Storage`, Git branch/worktree ownership in `Workbench.Project`, provider-neutral session execution behind the existing `IAgentRuntime`, and orchestration/UI composition in `Workbench.App`. `ExecutionStartRevision` is immutable; `CurrentAcknowledgedRevision` advances only after a user-approved revision and Worker `TaskRevisionAck`. Start uses a recoverable state machine because SQLite transactions cannot atomically include Git filesystem side effects or runtime process creation.

**Tech Stack:** C# / .NET 10 / Avalonia 12 / CommunityToolkit.Mvvm / Microsoft.Data.Sqlite / system Git CLI / provider-neutral `IAgentRuntime`

## Global Constraints

- Implement only M2-01. Do not implement Independent Auditor, `Verified`, `ReadyToMerge`, merge, auto-merge, push, same-Project parallel write Workers, replacement brains, RAG, embeddings, semantic retrieval, Worker-to-Worker collaboration, or complex Library integration.
- `Start Worker` is explicit user action. Leader Draft proposal and Draft persistence never freeze BaseCommit, create a worktree, start a runtime, or consume Worker runtime.
- At Start freeze `TaskId`, `ExecutionStartRevision`, initial `CurrentAcknowledgedRevision`, full `BaseCommit`, `TargetBranch`, ProviderAccount binding, ModelProfile, AgentRuntime/ExecutionProfile, WorkerBranch, and WorkerWorktreePath. After runtime creation succeeds persist AgentSessionId and ExternalSessionId.
- Execution state names are closed and consistent across Core, Migration007, repositories, Coordinator, reconciliation, and tests: `Preparing`, `WorkspaceCreating`, `WorkspaceCreated`, `RuntimeStarting`, `Running`, `Blocked`, `Interrupted`, `CompletedPendingReview`, `Failed`.
- A later user-approved Task Revision changes only `CurrentAcknowledgedRevision` after explicit Worker ACK. It never changes BaseCommit, TargetBranch, ProviderAccount, ExecutionProfile, Worker branch/worktree, or execution identity. A BaseCommit change requires user termination of the current execution and a new Task; no rebase.
- Main worktree must be clean at Start. Every Worker filesystem/Git operation must validate `WorkingDirectory == assigned WorkerWorktreePath` and ownership; prompts are not a boundary.
- One Active Write Worker per Project. A runtime/provider failure is recoverable or user-decided, not automatically `Failed`.
- Workbench Policy remains separate from provider/runtime approval. Hard boundaries cannot be Leader-approved; grants are capability + scope + TaskId and expire with Task lifecycle.
- Worker transcript, tool events, test attempts, and runtime logs stay in Task Archive/runtime storage. Leader Inbox queries only actionable structured events and bounded Result Digests; no periodic progress summaries.
- Worker self-report is not verification. M2-01 stops at `CompletedPendingReview`; retain branch/worktree until future Audit, Merge, or Discard.

## File / Component Map

### Existing files to modify

- `src/Workbench.Core/Projects/Project.cs` only if a task/execution identity needs a shared Project value object; otherwise keep Project unchanged.
- `src/Workbench.Storage/Database/MigrationRunner.cs` to register Migration007 after existing Migration006.
- `src/Workbench.Project/Git/GitCommandRunner.cs` and/or `src/Workbench.Project/Git/IGitInspector.cs` only where access must be extended from read-only inspection to a bounded Git command abstraction; preserve existing inspector behavior.
- `src/Workbench.Runtime/Runtime/IAgentRuntime.cs` only if a provider-neutral lifecycle primitive is genuinely missing; do not add Codex-specific contracts.
- `src/Workbench.App/Services/AppServices.cs` to compose repositories, Worktree service, Coordinator, Packet builder, Policy, Inbox, and Evidence collector.
- `src/Workbench.App/Leader/*` and `src/Workbench.App/ViewModels/Leader/*` for the provider-neutral structured Leader-to-Draft proposal boundary; preserve the existing visible Leader transcript and boot envelope privacy.
- `src/Workbench.App/ViewModels/WorkspaceViewModel.cs` and `src/Workbench.App/ViewModels/Panes/LeaderPaneViewModel.cs` for the minimal Draft/Start/Active/Completed interaction and explicit user action.
- `src/Workbench.App/Views/Panes/LeaderPaneView.axaml` (and `WorkspaceView.axaml` only if composition requires it) for the minimum UI slice.
- `tests/Workbench.Storage.Tests/Database/WorkbenchDatabaseTests.cs` for v7 migration/upgrade and foreign-key assertions.
- `tests/Workbench.App.Tests/AppTestContext.cs` and `tests/Workbench.App.Tests/Support/FakeAgentRuntime.cs` for injectable Worker orchestration tests.

### New files to create

- `src/Workbench.Core/Tasks/TaskModels.cs` (or focused records in this directory): Task, TaskRevision, Draft fields, lifecycle enums, and revision ACK contract.
- `src/Workbench.Core/Workers/WorkerExecutionModels.cs`: ExecutionStartRevision, CurrentAcknowledgedRevision, frozen execution identity, recoverable start states, Worker states, Completion package records.
- `src/Workbench.Core/Workers/PermissionModels.cs` and `TaskClarificationModels.cs`: structured requests, TaskGrant, policy capability/scope, clarification decisions.
- `src/Workbench.Storage/Migrations/Migration007LeaderWorkerDelegation.cs`.
- `src/Workbench.Storage/Tasks/TaskRepository.cs`, `TaskRevisionRepository.cs`, `WorkerExecutionRepository.cs`, and `TaskEventRepository.cs`.
- `src/Workbench.Storage/Workers/PermissionRepository.cs`, `TaskClarificationRepository.cs`, and `CompletionPackageRepository.cs`.
- `src/Workbench.Project/Git/GitWorktreeService.cs` plus public result/identity records under `src/Workbench.Project/Git/`.
- `src/Workbench.App/Worker/TaskPacketBuilder.cs`, `WorkerExecutionCoordinator.cs`, `WorkerPermissionPolicy.cs`, `LeaderInboxService.cs`, and `DeterministicEvidenceCollector.cs`.
- `src/Workbench.App/Worker/WorkerRuntimeContainmentAudit.cs` or a similarly narrow gate service, only if a provider-neutral capability probe is needed; no Windows sandbox implementation belongs here.
- `src/Workbench.App/ViewModels/Workers/TaskDraftViewModel.cs`, `ActiveWorkerViewModel.cs`, `WorkerResultViewModel.cs`, and `LeaderInboxViewModel.cs` if existing panes do not have suitable bounded state models.

### Tests to create/modify

- `tests/Workbench.Core.Tests/Tasks/TaskRevisionTests.cs`, `WorkerExecutionIdentityTests.cs`, and `PermissionModelTests.cs`.
- `tests/Workbench.Storage.Tests/Tasks/TaskRepositoryTests.cs`, `WorkerExecutionRepositoryTests.cs`, `TaskEventRepositoryTests.cs`, `PermissionRepositoryTests.cs`, and `CompletionPackageRepositoryTests.cs`.
- `tests/Workbench.Project.Tests/Git/GitWorktreeServiceTests.cs`.
- `tests/Workbench.App.Tests/Worker/TaskPacketBuilderTests.cs`, `WorkerExecutionCoordinatorTests.cs`, `WorkerPermissionPolicyTests.cs`, `LeaderInboxTests.cs`, and `DeterministicEvidenceCollectorTests.cs`.
- `tests/Workbench.App.Tests/Worker/LeaderDraftProposalTests.cs`, `WorkerRuntimeContainmentAuditTests.cs`, and `WorkerRuntimePersistenceCrashTests.cs`.
- `tests/Workbench.App.Tests/WorkerUiTests.cs` and a final opt-in `tests/Workbench.App.Tests/M2WorkerSmokeTests.cs` or equivalent integration fixture.

## Task 1: Core Task, Revision, and Permission Contracts

**Outcome:** Pure domain contracts make the approved revision and permission semantics executable without storage, Git, runtime, or UI.

**Files:** New `src/Workbench.Core/Tasks/*`, `src/Workbench.Core/Workers/WorkerExecutionModels.cs`, `PermissionModels.cs`, `TaskClarificationModels.cs`; new focused Core tests.

- [ ] Write failing tests for Draft side-effect-free fields, ReadyToStart eligibility, immutable complete Task Revision snapshots, user-approved material revision creation, and non-material clarification not creating a revision.
- [ ] Run RED: `dotnet test tests/Workbench.Core.Tests/Workbench.Core.Tests.csproj --filter FullyQualifiedName~TaskRevision`.
- [ ] Write failing tests proving `ExecutionStartRevision` is fixed, `CurrentAcknowledgedRevision` starts equal, ACK is the only advancing operation, and BaseCommit/TargetBranch/ProviderAccount/ExecutionProfile/branch/worktree never change on ACK.
- [ ] Write failing tests that the execution state contract contains exactly `Preparing`, `WorkspaceCreating`, `WorkspaceCreated`, `RuntimeStarting`, `Running`, `Blocked`, `Interrupted`, `CompletedPendingReview`, and `Failed`, with no separate ambiguous state; ambiguity is recorded as recovery detail while state remains `RuntimeStarting`.
- [ ] Write failing tests for Task-scoped grants (capability + scope + TaskId + expiry), hard-boundary rejection, PermissionRequest versus TaskClarificationRequest, and terminal Failed requiring explicit user decision while Leader may only recommend.
- [ ] Run RED: `dotnet test tests/Workbench.Core.Tests/Workbench.Core.Tests.csproj --filter "FullyQualifiedName~WorkerExecutionIdentity|FullyQualifiedName~PermissionModel"`.
- [ ] Implement the smallest immutable records/enums and guarded transition methods; do not add persistence or service abstractions here.
- [ ] Run focused Core tests, then `dotnet test tests/Workbench.Core.Tests/Workbench.Core.Tests.csproj`.
- [ ] Run `git diff --check` and commit `feat(m2): add worker task domain contracts`.

## Task 2: Migration007 and Durable Task/Execution Storage

**Outcome:** Existing databases upgrade safely from v6 and can persist the minimum task, revision, execution identity, session identity, and recoverable start state.

**Schema strategy:** Add one Migration007 after v6. Use eight tables, not one table per event subtype: `tasks`; `task_revisions`; `worker_executions` (includes frozen identity, `ExecutionStartRevision`, `CurrentAcknowledgedRevision`, WorkerBranch, WorkerWorktreePath, exact start/recovery state, AgentSessionId, ExternalSessionId, and final state); `task_events`; `permission_requests`; `task_grants`; `task_clarifications`; and `worker_completion_packages` containing Final Report and deterministic Evidence payloads. `worker_executions` must accept exactly these start/runtime states: `Preparing`, `WorkspaceCreating`, `WorkspaceCreated`, `RuntimeStarting`, `Running`, `Blocked`, `Interrupted`, `CompletedPendingReview`, `Failed`. Add foreign keys to `projects`, `tasks`, and revisions, unique `(project_id, active_write_execution)` enforcement through a partial unique index or equivalent guarded update, indexes on actionable execution/event status and `(project_id, created_at)`, and a unique `(task_id, revision_number)`. Preserve existing Project, Leader, Memory, and settings tables unchanged.

**Files:** New `src/Workbench.Storage/Migrations/Migration007LeaderWorkerDelegation.cs`; modify `src/Workbench.Storage/Database/MigrationRunner.cs`; new repositories under `src/Workbench.Storage/Tasks` and `Workers`; modify database tests.

- [ ] Write failing migration tests starting from v6 asserting user_version 7, all required tables/columns/foreign keys/indexes, project cascade behavior, and no changes to existing Leader/Memory/settings rows.
- [ ] Write failing repository tests for Draft create/update/cancel, immutable revision snapshots, project isolation, execution identity persistence, `ExecutionStartRevision`/`CurrentAcknowledgedRevision` ACK CAS, session identity persistence, and idempotent event insertion.
- [ ] Run RED: `dotnet test tests/Workbench.Storage.Tests/Workbench.Storage.Tests.csproj --filter "FullyQualifiedName~WorkbenchDatabaseTests|FullyQualifiedName~TaskRepository|FullyQualifiedName~WorkerExecutionRepository"`.
- [ ] Implement Migration007 and repository SQL using existing `WorkbenchDatabase` connection/transaction conventions. Store complete snapshots, not delta chains or task-vN files.
- [ ] Add repository tests for every exact execution state (`Preparing`, `WorkspaceCreating`, `WorkspaceCreated`, `RuntimeStarting`, `Running`, `Blocked`, `Interrupted`, `CompletedPendingReview`, `Failed`) and safe restart reads; reject spelling variants or implicit states.
- [ ] Run focused Storage tests, then `dotnet test tests/Workbench.Storage.Tests/Workbench.Storage.Tests.csproj`.
- [ ] Run `git diff --check` and commit `feat(m2): persist worker task execution state`.

## Task 3: Leader Draft Proposal and Draft Card

**Outcome:** A provider-neutral Main Leader conversation can emit a structured Draft Task proposal that Workbench validates and persists atomically as a Project-scoped Draft, without contaminating visible transcript or starting a Worker.

**Proposal contract:** The structured proposal contains `Title`, `Goal`, `Scope`, `OutOfScope`, `Acceptance`, `RiskLevel`, and `RecommendedExecutionProfile`. Workbench validates required fields, project ownership, and profile shape before one complete Draft insert. Invalid proposals produce no partial Draft. A recommendation remains editable until Start; it is never an execution freeze. User actions are Edit and Cancel only; proposal handling cannot call Start Worker.

**Files:** New `src/Workbench.App/Leader/LeaderDraftProposalBuilder.cs` or equivalent provider-neutral structured boundary; new `src/Workbench.App/ViewModels/Workers/TaskDraftViewModel.cs`; modify `src/Workbench.App/ViewModels/Panes/LeaderPaneViewModel.cs`, `WorkspaceViewModel.cs`, `LeaderPaneView.axaml`, and `AppServices.cs` only for proposal routing/card composition; repositories from Task 2; new `tests/Workbench.App.Tests/Worker/LeaderDraftProposalTests.cs`.

- [ ] Write failing tests that a Main Leader structured proposal maps to Title, Goal, Scope, OutOfScope, Acceptance, RiskLevel, and RecommendedExecutionProfile without referencing Codex types or transcript text.
- [ ] Write failing tests that valid proposals are Workbench-validated, Project-scoped, persisted as one Draft, and render a Draft Card with Edit/Cancel/Start Worker actions; RecommendedExecutionProfile remains editable before Start.
- [ ] Write failing tests that invalid/missing fields, wrong ProjectId, or invalid profile create no partial Draft, no revision row, no branch/worktree, no runtime session, and no Worker execution record.
- [ ] Write failing tests that Draft proposal/persistence uses an internal structured envelope only, leaves visible Leader messages and persisted Leader transcript free of that envelope, and never auto-starts a Worker.
- [ ] Run RED: `dotnet test tests/Workbench.App.Tests/Workbench.App.Tests.csproj --filter FullyQualifiedName~LeaderDraftProposal`.
- [ ] Implement the smallest provider-neutral proposal mapper, validation boundary, atomic Draft persistence call, and Draft Card bindings. Keep user edits/cancel idempotent and Project-scoped.
- [ ] Run focused proposal tests plus existing Leader persistence/boot tests: `dotnet test tests/Workbench.App.Tests/Workbench.App.Tests.csproj --filter "FullyQualifiedName~LeaderDraftProposal|FullyQualifiedName~LeaderPersistence|FullyQualifiedName~LeaderBoot"`.
- [ ] Run `git diff --check` and commit `feat(m2): add leader draft task proposals`.

## Task 4: Git Worktree Ownership and Deterministic Command Boundary

**Outcome:** Project layer can reject dirty main worktrees, freeze full HEAD/target branch, create uniquely owned branch/worktree from BaseCommit, validate paths, collect Git facts, and retain worktrees without exposing merge/push operations.

**Files:** New `src/Workbench.Project/Git/GitWorktreeService.cs` and result records; minimal modification to `GitCommandRunner.cs` if needed; new `tests/Workbench.Project.Tests/Git/GitWorktreeServiceTests.cs`.

- [ ] Write failing tests with `TemporaryGitRepository` for clean/dirty detection, full `rev-parse HEAD`, `branch --show-current`, branch/worktree creation from an exact BaseCommit, unique TaskId-traceable names, and ownership validation.
- [ ] Write failing tests proving commands reject main-worktree paths, reject paths outside assigned worktree, never expose merge/push APIs, and collect changed files, diff stat, status, and commit list from the Worker worktree.
- [ ] Run RED: `dotnet test tests/Workbench.Project.Tests/Workbench.Project.Tests.csproj --filter FullyQualifiedName~GitWorktreeService`.
- [ ] Implement bounded Git CLI operations with argument lists and normalized absolute paths. Keep `IGitInspector` read-only behavior intact for project opening.
- [ ] Add failure tests for branch creation, worktree creation, duplicate ownership, detached/invalid HEAD, and identifiable partial artifact cleanup versus retained diagnostic workspace.
- [ ] Run focused Project tests, then `dotnet test tests/Workbench.Project.Tests/Workbench.Project.Tests.csproj`.
- [ ] Run `git diff --check` and commit `feat(m2): isolate worker git worktrees`.

## Task 5: Worker Runtime Containment Audit / Spike Gate

**Outcome:** Before implementing real Worker write execution, establish whether the existing provider-neutral runtime and Codex App Server can enforce `Worker write boundary == assigned WorkerWorktreePath` beyond prompt instructions.

**Gate decisions:** The audit may conclude only `CONTAINMENT_SUPPORTED`, `CONTAINMENT_REQUIRES_MINIMAL_NEUTRAL_CONTRACT`, or `ARCHITECTURE_GAP`. Only the first two permit later Worker write implementation. `ARCHITECTURE_GAP` stops the implementation plan at this gate and requires an explicit architecture decision; it must not be hidden by a prompt convention or a Codex-specific Core type.

**Audit scope:** Inspect runtime sandbox/workspace capabilities, approval event coverage, cwd escape behavior, file-edit and shell-command escape behavior, whether Workbench can deny operations outside the assigned worktree, and whether the provider-neutral abstraction exposes enough policy hooks. Runtime permissions are not automatically Workbench security.

**Files:** New `src/Workbench.App/Worker/WorkerRuntimeContainmentAudit.cs` only if a narrow provider-neutral probe is needed; minimal neutral interface changes only if the audit identifies them; new `tests/Workbench.App.Tests/Worker/WorkerRuntimeContainmentAuditTests.cs` and focused Runtime tests. No Windows sandbox implementation.

- [ ] Write failing compatibility tests that exercise the existing `IAgentRuntime`/Codex App Server capability surface for assigned cwd, shell/file operations, approval events, outside-worktree attempts, and Workbench denyability.
- [ ] Write failing tests that distinguish runtime/provider approval from Workbench policy and reject any blanket auto-approval assumption.
- [ ] Run RED: `dotnet test tests/Workbench.App.Tests/Workbench.App.Tests.csproj --filter FullyQualifiedName~WorkerRuntimeContainmentAudit`; also run the relevant `dotnet test tests/Workbench.Runtime.Tests/Workbench.Runtime.Tests.csproj --filter FullyQualifiedName~CodexAgentRuntime`.
- [ ] Implement only the narrow capability probe/neutral contract required by the result; do not add Codex-specific Core types or a general Windows sandbox.
- [ ] Record the explicit gate result in a durable test/report artifact. If `ARCHITECTURE_GAP`, stop and return `ARCHITECTURE_GAP`; if `CONTAINMENT_REQUIRES_MINIMAL_NEUTRAL_CONTRACT`, require its RED/GREEN tests before Task 6.
- [ ] Run focused gate tests, `git diff --check`, and commit `docs(m2): gate worker runtime containment` (or the narrowly scoped implementation commit if a neutral contract is required).

## Task 6: Start Worker Coordinator and Cross-System Recovery

**Outcome:** Explicit Start performs the approved sequence and leaves recoverable durable states instead of claiming Running prematurely.

**Start sequence:** `clean main worktree check -> one active write Worker check -> freeze Git/runtime identity -> Preparing -> WorkspaceCreating -> create branch/worktree -> WorkspaceCreated -> durably bind workspace -> RuntimeStarting -> create Worker AgentSession -> persist AgentSessionId/ExternalSessionId -> Running`.

**Recovery rule:** SQLite transactions protect each durable transition, but never pretend to cover Git/runtime side effects. On startup, coordinator reconciliation examines `Preparing`, `WorkspaceCreating`, `WorkspaceCreated`, and `RuntimeStarting`: verify ownership and expected branch/worktree, complete a missing durable bind when unambiguous, remove only identifiable partial branch/worktree artifacts, retain a diagnosable workspace after runtime creation failure, and surface user decision for ambiguous cases. Never auto-create a replacement Worker session.

**Runtime-created / DB-persist-failed invariant:** After `CreateAgentSessionAsync` succeeds, persistence of AgentSessionId and ExternalSessionId is mandatory before `Running`. If that DB save fails, perform best-effort `StopAsync` on the created session, retain the execution in `RuntimeStarting` with explicit ambiguous-recovery detail, and never retry by creating a second Worker brain. On app crash before durable session persistence, restart reconciliation may resume the created session only if the provider-neutral runtime exposes a reliable discover/resume identity primitive. The current `IAgentRuntime` exposes `CreateSessionAsync`, `ResumeSessionAsync`, `SendAsync`, `StopAsync`, status, and transcript, but no provider-neutral discover-by-worktree/session primitive; implementation must test whether existing ExternalSessionId/session state is sufficient. If the created session cannot be reliably discovered or resumed, mark/surface `ARCHITECTURE_GAP` or ambiguous recovery and require a user decision. Do not create a replacement session.

**Files:** New `src/Workbench.App/Worker/WorkerExecutionCoordinator.cs`; modify `AppServices.cs`; new Worker coordinator tests and FakeAgentRuntime hooks.

- [ ] Write failing tests for Draft no side effects and Start requiring explicit user action.
- [ ] Write failing tests for dirty main rejection, one-active-write-Worker-per-Project, BaseCommit/TargetBranch/Profile/ProviderAccount freeze, and exact Worker worktree working directory passed to `CreateAgentSessionRequest`.
- [ ] Write failing tests for each partial failure: branch creation failure (no runtime), worktree failure (no runtime), DB bind failure after Git success (reconciliation required), runtime creation failure (not Running, workspace retained), and process-restart reconciliation.
- [ ] Write failing Coordinator/reconciliation transition tests for the exact sequence `Preparing -> WorkspaceCreating -> WorkspaceCreated -> RuntimeStarting -> Running` and the only post-start states `Blocked`, `Interrupted`, `CompletedPendingReview`, and `Failed`; reject implicit or alternate state names.
- [ ] Write failing tests for runtime session creation succeeding followed by AgentSessionId/ExternalSessionId DB save failure: best-effort StopAsync, no Running state, no second CreateSessionAsync on retry, and surfaced recoverable/ambiguous state.
- [ ] Write failing tests for a simulated app crash after runtime creation but before session persistence, restart reconciliation, reliable same-session resume when discoverable, and explicit user decision/`ARCHITECTURE_GAP` when not discoverable.
- [ ] Run RED: `dotnet test tests/Workbench.App.Tests/Workbench.App.Tests.csproj --filter FullyQualifiedName~WorkerExecutionCoordinator`.
- [ ] Implement coordinator transitions and startup reconciliation using repositories and `GitWorktreeService`; expose only explicit Start/Stop and user decision commands.
- [ ] Run focused coordinator tests, then affected Storage/Project/App suites.
- [ ] Run `git diff --check` and commit `feat(m2): coordinate recoverable worker starts`.

## Task 7: Task Packet and Bounded Project Memory

**Outcome:** One provider-neutral packet builder supplies the current contract, frozen execution identity, permission boundaries, and deterministic bounded Active Formal/Learned Memory without transcript leakage.

**Files:** New `src/Workbench.App/Worker/TaskPacketBuilder.cs`; reuse `ProjectMemoryRepository` queries and `LeaderBootContextBuilder` selection conventions without coupling Worker core to Leader boot; new packet tests.

- [ ] Write failing tests for packet fields: TaskId, applicable revision, Goal/Scope/OutOfScope/Acceptance, BaseCommit, TargetBranch, WorkerBranch, WorkerWorktreePath, frozen ProviderAccount/Profile, and permission boundaries.
- [ ] Write failing tests for Active Formal inclusion, bounded Active Learned inclusion, Formal-over-Learned same-topic suppression, deterministic ordering/whole-item selection, and exclusion of Candidate/Rejected/Superseded, all Leader transcript, all Worker history, and Library content.
- [ ] Run RED: `dotnet test tests/Workbench.App.Tests/Workbench.App.Tests.csproj --filter FullyQualifiedName~TaskPacketBuilder`.
- [ ] Implement a single builder with explicit byte/item budgets and no RAG, embeddings, semantic retrieval, or Codex-specific prompt assumptions.
- [ ] Run focused packet tests and `dotnet test tests/Workbench.App.Tests/Workbench.App.Tests.csproj --filter FullyQualifiedName~LeaderBootContextBuilder` to protect existing memory behavior.
- [ ] Run `git diff --check` and commit `feat(m2): build bounded worker task packets`.

## Task 8: Provider-Neutral Worker Session Create, Send, Interrupt, and Resume

**Outcome:** Coordinator starts and drives a Worker through existing `IAgentRuntime`, persists session IDs, and resumes the same ProviderAccount/AgentSession/ExternalSession/worktree/branch/CurrentAcknowledgedRevision.

**Files:** New `src/Workbench.App/Worker/WorkerRuntimeService.cs` only if orchestration is clearer when separated from coordinator; minimal `IAgentRuntime` change only if an existing neutral primitive is missing; modify `tests/Workbench.App.Tests/Support/FakeAgentRuntime.cs` and add Worker runtime tests.

- [ ] Write failing tests that `CreateAgentSessionAsync` receives the assigned worktree, selected ModelProfile, and frozen ProviderAccount; runtime registry lookup is account-scoped.
- [ ] Write failing tests for event streaming, interruption classification, persisted AgentSessionId/ExternalSessionId, same-session `ResumeSessionAsync`, and rejection of account/model/runtime switching during resume.
- [ ] Write failing tests that provider/runtime approval events are not converted into blanket Workbench grants and that runtime failures remain Interrupted/retryable until user decision.
- [ ] Run RED: `dotnet test tests/Workbench.App.Tests/Workbench.App.Tests.csproj --filter FullyQualifiedName~WorkerRuntime`.
- [ ] Implement the neutral adapter boundary around existing `IAgentRuntime`; do not reference `CodexAgentRuntime`, Codex thread IDs, or Codex approval semantics in Core/Storage/Coordinator contracts.
- [ ] Run focused Worker runtime tests plus `dotnet test tests/Workbench.Runtime.Tests/Workbench.Runtime.Tests.csproj`.
- [ ] Run `git diff --check` and commit `feat(m2): run and resume provider-neutral workers`.

## Task 9: Permission Policy, Task Grants, Clarifications, and Revision ACK

**Outcome:** Worker requests are structured, routed, durable, and scoped; contract questions do not masquerade as capability requests; approved revisions advance only after ACK.

**Files:** New `src/Workbench.App/Worker/WorkerPermissionPolicy.cs`; repositories from Task 2; new permission/clarification tests; coordinator integration tests.

- [ ] Write failing tests for hard-boundary capabilities (merge, push, main worktree, project-external writes/deletion, credentials/accounts, global install, system settings, irreversible actions) being policy-blocked beyond Leader approval.
- [ ] Write failing tests for low-risk reversible Task-scoped grants containing capability + scope + TaskId + expiry, blocking only dependent paths, and never executing before approval.
- [ ] Write failing tests for distinct `PermissionRequest` and `TaskClarificationRequest` persistence/events, Leader-first routing, ordinary clarification without revision, and material change requiring user-approved immutable revision.
- [ ] Write failing tests for revision ACK mismatch, duplicate ACK, successful ACK advancing only `CurrentAcknowledgedRevision`, and evidence/report revision identity after the advance.
- [ ] Run RED: `dotnet test tests/Workbench.App.Tests/Workbench.App.Tests.csproj --filter "FullyQualifiedName~WorkerPermissionPolicy|FullyQualifiedName~RevisionAck"`.
- [ ] Implement policy and coordinator transitions; keep Provider/runtime approval separate from Workbench grants and require explicit user decision for contract abandonment.
- [ ] Run focused tests plus Storage task/permission tests.
- [ ] Run `git diff --check` and commit `feat(m2): add scoped worker permissions and revision ack`.

## Task 10: Worker States, Task Events, Archive References, and Leader Inbox

**Outcome:** Durable structured events and bounded actionable attention exist without flooding Main Leader context.

**Files:** New `src/Workbench.App/Worker/LeaderInboxService.cs`, bounded view models; `TaskEventRepository` integration; modify `WorkspaceViewModel.cs` only for composition; new App tests.

- [ ] Write failing tests for Task states `Draft`/`ReadyToStart` and Execution states `Running`/`Blocked`/`Interrupted`/`CompletedPendingReview`/`Failed`, including recoverable pre-Running states and user-only terminal Failed.
- [ ] Write failing tests for durable state transition, Permission, Clarification, revision ACK/mismatch, interruption/resume, and completion events; archive references remain queryable without injecting transcript/tool chatter into Leader messages.
- [ ] Write failing tests that Inbox returns only Permission, Clarification, revision mismatch/ACK, Blocked, Interrupted, and CompletedPendingReview; ordinary progress and periodic summaries never appear.
- [ ] Write failing tests for bounded Active/Blocked/Interrupted/Pending Review queries and bounded Result Digest fields independent of total task count.
- [ ] Run RED: `dotnet test tests/Workbench.App.Tests/Workbench.App.Tests.csproj --filter "FullyQualifiedName~LeaderInbox|FullyQualifiedName~WorkerState"`.
- [ ] Implement event writes and bounded queries with status/time indexes; do not build a general event-sourcing framework or semantic retrieval.
- [ ] Run focused App tests and Storage event tests.
- [ ] Run `git diff --check` and commit `feat(m2): bound worker history and leader attention`.

## Task 11: Final Report, Deterministic Evidence, and CompletedPendingReview

**Outcome:** Worker narrative and Workbench facts form a retained completion package without verification or merge semantics.

**Files:** New `src/Workbench.App/Worker/DeterministicEvidenceCollector.cs`; completion repository/models from Task 2; coordinator integration; new evidence/report tests.

- [ ] Write failing tests for required Final Report fields: Summary, What Changed, Why, Tests Run, Known Limitations, Unresolved Issues, ExecutedAgainstRevision.
- [ ] Write failing tests that EvidenceCollector reads BaseCommit, Worker final HEAD, branch, worktree, changed files, diff stat, status, commit list, and ExecutedAgainstRevision directly from owned Git worktree/branch rather than Worker prose.
- [ ] Write failing tests proving Worker self-reported PASS is not verification, M2-01 does not rerun all tests or produce audit verdicts, and evidence collection failure does not claim CompletedPendingReview.
- [ ] Write failing tests that successful report + evidence enters `CompletedPendingReview`, retains branch/worktree, and exposes only Result Digest plus on-demand report/evidence.
- [ ] Run RED: `dotnet test tests/Workbench.App.Tests/Workbench.App.Tests.csproj --filter "FullyQualifiedName~DeterministicEvidenceCollector|FullyQualifiedName~Completion"`.
- [ ] Implement report validation, evidence collection, package persistence, and retention behavior. Do not add Verified, ReadyToMerge, merge, auto-merge, or cleanup on completion.
- [ ] Run focused tests plus Project Git and Storage completion tests.
- [ ] Run `git diff --check` and commit `feat(m2): package worker reports and git evidence`.

## Task 12: Minimal UI Slice and Explicit User Controls

**Outcome:** Existing Leader/Workspace panes expose only the approved Draft, Active Worker, attention, and CompletedPendingReview controls.

**Files:** Modify `src/Workbench.App/ViewModels/WorkspaceViewModel.cs`, `src/Workbench.App/ViewModels/Panes/LeaderPaneViewModel.cs`, `src/Workbench.App/Views/Panes/LeaderPaneView.axaml`; add Worker view models under `src/Workbench.App/ViewModels/Workers/`; tests under `tests/Workbench.App.Tests/`.

- [ ] Write failing UI tests for Draft card fields and Edit/Start Worker actions, with Start disabled until explicit user action and required preconditions pass.
- [ ] Write failing UI tests for Active Worker status, current acknowledged revision, frozen ProviderAccount/Profile, branch/worktree, Stop if supported, and actionable requests.
- [ ] Write failing UI tests for Completed Result Digest, Open Final Report, Open Evidence, retained workspace indication, and Leader Inbox Needs Attention/Results sections.
- [ ] Write failing markup tests ensuring no Kanban, agent graph, complex timeline, merge button, auto-merge action, or every-event Leader chat rendering.
- [ ] Run RED: `dotnet test tests/Workbench.App.Tests/Workbench.App.Tests.csproj --filter FullyQualifiedName~WorkerUi`.
- [ ] Implement the thinnest bindings/commands over coordinator and Inbox services. Preserve existing Leader boot, rollover, approval, and Project Memory behavior.
- [ ] Run focused UI tests, then the full App test project.
- [ ] Run `git diff --check` and commit `feat(m2): expose minimal leader worker controls`.

## Task 13: Cross-Layer Regression and Real Worker Smoke

**Outcome:** The vertical slice is proven with fakes and one opt-in real Codex Worker in a disposable safe repository; the main project remains unchanged.

**Files:** New/modify `tests/Workbench.App.Tests/M2WorkerSmokeTests.cs`, smoke fixture/support files, and documentation only if the existing test README requires an opt-in command.

- [ ] Write failing integration coverage for Leader proposal to validated Draft Card, Draft no side effects, dirty Start rejection, BaseCommit/Profile/account freeze, one active Worker per Project, containment gate result, isolation, partial-start recovery including runtime-created-before-persist failure, same-session resume, permissions, clarifications, revision ACK, bounded Inbox, no progress flooding, report/evidence separation, retained worktree, and unchanged main worktree.
- [ ] Run RED: `dotnet test tests/Workbench.App.Tests/Workbench.App.Tests.csproj --filter FullyQualifiedName~M2Worker`.
- [ ] Implement fixture wiring using the existing `AppTestContext`, temporary directories, fake runtime controls, and real Git CLI repositories; keep smoke opt-in and never touch `玉牌劫` or `立围`.
- [ ] Run all affected projects: `dotnet test tests/Workbench.Core.Tests/Workbench.Core.Tests.csproj`; `dotnet test tests/Workbench.Storage.Tests/Workbench.Storage.Tests.csproj`; `dotnet test tests/Workbench.Project.Tests/Workbench.Project.Tests.csproj`; `dotnet test tests/Workbench.Runtime.Tests/Workbench.Runtime.Tests.csproj`; `dotnet test tests/Workbench.App.Tests/Workbench.App.Tests.csproj`.
- [ ] Run the opt-in real smoke only after the containment gate is `CONTAINMENT_SUPPORTED` or `CONTAINMENT_REQUIRES_MINIMAL_NEUTRAL_CONTRACT` and its tests are green. Use an explicit environment flag and disposable `AI Game Workbench M2-01 Smoke` or new temporary M2-01 Smoke repo. Verify real Worker modifies a trivial safe file, commits on isolated branch, submits report, produces deterministic evidence, reaches CompletedPendingReview, leaves main status/HEAD unchanged, performs no merge, and retains worktree.
- [ ] Run solution-wide verification: `dotnet build AI.Game.Workbench.sln --no-restore` and `dotnet test AI.Game.Workbench.sln --no-build`.
- [ ] Run `git diff --check`, inspect all commits, and commit `test(m2): verify end to end worker delegation`.

## Migration Strategy

Current database version is v6 (`Migration006LeaderBootDelivery`). Add exactly Migration007 and register it after v6; do not execute it in this plan. Migration007 creates the minimal task/execution/event/request/completion schema described in Task 2, including foreign keys, uniqueness, and actionable indexes. `worker_executions` persists only the closed state set `Preparing`, `WorkspaceCreating`, `WorkspaceCreated`, `RuntimeStarting`, `Running`, `Blocked`, `Interrupted`, `CompletedPendingReview`, and `Failed`. Existing Project, Leader, Memory, and settings data must remain readable and isolated. Repository tests must cover upgrade from a representative v6 database and new-database initialization to v7.

## Start Recovery Strategy

Git filesystem and runtime process effects are outside SQLite transaction atomicity. Persist a durable start state before each external side effect and reconcile on restart:

`Preparing -> WorkspaceCreating -> WorkspaceCreated -> RuntimeStarting -> Running`

Post-start execution states are `Blocked`, `Interrupted`, `CompletedPendingReview`, and `Failed`. `Failed` requires explicit user abandonment of the Task contract. No other start or recovery state name is permitted.

If the main worktree becomes dirty, reject before any side effect. If branch/worktree creation fails, do not start runtime and remove only identifiable partial artifacts. If the durable workspace bind fails after Git succeeds, keep the record in a recoverable state and reconcile by validating the expected owned path/branch; do not create a second workspace. If runtime creation fails, do not mark Running and retain the owned workspace for retry or explicit cleanup.

If runtime creation succeeds but persisting AgentSessionId/ExternalSessionId fails, attempt best-effort `StopAsync` with the in-memory session and retain `RuntimeStarting` with failure details. On crash before persistence, reconciliation may resume only a reliably discovered same ProviderAccount/session identity. The current neutral API has no discover-by-worktree/session method; it must be proven sufficient by Task 6 tests or implementation must return `ARCHITECTURE_GAP`. If identity cannot be known, surface an ambiguous recovery requiring user decision. Retrying must never call `CreateSessionAsync` as a substitute for the unknown prior Worker brain.

## Execution Identity

Frozen at Start:

- TaskId
- ExecutionStartRevision
- initial CurrentAcknowledgedRevision (equal to StartRevision)
- full BaseCommit
- TargetBranch
- ProviderAccount binding
- ModelProfile
- AgentRuntime / ExecutionProfile
- WorkerBranch
- WorkerWorktreePath

Persist after runtime creation succeeds:

- AgentSessionId
- ExternalSessionId
- WorkingDirectory, validated equal to WorkerWorktreePath

Advancing field:

- CurrentAcknowledgedRevision only, after user-approved immutable revision plus successful Worker `TaskRevisionAck`.

No revision update changes the frozen identity. A BaseCommit change requires user termination and a new Task; no automatic rebase.

## Permission / Clarification

`PermissionRequest` means the Worker knows the action but lacks capability; `TaskClarificationRequest` means the Worker cannot determine the contract. They use separate records/events and separate Inbox items. Workbench Policy rejects hard boundaries before Leader routing. Leader may approve only low-risk reversible Task-scoped grants with capability, scope, TaskId, and expiry. Provider/runtime approval remains distinct and is never blanket auto-approved. Ordinary clarification is an interaction only; material contract changes require user approval, immutable new revision, Worker ACK, and only then CurrentAcknowledgedRevision advancement. Terminal abandonment/Failed also requires explicit user decision.

## Attention Bound

Worker transcript, tool events, ordinary progress, tests, and runtime logs remain in Task Archive/runtime history. Leader Inbox queries indexed actionable states only and returns bounded Result Digests; it never replays all Tasks or history at boot and never schedules periodic progress summaries. This remains bounded when the Project has thousands of Tasks and leaves room for future retrieval without implementing semantic retrieval.

## Testing Strategy

- **Core:** immutable revision pointers, execution identity, closed state names, grant boundaries, clarification/Failed authority.
- **Storage:** v7 upgrade, foreign keys, repositories, CAS ACK, all exact durable execution states/event/request/report/evidence records, project isolation.
- **Project:** clean/dirty Git, full HEAD, branch/worktree ownership, path containment, deterministic evidence, partial artifact handling.
- **Runtime:** containment audit gate; existing provider-neutral create/send/resume/interrupt; runtime-created-before-persist failures; same account/session; no Codex coupling in Worker core.
- **App:** Leader structured Draft proposal privacy/validation, coordinator sequencing/recovery, packet memory bounds, policies, Inbox bound, completion, UI controls, regression with existing Leader boot/rollover.
- **Real smoke:** opt-in disposable safe repository and real Codex Worker only after fake/integration suites and a passing/contract-extended containment gate.

## Commit Strategy

Each implementation task produces one focused, reviewable commit in order: core contracts; v7 storage; Leader Draft proposal; Git worktree isolation; containment gate; recoverable Start coordinator; packet builder; runtime session lifecycle; permissions/clarifications/ACK; events/Inbox; completion/evidence; UI; end-to-end smoke/stabilization. No squash-all-M2-01 commit and no M2-02 commit.

## Spec Coverage

Missing: none. The plan maps provider-neutral Leader proposal to validated Draft persistence/Card, Draft/Start separation, dual revision pointers, frozen Git/runtime identity, dirty and concurrency guards, worktree isolation, containment gate, runtime-created-before-persist recovery, bounded memory packet, provider-neutral runtime, permissions, clarifications, ACK, interruption/resume, event/archive/Inbox bounds, report/evidence, CompletedPendingReview retention, minimal UI, and M2-02 exclusion to concrete tasks and tests.

## Self Review

Placeholders: none.

Type inconsistencies: none. `ExecutionStartRevision` and `CurrentAcknowledgedRevision` are used consistently across Core, Storage, Coordinator, Resume, Evidence, and tests. The closed start/runtime state list is `Preparing`, `WorkspaceCreating`, `WorkspaceCreated`, `RuntimeStarting`, `Running`, `Blocked`, `Interrupted`, `CompletedPendingReview`, `Failed` everywhere.

Dependency ordering: Tasks 1–2 establish contracts/storage; Task 3 produces provider-neutral Drafts before any Start capability; Task 4 establishes Git isolation; Task 5 gates enforceable Worker containment; Task 6 composes Start recovery only after that gate; Tasks 7–11 add packet/runtime/policy/events/completion; Task 12 binds UI; Task 13 performs cross-layer and real smoke verification.

Scope leaks: none. No production implementation, test implementation, migration execution, worktree creation, Auditor, verification verdict, merge, push, or automatic replacement Worker is performed by this plan.

## Discovered but Unhandled

The existing runtime API provides provider-neutral create/resume/send/stop primitives and account-scoped registry lookup, but its current surface has no discover-by-worktree/session primitive. Task 5/Task 6 must prove that containment and runtime-created-before-persist recovery are enforceable; otherwise implementation returns `ARCHITECTURE_GAP` rather than guessing. Exact branch naming and cleanup command details remain implementation choices constrained by the ownership and recovery invariants above.
