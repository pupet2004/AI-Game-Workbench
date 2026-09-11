# AI Game Workbench 当前实现审计

审计日期：2026-09-09  
审计对象：当前工作树 `master`，HEAD `f719f4a`，不是历史 release snapshot。

## 1. Executive Summary

当前 Workbench 已经拥有可持久化的 Project World、B1 Authority/Claim/Handoff/AcceptedProjectState 投影、Worker 执行骨架、Leader Summary/Library/Session Rotation 与 Evolution 治理路径；但传统 UI Worker 完成链与 B1 Authority 闭环仍是两条并存路径，完整的“真实用户改代码 → Worker → Claim → Authority → AcceptedProjectState → 新 Session 继续”的端到端公开 Demo 尚未被当前工作树完整证明。

最准确的等级判断是：**核心 Continuity primitives 为 Level 2/3；Evolution→Library 路径有一次真实 UI/restart Level 4 记录；B1 手工连续性是高可信 certification、但不是用户级 Level 4；真实 Provider 与完整 Worker-to-Authority Continuity 为 PARTIAL，不能宣称全系统 Level 4。**

本次审计工作树有未提交修改：11 个已修改文件、未跟踪 `Game` 目录、未跟踪 `docs/validation/semantic-lifecycle-audit-20260908.md`。因此下述结论针对这个工作树，而不是干净 commit。

## 2. Capability Maturity Matrix

| Capability | Concept | Implemented | Tested | Integrated | E2E Verified | Evidence | Main Gap |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Project Identity |  | ✓ | ✓ | ✓ | 🟡 | `ProjectRepository`; project-open/recovery tests | 需更明确区分所有 legacy session-derived 状态 |
| Leader |  | ✓ | ✓ | ✓ | 🟡 | `LeaderPaneViewModel`; boot/rotation tests | B1 Project World 不是每次 Leader turn 的唯一治理入口 |
| Worker |  | ✓ | ✓ | ✓ | 🟡 | `WorkerSessionRouter`; Worker routing tests | 实际 Provider E2E 默认跳过 |
| Task / Revision |  | ✓ | ✓ | ✓ | 🟡 | `TaskRepository`, `TaskRevisionRepository` | B1 Assignment 与 legacy Task 的统一生命周期仍靠 bridge |
| Workspace |  | ✓ | ✓ | ✓ | 🟡 | `WorkerExecutionIdentity`, workspace baseline | 真实代码变更 Demo 未在本次复跑 |
| Worktree |  | ✓ | ✓ | 🟡 | 🟡 | Worker identity/contract tests | 主流程是否总是隔离 worktree 取决于 Git/UI 条件 |
| BaseCommit |  | ✓ | ✓ | ✓ | 🟡 | `WorkerExecutionIdentity.Start`; certification tests | 未证明 crash 后 workspace 与 DB 自动 reconciliation |
| Worker State Machine |  | ✓ | ✓ | ✓ | 🟡 | `WorkerExecutionRepository`, routing tests | Failed/Interrupted recovery 主要是状态恢复，不是完整重试 |
| Final Report |  | ✓ | ✓ | ✓ | 🟡 | `WorkerFinalReportReceived`; completion tests | legacy FinalReport 不自动生成 B1 Claim |
| Evidence |  | ✓ | ✓ | ✓ | 🟡 | `B1EvidenceRepository`; verification evidence | 真实 provider evidence 链未默认执行 |
| Handoff |  | ✓ | ✓ | ✓ | 🟡 | `GuidedHandoffComposerService`; B1 manual certification | certification 是 service-level，不是完整用户级 E2E；legacy handoff 不等价 |
| Summary |  | ✓ | ✓ | ✓ | 🟡 | `LeaderPaneViewModel` summary persistence; recovery tests | 失败时存在 pending summary，需要 recovery owner |
| Library |  | ✓ | ✓ | ✓ | ✓* | 2026-09-08 live UI acceptance record | ✓* 是 Evolution→Library 路径，不是完整 Worker Demo |
| Context Selection |  | ✓ | ✓ | ✓ | 🟡 | `LeaderBootContextBuilder`, continuity material service | B1 Worker context 主要读取 handoff，不是统一 selective packet |
| Claim |  | ✓ | ✓ | ✓ | 🟡 | `B1ClaimHandoffRepository`; manual/live B1 paths | service-level B1 path proven; legacy Worker 完成不会自动转 Claim |
| Authority Evaluation |  | ✓ | ✓ | ✓ | 🟡 | `B1AuthorityEvaluator`; authority tests | 真实 UI Worker 回路未统一接入 |
| Authority Commands |  | ✓ | ✓ | ✓ | 🟡 | `B1AuthorityCommandService`, `GuidedDecisionService` | 调用者分裂为手工 Guided UI 与 Leader draft |
| Authority History |  | ✓ | ✓ | ✓ | 🟡 | `b1_authority_decisions`, append transaction | service/restart proof exists; no full user-level Worker E2E |
| AcceptedProjectState |  | ✓ | ✓ | ✓ | 🟡 | `B1Projector`, restart certification | B1 state proven; complete Worker-originated route not proven |
| Projection |  | ✓ | ✓ | ✓ | 🟡 | `B1ProjectionService` | 未提供独立 replay command/verification |
| CAS |  | ✓ | ✓ | ✓ | 🟡 | project commit sequence and retry | 只覆盖 Authority project sequence，不覆盖所有 cross-spine mutation |
| Atomicity |  | ✓ | ✓ | 🟡 | 🟡 | handoff-and-select; authority transaction | Handoff/summary/library/worker 状态跨 repository 仍有半成功窗口 |
| Recovery |  | ✓ | ✓ | ✓ | 🟡 | provider-independent recovery certification | provider session 本身不可恢复时只能恢复持久 kernel |
| Session Rotation |  | ✓ | ✓ | ✓ | 🟡 | `LeaderSessionRolloverService`; rotation tests | 完整 A→B 无人工解释的真实 Provider E2E 未证明 |
| Evolution |  | ✓ | ✓ | ✓ | ✓* | 2026-09-08 live Candidate→Library record | Candidate 语义粒度仍明确为 partial boundary |
| Auto Advance |  | ✓ | ✓ | ✓ | 🟡 | `LeaderReviewAutoProceedExecutor` | 是 legacy review/task completion，不是 B1 Authority 后续 Assignment |
| Human Escalation |  | ✓ | ✓ | ✓ | 🟡 | ask-user gate/restart tests | B1 与 legacy escalation 尚未统一 |
| Provider Abstraction |  | ✓ | ✓ | 🟡 | 🟡 | `IAgentRuntime`, Codex/OpenCode adapters | Codex live skipped；OpenCode live skipped；Claude 无 adapter |

`✓*` 表示存在真实闭环证据，但只覆盖该行的特定路径，不代表全系统链路。

## 3. Full E2E Flow

### 3.1 当前可证明的 B1 手工/参与路径（非 Level 4 用户 E2E）

```text
Human
  ↓ 🟡
Project / B1 Governance
  ↓ 🟡
Authority Command
  ↓ ✅
AcceptedProjectState projection
  ↓ ✅
Assignment / Revision / Attempt
  ↓ ✅
B1 Agent Participation Adapter
  ↓ ✅
Agent Session / runtime turn
  ↓ 🟡（provider-neutral adapter/service path；默认测试非真实 provider）
Result Claim + Handoff + Evidence
  ↓ 🟡
Guided Decision
  ↓ 🟡
Authority Decision + append transaction
  ↓ 🟡
AcceptedProjectState
  ↓ 🟡
SQLite persistence
  ↓ 🟡
restart / reload
  ↓ 🟡
replacement context can read Project World
```

证据：`B1ManualContinuityCertificationTests`；其测试确实在关闭 service 后重新打开 DB，并继续创建后续 Delegation。但它是 service/certification path，不包含真实用户 UI、真实 Agent、真实 Worker code change，因此按本审计定义不能升为 Level 4。

### 3.2 当前真实 UI Worker 主链

```text
Human
  ↓ ✅
Leader structured draft_proposal
  ↓ ✅
Task + TaskRevision + user confirmation
  ↓ ✅
CanonicalWorkerLaunch（满足 Git 条件时）
  ↓ 🟡
WorkerSessionRouter
  ↓ ✅
AgentSession / WorkerExecution / BaseCommit / Workspace baseline
  ↓ ✅
Worker FinalReport
  ↓ ✅
Task review decision
  ↓ ✅
Summary consumer + legacy review/auto-proceed
  ↓ ✅
Task Completed
  ↓ ❌
B1 Claim/Handoff/Authority Decision
  ↓ ❌
B1 AcceptedProjectState update
```

Worker UI 的完成回调在 `WorkerSessionRouter` 中持久化 legacy `WorkerHandoff`、review event 和 Summary；B1 handoff 生成则位于 `B1AgentParticipationAdapter`/`GuidedHandoffComposerService`，没有发现 legacy Worker FinalReport 自动调用该 Composer 的证据。

### 3.3 Continuity Context

```text
Session / Epoch
  ↓ ✅
Summary / Handoff / Library / AcceptedProjectState repositories
  ↓ ✅
LeaderBootContextBuilder
  ↓ ✅
selected continuity materials
  ↓ 🟡
new Leader Session
  ↓ 🟡
new Worker
```

Selected context 已存在并有预算/来源标记，但 B1 Worker 的 `BuildContinuationContext` 主要是当前 selected Handoff 与 Claims；它不是完整的统一 `AcceptedState + Summary + Library + Evidence` packet。

## 4. Designed Architecture vs Actual Architecture

| 设计目标 | 当前实际 |
| --- | --- |
| Project 是长期一等实体，Agent/Session 可替换 | Project、leader epoch、task、worker execution、B1 state 均持久化；这是已实现的主方向 |
| Leader 读取 Project World 后分配 Assignment | B1 `CanonicalWorkerLaunchService` 可以这样做；普通 Leader UI 仍先生成 legacy Task/Revision，再在满足条件时 bridge 到 B1 |
| Worker FinalReport 自动进入 Claim/Handoff/Authority | B1 adapter 可以产生 Claim/Handoff；legacy Worker Router 产生 legacy handoff/review，未证明自动转 B1 |
| Authority History 唯一产生 AcceptedProjectState | B1 projection 按 authority state 构建；但旧 Task review/status/summary 仍是旁路事实体系 |
| Summary 是稀疏 continuity compression | 当前已接入 Leader turn 和 Worker completion consumer，并有 recovery |
| Library 与 Truth 分离 | 已实现；Library proposal acceptance 不等于 AcceptedProjectState |
| Candidate → Proposal/Authority → resolution | 当前已持久化、可 UI 接受并重启恢复；状态 active/history 已在当前修改中接入 |
| Provider-neutral real execution | `IAgentRuntime`、Codex、OpenCode 已有 adapter；默认测试使用 fake/offline，live tests 显式 Skip |
| 完整用户 Demo 可录制 | 自动彩排可启动/重定位/restart/resume；完整人工 Worker + governance + new session 仍待执行 |

## 5. Evidence Index

| 结论 | 文件 / 类型 / 方法 | 测试或记录 | 实际调用链 |
| --- | --- | --- | --- |
| Project 与 DB 独立存在 | `src/Workbench.Storage/Projects/ProjectRepository.cs`; `ProjectOpenService` | `ProjectRepositoryTests`, `ProviderIndependentProjectRecoveryCertificationTests` | Project open → DB reload |
| DB migration 当前版本 29 | `src/Workbench.Storage/Database/MigrationRunner.cs`; migrations 001–029 | `WorkbenchDatabaseTests`, historical migration tests | initialize → sequential migration |
| B1 state 持久化且 CAS commit | `src/Workbench.Storage/Continuity/B1AuthorityRepository.cs:33-75` | `B1AuthorityCommandServiceTests`, `B1AuthorityRepositoryTests` | command → evaluator → `TryCommitAsync` → transaction |
| B1 projection 来源于 Authority state | `src/Workbench.App/Continuity/B1ProjectionService.cs`; `B1Projector` | B1 projection/recovery tests | load state → projector → AcceptedProjectState |
| B1 Agent 会读取 Accepted Assignment/Revision | `src/Workbench.App/Continuity/B1AgentParticipationAdapter.cs:57-76` | B1 participation tests | runtime request → projection → assignment/revision |
| B1 handoff 结构化生成 Claims | `src/Workbench.App/ProjectWorld/GuidedHandoffComposerService.cs` | `B1ClaimHandoffRepositoryTests`, manual certification | final text → typed claims → handoff-and-select |
| legacy Worker execution 持久化 | `src/Workbench.App/Worker/WorkerSessionRouter.cs:571-870` | `WorkerSessionRoutingTests`, canonical certification | start → execution → session → final report → handoff/review |
| legacy review 自动推进 | `src/Workbench.App/Leader/LeaderReviewOrchestrator.cs`; `LeaderReviewAutoProceedExecutor.cs` | review auto-proceed tests | final report → Leader review → Task Completed |
| Worker completion Summary | `src/Workbench.App/ViewModels/Panes/LeaderPaneViewModel.cs:1017-1045`; `WorkerCompletionSummaryConsumer` | `WorkerCompletionSummaryConsumerTests`, summary recovery tests | completed turn/final report → summary entry |
| Selected context | `src/Workbench.App/Leader/LeaderBootContextBuilder.cs:48-108` | boot/context selection tests | epoch/plan → memory API → selected materials → AgentRequest |
| Session rotation | `src/Workbench.App/Leader/LeaderSessionRolloverService.cs`; `LeaderPaneViewModel.cs:1168-1240` | rollover/recovery tests | user action → policy → rollover → new epoch |
| Evolution candidate durable lifecycle | `src/Workbench.App/ViewModels/Panes/LeaderPaneViewModel.cs:900-947`; `ProjectEvolutionCandidateRepository` | evolution/library tests; current working-tree diff | structured response → candidate → governance route |
| Library live UI closure | `docs/validation/core-continuity-governance-loop-live-verified-20260908.md` | live record says real GPT-5.6/UI/restart | Candidate → Library proposal → UI accept → restart |
| Real provider verification boundary | `tests/Workbench.App.Tests/Continuity/OpenCodeLiveB1AcceptanceTests.cs:12`; Codex live tests | tests are `[Fact(Skip=...)]` | no default live provider execution |
| Current UI regression failures | `tests/Workbench.App.Tests/LeaderPaneViewTests.cs:92-110` | full App suite | markup count/order assertions fail |

### Authority bypass audit

正式 B1 `AcceptedProjectState` 的 runtime 写入路径在当前代码中集中于 `B1AuthorityRepository.TryCommitAsync` 的 Authority transaction；`B1ProjectionService` 只读投影。Library、Summary、legacy Task review 和 Evolution Candidate repository 是独立 projection/working-set，不应被误写成 AcceptedProjectState。

需要持续警惕的旁路不是普通 runtime service 直接写 AcceptedProjectState，而是把 legacy `TaskLifecycleStatus.Completed`、Summary 或 Library acceptance 误当成 Authority acceptance。另有 `Migration029EvolutionCandidateConsideredRefs` 在历史迁移中直接规范化 `b1_accepted_state_contributions.statement`；这是 migration-time data normalization，不是运行时绕过 Authority，但说明“append-only/immutable”不能被表述成数据库物理不可变。

## 6. Tests

实际执行：

```text
dotnet build AI.Game.Workbench.sln --no-restore
成功：0 warnings, 0 errors

dotnet test AI.Game.Workbench.sln --no-restore --verbosity minimal
首次并行执行因 build/test DLL 文件锁退出，不作为产品失败计入。

dotnet test AI.Game.Workbench.sln --no-build --verbosity minimal
Core:    128 passed, 0 failed, 0 skipped
Runtime: 66 passed, 0 failed, 1 skipped
Project: 28 passed, 0 failed, 0 skipped
Storage: 316 passed, 0 failed, 0 skipped
App:    584 passed, 2 failed, 3 skipped
总计：1122 passed, 2 failed, 4 skipped

dotnet test tests/Workbench.App.Tests/Workbench.App.Tests.csproj --no-build
  --filter "FullyQualifiedName~Certification|FullyQualifiedName~Continuity|FullyQualifiedName~ProjectRecovery|FullyQualifiedName~LeaderReviewAuthorityRestart|FullyQualifiedName~LiveLeader"
认证子集：111 passed, 0 failed, 3 skipped
```

分类判断：

| 类别 | 当前证据 |
| --- | --- |
| Unit | Core、Runtime contract、parser、state machine 覆盖较好 |
| Storage integration | Storage 316 全通过，含 migration/reopen/repository |
| Runtime integration | Codex protocol tests 多为 fake transport；real Codex 1 skipped |
| App integration | App 584 通过，但 2 个 UI markup tests 失败 |
| Certification | B1 manual、recovery、authority restart 等通过 |
| Fake Agent | 大量，证明 deterministic lifecycle |
| Real Agent | 记录中有真实 GPT-5.6 Library closure；当前默认命令不重演 |
| UI | Library closure 有真实 UI 记录；当前 suite 有 2 个 UI assertion failure |
| Recovery | provider-independent project/review/B1 restart 覆盖 |
| Concurrency | Authority sequence/CAS 有测试；没有完整多 Worker crash/reconciliation 认证 |
| E2E | B1 manual/adapter 与 Evolution→Library 有闭环证据；完整代码修改 Demo 无 |

两项当前失败均在 `LeaderPaneViewTests`：状态滚动区域顺序断言失败，以及预期 `Foreground="#101828"` 出现 9 次但实际 8 次。它们使完整 App suite 不能宣称全绿。

## 7. Top 10 Gaps

1. **P0 Integration gap：legacy Worker FinalReport 未证明自动进入 B1 Claim/Handoff/Authority。** 当前两条 execution/governance spine 并存。
2. **P0 E2E gap：没有当前工作树下完整“真实用户改代码并更新测试 → restart → 新 Leader/Session 继续”的现场证据。**
3. **P1 Architecture gap：Task/TaskRevision/WorkerExecution 与 Assignment/Revision/Attempt 两套模型通过 bridge 连接，统一语义仍不完整。**
4. **P1 E2E gap：Codex/OpenCode live acceptance 默认跳过；真实 provider 的完整 Worker/Authority 路径无法由默认 suite 重现。**
5. **P1 Integration gap：Auto Advance 当前完成的是 legacy Assignment/Task review completion，不是 Authority 接受后的 B1 successor Assignment 自动创建/下发。**
6. **P1 Atomicity gap：Handoff、Summary、Library、legacy Task status、B1 state 跨多个 repository；存在跨 spine 半成功/待 recovery 窗口。**
7. **P1 Recovery gap：已有状态恢复，但未证明 crash 后 workspace、execution、final report、handoff、projection 能自动 reconciliation。**
8. **P2 Provider gap：Codex/OpenCode 有 adapter；Claude/Claude Code、OpenAI Agents SDK、自定义 provider 没有被当前代码和测试分别证明为 integrated provider。**
9. **P2 Research/granularity gap：Evolution Candidate 的语义粒度与 stale/impact policy 仍被 validation 明确列为边界，不应宣称自动严重度治理完成。**
10. **P2 UX gap：完整 App suite 有 2 个 Leader pane markup assertion failures；Demo UI 的视觉/结构验收尚未全绿。**

### Architecture gap

双 spine：legacy Leader review/Task lifecycle 与 B1 Authority/AcceptedProjectState 同时存在；二者有 bridge，但没有单一 canonical completion event。

### Integration gap

真实 UI Worker 确实走 `WorkerSessionRouter`，但 B1 `GuidedHandoffComposerService` 主要由 B1 participation/manual path 使用。

### E2E gap

当前最强证据分别是 B1 service-level restart certification 与 Evolution→Library live UI closure，尚未是完整 Worker code-change continuity demo。

### UX gap

Leader pane 的两个 markup test 失败；这不否定核心机制，但否定“当前 App suite 全绿”。

### Research / granularity gap

Candidate 的持久性、provenance 和 active/history 分离已有实现；复杂的 stale、merge、supersede、impact policy 仍不是完整研究结论。

## 8. Demo Readiness

**ALMOST READY。**

可以诚实展示：

- Project 持久化与重启恢复；
- B1 Assignment/Revision/Attempt；
- Claim/Handoff/Authority/AcceptedProjectState；
- Leader Summary/Library/Evolution；
- 真实 UI 的 Evolution Candidate → Library Proposal → Accept → restart；
- Worker execution identity、BaseCommit、workspace baseline、FinalReport/review/auto-proceed。

尚不能无造假地展示为一个已经被当前审计完整证明的单一路径：

```text
Human
→ Leader
→ real code-changing Worker
→ test evidence
→ B1 Claim/Handoff
→ Authority
→ AcceptedProjectState
→ Summary/Library
→ new Session
→ restart
→ next Worker continues
```

最少 blocker：

1. 让真实 UI Worker completion 明确接入 B1 typed handoff/claim，或明确证明当前 demo 使用的是 B1 Agent Participation path；
2. 用真实 provider 跑一次代码修改与测试，不使用 fake-only completion；
3. 关闭应用并启动 replacement Leader/Session，验证四项 continuity facts 与下一 task；
4. 修复两个 Leader pane UI test failures。

## 9. What We Can Honestly Claim

- “Workbench 已实现并测试了 Project-level durable state、B1 Authority、Claim/Handoff、AcceptedProjectState projection 和 restart recovery。”
- “Workbench 已实现 Leader boot context 的选取式 continuity material，并持久化 Summary、Library、Evolution Candidate。”
- “B1 manual continuity certification 已证明 Authority state、Handoff selection、Accepted contribution 可跨 restart 恢复。”
- “Evolution Candidate → Library Proposal → visible UI acceptance → durable Library provenance → restart recovery 已有真实 2026-09-08 记录。”
- “Worker execution 已记录 Task/Revision、BaseCommit、branch/worktree identity、Agent Session、FinalReport、validation evidence 和 review state。”
- “Codex 与 OpenCode runtime adapter 存在并有 deterministic contract/integration coverage；live provider acceptance 是独立 gate。”

## 10. What We Must NOT Claim Yet

- “完整解决长期 Agent 协作。”
- “所有 Worker 完成都会自动进入 Authority。”
- “Leader 已统一管理 B1 Assignment、legacy Task、Claim、Handoff 和 AcceptedProjectState。”
- “Codex、Claude、OpenAI Agents SDK、自定义 Provider 都已支持并通过真实 E2E。”
- “完整代码修改 Demo 已经从 Human 跑通到新 Session 继续。”
- “所有项目状态都只能通过 Authority mutation 修改。”
- “Crash/restart 后 Workspace、FinalReport、Handoff、Projection 自动原子恢复。”
- “自动推进已经生成并执行 Authority-approved 的下一 B1 Task。”
- “当前完整 App test suite 全部通过。”

## Appendix: Database Schema and Main Relations

当前 migration chain 为 `Migration001Initial` 至 `Migration029EvolutionCandidateConsideredRefs`，`PRAGMA user_version = 29`。

主要关系：

```text
projects
 ├─ project_layouts
 ├─ leader_session_epochs ── leader_messages
 ├─ tasks ── task_revisions
 │        ├─ worker_executions ── worker_completion_packages
 │        ├─ task_events
 │        └─ task_review_decisions ── task_review_user_gates
 ├─ project_summary_entries ── project_summary_source_refs
 ├─ project_library_objects
 │    ├─ project_library_timeline_nodes
 │    │    └─ project_library_material_refs
 │    └─ project_library_proposals
 ├─ project_evolution_candidates
 └─ B1 project world
      └─ b1_project_governance
           ├─ b1_authority_decisions
           │    └─ b1_decision_considered_refs
           ├─ b1_logical_actors
           ├─ b1_responsibilities
           ├─ b1_assignments ── b1_revisions ── b1_revision_dispositions
           ├─ b1_attempts ── b1_session_bindings
           ├─ b1_claims
           ├─ b1_handoffs ── b1_handoff_claim_refs
           ├─ b1_accepted_state_contributions
           ├─ b1_assignment_routing
           ├─ b1_attempt_routing
           ├─ b1_worker_task_links
           ├─ b1_worker_execution_links
           ├─ b1_worker_session_links
           └─ b1_worker_execution_evidence
```

Migration tests cover historical databases and multiple destructive table rebuilds. This is good evidence for sequential upgrades, but not proof that every arbitrary production-era database with external corruption or manually edited rows can be upgraded.
