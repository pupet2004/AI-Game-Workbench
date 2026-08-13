# M2-01 Leader Can Hire Design Spec

## 1. Product Core

AI Game Workbench is an Agent Session Router, not a development execution engine, Git control plane, Agent sandbox, or OS security layer.

The product core is:

1. Project identity.
2. A long-lived Main Leader session for discussion, planning, judgment, audit, and Worker selection.
3. Worker sessions with clear Task, Provider/Model/Agent, status, and activity labels.
4. Durable routing of Leader prompts, Worker reports, and follow-up instructions.
5. Project Library browsing and persistence.

The product rule is: **Agent 负责工作，Workbench 负责交接.** Workbench routes and records handoffs; the selected Agent/CLI performs implementation, builds, tests, shell work, file edits, and any Git workflow requested by the Leader.

The boundary question for every proposed feature is: **“这个功能原本的 Agent 自己有吗？”** If Codex, Claude Code, OpenCode, Kimi, or another Agent already provides Git, worktrees, shell, file editing, tests, commit, planning, code understanding, or native conversation lifecycle, Workbench does not reimplement it by default. Workbench focuses on cross-Agent, cross-Session, and cross-Project capabilities: session aggregation, prompt/handoff routing, Worker status and labels, window management, Project persistence, Project Library indexing, and aggregation of available Agent resources.

The product core is compressed to `Project`, `AgentSession`, `Handoff`, and `LibraryEntry`. Existing `Task`, `TaskRevision`, `WorkerExecution`, and related Storage records may remain as internal reliability substrate; they are not the center of the user's product model. Do not delete them or roll back migrations.

## 2. Scope And Boundaries

Workbench knows the current Project, Leader Session, Worker Sessions, Task labels, routing relationships, timestamps, and resumable session identities. It does not independently choose a model, infer whether to reuse a Worker, validate a commit, interpret a Worker report, manage worktrees, enforce filesystem containment, stage or commit Git, merge, or provide OS/provider security.

The Leader Skill may decide when to open or reuse a Worker, how to phrase the Prompt, whether the Agent should create a worktree, whether to commit, when to close a Session, and what Library note to record. The user may edit the Leader recommendation and must explicitly confirm `Start Worker`.

Provider/runtime approvals remain provider concerns. Workbench records approval and routing events but does not treat them as a security boundary.

## 3. Completed History

M2-01 Tasks 1, 2, and 3 remain completed historical work. Task 2's WorkerExecution, TaskRevision, Permission, TaskGrant, TaskEvent, CompletionPackage, and Evidence-related storage are retained and remain covered by existing tests.

Those records are now an **Optional Reliability Layer**. They may preserve interruption, routing, report, or evidence detail, but they do not have to drive every Worker handoff and do not block the MVP session loop. No migration rollback or storage deletion is part of this revision.

The Task 4A containment spike is closed as architectural exploration after product simplification. Do not continue Windows sandbox, `writableRoots`, ACL, `CreateProcessAsUserW`, or Git-containment work in this M2-01 route.

## 4. Domain Model

### Project

The selected repository/project identity and its Library.

### Leader Session

A persistent, pinned Main Leader Agent Session scoped to a Project, with Leader Skill and Project Context. It produces structured Draft proposals and receives routed Worker reports. It is not a special execution engine.

### Worker Session

A normal Agent Session selected by the Leader Skill and confirmed by the user. Work manages it as a card with only Task/Label, Agent/Model, Status, and LastActiveAt. A Worker Session may continue the same Task revision, perform Leader-audited rework, or handle tightly related follow-up work when the Leader explicitly selects reuse.

### Task

A lightweight Project-scoped label for what the Leader and a Worker are discussing or executing. It minimally carries Title, Goal/Prompt context, LeaderSession, WorkerSession, status, and timestamps. Its value is label, routing relation, and history, not control of how an Agent works.

Use the existing Task lifecycle contract and map it to the target UI meanings `Draft`, `Working`, `WaitingForLeader`, `Completed`, `Interrupted`, and `Closed` without adding a new enum in this documentation revision.

### Task Revision

Existing immutable revision snapshots remain valid where already implemented. A revision identifies the contract context the Leader and Worker are discussing; it is not a Workbench execution lock. Material changes can be recorded as a new user-approved revision, but M2-01 no longer requires a BaseCommit freeze, worktree freeze, deterministic evidence, or a Workbench-managed execution identity.

### ExecutionProfile

The Leader recommends Provider, frozen ProviderAccount binding, ModelProfile, Reasoning, and AgentRuntime. The user may edit the recommendation before Start. Once a Worker Session starts, the selected account/session identity is persisted for resume; Workbench must not silently switch account or session.

## 5. Draft And Start

The Main Leader may emit an internal structured Draft proposal containing at least `Title`, `Goal`, `Scope`, `OutOfScope`, `Acceptance`, `RiskLevel`, and `RecommendedExecutionProfile`. Workbench validates and persists one complete Project-scoped Draft and renders a Draft Card. The structured envelope does not pollute the visible Leader transcript. Invalid proposals create no partial Draft and never start a Worker.

`Start Worker` is the only execution-side-effect action and always requires explicit user confirmation. After confirmation, Workbench either creates a new Agent Session or reuses the exact Worker Session named by the Leader proposal. It sends the Leader Prompt to that session and records the Task-to-Session relation. WorkingDirectory is the Project directory unless the Leader/Agent manages a worktree in its own Prompt and runtime.

Start does not create a Worktree, freeze BaseCommit, enforce OS containment, stage/commit Git, collect deterministic Git evidence, merge, or automatically select a Provider/Model/Worker.

## 6. Handoff And Routing

Workbench is the routing record point so every handoff retains sender, receiver, Task, Worker Session, timestamp, and active/closed status. The normal flow is:

`Leader conversation -> Draft proposal -> user confirmation -> Worker Session -> Worker report -> Leader conversation`.

The Work area shows the Worker transcript and session card. A Leader follow-up is routed to the same session only when the Leader Skill explicitly selects reuse; otherwise Workbench opens the specified new session. Workbench does not judge whether a report, commit hash, test count, or diff is correct.

Worker completion may be ordinary Agent text such as `TASK_COMPLETE`, a commit hash, tests, and a report. V1 records the report and routes it back to the Leader without requiring Workbench verification. Existing Evidence/Completion infrastructure remains available as optional reliability detail and must not block this loop.

## 7. Session States And Reliability

Use existing session/task contracts and labels; the target semantics are Working, WaitingForLeader, Completed, Interrupted, and Closed. A runtime create followed by persistence failure receives best-effort `StopAsync`. After restart, an unclear Worker is marked Interrupted/Unknown and left to user or Leader choice: resume, reuse, close, or create a new session. Do not implement session discovery or exactly-once replacement prevention in this simplification.

Leader may recommend termination. A formal terminal abandonment decision for a Task remains an explicit user decision; Workbench does not independently declare the work failed.

## 8. Project Library

Project Library is a Project detail/knowledge index. By default it shows project overview, category, topic, time, and short summary; clicking a category, time, or entry opens the full detail/source. It is not a large knowledge graph, full-history replacement, automatic project auditor, or automatic semantic retrieval system.

Leader/Worker Skills create concise high-quality notes, for example: “听牌茶盏正式改为连续听牌累计机制。实现 commit abc123。” Workbench stores, classifies, timestamps, links, displays, and searches those notes; intelligence and synthesis belong to the Skills.

## 9. Worker Windows And Removal

Clicking a Worker card opens an independent Agent Conversation Window containing that Worker Session's complete conversation. The Work column remains a card manager; it does not expand into an inline full transcript. The unified session shell may host Codex, Claude Code, OpenCode, Kimi, Mimo, DeepSeek harness, or another AgentRuntime without reimplementing the Agent's native abilities.

Clicking the independent window's `×` only closes the foreground window. It does not delete the Session, end the Task, remove the Worker card, or delete the source Agent conversation. The Worker remains in the background and its card remains in Work; clicking the card reopens the same Session window.

Permanent Workbench removal is explicit: Worker card -> right-click -> “从 Workbench 移除” -> confirmation. It removes the Worker from the current Project list, stops Workbench's current run/connection when applicable, and deletes or detaches Workbench's own Session management record. By default it does not delete the source Agent's conversation/thread. V1 does not implement permanent deletion of source Agent conversations.

Keep these dimensions distinct: Task/Agent status (`Working`, `Completed`, `Interrupted`, etc.), Window state (`Open`, `Hidden`), and Workbench management (`Present`, `Removed`). `Completed != Removed`, `Close Window != Close Session`, and `Removed != Delete Source Conversation`.

## 10. UI Minimum

Keep the three-column layout: `LEADER | WORK | PROJECT LIBRARY`.

- Leader: one pinned complete Main Leader Agent Session with Leader Skill and Project Context.
- Work: Worker Session cards showing Task/Label, Agent/Model, Status, and LastActiveAt; card activation opens the independent Worker Conversation Window.
- Project Library: project overview plus category, topic, time, summary, and drill-down to detail/source.

Do not add an Agent graph, org chart, workflow dashboard, enterprise control plane, or complex multi-agent chat.

## 11. Testable Invariants

- Draft persistence is Project-scoped, atomic, side-effect-free, and never starts a Worker.
- `Start Worker` requires explicit user confirmation.
- The selected ProviderAccount and Worker Session identity are retained for resume; no silent account/session switch.
- Task and Worker Session are distinct; reuse occurs only when explicitly selected by the Leader Skill.
- Every routed message identifies sender, receiver, Task, Worker Session, and timestamp.
- Worker reports are routed to the Leader without Workbench claiming verification.
- Runtime persistence failure gets best-effort stop; uncertain restart state is surfaced for user/Leader decision.
- Worker status and Task label remain understandable after restart.
- Library records retain category, topic, time, summary, and source reference.
- Workbench does not require worktree creation, BaseCommit freeze, containment, Git commit/evidence, or merge for the core handoff loop.
- Closing a Worker window preserves its Session, Task, card, and source conversation.
- Removing a Worker is explicit and does not delete the source Agent conversation.
- Task/Agent status, Window state, and Workbench management state are independent.

## 12. Current M2 Boundary

This revision is design-only. Current M2 remains Draft -> Confirm -> exactly one Worker -> real smoke. Do not begin independent Worker windows, right-click removal, Agent Resource Discovery, login detection, or source conversation deletion in this M2 task. Independent Worker windows are a subsequent small UI task.

## 13. M2-02 Boundary

M2-02 and future reliability work may add auditing, verification, merge readiness, richer evidence, or optional workspace conveniences. None is required for the M2-01 session-routing MVP.

## Self-Review

No core route requires Workbench-created worktrees, BaseCommit freeze, containment gates, Workbench Git commit/evidence, Workbench model selection, or Workbench judgment of Library content. Task 4A is recorded as stopped exploration, not a pending blocker. No `TBD` or `TODO` remains.
