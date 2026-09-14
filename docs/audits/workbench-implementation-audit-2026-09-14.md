# AI Game Workbench Implementation Audit

审计日期：2026-09-14  
审计对象：当前工作树 `master`，HEAD `b909179`  
审计范围：从 2026-08-12 初始脚手架到当前 HEAD 的实现、测试、认证记录与产品表面。

## 1. Executive Summary

Workbench 已经从一个桌面应用脚手架，发展为一个拥有持久化 Project World、可替换 Agent runtime、Worker execution、Authority governance、AcceptedProjectState、Summary/Continuity、restart recovery 和项目级产品页面的长期 AI 项目工作台。

当前最重要的结论是：

> **Kernel-complete, product-incomplete**  
> 核心机制基本齐全，当前任务是收束唯一主流程和产品体验。

原先的 “canonical Worker completion” 命名已经不准确。当前 P0 应正式称为：

> **P0 — Canonical Acceptance Spine Certification**  
> **P0：唯一接受链认证**

唯一允许 Agent 产生的变化进入项目真相的路径是：

```text
Worker Execution
      ↓
Completion
      ↓
Evidence / Result / Diff / Verification
      ↓
Claim / Proposed Change
      ↓
Authority Decision
   ↙           ↘
Accept        Reject / Revise
  ↓
AcceptedProjectState
  ↓
Summary / Continuity / Next Session
  ↓
Restart / Recovery
```

截至当前 HEAD，以下内容已经有代码和自动化证据：

- Worker 完成会生成持久化 Completion Package。
- Diff、Verification、Evidence、Claim、Handoff 均可保留。
- Completion 不直接写 `AcceptedProjectState`。
- 只有 Authority Decision 能建立 accepted contribution 和 accepted assignment state。
- Accept 前 accepted state 与 Summary 不变。
- Accept 后 accepted state 与 Summary 更新。
- Reject/Revise 不删除 Completion、Evidence、Claim、Handoff 或 WorkerExecution。
- 真实项目文件可以被 Worker 修改，并经过三轮 `+1 → +2 → +3` 接受。
- 服务重开后，新的 Leader boot context 可以读取之前接受的状态。
- Accept 后可以创建显式 successor Assignment、Task/Revision 并通过 canonical Worker launch 派发。
- Worker crash reconciliation、successor dispatch failure 和 retry 已有持久化恢复路径。
- Project Overview、Work、Review、World、History、Settings、Diagnostics 等产品入口已经存在。

仍然不能过度声称的内容：

- 这不是说所有桌面 UI 入口都已经收敛为一次完整的真实用户 walkthrough。
- 默认 deterministic test suite 不会自动运行真实外部 Provider；真实 Provider 是显式 live gate。
- Legacy Task/Review 与 B1 Assignment/Authority 仍通过 bridge 共存，迁移尚未完全删除旧模型。
- Authority commit 与 successor runtime 启动不是一个跨系统原子事务；失败后通过 `FailedAfterAuthorityCommit` 和持久化事件恢复。
- 当前审计证明的是服务/数据库重开级别的 restart continuity；不能把它写成已经自动化验证了完整桌面进程 kill/relaunch walkthrough。

## 2. 审计口径

本审计将证据分为四级：

| 级别 | 含义 |
| --- | --- |
| 已实现 | 当前源码中有明确模型、服务、持久化或 UI 入口 |
| 已自动化验证 | deterministic test 或 integration test 已覆盖 |
| 真实 Provider 已验证 | 显式 live gate 使用真实 Codex/OpenCode 等外部 runtime |
| 产品级完成 | 用户可以从正常产品入口走完流程，且主要边界和恢复状态已被验证 |

“已实现”不自动等于“已产品化”；“live gate 通过”也不自动等于“首次用户完整 UI walkthrough 已通过”。

审计刻意不修改以下用户已有工作区内容：

```text
M  .gitignore
?? _hf_template_probe/
?? anything2explainer/
?? workbench-promo/
```

## 3. 从最开始到现在

### 阶段一：应用与项目骨架

提交区间：`f3097f6` 至 `689f24f`

完成内容：

- 建立 `Workbench.Core`、`Workbench.Storage`、`Workbench.Project`、`Workbench.Runtime`、`Workbench.App` 和测试项目。
- 建立 Project identity、Workspace layout 和本地 SQLite 数据库。
- 支持项目检测、Git 状态检查、Recent Projects 和 Project Home。
- 建立三栏 Project Workspace。
- 引入 provider-neutral Agent contracts。
- 集成 Codex app-server protocol。
- 支持 Agent session context、approval response 和 runtime connection。

这一阶段解决的是：

```text
应用能启动
项目能打开
状态能持久化
Agent 能被连接
```

### 阶段二：Leader 与第一代连续性

提交区间：`9ac4ca6` 至 `4de08fd`

完成内容：

- Leader session epoch、rotation policy、handoff、rollover 和历史 timeline。
- Project memory foundation。
- 从 Leader session 合成 memory，并在 Leader boot 时注入。
- 建立 Project Summary、continuity materials 和 Library。
- 支持 Library category/time views、proposal 和 confirmed material。
- 将 Leader continuity policy 接入 Agent context selection。

这一阶段把 Workbench 从“聊天窗口”推进为：

```text
Project
  → Leader Session
  → Summary / Memory
  → 下一次 Leader Boot
```

### 阶段三：Worker execution lifecycle

提交区间：`33806f9` 至 `f4f399f`

完成内容：

- Task、TaskRevision、WorkerExecution 和 Worker request persistence。
- Worker revision acknowledgement 和 active worker slot。
- BaseCommit、branch/worktree identity、workspace baseline。
- Agent session persistence、Worker progress、approval、stop、resume。
- FinalReport、validation evidence、completion persistence。
- Leader draft task proposal、用户确认和 Worker routing。
- Worker handoff、review decision、ask-user gate、auto-proceed。
- Native Codex Worker surface 和桌面 Worker session hosting。
- Worker status correction、completed task reconciliation、scope changed files。

这阶段建立了真实 execution substrate：

```text
Leader Request
  → Task / Revision
  → Worker Execution
  → Agent Session
  → FinalReport
  → Review / Handoff
```

但当时 Legacy Worker completion 与 B1 Authority 仍是两条路径，不能把它们称为一个完整 acceptance chain。

### 阶段四：R5 连续性与 Project World 设计

提交区间：`c6bbf72` 至 `12ec9a7`

完成内容：

- Summary delta contracts、持久化和 crash-durable Leader result。
- Repository ownership、call/data ownership 和 boundary reconciliation audit。
- R5-B1 durable identities、claims、commands、current state。
- SQLite schema、governance bootstrap、attempt/session binding。
- Claim、Handoff、Evidence 和 Authority command。
- Authority validation、atomic commit、CAS sequence。
- Accepted state recovery、projection 和 manual continuity certification。
- Legacy coexistence boundaries。

这一阶段明确了核心治理原则：

```text
Evidence ≠ Truth
Handoff ≠ Authority transfer
Completion ≠ Acceptance
AcceptedProjectState = Authority history 的 projection
```

### 阶段五：B1 Project World 产品切片与 Provider abstraction

提交区间：`d507e4c` 至 `f4f399f`

完成内容：

- Accepted-state 和 Project Library overview/category/time projections。
- Provider-neutral B1 Agent participation adapter。
- OpenCode multi-agent runtime support。
- Windows self-contained distribution flow。
- 中英文设置和主 UI 本地化。
- Manual Project World experience slice。
- Native Agent surface、Alpha release package 和桌面启动路径。

这一阶段让 B1 从数据层进入实际产品表面，但仍保留 Legacy Task/Review compatibility。

### 阶段六：Alpha Worker、Library 和 Evolution

提交区间：`bbd6b09` 至 `f719f4a`

完成内容：

- 完整 Worker execution lifecycle 和 incremental progress。
- Worker approval path。
- Alpha continuity convergence。
- Zero-hour continuity demo 和 repeatable continuity demo runner。
- Evolution Candidate governance experiment。
- Candidate → Library Proposal → accept → restart 的真实 UI 记录。
- 项目迁移、重定位、demo recovery 和 continuity rehearsal。

这一阶段证明了 Project continuity 和 Library governance 的可用性，但还没有证明真实 Worker code change 已经自动进入 B1 Authority。

### 阶段七：Canonical Acceptance Spine

提交区间：`09cb2b7` 至 `c6d2810`

完成内容：

- Canonical Worker Completion model、repository 和 bridge。
- Legacy Worker completion adapter。
- Completion → Claim/Handoff 的 canonical route。
- Completion 不直接写 accepted state 的边界。
- Pending completion、idempotent bridge 和 recovery。
- Completion race：Legacy task 已进入 Reviewing 时，canonical completion 仍能被治理。
- Leader continuity after restart。
- Reject/Revise retention 和 Leader rollover。
- Project Overview 显示 pending handoff、continuity summary 和 authority review。
- Worker cards 进入 canonical authority review。

关键认证提交：

```text
09cb2b7  certify canonical acceptance spine
177b990  certify leader continuity on acceptance spine
0391f26  certify rejection retention and leader rollover
c6d2810  route raced worker completions through canonical spine
e41ba1f  record acceptance spine certification
```

### 阶段八：Live Provider、Crash Recovery、Successor 与产品壳

提交区间：`705c1d0` 至当前 HEAD `b909179`

完成内容：

- 显式 live provider acceptance gates。
- Codex live Acceptance Spine。
- Worker crash reconciliation。
- crash audit event 保留。
- 真实 Codex 三轮 `+1 → +2 → +3`。
- worker access mode 显式传播。
- Accept 后 explicit successor Assignment。
- successor Task/Revision 创建。
- successor 通过 canonical launch 派发。
- successor runtime 失败后的 `FailedAfterAuthorityCommit`。
- `SuccessorDispatchFailed` 持久化事件和 retry reuse。
- 多 current Assignment 下显式 Assignment routing。
- restart 后从 Task bridge 恢复 Assignment link。
- Overview 只显示 current Assignment。
- Overview 和 Worker cards 显示持久化 execution state。
- Review Queue、History、Settings、Diagnostics、Backup surface。

## 4. 当前核心架构

### 4.1 领域层

核心对象已经覆盖：

```text
Project
ProjectWorld
Governance
LogicalActor
Responsibility
Assignment
AssignmentRevision
Attempt
SessionBinding
Claim
Handoff
Evidence
AuthorityDecision
AcceptedProjectState
Task
TaskRevision
WorkerExecution
CompletionPackage
ProjectSummary
LibraryObject
EvolutionCandidate
```

### 4.2 持久化层

当前 migration chain 为 `Migration001` 至 `Migration031`。主要表和关系包括：

```text
projects
 ├─ project_layouts
 ├─ leader_session_epochs ── leader_messages
 ├─ tasks ── task_revisions
 │        ├─ worker_executions ── worker_completion_packages
 │        ├─ task_events
 │        └─ task_review_decisions
 ├─ project_summary_entries ── project_summary_source_refs
 ├─ project_library_objects
 │    ├─ project_library_timeline_nodes
 │    └─ project_library_proposals
 ├─ project_evolution_candidates
 └─ B1 project world
      └─ b1_project_governance
           ├─ b1_authority_decisions
           ├─ b1_logical_actors
           ├─ b1_responsibilities
           ├─ b1_assignments ── b1_revisions
           ├─ b1_attempts ── b1_session_bindings
           ├─ b1_claims
           ├─ b1_handoffs
           ├─ b1_accepted_state_contributions
           ├─ b1_assignment_routing
           ├─ b1_worker_task_links
           ├─ b1_worker_execution_links
           └─ b1_worker_execution_evidence
```

### 4.3 Authority ownership

当前运行时的 accepted-state 写入边界为：

```text
Guided Decision / Authority Command
        ↓
B1AuthorityEvaluator
        ↓
B1AuthorityRepository.TryCommitAsync
        ↓
AuthorityDecision history
        ↓
B1ProjectionService
        ↓
AcceptedProjectState
```

`CanonicalWorkerCompletionBridgeService` 只能保存 Completion、Claims 和 Handoff。它不会调用 Authority command，也不会写 `AcceptedProjectState`。

`B1ProjectionService` 只负责读取 Authority state 并投影 accepted state，不负责裁决。

Summary 在 Authority decision 之后追加 accepted decision delta；因此 Summary 不是 accepted state 的写入口。

## 5. P0 Acceptance Spine 认证

### 5.1 Deterministic Tiny Counter

测试：

```text
tests/Workbench.App.Tests/Continuity/TinyCounterAcceptanceSpineCertificationTests.cs
```

认证过程：

```text
+1 → +2 → +3
```

每一轮覆盖：

1. 建立或恢复 Project World。
2. Leader boot 读取上一轮 accepted statement。
3. Worker 修改真实临时 Git workspace 中的 `src/counter.js`。
4. 保存 WorkerExecution identity、BaseCommit、workspace baseline 和 session。
5. 保存 FinalReport、verification evidence 和 completion package。
6. 生成 Claim、Handoff 和 pending governance material。
7. 在 Accept 前确认 AcceptedProjectState 不变。
8. 用户级 Authority decision 执行 Accept。
9. AcceptedProjectState 增加 accepted contribution。
10. Summary 在 decision 后增加对应条目。
11. 关闭服务并重开数据库。
12. 新 Leader boot context 读取所有上一轮 accepted statements。
13. 下一轮继续执行。

这证明了 provider-independent 的核心 acceptance chain 和连续三轮状态推进。

### 5.2 Real Codex Worker

测试：

```text
tests/Workbench.App.Tests/Continuity/LiveCodexAcceptanceSpineTests.cs
```

显式 gate：

```text
WORKBENCH_RUN_CODEX_WORKER_LIVE=1
```

记录结果：

- Codex 修改真实临时 Git workspace。
- `src/counter.js` 从 `+1` 变成 `+2`，再变成 `+3`。
- 每轮返回结构化 FinalReport。
- 每轮保存 Completion、Verification、Evidence、Claim 和 Handoff。
- Accept 前 accepted state 不变。
- Accept 后 accepted state 和 Summary 更新。
- 每轮服务重开后 Leader boot context 读取之前的状态。
- 下一轮建立在此前 accepted state 上。

这是真实 Provider 的三轮 gate。它不是默认测试的自动行为，且测试内部是 service/database reopen，而不是完整桌面进程级 kill/relaunch。

### 5.3 Reject / Revise

已验证：

- Reject 不把 proposed contribution 加入 accepted state。
- Revise 激活 successor revision，但不接受原 proposed contribution。
- Completion、Evidence、Claim、Handoff 和 WorkerExecution 保留。
- Leader rollover 后仍可读取正确的 accepted state 和 pending/rejected history。

### 5.4 Crash recovery

`WorkerSessionRouter.ReconcileInterruptedExecutionsAsync` 在 Workspace load 时处理 stale in-progress execution：

```text
workspace delta capture
  → WorkerExecution.Interrupted
  → Working Task.NeedsLeaderDecision
  → WorkerExecutionCrashReconciled event
```

它不会把 crash 自动解释成 Completion，也不会修改 AcceptedProjectState。Terminal `Interrupted` execution 不会被重复选择，路径具备幂等性。

### 5.5 Legacy bridge

Legacy Worker/Review 仍然存在，但其完成产物可经过：

```text
Legacy Worker completion
  → Canonical completion package
  → Claim / Handoff
  → existing Authority command
```

已有 race test 覆盖 Legacy task 先进入 Reviewing、随后 Completion bridge 到达的情况。Legacy Task status 仍是兼容层事实，不能被当成 Authority acceptance。

## 6. Successor Assignment

Accept 后的 successor 路径已从“手动再派任务”推进到可持久化 dispatch：

```text
Accept Authority Decision
  → successor Assignment Decision
  → Task / TaskRevision
  → CanonicalWorkerLaunchService
  → WorkerSessionRouter
```

当前行为：

- 空 successor contract 不创建 successor。
- Reject/Revise 不创建 successor。
- successor Assignment 有明确责任人、责任和 revision contract。
- 已有 current Assignment 并存时，dispatch 使用显式 AssignmentRef，不靠“当前唯一 assignment”猜测。
- runtime 启动失败返回 `FailedAfterAuthorityCommit`。
- Task 创建后记录 `SuccessorDispatchFailed`。
- 重试复用已有 Authority decision 和 Task，不重复创建 successor。
- restart 后通过持久化 Worker Task bridge 恢复 Assignment link。

边界：Authority commit 与外部 runtime 启动横跨不同系统，不能宣称为单一事务。当前设计把半成功状态显式化，并提供恢复与重试。

## 7. 产品壳现状

当前可见产品入口：

```text
Project Home
Project Overview
Work / Leader / Library Workspace
Review Queue
Project World
History
Settings
Diagnostics
Backup / Restore inspection
```

已完成的产品化收束：

- Overview 只显示 current Assignment，避免展示已被 successor 替换的旧 assignment。
- Overview 显示持久化 Agent execution。
- Worker cards 显示 execution status。
- Worker card 区分 session status、Worker execution status 和 authority pending decision。
- Review 可以从 Overview 和 Workspace 到达。
- Settings、Diagnostics、History 和 Backup inspection 已有入口。
- Agent、runtime、policy、project override 等设置已有可视化表面。

仍在收束：

- 首次用户从 Project Home 到 Worker 到 Review 到 Accept 的完整 UI walkthrough。
- Legacy UI 与 B1 UI 的概念统一和文案统一。
- 更清楚地把 “完成”“待确认”“已接受”“已拒绝”“需修订”表达为不同产品状态。
- 真实游戏项目而非 Tiny Counter 临时项目的产品 demo。
- 完整桌面重启后，从 UI 重新进入同一 Project 并完成下一轮工作的演示。

## 8. 能力矩阵

| 能力 | 当前实现 | 自动化证据 | 当前判断 |
| --- | --- | --- | --- |
| Project identity / local DB | 已实现 | Project/storage/recovery tests | 已完成核心 |
| Project Home / Overview | 已实现 | App view-model tests | 产品表面已存在 |
| Leader session / epoch / rollover | 已实现 | rollover/recovery/certification tests | 已验证 |
| Summary / continuity context | 已实现 | summary/boot context tests | 已验证 |
| Library / Evolution Candidate | 已实现 | evolution/library tests 与 live record | 已验证特定路径 |
| Task / TaskRevision | 已实现 | storage/app tests | Legacy 模型仍存在 |
| WorkerExecution | 已实现 | execution lifecycle tests | 已验证 |
| BaseCommit / workspace baseline | 已实现 | worker identity/baseline tests | 已验证 |
| Agent runtime abstraction | 已实现 | runtime contract tests | 已验证 deterministic path |
| Codex app-server | 已实现 | protocol/live lifecycle tests | live gate 独立 |
| OpenCode adapter | 已实现 | adapter/live B1 test | live gate 独立 |
| Completion Package | 已实现 | canonical completion tests | 已验证 |
| Verification / Evidence | 已实现 | bridge/counter tests | 已验证 |
| Claim / Handoff | 已实现 | B1 and bridge tests | 已验证 |
| Authority evaluator / command | 已实现 | authority tests | 已验证 |
| AcceptedProjectState | 已实现 | projection/restart tests | 已验证 |
| Summary after Authority | 已实现 | authority summary tests | 已验证 |
| Reject / Revise retention | 已实现 | retention certification | 已验证 |
| Crash reconciliation | 已实现 | crash certification | 已验证 |
| Successor Assignment | 已实现 | successor dispatch tests | 已验证，存在跨系统边界 |
| Review Queue | 已实现 | App tests | 产品表面已存在 |
| History / Settings / Diagnostics | 已实现 | view-model tests | 产品表面已存在 |
| Backup inspection | 已实现 | backup tests | 已验证 |
| Real Codex three-round spine | 已实现 | explicit live gate | 已验证 |
| Full first-user UI walkthrough | 部分实现 | 尚无统一现场记录 | 未完成 |
| Real game-engine integration | 未作为主线认证 | 无当前 P0 gate | 后续 vertical integration |
| All external Providers | 未完成 | 默认不运行 live provider | 不应声称完成 |

## 9. 测试与验证

当前默认测试命令：

```powershell
dotnet test AI.Game.Workbench.sln --no-restore -m:1 --verbosity minimal
```

本次审计执行结果：

```text
Core:    128 passed, 0 failed, 0 skipped
Project:  28 passed, 0 failed, 0 skipped
Storage: 317 passed, 0 failed, 0 skipped
App:    633 passed, 0 failed, 4 skipped
Runtime: 66 passed, 0 failed, 1 skipped

Total: 1172 passed, 0 failed, 5 skipped
```

跳过项均为显式 live-provider gate；默认命令不会启动本机 Codex/OpenCode
进程。

最新工作区验证目标是：

- Core、Project、Storage、Runtime、App 全部顺序执行。
- 不启用外部 Provider live gate。
- 不改动用户已有未跟踪目录。

专项认证包括：

```text
TinyCounterAcceptanceSpineCertificationTests
LiveCodexAcceptanceSpineTests
CanonicalWorkerCompletionBridgeTests
B1SuccessorDispatchServiceTests
B1ManualContinuityCertificationTests
B1LegacyCoexistenceCertificationTests
ProviderIndependentProjectRecoveryCertificationTests
LeaderReviewAuthorityRestartCertificationTests
```

历史上曾经存在的旧审计失败项，已被后续提交推进或替换，不能继续照搬 2026-09-09 旧审计：

- “没有三轮真实 Codex”已被 `b03ea85` 和 live test 记录推进。
- “没有 crash reconciliation”已被 `e1b6750`、`a723fc7` 推进。
- “没有 successor dispatch”已被 `7f34939`、`a93d80b`、`993cb68` 推进。
- “Legacy completion 不进入 canonical path”已被 bridge 和 race certification 推进。

但是，旧审计中关于 Provider live gate、完整 UI walkthrough、Legacy/B1 双模型共存和跨系统 atomicity 的谨慎边界仍然成立。

## 10. 当前仍存在的边界

### P0：产品主流程仍需收束

核心 Acceptance Spine 已认证，下一步不是继续发明平行功能，而是把正常用户路径压缩成一条可发现、可重复、可解释的产品流程：

```text
Project
  → Current Work
  → Worker Execution
  → Pending Review
  → Accept / Reject / Revise
  → Accepted State
  → Next Assignment
```

### P1：Legacy 与 B1 仍是两套领域语言

`Task/TaskRevision/WorkerExecution` 与 `Assignment/Revision/Attempt` 通过 bridge 连接。当前已经足够支撑认证，但还没有完成所有 UI、查询和生命周期语义的统一。

### P1：桌面进程级 restart walkthrough

服务/数据库 reopen 已经通过；仍建议补一条真实桌面流程：

```text
启动 Workbench
→ 完成一次 Worker
→ Accept
→ 完全退出进程
→ 重新启动 Workbench
→ 打开同一 Project
→ 新 Leader 读取 accepted state
→ 发起下一轮 Worker
```

### P1：跨系统 successor dispatch

Authority commit、SQLite Task 创建和外部 runtime 启动之间仍存在系统边界。当前有明确失败状态和 retry，但没有把外部 Provider 启动变成数据库事务，这是合理边界，也必须在产品状态里持续表达。

### P1：真实 Provider 的默认可重复性

Codex/OpenCode live tests 需要显式环境变量和本机 Provider。默认 suite 的 skipped 不代表失败，但也不能代表所有真实 Provider 都已认证。

### P2：真实游戏纵向集成

Godot、Unity、Editor、Git、runtime provider 应作为 vertical integration。它们不是 Workbench kernel 本身，不应先于 Acceptance Spine 继续扩张。

### P2：产品视觉和首次使用体验

当前已有页面和导航，但仍需围绕一个用户能理解的主叙事继续打磨：

```text
这个项目现在被接受的状态是什么？
哪个 Agent 正在工作？
哪些变化还没有被接受？
我接受之后，下一个工作是什么？
```

## 11. 现在可以怎么描述 Workbench

可以诚实地说：

> Workbench 是一个面向长期 AI 项目的项目连续性与接受层。它把 Worker execution、evidence、claims、authority decisions、accepted project state、summary、handoff 和 recovery 持久化在一个 Project World 中。AI Game Workbench 是它在游戏开发场景里的第一种产品形态。

也可以说：

> P0 Canonical Acceptance Spine 已通过 deterministic certification 和三轮真实 Codex Worker gate。Workbench 当前进入产品壳、主流程收束和 vertical integration 阶段。

不应说：

- “任何 Worker 完成都会自动改变项目真相。”
- “Completion、Evidence 或 Handoff 本身就是 Truth。”
- “所有旧 Task/Review 路径已经消失。”
- “所有 Provider 都已真实 E2E 认证。”
- “完整首次用户桌面 walkthrough 已经完成。”
- “Successor runtime 启动和 Authority commit 是一个原子事务。”
- “Workbench 已经完成 Godot/Unity 产品集成。”

## 12. 后续路线图

### P0：Acceptance Spine product certification

1. 用当前真实 Codex counter gate 作为内部 regression。
2. 增加完整桌面进程级退出/重启脚本。
3. 从 Project Overview 进入 Worker、Review、Accept 和 successor dispatch。
4. 记录一次不依赖 CLI rescue、不手改数据库的完整 UI 证据。
5. 将该流程固定成 release gate。

### P1：Legacy adapter convergence

1. 明确 Legacy Task/Review 是兼容投影。
2. 所有 Worker completion 统一进入 Canonical Completion Package。
3. 所有 pending review 都以 Claim/Handoff/Authority 语义呈现。
4. 逐步减少 UI 中重复的状态语言。

### P1：Product shell completion

1. Overview 只保留当前状态、当前工作和待确认变化。
2. Review Queue 成为所有 proposed change 的统一入口。
3. World/History 负责解释 accepted state、decision 和 provenance。
4. Settings/Diagnostics 负责 Provider、runtime、backup 和 recovery。

### P2：Vertical integration

1. Tiny Counter 继续作为 deterministic certification fixture。
2. 加入一个真实 Godot 小项目。
3. 验证项目文件、测试/运行结果、diff、evidence 和 accepted state 的对应关系。
4. 再考虑 Unity、Rider、VS Code 和更多 Provider。

## 13. 证据索引

主要文档：

```text
docs/architecture/canonical-acceptance-spine.md
docs/validation/canonical-acceptance-spine-certification-20260913.md
docs/validation/real-provider-acceptance-20260913.md
docs/audits/workbench-current-state-audit-2026-09.md
```

主要测试：

```text
tests/Workbench.App.Tests/Continuity/TinyCounterAcceptanceSpineCertificationTests.cs
tests/Workbench.App.Tests/Continuity/LiveCodexAcceptanceSpineTests.cs
tests/Workbench.App.Tests/Continuity/CanonicalWorkerCompletionBridgeTests.cs
tests/Workbench.App.Tests/Continuity/B1SuccessorDispatchServiceTests.cs
tests/Workbench.App.Tests/Continuity/B1ManualContinuityCertificationTests.cs
tests/Workbench.App.Tests/Continuity/OpenCodeLiveB1AcceptanceTests.cs
```

关键实现：

```text
src/Workbench.App/Continuity/CanonicalWorkerCompletionBridgeService.cs
src/Workbench.App/Continuity/CanonicalWorkerLaunchService.cs
src/Workbench.App/Continuity/B1SuccessorDispatchService.cs
src/Workbench.App/Continuity/GuidedDecisionService.cs
src/Workbench.App/Continuity/B1ProjectionService.cs
src/Workbench.Storage/Continuity/B1AuthorityRepository.cs
src/Workbench.App/Worker/WorkerSessionRouter.cs
```

关键提交：

```text
09cb2b7  Canonical Acceptance Spine certification 起点
177b990  Leader continuity after restart
0391f26  Reject/Revise retention 与 Leader rollover
c6d2810  Worker completion race 接入 canonical path
8864d07  Real Codex Acceptance Spine
e1b6750  Crash recovery
a723fc7  Crash audit event retention
b03ea85  Real Codex 三轮 +1 → +2 → +3
7f34939  Explicit successor Assignment
a93d80b  Successor dispatch through canonical spine
993cb68  Restart assignment-link recovery
2cd66f5  Persisted execution in Overview
b909179  Persisted execution state in Worker cards
```

## 14. Final Assessment

Workbench 现在已经不再是“功能很多但没有主干”的早期系统。它已经有一条被代码、持久化边界、deterministic certification、crash recovery 和真实 Codex 三轮 gate 共同证明的 Acceptance Spine。

当前真正的工作已经从“有没有这个机制”转成：

```text
用户从哪里开始？
系统认哪条路径？
什么状态最终是 accepted truth？
重启之后用户能否立即继续？
```

因此当前阶段应标记为：

> **P0 Acceptance Spine：已认证**  
> **Kernel：基本完成**  
> **Product shell：进行中**  
> **Godot/Unity vertical integration：后续阶段**
