# M2-01 - Leader Can Hire Design Specification

Status: **Approved design, M2-01 scope only**
Date: 2026-08-13
Predecessors: M1 - Leader Lives (sealed); M1.5 - Project Memory (sealed)

## 1. Status / Scope

M2-01 enables the Main Leader to prepare a development task and, after an explicit user action, hire one isolated Worker to execute it. The slice ends when the Worker submits a Final Report and Workbench records deterministic Git evidence, placing the execution in `CompletedPendingReview`.

This document is a behavioral and data contract for a later implementation plan. It does not prescribe classes, migrations, SQL, provider adapters, or coding tasks.

## 2. Product Goal

The minimum complete path is:

`User goal -> Leader creates Draft Task -> user reviews/edits -> Start Worker -> freeze ExecutionStartRevision/BaseCommit/TargetBranch/ProviderAccount/ExecutionProfile -> create branch/worktree -> start Worker AgentSession -> Worker edits and may commit -> Workbench records events/evidence -> Final Report -> CompletedPendingReview`.

Leader plans and coordinates; Worker executes. Workbench is the control plane and policy authority; the runtime is the execution engine. The main project working tree remains protected throughout.

## 3. Non-Goals

M2-01 does not include Independent Auditor, `Verified`, `ReadyToMerge`, merge or auto-merge, push, same-project parallel write Workers, automatic replacement of a Worker brain, Worker-to-Worker collaboration, task dependency graphs, semantic task retrieval, RAG, embeddings, vector databases, complex Library integration, periodic background progress summaries, or automatic approval of high-risk actions. M2-01 does not expand Project Memory: a Worker result is not directly Formal Memory.

## 4. Architecture Principles

- Reproducibility is defined by the immutable `ExecutionStartRevision`, frozen full `BaseCommit`, frozen `ProviderAccount` binding and `ExecutionProfile`, `TargetBranch`, and owned Worker branch/worktree. A later acknowledged revision changes the active contract without changing that execution identity.
- Worker history may grow in durable storage; Leader attention and boot context remain bounded.
- A Worker does not self-verify. Its report is narrative evidence from the executor, not an authoritative verification result.
- All Worker filesystem and Git writes are constrained by Workbench to the assigned worktree path; prompts are not a security boundary.
- Core task/orchestration code depends on provider-neutral `IAgentRuntime`, never directly on a Codex adapter.

### Thin architecture boundaries

These are responsibility boundaries, not mandatory class names:

- **TaskService / TaskRepository / TaskRevisionRepository:** create and edit Drafts, persist immutable contract snapshots, enforce lifecycle and revision rules, and expose bounded task views.
- **WorkspaceManager / GitWorktreeService:** inspect main-worktree cleanliness and full HEAD, create and own the Worker branch/worktree, validate path/branch ownership, and retain or explicitly clean up the workspace. They do not interpret Task semantics, build prompts, or audit results.
- **WorkerExecutionService / Coordinator:** orchestrate the Start sequence, Task Packet, runtime lifecycle, state transitions, requests, ACKs, completion, and recovery without provider-specific calls.
- **ExecutionProfile / provider-neutral `IAgentRuntime`:** select Provider, ModelProfile, and AgentRuntime and expose session create/resume primitives. Runtime executes; it does not decide Workbench permissions or Task meaning.
- **Worker Session persistence:** retain the identity needed for same-session interruption recovery.
- **PermissionPolicy / PermissionRequest / TaskGrant:** enforce hard boundaries, route structured requests, and issue expiring Task-scoped grants.
- **TaskClarification:** carry contract questions separately from capability requests and route them through Leader/user decision boundaries.
- **LeaderInbox:** project actionable, bounded attention items from the event store; it is not a transcript sink.
- **EvidenceCollector:** collect deterministic Git facts from the owned worktree and package them with the Worker report; it does not declare verification or merge readiness.

## 5. Domain Model

### Project

The repository identity and protected main worktree. At most one `Active Write Worker` execution may exist for a Project. Different Projects may each have one.

### Task

Stable `TaskId`, title, current immutable revision, lifecycle status, timestamps, and execution identity when started. A Task has one current contract revision; old revisions remain queryable.

### Task Revision

An immutable, complete contract snapshot stored in SQLite: `TaskId`, `RevisionNumber`, `Goal`, `Scope`, `OutOfScope`, `Acceptance`, `RiskLevel`, `RecommendedExecutionProfile`, `ChangeReason`, `ApprovedBy`, `CreatedAt`, and optional `PreviousRevisionId`. Revisions are snapshots, not delta chains and not task-vN files.

### Worker Execution

The execution record freezes one `ExecutionStartRevision` at Start and tracks a `CurrentAcknowledgedRevision`, initially equal to `ExecutionStartRevision`. After the user approves a new Task Revision and the Worker emits a successful `TaskRevisionAck`, only `CurrentAcknowledgedRevision` advances. The same Worker Execution, `BaseCommit`, `TargetBranch`, frozen `ProviderAccount`, `ExecutionProfile`, `WorkerBranch`, `WorkerWorktreePath`, and resumable Worker Session remain unchanged. A Running execution must have both revision pointers and all other frozen identities plus a successfully started session.

### ExecutionProfile

Three separate concepts are frozen together at Start: `Provider`, `ModelProfile`, and `AgentRuntime` (for example, GPT-5.6 via Codex). The frozen execution identity also includes the selected `ProviderAccount` binding (or `ProviderAccountId`) used by that Provider. The contract remains provider-neutral and can later represent other providers/runtimes.

### Task Packet

Structured input to the Worker containing TaskId, the applicable Task Revision, complete contract, BaseCommit, TargetBranch, WorkerBranch, WorkerWorktreePath, frozen profile and ProviderAccount binding, permission boundaries, all relevant Formal Project Memory, and a small bounded selection of relevant Learned Memory. The initial packet uses `ExecutionStartRevision`; an acknowledged revision update advances the same execution to `CurrentAcknowledgedRevision`. Project Memory authority is explicit: `Formal > Learned`; a normalized Formal topic suppresses a Learned item on that topic. Pending Candidate, Rejected, and Superseded memory are excluded. M2-01 uses deterministic bounded selection only; no semantic retrieval. Full Leader transcript, all Worker histories, all Project Memory, and all Library content are not injected by default.

## 6. Task Draft / Revision Model

The Leader may generate a Draft automatically. Draft creation has no execution side effect: it does not freeze BaseCommit, create a branch/worktree, start an Agent, or consume Worker runtime. A Draft includes TaskId, Title, Goal, Scope, OutOfScope, AcceptanceCriteria, RiskLevel, RecommendedExecutionProfile, Status, CreatedAt, and CurrentRevision. The user may view, edit, or cancel it.

`Start Worker` is the only transition that begins execution and requires explicit user action. That action simultaneously approves execution, freezes the current Task Revision as `ExecutionStartRevision`, freezes BaseCommit, TargetBranch, ProviderAccount, and ExecutionProfile, and grants Level 2 capability only within the current Task and Worker worktree. At Start, `CurrentAcknowledgedRevision == ExecutionStartRevision`. Material changes to Goal, Scope, Acceptance, or an architecture/product decision require a new user-approved Task Revision; the old snapshot is never overwritten. Non-material terminology or boundary explanations are interactions/events only and do not create a revision.

When a new revision is created, the Worker must emit an explicit `TaskRevisionAck` before it is treated as using that contract. Only a successful ACK advances `CurrentAcknowledgedRevision`; until then, execution remains governed by the previous acknowledged revision and the dependent path may be Blocked. Evidence and Final Report must identify `ExecutedAgainstRevision`. Leader clarification may explain the existing contract using Active Formal Memory (user-certified / authoritative) and Active Learned Memory (AI-synthesized / provisional), but may not silently rewrite it. Material ambiguity is escalated to the user. If a revision change is large enough to require a different BaseCommit, the user must decide to terminate the current execution and create a new Task; M2-01 does not rebase or change its BaseCommit.

## 7. Start Worker Flow

On the user's `Start Worker` action, Workbench, in one recoverable operation:

1. Reject if the Project main worktree is dirty. No stash, snapshot, auto-commit, or ignored changes are allowed. UI explains that Worker execution starts only from committed HEAD.
2. Enforce the one-active-write-Worker-per-Project rule.
3. Read and persist the complete current Git HEAD as `BaseCommit`; read and persist `TargetBranch`.
4. Freeze the selected `ProviderAccount` binding and ExecutionProfile, freeze the current Task Revision as `ExecutionStartRevision`, and initialize `CurrentAcknowledgedRevision` to the same revision.
5. Create and record a unique WorkerBranch and WorkerWorktreePath from BaseCommit, validating ownership and non-conflict with the main branch.
6. Build the Task Packet and create the provider-neutral Worker AgentSession in the assigned worktree.
7. Only after all required identities exist and the runtime starts, expose the execution as `Running`.

Start is also a Workbench-level, Task-scoped and worktree-scoped Level 2 execution grant. It approves normal in-task reading, editing in the assigned worktree, build/test commands, local Git commands, and commits to the Worker branch without per-step prompts. It never approves hard-boundary capabilities.

## 8. Git / Worktree Model

Each execution owns exactly one unique, traceable WorkerBranch and one WorkerWorktreePath created from frozen BaseCommit. The Worker may read the project, modify only its worktree, run normal build/test/local Git commands, and commit to its branch. It may not write the main worktree, merge, push, or write outside the project/worktree scope. The main branch is never the Worker checkout.

`TargetBranch` is metadata for a future merge destination (normally `master`); it is not the Worker checkout. Main branch drift during execution does not change BaseCommit. M2-01 performs no automatic rebase or merge. Branch and worktree remain retained after completion until Audit, Merge, or Discard; `CompletedPendingReview` is not cleanup.

## 9. ExecutionProfile

The Draft contains a recommendation. The user may edit it. At Start, the chosen Provider, ProviderAccount binding, ModelProfile, and AgentRuntime are persisted as an immutable execution freeze. The account cannot be silently switched after Start; any account change requires a new execution. Workbench-level approval is distinct from provider/runtime approval; any provider-specific prompt must be mapped through Workbench policy and existing Task grants, never blanket auto-approved.

## 10. Worker Session / Resume

Persistence must retain enough identity to resume: Provider, frozen ProviderAccount binding, Model, Runtime selection, AgentSessionId, ExternalSessionId, WorkingDirectory, TaskId, `ExecutionStartRevision`, and `CurrentAcknowledgedRevision`. On interruption (runtime crash, provider/network interruption, Workbench restart, or process loss), resume the same AgentSession with the same ProviderAccount, worktree, branch, BaseCommit, TargetBranch, and current acknowledged revision. If the runtime explicitly cannot resume, stop and request a user decision; do not silently create a replacement session, switch accounts, or switch brains. Cross-session continuation is out of scope.

## 11. Permission Model

Workbench Policy defines hard boundaries first. A Worker that knows the action but lacks capability emits a structured `PermissionRequest` containing TaskId, Revision, RequestedCapability, Scope, AccessMode, Reason, Risk, and RequestedDuration. Routing is `Worker -> Workbench Policy -> Leader-Decidable Zone -> Main Leader -> User if needed`.

Leader approval is permitted only for low-risk, reversible, clearly task-scoped capabilities. Every extra approval is represented as `capability + scope + TaskId` and expires when that Task lifecycle ends; no permanent Project grant is allowed. A request blocks only dependent execution paths; unrelated in-scope work may continue. The Worker must never execute first and seek approval later.

Hard boundaries that cannot be Leader-approved include merge, push, main-worktree writes, project-external writes or deletion, credentials/accounts, system settings, global software/dependency installation, irreversible destructive actions, and material product/architecture/contract changes. These require user escalation (or remain disallowed by policy).

## 12. Clarification Model

`TaskClarificationRequest` is distinct from a PermissionRequest and means the Worker cannot determine what the Task contract requires: goal ambiguity, scope/acceptance conflict, BaseCommit contradicting assumptions, or a necessary architecture decision. It routes first to Main Leader. The Leader may clarify terms and existing boundaries from the Task, Active Formal Memory (user-certified / authoritative), and Active Learned Memory (AI-synthesized / provisional), but cannot change Goal, Acceptance, materially expand Scope, or decide architecture/product direction on the user's behalf. Material changes require a new user-approved revision.

## 13. Worker State Machine

Task states: `Draft`, `ReadyToStart`. Execution states: `Running`, `Blocked`, `Interrupted`, `CompletedPendingReview`, `Failed`.

- `Draft -> ReadyToStart`: draft is complete and available for review; no execution side effects.
- `ReadyToStart -> Running`: explicit Start succeeds, including clean main worktree, frozen identity, worktree, and session.
- `Running -> Blocked`: a PermissionRequest, TaskClarificationRequest, User Decision, or required Revision ACK blocks a critical path. A pending revision does not advance `CurrentAcknowledgedRevision`. Blocked is not Failed.
- `Blocked -> Running`: the blocking decision is resolved. For a revision change, this transition requires successful `TaskRevisionAck`; it advances only `CurrentAcknowledgedRevision` and keeps the same execution identity.
- `Running -> Interrupted`: external runtime/process/provider interruption; contract is not considered failed and Resume is expected.
- `Interrupted -> Running`: same-session resume succeeds with the same ProviderAccount, worktree, branch, BaseCommit, TargetBranch, and current acknowledged revision.
- `Running -> CompletedPendingReview`: Worker submits Final Report and Workbench collects deterministic evidence.
- `Running` or `Interrupted` -> `Failed`: only after the user explicitly decides to abandon the current Task contract, whether because Acceptance is reported impossible or same-session Resume cannot continue. The Leader may recommend termination and record rationale, but cannot unilaterally make the terminal Failed decision.

No M2-01 transition reaches Verified, ReadyToMerge, or a merge operation.

## 14. Task Event Store

Durable structured Task/Worker Events record state transitions, starts, permission requests/grants, clarifications, revision ACKs/mismatches, interruptions/resumes, runtime failures, and final completion. The store is a durable event history, not a new high-scale event-sourcing framework. Full Worker transcript, tool events, test attempts, runtime events, progress, internal logs, and permission/clarification history belong in the Task Archive and are not automatically promoted to Leader context.

## 15. Leader Inbox / Attention Budget

Workbench is the message and state bus; the Leader is not a transcript bus. Only actionable structured events enter Leader Inbox: PermissionRequest, TaskClarificationRequest, revision ACK/mismatch, Interrupted, Blocked, and CompletedPendingReview. Periodic summaries (time-based, tool-count-based, or percentage progress) are prohibited.

The default attention view is bounded: Active, Blocked, Interrupted, Pending Review, and recent relevant results. Completed results appear as a Result Digest with Task, Revision, Result Status, BaseCommit, Worker HEAD, changed-file count, commit count, Worker self-report, evidence status, and next action. Full report/evidence is on demand. Leader boot and normal operation must not scan all history linearly even when Projects contain thousands of Tasks; the schema must leave room for future retrieval without implementing semantic retrieval now.

## 16. Final Report / Deterministic Evidence

The Worker submits a concise narrative Final Report containing Summary, What Changed, Why, Tests Run, Known Limitations, Unresolved Issues, and ExecutedAgainstRevision. It may self-report PASS or tests passed, but that is not verification.

Workbench independently collects deterministic evidence: BaseCommit, Worker final HEAD, Worker branch, Worker worktree, changed files, `git diff --stat`, `git status`, commit list, and `ExecutedAgainstRevision`. These facts cannot be accepted solely from Worker prose. Structured test/build reruns and verdicts are M2-02 concerns.

The CompletedPendingReview package is exactly Final Report plus Workbench Deterministic Evidence. It is input to a future Auditor, not a Verified or Done result.

## 17. Error / Recovery Semantics

| Condition | Required result |
|---|---|
| Dirty main worktree | Reject Start; no branch/worktree/runtime side effect; remain ReadyToStart. |
| Existing active write Worker | Reject Start; remain ReadyToStart. |
| Branch/worktree creation failure | Do not start runtime or mark Running; clean up only identifiable partial artifacts; retain a retryable pre-running Task state. |
| Runtime creation failure after worktree success | Do not mark Running; retain execution ownership and workspace identity for retry or explicit cleanup; do not silently delete diagnostic workspace. |
| Permission request | Enter Blocked for dependent path; route structured request; never execute without grant. |
| Clarification request | Enter Blocked for dependent path; route to Leader then user when material. |
| Revision pending ACK | Worker remains governed by the previous CurrentAcknowledgedRevision; no new-contract evidence until explicit ACK. |
| Runtime interruption | Enter Interrupted; preserve session, ProviderAccount, worktree, branch, BaseCommit, TargetBranch, ExecutionStartRevision, and CurrentAcknowledgedRevision; allow same-session Resume. |
| Resume failure | Do not auto-replace Worker or account; request user decision and allow Failed only after the user explicitly abandons the current Task contract. |
| Final evidence collection failure | Do not claim CompletedPendingReview; retain Worker result/worktree and surface a recoverable evidence error for retry or user decision. |

Start atomicity invariant: no externally visible `Running` execution exists without complete frozen configuration, including ProviderAccount and `ExecutionStartRevision == CurrentAcknowledgedRevision`, owned branch/worktree, and (once started) Worker Session identity. A later acknowledged revision may advance only `CurrentAcknowledgedRevision`; it never changes the execution identity. Recovery may reconcile durable intermediate records without inventing a different execution identity.

## 18. Security Boundaries

Workbench validates that Worker `WorkingDirectory == assigned WorkerWorktreePath` before filesystem/Git operations and enforces path ownership on every operation. Start grants Workbench capabilities only; provider/runtime approvals are separately policy-mapped. User escalation is mandatory for merge, push, main-worktree or external writes, credentials/accounts, software/global installation, system configuration, irreversible destruction, material contract changes, and unresolved architecture/product decisions. Leader cannot bypass these boundaries.

## 19. UI Minimum Slice

The UI is intentionally thin:

- Draft Task card: Goal, Scope, Acceptance, Recommended ExecutionProfile; actions Edit and Start Worker.
- Active Worker area: status, Task Revision, frozen profile, branch/worktree, Stop if supported, and requests needing attention.
- Completed area: bounded Result Digest, Open Final Report, Open Evidence.
- Leader Inbox: `Needs Attention` for Permission, Clarification, Blocked, Interrupted; `Results` for Pending Review.

Do not render a complex Kanban, multi-agent map, visual graph, timeline dashboard, or every Task event as Leader chat.

## 20. Persistence Requirements

Persist immutable Task Revisions and their approval metadata; current Task lifecycle; execution freeze (`ExecutionStartRevision`, `CurrentAcknowledgedRevision`, BaseCommit, TargetBranch, frozen ProviderAccount, and ExecutionProfile); branch/worktree ownership; Worker Session identity; Task/Worker Events; PermissionRequests and Task-scoped grants with expiry; Clarification interactions; Final Report with `ExecutedAgainstRevision`; deterministic evidence with `ExecutedAgainstRevision`; and archive references. Preserve worktrees/branches through CompletedPendingReview. Design indexes/queries so Leader Inbox and Active/Pending views are bounded by actionable status/time rather than total Task count. No task-vN files, project copies, or migration design is part of this spec.

## 21. Testable Invariants

- Draft creates no execution side effects.
- Start requires explicit user action.
- Dirty main worktree rejects Start.
- `ExecutionStartRevision`, BaseCommit, TargetBranch, ProviderAccount, and ExecutionProfile freeze at Start.
- `CurrentAcknowledgedRevision` initially equals `ExecutionStartRevision` and advances only after a user-approved revision receives Worker `TaskRevisionAck`.
- Acknowledging a new revision does not change execution, BaseCommit, TargetBranch, ProviderAccount, branch, or worktree.
- One active write Worker per Project.
- Worker writes only its assigned worktree.
- Worker cannot merge or push.
- Grants are capability-, scope-, and TaskId-bound and expire with the Task.
- Hard-boundary permissions cannot be Leader-approved.
- Material Task changes require a user-approved immutable Revision.
- Worker explicitly ACKs a new Revision before executing against it; Evidence and Final Report declare `ExecutedAgainstRevision`.
- Interrupted resumes the same Session/ProviderAccount/worktree/branch/BaseCommit/TargetBranch with the current acknowledged revision, or asks the user.
- Terminal `Failed` requires an explicit user decision to abandon the current Task contract; Leader may only recommend termination.
- Worker progress does not flood Leader context.
- Worker self-report is not verification.
- Git evidence is Workbench-collected and deterministic.
- CompletedPendingReview retains branch/worktree.
- Leader Inbox contains actionable events, not execution chatter.

## 22. M2-02 Boundary

M2-02 is expected to add the Independent Auditor, expand deterministic verification, define `PASS`/`BASIC_PASS`/`FAIL`/`INSUFFICIENT_EVIDENCE`, and establish the `ReadyToMerge` boundary. Those capabilities are deliberately absent from M2-01; this spec stops at `CompletedPendingReview`.

## Self-Review

No unresolved implementation-affecting TBD/TODO remains in this design. Naming details for branch paths, exact memory budgets, concrete persistence schema, and cleanup sequencing are implementation-plan concerns only and do not change the behavioral contract above. There is no Codex-specific dependency in the core model, no implicit auto-merge, and no same-Project parallel write Worker path.
