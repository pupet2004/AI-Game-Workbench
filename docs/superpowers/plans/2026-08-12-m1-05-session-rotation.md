# M1-05 Session Rotation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver M1-05 session rotation in phases: M1-05A policy/settings, M1-05B safe handoff and real rollover, then M1-05C history UI.

**Architecture:** Keep policy and workday evaluation in `Workbench.Core`, with a caller-supplied `TimeZoneInfo` for deterministic local-day semantics. Add SQLite-backed settings repositories through Migration003, then compose their resolved policy into the existing App service, project workspace, and Leader pane. M1-05B will execute rollover/handoff; M1-05C will display archived epochs.

**Tech Stack:** .NET 10, Avalonia 12, CommunityToolkit.Mvvm, Microsoft.Data.Sqlite, xUnit.

## Global Constraints

- M1-05A only: evaluate whether a session is due; never create/end/archive an epoch, invoke a provider, or create a handoff.
- Global policy defaults to `Auto`; project override uses nullable policy (`null` = inherit global).
- Workday due means different machine-local calendar date **and** idle duration at least six hours.
- Do not modify Core project layout ratios, the F1 layout-persistence path, Runtime, or SQLite data already owned by M1-04.
- Migration003 must be transactional, upgrade user version 2 to 3, preserve all existing data, and reject unknown persisted policy values.
- Keep 6 hours a domain constant; do not expose it as a user setting.

---

### Task 1: Define pure rotation policy and evaluation

**Files:**
- Create: `src/Workbench.Core/Leaders/LeaderSessionRotationPolicy.cs`
- Create: `src/Workbench.Core/Leaders/LeaderSessionRotationReason.cs`
- Create: `src/Workbench.Core/Leaders/LeaderSessionRotationEvaluation.cs`
- Create: `src/Workbench.Core/Leaders/LeaderSessionRotationEvaluator.cs`
- Test: `tests/Workbench.Core.Tests/Leaders/LeaderSessionRotationEvaluatorTests.cs`

**Interfaces:**
- Produces `LeaderSessionRotationPolicy { Auto, Ask, ManualOnly }`.
- Produces `LeaderSessionRotationEvaluation(LeaderSessionRotationPolicy EffectivePolicy, bool IsDue, LeaderSessionRotationReason? Reason)`.
- Produces `Evaluate(LeaderSessionRotationPolicy policy, DateTimeOffset? lastActiveAt, DateTimeOffset? endedAt, DateTimeOffset now, TimeZoneInfo timeZone)`.

- [ ] **Step 1: Write failing evaluator tests** for same-day 12h, next-day 30m, 5h59m, exact 6h, 11h, two days, no epoch, ended epoch, immutability, and fixed-zone local-day boundaries.
- [ ] **Step 2: Run Core tests** and verify failures reference missing policy/evaluator types.
- [ ] **Step 3: Implement the smallest pure evaluator** using `TimeZoneInfo.ConvertTime` and `TimeSpan.FromHours(6)`.
- [ ] **Step 4: Run Core tests** and verify all evaluator cases pass.

### Task 2: Persist global policy and nullable project override

**Files:**
- Create: `src/Workbench.Storage/Migrations/Migration003LeaderRotationSettings.cs`
- Modify: `src/Workbench.Storage/Database/MigrationRunner.cs`
- Create: `src/Workbench.Storage/Settings/WorkbenchSettingsRepository.cs`
- Create: `src/Workbench.Storage/Settings/ProjectSettingsRepository.cs`
- Test: `tests/Workbench.Storage.Tests/Settings/LeaderRotationSettingsRepositoryTests.cs`
- Test: `tests/Workbench.Storage.Tests/Database/WorkbenchDatabaseTests.cs`

**Interfaces:**
- `GetLeaderSessionRotationPolicyAsync()` returns `Auto` when no row exists.
- `SaveLeaderSessionRotationPolicyAsync(LeaderSessionRotationPolicy policy)` writes a validated enum string.
- `GetLeaderSessionRotationPolicyOverrideAsync(Guid projectId)` returns nullable policy.
- `SaveLeaderSessionRotationPolicyOverrideAsync(Guid projectId, LeaderSessionRotationPolicy? policy)` clears to inherit when null.

- [ ] **Step 1: Write failing storage tests** for v2→v3 preservation, version 3, global default/round-trip, override round-trip/clear/cascade/reopen, and unknown global/project values rejected.
- [ ] **Step 2: Run storage tests** and verify the missing migration/repositories cause RED failures.
- [ ] **Step 3: Implement transactional Migration003** with `workbench_settings` and `project_settings` and foreign-key cascade.
- [ ] **Step 4: Implement typed repositories** with strict enum parsing and no silent fallback for persisted invalid values.
- [ ] **Step 5: Run storage tests** and verify GREEN.

### Task 3: Resolve effective policy and compose M1-04 epoch state

**Files:**
- Create: `src/Workbench.Core/Leaders/LeaderSessionRotationPolicyResolver.cs`
- Create: `src/Workbench.App/Leader/LeaderSessionRotationStateService.cs`
- Modify: `src/Workbench.App/Services/AppServices.cs`
- Modify: `src/Workbench.App/ViewModels/Leader/ProjectLeaderSessionManager.cs`
- Test: `tests/Workbench.Core.Tests/Leaders/LeaderSessionRotationPolicyResolverTests.cs`
- Test: `tests/Workbench.App.Tests/LeaderSessionRotationStateServiceTests.cs`

**Interfaces:**
- `Resolve(globalPolicy, projectOverride)` returns override when non-null, otherwise global.
- State service reads setting repositories and current epoch, evaluates without changing it, and returns no-due when current epoch is absent/ended.

- [ ] **Step 1: Write failing resolver and App tests** for all global/override combinations, opening due projects without creating/archiving epochs or calling Runtime, and Auto/Ask/Manual evaluation presentation.
- [ ] **Step 2: Run selected tests** and verify RED.
- [ ] **Step 3: Implement resolver and read-only state service**, passing the App `TimeProvider` and `TimeZoneInfo.Local`.
- [ ] **Step 4: Run selected tests** and verify GREEN.

### Task 4: Add smallest usable global and project settings UI

**Files:**
- Create: `src/Workbench.App/ViewModels/SettingsViewModel.cs`
- Create: `src/Workbench.App/Views/SettingsView.axaml`
- Create: `src/Workbench.App/Views/SettingsView.axaml.cs`
- Modify: `src/Workbench.App/ViewModels/MainWindowViewModel.cs`
- Modify: `src/Workbench.App/Views/HomeView.axaml`
- Modify: `src/Workbench.App/ViewModels/WorkspaceViewModel.cs`
- Modify: `src/Workbench.App/Views/Panes/LibraryPaneView.axaml`
- Modify: `src/Workbench.App/ViewModels/Panes/LeaderPaneViewModel.cs`
- Modify: `src/Workbench.App/Views/Panes/LeaderPaneView.axaml`
- Test: `tests/Workbench.App.Tests/SettingsViewModelTests.cs`
- Test: `tests/Workbench.App.Tests/LeaderPaneViewModelTests.cs`

**Interfaces:**
- Settings page can select Auto, Ask, or Manual only and persists global policy.
- Workspace project settings select inherit, Auto, Ask, or Manual only and persist nullable override.
- Leader pane displays a due message for Auto/Ask only; sending continues existing M1-04 session behavior.

- [ ] **Step 1: Write failing App tests** for global changes/recreation, override changes/inherit/reopen/project isolation, and due message behavior without session mutation.
- [ ] **Step 2: Run selected App tests** and verify RED.
- [ ] **Step 3: Implement focused view models and bindings** with no new settings framework or modal rollover flow.
- [ ] **Step 4: Run selected App tests** and verify GREEN.

### Task 5: Documentation, verification, and acceptance

**Files:**
- Modify: `README.md`
- Test: all test projects

- [ ] **Step 1: Update README** to mark M1-04 complete, M1-05A current, and M1-05B/C planned.
- [ ] **Step 2: Run** `dotnet restore`, `dotnet build`, Storage tests, App tests, Core tests, `dotnet test`, and `git diff --check`.
- [ ] **Step 3: Upgrade the real M1-04 database** with the app; inspect `user_version`, original project, current epoch, and transcript counts without deleting data.
- [ ] **Step 4: Run the app at 1280×720 logical size**; verify global policy UI, project override UI, due presentation, all workspace panes, and no overflow.
- [ ] **Step 5: Quick-regress F1** by dragging a divider, returning to projects, reopening, and confirming the layout persists.
- [ ] **Step 6: Inspect the diff for absent M1-05B/C work** and commit `feat(leader): add session rotation policy settings`.

## Future M1-05 phases

- **M1-05A — Policy & Settings:** completed in `2487e935f1486c6a536aff173527c3a20e17696f`.
- **M1-05B — Handoff & Rollover:** completed in `ea6b3ce21f9f47e5c953e658a7be783927c7e75c`.
- **M1-05C — Epoch History UI:** current execution phase; show archived epoch history and transcripts with the current epoch expanded.

## M1-05B execution

### Task 6: Atomic storage rollover and predecessor lookup

- [x] Add failing Storage tests for archive/handoff/reason, fresh current epoch, identity/metadata preservation, deterministic predecessor lookup, rollback, and project isolation.
- [x] Add `ProjectLeaderRepository.RolloverAsync(...)` as one SQLite transaction and `LeaderSessionEpochRepository.GetMostRecentArchivedForProjectAsync(...)` with deterministic ordering.
- [x] Run Storage tests green.

### Task 7: Bounded handoff and provider-neutral rollover orchestration

- [x] Add failing App tests for semantic handoff isolation, approval/error/empty/oversized fallback, UTF-8 bounded fallback, fresh runtime creation, cleanup after persistence failure, and immediate-predecessor-only boot context.
- [x] Add focused `LeaderHandoffBuilder` and `LeaderSessionRolloverService` units depending only on `IAgentRuntime`, `AgentRuntimeRegistry`, primitive Storage records/repositories, and `TimeProvider`.
- [x] Run focused App tests green.

### Task 8: Policy execution, Ask decision, Manual New Brain, and boot context

- [x] Add failing ViewModel tests for Auto, Ask Continue/Start Fresh, ManualOnly, busy/approval guards, original-draft preservation, and one-time boot envelope isolation.
- [x] Route all rollover entry points through `LeaderSessionRolloverService`; keep policy interception and presentation state in `LeaderPaneViewModel`.
- [x] Add the lightweight inline Ask decision and New Brain controls; do not add archived-history UI.
- [x] Run App tests green and render the pane at 1280×720.

### Task 9: Acceptance and commit

- [x] Update README status, run restore/build/Storage/App/full tests and `git diff --check`, then complete independent review.
- [x] Run the opt-in real Codex manual rollover smoke using a dedicated safe temporary Git project; verify identities, handoff marker, DB isolation, and no internal-message leakage.
- [x] Commit exactly `feat(leader): add session handoff and rollover`, then verify clean `master`.
