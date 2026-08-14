# M3 Leader Review Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an automatic, recoverable Assignment-level Leader Review loop that turns explicit Worker Final Reports into PASS, FIX, CONTINUE, or ASK_USER actions and a concise user brief.

**Architecture:** Use `TaskId` as `AssignmentId`, `tasks.status` as the current recovery projection, and `task_events` as the append-only journal. Leader Skill performs review judgment; a small App coordinator serializes automatic Leader turns and routes outcomes under a resolved global/Project authority policy. Do not add a Review table.

**Tech Stack:** .NET 10, C#, Avalonia MVVM, SQLite/Microsoft.Data.Sqlite, xUnit, existing AgentRuntime abstractions.

## Global Constraints

- Review unit is one approved Task/Assignment, never a Worker Session.
- Final Report is an explicit structured Worker handoff; no heuristic completion detection.
- Outcomes are exactly PASS, FIX, CONTINUE, ASK_USER; action levels are exactly L1, L2, L3.
- Global authority default is BALANCED with nullable Project override; L3 always asks the user.
- Leader Review judgment belongs to the Leader Skill. Workbench owns state, routing, recovery, settings, and thin UI.
- Do not add `leader_reviews`, review evidence/notes/memory/summary tables, a workflow engine, CI, diff analysis, or Agent scoring.
- Do not remove Worker sessions/cards on PASS and do not implement Final Report retention slimming before Slice G.
- Every implementation step follows RED -> GREEN and preserves Project isolation/idempotency.

---

### Slice A: Assignment Review State Foundation

**Files:**
- Modify: `src/Workbench.Core/Tasks/TaskModels.cs`
- Create: `src/Workbench.Core/Tasks/AssignmentReviewModels.cs`
- Modify: `src/Workbench.Storage/Tasks/TaskRepository.cs`
- Create: `src/Workbench.Storage/Tasks/AssignmentReviewStateRepository.cs`
- Create: `src/Workbench.Storage/Migrations/Migration012LeaderReviewState.cs`
- Modify: `src/Workbench.Storage/Database/WorkbenchDatabase.cs`
- Test: `tests/Workbench.Core.Tests/Tasks/AssignmentReviewModelsTests.cs`
- Test: `tests/Workbench.Storage.Tests/Tasks/AssignmentReviewStateTests.cs`

**Interfaces:**
- Produces: `LeaderReviewOutcome`, `LeaderReviewActionLevel`, expanded `TaskLifecycleStatus`, and transactional Assignment state/event operations consumed by all later slices.

- [ ] **Step 1: Write failing Core tests** proving outcomes/action levels are separate, TaskId remains AssignmentId, PASS does not imply Worker removal, and valid lifecycle edges are `ReadyToStart -> Working -> Reviewing -> Completed`, `Reviewing -> Working`, and `Reviewing -> NeedsUserDecision -> Reviewing`.
- [ ] **Step 2: Run** `dotnet test tests/Workbench.Core.Tests/Workbench.Core.Tests.csproj --filter FullyQualifiedName~AssignmentReviewModelsTests` and confirm RED due to missing models/states.
- [ ] **Step 3: Add minimal enums/value records** in `AssignmentReviewModels.cs`; extend `TaskLifecycleStatus` with `Working`, `NeedsLeaderDecision`, `Reviewing`, `NeedsUserDecision`, and `Completed`. Do not combine outcome, action level, and authority.
- [ ] **Step 4: Write failing Storage tests** for Project-scoped compare-and-set transitions, an atomic task-event + status update, duplicate event idempotency, and no mutation of another Assignment.
- [ ] **Step 5: Add Migration012** by rebuilding the `tasks` status CHECK constraint while copying every existing row unchanged; add no Review table.
- [ ] **Step 6: Implement `AssignmentReviewStateRepository`** with transaction-scoped methods such as `TryStartWorkingAsync`, `TryRecordFinalReportAsync`, `TryRecordDecisionAsync`, and `GetRecoveryStateAsync`. Require expected prior state and stable event ids.
- [ ] **Step 7: Run focused Core/Storage tests**, then `git diff --check`.
- [ ] **Step 8: Commit** `feat(review): add assignment review state foundation`.

### Slice B: Explicit Final Report to Review Trigger

**Files:**
- Modify: `src/Workbench.App/Worker/WorkerSessionRouter.cs`
- Create: `src/Workbench.App/Worker/WorkerHandoffPayloadParser.cs`
- Modify: `src/Workbench.App/ViewModels/Panes/LeaderPaneViewModel.cs`
- Modify: `src/Workbench.App/ViewModels/Leader/ProjectLeaderSessionManager.cs`
- Modify: `src/Workbench.App/Services/AppServices.cs`
- Test: `tests/Workbench.App.Tests/Worker/WorkerSessionRoutingTests.cs`
- Test: `tests/Workbench.App.Tests/Worker/LeaderDraftProposalTests.cs`

**Interfaces:**
- Consumes: Assignment transitions from Slice A.
- Produces: `WorkerHandoffKind.NeedsLeaderDecision|FinalReport` carrying AssignmentId, RevisionId, WorkerSessionId, message, validation summary, and source time.

- [ ] **Step 1: Write failing parser/router tests**: only structured `FinalReport` starts Review; `NeedsLeaderDecision` routes to Leader and leaves the Assignment non-reviewing; ordinary completion text is not guessed to be a Final Report.
- [ ] **Step 2: Run** `dotnet test tests/Workbench.App.Tests/Workbench.App.Tests.csproj --filter 'FullyQualifiedName~WorkerSessionRoutingTests|FullyQualifiedName~LeaderDraftProposalTests'` and confirm the current generic handoff behavior fails the new contract.
- [ ] **Step 3: Add the structured Worker handoff envelope** and include Task revision identity in `WorkerStartRequest`, persisted session linkage, and handoff payload.
- [ ] **Step 4: Replace the generic completion callback** with explicit kind routing. For FinalReport, commit the event and `Working -> Reviewing` before notifying the review coordinator. For NeedsLeaderDecision, persist/route the intermediate handoff without Review.
- [ ] **Step 5: Add restart and duplicate-trigger tests**: a committed Reviewing Assignment is discoverable after restart; duplicate callback/event id schedules no second Review.
- [ ] **Step 6: Run focused App/Storage tests and `git diff --check`**.
- [ ] **Step 7: Commit** `feat(review): trigger review from explicit final reports`.

### Slice C: Structured Leader Review Decision

**Files:**
- Create: `src/Workbench.App/Leader/LeaderReviewPromptBuilder.cs`
- Create: `src/Workbench.App/Leader/LeaderReviewPayloadParser.cs`
- Create: `src/Workbench.App/Leader/LeaderReviewCoordinator.cs`
- Modify: `src/Workbench.App/ViewModels/Leader/ProjectLeaderSessionManager.cs`
- Modify: `src/Workbench.App/Services/AppServices.cs`
- Test: `tests/Workbench.App.Tests/LeaderReviewPayloadParserTests.cs`
- Test: `tests/Workbench.App.Tests/LeaderReviewCoordinatorTests.cs`

**Interfaces:**
- Consumes: latest Final Report recovery state from Slices A/B and the persisted Main Leader session.
- Produces: `LeaderReviewDecision` with AssignmentId, FinalReportEventId, Outcome, ActionLevel, concise summaries, optional Worker instruction, optional user question/options, Important Note, and brief.

- [ ] **Step 1: Write failing prompt tests** requiring report-first input, Assignment acceptance, reported validation, Project/Worker source references, risk-based depth instructions, and no default exhaustive-code-review command.
- [ ] **Step 2: Write failing parser tests** for all four outcomes, L1/L2/L3 validation, required FIX/CONTINUE instruction, required ASK_USER question, bounded brief, mismatched Assignment/report rejection, and malformed/oversized payload rejection.
- [ ] **Step 3: Run focused tests** and confirm RED for missing builder/parser.
- [ ] **Step 4: Implement builder/parser** as provider-neutral structures. Keep review depth transient and never persist chain-of-thought.
- [ ] **Step 5: Write failing coordinator tests** for automatic review, serialized access with ordinary Leader turns, resume of Reviewing after restart, missing Leader runtime, unavailable Worker source, and decision persistence before action.
- [ ] **Step 6: Implement `LeaderReviewCoordinator`** with a per-Project gate and startup recovery scan. Use the existing Main Leader session/runtime; do not create a second auditor Agent.
- [ ] **Step 7: Run focused plus Leader persistence/session tests**.
- [ ] **Step 8: Commit** `feat(review): add structured leader assignment judgment`.

### Slice D: Authority Settings and Resolution

**Files:**
- Create: `src/Workbench.Core/Leaders/LeaderAuthorityMode.cs`
- Create: `src/Workbench.Core/Leaders/LeaderReviewAuthorityResolver.cs`
- Modify: `src/Workbench.Storage/Settings/WorkbenchSettingsRepository.cs`
- Modify: `src/Workbench.Storage/Settings/ProjectSettingsRepository.cs`
- Create: `src/Workbench.Storage/Migrations/Migration013LeaderAuthority.cs`
- Modify: `src/Workbench.Storage/Database/WorkbenchDatabase.cs`
- Modify: `src/Workbench.App/ViewModels/SettingsViewModel.cs`
- Modify: `src/Workbench.App/Views/SettingsView.axaml`
- Modify: `src/Workbench.App/ViewModels/Panes/LibraryPaneViewModel.cs`
- Test: `tests/Workbench.Core.Tests/Leaders/LeaderReviewAuthorityResolverTests.cs`
- Test: `tests/Workbench.Storage.Tests/Settings/LeaderAuthoritySettingsTests.cs`
- Test: `tests/Workbench.App.Tests/SettingsViewModelTests.cs`

**Interfaces:**
- Produces: effective `LeaderAuthorityMode` and `LeaderAuthorityAction` (`AutoAct`, `NotifyAndContinue`, `AskUser`) consumed by Slice E.

- [ ] **Step 1: Write failing matrix tests** for PASS, L1, L2, L3, and ASK_USER across Cautious/Balanced/Autonomous; assert every L3 maps to AskUser.
- [ ] **Step 2: Implement the pure resolver** with default Balanced and no custom policy.
- [ ] **Step 3: Write failing repository tests** for global default/save, nullable Project override, invalid values, Project isolation, and restart.
- [ ] **Step 4: Add the generic global key and Migration013 Project column**, then implement repositories following the existing rotation policy pattern.
- [ ] **Step 5: Write failing UI tests**, then add thin global Settings controls and Project override controls without redesigning Settings/Library.
- [ ] **Step 6: Run Core/Storage/App focused tests and commit** `feat(review): configure leader review authority`.

### Slice E: FIX, CONTINUE, and ASK_USER Routing

**Files:**
- Modify: `src/Workbench.App/Leader/LeaderReviewCoordinator.cs`
- Modify: `src/Workbench.App/Worker/WorkerSessionRouter.cs`
- Modify: `src/Workbench.Storage/Tasks/AssignmentReviewStateRepository.cs`
- Modify: `src/Workbench.App/ViewModels/Panes/LeaderPaneViewModel.cs`
- Test: `tests/Workbench.App.Tests/LeaderReviewRoutingTests.cs`
- Test: `tests/Workbench.Storage.Tests/Tasks/AssignmentReviewStateTests.cs`

**Interfaces:**
- Consumes: structured decision from Slice C and effective authority action from Slice D.
- Produces: idempotent Worker directive routing or persistent NeedsUserDecision state.

- [ ] **Step 1: Write failing routing tests** for PASS completing only the Assignment; L1 auto action; Cautious L2 asking; Balanced L2 notify-then-continue; Autonomous L2 auto; and all L3 asking.
- [ ] **Step 2: Add failure-window tests** for crash after decision/before Worker send, crash after send/before local acknowledgement, unavailable/removed Worker Session, and duplicate coordinator wakeup.
- [ ] **Step 3: Implement pending-action sequencing**: keep Reviewing until the deterministic directive is durably recorded as routed, then atomically move to Working. Include directive id in the Worker prompt for idempotent retry.
- [ ] **Step 4: Implement ASK_USER sequencing**: atomically persist question/options and NeedsUserDecision; record one user answer; return to Reviewing for a new Leader decision. Do not reuse NeedsLeaderDecision because its actor/direction differs.
- [ ] **Step 5: Verify FIX/CONTINUE keep TaskId and normally WorkerSessionId**, while an intent-changing answer creates a user-approved TaskRevision before resuming.
- [ ] **Step 6: Run focused routing, Worker, Leader persistence, and restart tests; commit** `feat(review): route leader review outcomes`.

### Slice F: Minimal Work Status, User Brief, and Details

**Files:**
- Modify: `src/Workbench.App/ViewModels/Panes/WorkPaneViewModel.cs`
- Modify: `src/Workbench.App/Views/Panes/WorkPaneView.axaml`
- Modify: `src/Workbench.App/ViewModels/Panes/LeaderPaneViewModel.cs`
- Modify: `src/Workbench.App/Views/Panes/LeaderPaneView.axaml`
- Modify: `src/Workbench.Storage/Tasks/AssignmentReviewStateRepository.cs`
- Test: `tests/Workbench.App.Tests/Worker/WorkPaneViewModelTests.cs`
- Test: `tests/Workbench.App.Tests/LeaderReviewUiTests.cs`

**Interfaces:**
- Consumes: Assignment status, current Final Report/decision projection, and routed action state.
- Produces: `Working`, `Reviewing`, `Needs User Decision`, `Completed` card labels and exactly-once concise Leader brief.

- [ ] **Step 1: Write failing Work card tests** proving Assignment status is independent from Agent session/card presence and PASS preserves the Worker card.
- [ ] **Step 2: Write failing brief tests** for compact Worker/outcome/validation/Git/risk/next-step content, optional Important Note, no long file/command list, and no raw structured payload.
- [ ] **Step 3: Write failing idempotency tests** for atomic ordinary `leader_message` + `LeaderReviewBriefPublished` event and restart without duplicate brief.
- [ ] **Step 4: Implement the transactional publisher** using deterministic event id and conditional message insert in one SQLite transaction; add no report table.
- [ ] **Step 5: Add minimal status labels, collapsible ephemeral details, and focused ASK_USER surface**. Details reconstruct from current report/decision and do not create persistence.
- [ ] **Step 6: Run App visual/view-model, Storage, accessibility, and restart tests; commit** `feat(review): show minimal assignment review status`.

### Slice G: Final Report Retention Slimming After Seal

**Prerequisite:** Execute only after Slices A–F pass full solution tests and the real Review loop is sealed.

**Files:**
- Modify: `src/Workbench.Storage/Tasks/AssignmentReviewStateRepository.cs`
- Modify: `src/Workbench.App/Worker/WorkerSessionRouter.cs`
- Test: `tests/Workbench.Storage.Tests/Tasks/FinalReportRetentionTests.cs`
- Test: `tests/Workbench.App.Tests/LeaderReviewRetentionTests.cs`

**Interfaces:**
- Consumes: terminal/acknowledged Review cycle and brief publication marker.
- Produces: retained thin outcome/status/time/session/source metadata with disposable duplicate report body.

- [ ] **Step 1: Re-audit actual post-M3 report copies** in Agent transcript, task events, leader messages, and completion packages; document which production copy is authoritative before editing retention behavior.
- [ ] **Step 2: Write failing retention tests**: full report remains before decision/action/brief completion; only the consumed duplicate body is slimmed afterward; source Agent transcript is untouched; open/restart works; pending FIX/ASK_USER never loses required payload.
- [ ] **Step 3: Write failing source-removal tests** ensuring the UI warns about report availability and offers Daily Summary/Library Proposal elevation without making promotion a Review-completion gate.
- [ ] **Step 4: Implement the smallest cleanup** supported by the re-audit. Do not add report archives/snapshots or delete Worker transcripts, Tasks, Assignments, or completion compatibility rows.
- [ ] **Step 5: Run focused retention tests plus all M3, Memory/Continuity, Worker, Storage, App, Core, Runtime, solution, build, and `git diff --check`**.
- [ ] **Step 6: Commit** `refactor(review): slim consumed final report payloads`.

## Final Seal

- [ ] Run `dotnet test AI.Game.Workbench.sln --no-restore -m:1` to completion.
- [ ] Run `dotnet build AI.Game.Workbench.sln --no-restore -m:1` and require 0 warnings/0 errors.
- [ ] Run `git diff --check`.
- [ ] Verify no Review table, workflow graph, CI/diff engine, Worker removal on PASS, or pre-seal retention cleanup was introduced.
- [ ] Perform one opt-in real smoke: explicit Final Report -> automatic Review -> each authority route -> exactly one brief -> restart recovery.

## Self-Review

All spec requirements map to a Slice: identity/state (A), explicit trigger (B), structured judgment (C), authority (D), action routing/recovery (E), UI/brief (F), and delayed retention slimming (G). Type names are consistent across slices. No placeholder, generic “add tests,” Review persistence subsystem, or premature retention implementation remains.
