# Zero Hour Demo Continuity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在不改变 Authority 边界的前提下，交付一条 5–10 分钟可演示的 Zero Hour 项目连续性闭环。

**Architecture:** 以 Project World 为默认入口；将一次 Worker 执行统一呈现为 Assignment → Attempt → SessionBinding → Handoff → Review → Library projection。Handoff 始终是非权威输入，只有显式的用户/Leader 决策才能更新 AcceptedProjectState。新 Leader 通过 Accepted State、已审核 Handoff 和 Library 检索结果恢复上下文。

**Tech Stack:** C#/.NET、Avalonia UI、SQLite repositories、现有 B1 Continuity services、xUnit。

**Spec:** `docs/validation/current-state-alignment-audit-20260904.md` 与用户提供的 `WORKBENCH ALIGNMENT AUDIT — DEMO-READY / PRODUCT-SEMANTICS REVIEW`。

> **Milestone rule:** This milestone unifies the demo presentation, not the underlying identity or authority models. Prefer read-model/projection changes over persistence-model convergence. If a demo requirement would require re-unifying B1 and Legacy Worker lifecycles, STOP and report instead of implementing it.

## Global Constraints

- 不修改或迁移 frozen historical acceptance database。
- 不把 Claim、Handoff、Summary 或 Library projection 直接当作 AcceptedProjectState。
- 不把 Agent、model、provider、runtime、session identity 视为 authority。
- 保持 B1 responsibility/continuity lane 与 Worker execution lane 的 typed separation。
- 不宣称 provider equivalence、full crash recovery、semantic-drift elimination 或 token savings，除非新增证据支持。
- 每个任务必须有确定性测试；live-provider 测试必须显式标记为独立 integration 测试或 Skip。

### Task 0: 冻结 Demo Path 与数据来源

**Files:**
- Create: `docs/demo/zero-hour-continuity-script.md`
- Create: `docs/demo/zero-hour-demo-data-contract.md`

**Interfaces:**
- Defines the one operator path, the exact source of every displayed field, and the boundary between B1 Handoff, Legacy Worker Completion, Summary, Proposal, and Accepted State.

- [ ] **Step 1: Write the script** for Project World → Leader delegation → Worker artifact → Handoff display → Library search → Leader rotation → recovery display.
- [ ] **Step 2: For each screen, record the existing repository/service/model that supplies its data.**
- [ ] **Step 3: Mark any missing field as a presentation gap, not a permission to merge identity models.**
- [ ] **Step 4: Define the stop condition:** any change requiring B1/Legacy lifecycle convergence is reported and removed from this milestone.
- [ ] **Step 5: Review the script against the success screen before implementation begins.**

### Task 1: 建立 Project-first 展示与隔离的 Zero Hour fixture

**Files:**
- Modify: `src/Workbench.App/ViewModels/HomeViewModel.cs`
- Modify: `src/Workbench.App/Views/HomeView.axaml`
- Modify: `src/Workbench.App/ViewModels/MainWindowViewModel.cs`
- Modify: `src/Workbench.App/ProjectWorld/ProjectWorldExplorerViewModel.cs`
- Modify: `src/Workbench.App/Views/ProjectWorldExplorerView.axaml`
- Test: `tests/Workbench.App.Tests/HomeViewModelTests.cs`
- Test: `tests/Workbench.App.Tests/ProjectWorldExplorerViewModelTests.cs`
- Create: `src/Workbench.App/ProjectWorld/ZeroHourDemoSeeder.cs` (only if a one-click fixture is needed)

**Interfaces:**
- Produces a project entry model exposing `ProjectName`, `ProjectStateLabel`, accepted constraints, active assignments, and pending handoffs before opening the chat workspace.

- [ ] **Step 1: Write failing tests** for a recent project card that reports Project World readiness and for Explorer data that shows accepted constraints and active assignment summary.
- [ ] **Step 2: Run the focused tests** and confirm failure.
- [ ] **Step 3: Implement the smallest UI/data changes** so opening a ready project lands on Project World Explorer or presents it as the primary next action.
- [ ] **Step 4: Keep Zero Hour content in the fixture/seeder or operator script**; never hard-code it into generic Home or Project World ViewModels. Use existing initialization/authority commands to create any accepted facts.
- [ ] **Step 5: Run focused tests** and verify no source database migration is touched.

### Task 2: 统一 Worker completion 与结构化 Handoff 展示（仅 presentation）

**Files:**
- Modify: `src/Workbench.App/Worker/WorkerCompletionVerifier.cs` (only if display metadata is unavailable)
- Modify: `src/Workbench.App/Worker/WorkerSessionRouter.cs` (only to expose existing completion data)
- Modify: `src/Workbench.App/ProjectWorld/ManualWorkViewModel.cs`
- Modify: `src/Workbench.App/Views/ManualWorkView.axaml`
- Create: `src/Workbench.App/ProjectWorld/HandoffDisplayModel.cs`
- Create: `src/Workbench.App/ProjectWorld/HandoffDisplayModelFactory.cs`
- Create: `src/Workbench.App/ProjectWorld/HandoffDisplayViewModel.cs`
- Create: `src/Workbench.App/Views/HandoffDisplayView.axaml`
- Test: `tests/Workbench.App.Tests/GuidedHandoffComposerServiceTests.cs`
- Test: `tests/Workbench.App.Tests/Worker/WorkerContractAndVerificationTests.cs`

**Interfaces:**
- `HandoffDisplayModelFactory` reads either a B1 Handoff or Legacy Worker Completion plus typed bridge provenance and returns one presentation model. It never writes a B1 Handoff and never changes authority.
- `HandoffDisplayViewModel` consumes `HandoffDisplayModel` and exposes result, artifact paths, changed paths, unresolved issues, recommendations, and provenance labels.

- [ ] **Step 1: Write failing tests** asserting that a completed bounded task produces a structured, non-authoritative handoff view and retains Attempt/SessionBinding provenance.
- [ ] **Step 2: Run tests** and confirm failure.
- [ ] **Step 3: Implement read-only display normalization:** B1 Handoff remains B1; Legacy Worker Completion remains Legacy; both map to `HandoffDisplayModel`.
- [ ] **Step 4: Render one Handoff card** with clear status: “Worker result”, “Needs review”, “Accepted project fact” only after decision.
- [ ] **Step 5: Keep content verification honest:** path/scope checks may pass, semantic content remains `NotVerifiable` unless a concrete checker exists.
- [ ] **Step 6: If the required display fields cannot be read without lifecycle convergence, stop and report the gap.**
- [ ] **Step 7: Run Worker and Handoff tests.**

### Task 3: 增加可演示的 Library 检索与 provenance 表达

**Files:**
- Modify: `src/Workbench.App/ViewModels/Panes/LibraryPaneViewModel.cs`
- Modify: `src/Workbench.App/Views/Panes/LibraryPaneView.axaml`
- Modify: `src/Workbench.Storage/Memory/ProjectLibraryRepository.cs`
- Test: `tests/Workbench.App.Tests/LibraryProjectionContractServiceTests.cs`
- Test: `tests/Workbench.App.Tests/LibraryVisualClosureTests.cs`
- Test: `tests/Workbench.Storage.Tests/Memory/ProjectLibraryRepositoryTests.cs`

**Interfaces:**
- Add `LibrarySearchText`, `SearchLibraryCommand`, and a read-only search result model containing `Kind`, `AuthorityStatus`, category, topic, current overview, and source/provenance locator.

- [ ] **Step 1: Write failing repository and ViewModel tests** for searching “time permit”.
- [ ] **Step 2: Run focused tests** and confirm failure.
- [ ] **Step 3: Based on Task 0's data-source mapping, implement a read-only query/aggregation layer over Library projections, accepted contributions, proposals, and summaries. Do not move Proposal/Summary persistence into `ProjectLibraryRepository` merely to support search.** Label each result instead of collapsing them into Accepted State.
- [ ] **Step 4: Add visible labels** distinguishing Accepted State, Proposal, Summary, and Source.
- [ ] **Step 5: Do not add a new context-selection system.** Reuse an existing context hook only if one already exists; otherwise the demo ends at search and display.
- [ ] **Step 6: Run storage and app tests.**

### Task 4: 实现 Leader replacement / continuation proof

**Files:**
- Modify: `src/Workbench.App/ViewModels/Panes/LeaderPaneViewModel.cs`
- Modify: `src/Workbench.App/ViewModels/Leader/ProjectLeaderSessionManager.cs`
- Modify: `src/Workbench.App/ViewModels/MainWindowViewModel.cs`
- Modify: `src/Workbench.App/Views/Panes/LeaderPaneView.axaml`
- Test: `tests/Workbench.App.Tests/LeaderBootContextBuilderTests.cs`
- Test: `tests/Workbench.App.Tests/LeaderSummaryRecoveryTests.cs`
- Test: `tests/Workbench.App.Tests/ProviderIndependentProjectRecoveryCertificationTests.cs`

**Interfaces:**
- Reuse existing `LeaderBootContext`, project summaries, selected Handoff, AcceptedProjectState, and Library projection data. Add only a `RecoveryViewModel` for presentation; do not create a second memory/context model.

- [ ] **Step 1: Write failing tests** for a new Leader epoch displaying a recovery view assembled from existing boot/context sources without transcript replay.
- [ ] **Step 2: Run tests** and confirm failure.
- [ ] **Step 3: Build `RecoveryViewModel` from persisted Project World projections** and existing boot/context records.
- [ ] **Step 4: Render a visible “Recovered from Project World” panel** in the Leader pane.
- [ ] **Step 5: Add an operator action** to rotate/reopen the Leader session and preserve Project identity.
- [ ] **Step 6: Run recovery tests** and verify provider/session identifiers are shown as replaceable execution details.

### Task 5: 固定演示脚本、最小 wiring gap 与证据

**Files:**
- Modify: `src/Workbench.App/Continuity/CanonicalWorkerLaunchService.cs`
- Modify: `src/Workbench.App/Services/AppServices.cs`
- Modify: `tests/Workbench.App.Tests/Continuity/OpenCodeLiveB1AcceptanceTests.cs`
- Create: `docs/validation/zero-hour-demo-evidence-20260905.md`

**Interfaces:**
- The demo script names one canonical path and explicitly labels legacy/manual fallback steps.

- [ ] **Step 1: Write a failing integration-style contract test** proving the selected UI launch path resolves to one explicit Worker/Handoff flow.
- [ ] **Step 2: Run the test** and confirm the current wiring gap.
- [ ] **Step 3: Apply only the smallest UI wiring needed to expose the selected existing path.** Do not converge B1 participation, WorkerCompletionVerifier, and Legacy Worker lifecycle.
- [ ] **Step 4: Change live-provider tests** so disabled providers are explicit Skip/independent integration tests and cannot inflate deterministic pass counts.
- [ ] **Step 5: Write the operator script** with exact prompts, expected screens, manual steps, and recovery claims.
- [ ] **Step 6: Run the complete focused test matrix** and record commit/ref, dirty state, provider, model, database path, and environment variables.

## Verification gate

- [ ] `dotnet build .\AI.Game.Workbench.sln --no-restore --verbosity minimal`
- [ ] Run Core, Runtime, Project, Storage, and App test projects separately.
- [ ] Run the Zero Hour focused tests.
- [ ] Run live-provider tests only with explicit environment variables and record them separately from deterministic tests.
- [ ] Perform one manual 5–10 minute walkthrough using the script.
- [ ] Confirm that a new Leader can state: Chapter 1 completed, what it established, what remains, and which constraints remain accepted.
- [ ] Confirm that no Handoff, Summary, or Library action bypasses AuthorityDecision.

## Out of scope for this milestone

- Full temporal model redesign.
- Provider parity or provider equivalence.
- Universal active-process resurrection.
- Token-use benchmark claims.
- Automated semantic-drift measurement.
- Enterprise governance dashboards.
