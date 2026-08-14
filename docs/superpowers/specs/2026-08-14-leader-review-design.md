# M3 Leader Review Design Spec

## 1. Product Semantics

Leader Review closes the loop `Worker Assignment -> Worker Final Report -> Leader Review -> PASS / FIX / CONTINUE / ASK_USER -> routed action -> concise user brief`.

The review asks whether the current Assignment meets its approved acceptance criteria with enough confidence to continue. It is not a second implementation Agent, exhaustive code audit, CI platform, or proof that no bug exists. The Leader Skill owns judgment. Workbench owns identity, durable handoff, authority resolution, routing, recovery state, and minimal UI.

The default strategy is **report first, performance validation, risk-based escalation**. The Leader starts with the Final Report, reported verification, visible product behavior, and only the Git/test/smoke evidence needed for the Assignment. It reads code or expands verification only for high-risk changes, contradictions, failing evidence, missing evidence, scope violations, anomalous results, or an explicit user request. Review depth is independent of authority to act.

## 2. Existing Architecture Audit

### Assignment, Task, and revision

`TaskId` is already the strongest minimal Assignment identity. A confirmed Leader Draft creates one Project-scoped `Task` with immutable revisions containing Goal, Scope, OutOfScope, Acceptance, RiskLevel, and ExecutionProfile. Those fields are exactly the contract a review evaluates.

The current status enum only has `Draft`, `ReadyToStart`, and `Cancelled`; production does not yet advance it when a Worker starts or reports. `TaskRevision` identifies the approved contract version, not a review attempt. A Worker Session is a reusable transport/session and must not become the review unit.

`worker_executions.ExecutionId` is not the V1 Assignment identity. Although its schema includes `CompletedPendingReview`, the current Draft-confirmation route bypasses `WorkerExecutionRepository` and starts `WorkerSessionRouter` directly. Requiring it would revive worktree/base-commit execution-control semantics previously made optional.

### Final Report path

`WorkerSessionRouter` currently treats every non-empty `AgentTurnCompleted` final text as a `WorkerHandoff`. It persists a `WorkerToLeaderHandoff` task event containing `ProjectId`, `TaskId`, `WorkerSessionId`, status, text, and time, then invokes `ProjectLeaderSessionManager.IngestWorkerHandoffAsync`. The manager wraps the report into an assistant `leader_message`. No explicit distinction exists between `NeedsLeaderDecision` and `FinalReport`, the event has no Assignment revision, and the callback has no durable review trigger.

`worker_completion_packages` can bind a report to Execution/Task/Revision, but production does not write it. It remains compatibility/reliability storage and is not made mandatory by M3.

### Worker state and UI

`TaskEventWorkerRoutingStore` reconstructs Worker cards from `WorkerSessionStarted`, `WorkerToLeaderHandoff`, and `WorkerRemoved`. `WorkPaneViewModel` projects Agent session status to Working, Completed, Interrupted, or Closed. Completion currently describes the session turn, not Assignment closure. Removing a Worker is already independent from task/session completion and remains so.

### Leader ingest

The Main Leader is a persisted Project-scoped session. Worker reports are copied into `leader_messages`, but there is no automatic Leader reasoning turn, structured review response, action routing, or idempotent brief publication. M3 replaces the wrapper-copy behavior for Final Reports with a durable review trigger; intermediate Leader-decision handoffs continue to route without starting Review.

### Settings and recovery

Leader session rotation establishes the correct settings pattern: a global value in `workbench_settings`, a nullable Project override in `project_settings`, and a service that resolves the effective policy. Leader Authority reuses this pattern with default `BALANCED`.

Task events are append-only and `INSERT OR IGNORE` by event id. Tasks, events, sessions, Leader epochs/messages, and settings all live in the same SQLite database, so narrow transactional state transitions can provide crash recovery without a review table or distributed workflow engine.

## 3. Assignment Review Unit

V1 defines one approved `Task` as one Assignment. Therefore:

- `AssignmentId == TaskId`.
- `TaskRevisionId` identifies the acceptance contract reviewed.
- `WorkerSessionId` identifies the reusable Agent conversation that performed it.
- a Worker Session may be linked to Assignment A, then B, then C through separate Task events; PASS never removes or closes that session.
- FIX and CONTINUE keep the same Assignment/Task and same approved revision unless the requested action changes approved intent. A material intent change is L3 and requires user approval/new revision before work resumes.

The routing record must add `AssignmentId`, `RevisionId`, and a handoff kind. New Worker output uses a structured envelope with exactly two relevant kinds:

- `NeedsLeaderDecision`: an intermediate question/conflict. It routes to Leader and Worker can continue; it never starts Review.
- `FinalReport`: the Worker declares the current Assignment ready for review. It atomically records the report and moves the Task to `Reviewing`.

Workbench does not infer completion from prose, inactivity, tool use, or session status.

## 4. Review Input and Depth

The Leader Skill receives:

1. Assignment title and selected TaskRevision contract.
2. the exact Final Report event and Worker Session source reference.
3. the Worker's reported validation/evidence.
4. effective Leader Authority.
5. available visible project/runtime state.

The prompt instructs the Leader to begin at `ReportOnly`. It may choose `TargetedValidation` or `DeepInspection` only for the escalation reasons in section 1. Depth is transient request context, not a durable permission or database field. Authority never forces deeper review.

## 5. Outcomes and Action Levels

Review outcome and action level are separate values.

### Outcomes

- `PASS`: the Assignment meets acceptance. Persist `Completed`; keep Worker Session/card.
- `FIX`: a concrete defect must be corrected. Route a bounded correction to the same Assignment and normally the same Worker Session.
- `CONTINUE`: current work may be valid but is not at the acceptance endpoint. Route the missing work under the same Assignment.
- `ASK_USER`: approved intent cannot determine the next action. Persist `NeedsUserDecision` and present one focused question with finite options when possible.

### Action levels

- `L1 LocalFix`: no change to approved goal, design, scope, or data semantics; small, local correction or missing acceptance case.
- `L2 TaskRework`: substantial rework or rollback may be needed, but the same approved intent still determines the answer.
- `L3 DecisionRequired`: action changes product/design/data semantics, expands scope, adds meaningful cost or irreversible external effects, conflicts with a user decision, or has multiple reasonable directions not resolvable from approved intent.

If `Can Leader act without changing approved intent?` is yes, choose L1/L2. Otherwise choose L3 and `ASK_USER`. A large rollback is L2 by size alone, not L3.

The structured decision contains `AssignmentId`, `FinalReportEventId`, `Outcome`, `ActionLevel`, `AcceptanceSummary`, `KeyValidation`, optional `WorkerInstruction`, optional `UserQuestion/Options`, optional `ImportantNote`, and a concise `UserBrief`. It does not contain a permanent chain-of-thought or exhaustive file/command log.

## 6. Leader Authority

`LeaderAuthorityMode` has `Cautious`, `Balanced`, and `Autonomous`; global default is `Balanced`, with a nullable Project override. Custom policies are out of scope.

| Outcome / level | Cautious | Balanced | Autonomous |
|---|---|---|---|
| PASS / no action | Auto close + brief | Auto close + brief | Auto close + brief |
| FIX or CONTINUE / L1 | Auto act | Auto act | Auto act |
| FIX or CONTINUE / L2 | Ask user before action | Notify user, then continue without blocking | Auto act; brief records material rework |
| Any L3 | ASK_USER | ASK_USER | ASK_USER |
| ASK_USER | Ask user | Ask user | Ask user |

Destructive external actions, meaningful cost increase, scope expansion, and product/data design changes remain L3 in all modes. `Autonomous` is not unlimited authority.

## 7. Minimal Durable State Machine

Persist only states required for UI and restart:

`Draft -> ReadyToStart -> Working -> Reviewing -> Completed`

Branches:

- `Working -> NeedsLeaderDecision -> Working` for an intermediate Worker question. This is not Review.
- `Reviewing --FIX/CONTINUE authorized--> Working` after the follow-up instruction is durably routed.
- `Reviewing --ASK_USER or authority gate--> NeedsUserDecision -> Reviewing` after the user's answer is durably recorded.
- existing `Cancelled` remains terminal by explicit user action.

No workflow engine is introduced. `tasks.status` is the current projection. `task_events` is the ordered recovery journal and stores only routing facts needed to resume:

- Assignment started/Worker session linked.
- Needs-Leader-Decision received/resolved.
- Final Report received.
- Review decision produced (thin outcome plus required pending instruction/question).
- Worker action routed.
- User decision received.
- concise brief published.

Review cycles are derived per Assignment from Final Report events. A new Final Report after FIX/CONTINUE starts the next cycle for the same Assignment.

## 8. Transaction and Idempotency Boundaries

### Final Report

Persist `WorkerFinalReportReceived` and compare/update `tasks.status: Working -> Reviewing` in one SQLite transaction. Only the transaction winner schedules Review. Duplicate callback/event ids are ignored. A second report while the same cycle is already Reviewing does not create another review.

### Review

At startup and after each committed Final Report, find Tasks in `Reviewing`. If no decision event exists for the latest report, run Leader Review. Persist the structured decision event before acting. A crash after receiving the report or during review therefore resumes from the event log without losing the report.

### FIX / CONTINUE

Keep the Task `Reviewing` while an authorized Worker instruction is pending. Route an instruction carrying deterministic `AssignmentId + FinalReportEventId + ReviewDecisionEventId`. When the routing record commits, transition to `Working` in the same database transaction. Restart retries an unacknowledged pending instruction; the Worker Skill treats the deterministic directive id idempotently. This is bounded at-least-once delivery, not a distributed exactly-once claim.

### ASK_USER

Persist the review decision and transition to `NeedsUserDecision` atomically. Restart reconstructs the same focused question/options. A user answer is a Project/Assignment-scoped event; accepting it atomically returns the Task to `Reviewing`, where the Leader decides whether to route work or complete.

### User brief

The brief is an ordinary `leader_message`. Publishing it and inserting a deterministic `LeaderReviewBriefPublished` event occur in one SQLite transaction; retries insert neither twice. Full report/review details are not copied into the brief.

### Missing sessions

If the Worker card is removed or the external Agent Session is unavailable, the Assignment remains recoverable. PASS can still complete from existing evidence. FIX/CONTINUE becomes `ASK_USER` or waits for an explicit new/reused Worker choice; Workbench does not silently create a replacement session.

## 9. Persistence Decisions

No `leader_reviews`, `review_evidence`, `review_notes`, `review_memory`, or `review_summary` table is added.

The minimum schema evolution is:

- extend `TaskLifecycleStatus` and the `tasks.status` CHECK constraint for `Working`, `NeedsLeaderDecision`, `Reviewing`, `NeedsUserDecision`, and `Completed`;
- add nullable `leader_authority_mode` to `project_settings`; global authority uses a new generic `workbench_settings` key;
- enrich task-event payload contracts. Existing `task_events` remains the journal.

Review details are constructed while active from the TaskRevision, Final Report, current project evidence, and Leader decision. After the action/brief is durable, only thin outcome/action/time/session/source references and any still-needed routing instruction remain. The final user brief is a normal `leader_message`.

## 10. User Experience

### Work card

The Assignment-derived label shows `Working`, `Reviewing`, `Needs User Decision`, or `Completed`. Worker Session presence/removal and Agent status remain separate. PASS changes only the Assignment status.

### Leader area

Final Report arrival automatically starts Review when the Leader is available; no Start Review button exists. The default visible output is compact:

- what the Worker delivered;
- PASS/FIX/CONTINUE/ASK_USER;
- key validation;
- commit/hash when supplied;
- a material risk or optional Important Note;
- next action.

`Review details` is an ephemeral/collapsible projection of the current Final Report plus Leader action. It is not a new persistence subsystem. After restart, details can be reconstructed while the source report event is retained.

For `ASK_USER`, the UI shows why approved intent is insufficient and a bounded question/options. It does not expose internal reasoning or a generic workflow dashboard.

## 11. Final Report Future Retention Boundary

Before Review completes, the full Final Report must remain durably accessible in `WorkerFinalReportReceived` and the source Agent Session. After the review decision, routed action, and user brief are durable, that Review cycle becomes the natural future slimming boundary:

- retain AssignmentId, outcome, action level, timestamps, WorkerSessionId, FinalReport source reference, validation summary, commit/hash when material, and brief publication marker;
- prefer the complete report body in the original Agent Session;
- do not permanently copy every report into `leader_messages` or `worker_completion_packages`;
- before a user explicitly removes a source Agent Session, surface any long-lived value through Daily Summary (`what happened and why`) or Library Proposal (`what the project is/how it evolved`). Do not block review completion on that promotion.

Physical Final Report slimming is a later Slice only after the Review loop is stable. M3 design does not implement retention cleanup.

## 12. Non-Goals

- automatic code-review engine, diff analyzer, test runner, CI, or proof of correctness;
- workflow graph, issue tracker, multi-reviewer approval, Agent scoring, RAG, or knowledge base;
- parallel Worker scheduling, cost optimization, or automatic model selection;
- automatic Worker removal/session deletion;
- Final Report, Assignment, transcript, or completion-package cleanup in the initial Review slices;
- storing Leader reasoning or creating a Review history subsystem;
- UI redesign beyond status, concise brief, optional details, and ASK_USER prompt.

## 13. Testable Invariants

- `TaskId` is the Assignment review identity; revision and Worker Session are references, not substitutes.
- only an explicit structured Final Report changes Working to Reviewing.
- Needs Leader Decision never triggers Review.
- each Final Report cycle produces at most one durable review decision and one visible brief.
- PASS completes only the Assignment and preserves Worker Session/card/source conversation.
- FIX/CONTINUE reuse the Assignment; a new revision requires user approval when approved intent changes.
- all L3 actions ask the user under every authority mode.
- restart resumes Reviewing, pending Worker action, or NeedsUserDecision without duplicate action/brief.
- no long-lived Review table or detailed reasoning record exists.

## Self-Review

The design contains no placeholders. Assignment identity, Final Report binding, PASS closure, FIX/CONTINUE reuse, ASK_USER distinction, authority resolution, persistence, crash recovery, UI, and future retention have one explicit interpretation. Review judgment remains in the Leader Skill; Workbench remains the handoff and recovery layer.
