# AI Game Workbench 项目审计：现状与目标功能对齐度

审计日期：**2026-09-04**
审计对象：`C:\Users\pupet\Documents\ChatGPT\AI Game Workbench` 当前工作树
目标：核对技术说明中的目标功能、架构约束、已验收声明与当前代码/测试/运行证据是否一致。

## 结论摘要

**总体判断：部分对齐。**

Workbench 的核心架构方向已经落地：Project World、Authority Decision、Accepted Project State、可恢复投影、非权威 Claim/Handoff、Single Instance，以及 OpenCode 的 B1 participation path 均有实现和测试证据。

但当前仍是一个**新 B1 脊柱与旧 Leader/Worker/Memory 产品路径并存的过渡系统**。因此不能把“架构边界已实现”直接等同于“所有用户工作流都已经统一接入 B1”。尤其是：

1. B1 Agent participation 会记录 Attempt、SessionBinding、Claim/Handoff，但不会执行 WorkerCompletionVerifier 的语义验收，也不会自动产生 AuthorityDecision。
2. “OpenCode/DeepSeek → Codex 接力”在测试代码中仍由 `WORKBENCH_RUN_CODEX_LIVE=1` 条件控制，本次审计只实跑并确认了 OpenCode live path，未重新证明 Codex live continuation。
3. Acceptance content 当前明确返回 `NotVerifiable`；路径存在性和修改范围通过，不等于交付内容被机器证明。
4. 默认全套测试中的 live-provider 测试通过提前 `return` 跳过实际执行，却仍计为 Passed，不能把总通过数当作 live-provider 证据。

因此，适合对外/对导师的表述是：

> **Project-level authority and recovery spine 已基本对齐；完整的多 Agent 产品闭环和运行时交互能力仍处于部分对齐/过渡状态。**

## 审计口径

- **Aligned**：目标行为在生产代码中存在，并有针对性测试或可复现运行证据。
- **Partial**：核心机制存在，但只覆盖部分路径、仍与 Legacy 并存，或缺少关键端到端证据。
- **Gap / Not proven**：目标仍是设计声明、能力受运行时限制，或当前证据不足以支持该声明。
- 本报告区分**当前工作树**与历史 Alpha/封存 HEAD。当前工作树是 dirty，不能当作已发布版本。

## 目标—现状对齐矩阵

| 目标功能/不变量 | 当前证据 | 对齐度 | 审计判断 |
|---|---|---:|---|
| Project-first：Project 不依赖 Agent/Session 存活 | `B1Projector`、B1 recovery tests、架构 ownership map | **Aligned** | Accepted state 从持久化 AuthorityDecision 重建；没有独立 writer。 |
| Authority Spine：Claim/Handoff 不直接成为 Truth | `B1AuthorityEvaluator`、`B1AuthorityCommandService`、B1 command tests | **Aligned** | AuthorityDecision 是正式状态变化入口。 |
| Context Spine：Accepted State / Library / Summary 分层 | `B1ProjectionService`、Library projection tests、当前 UI 投影 | **Partial** | B1 accepted-state/Library 投影已接通；Legacy Memory、Daily Summary、R5-A Summary 仍共存，完整 B1 Agent/Manual 体验仍是 transitional。 |
| Manual Project World 初始化与恢复 | `B1ManualContinuityCertificationTests`、`ManualProjectSliceCertificationTests` | **Aligned** | Manual path 的 zero-Session flow 与 restart recovery 有证据。 |
| Agent participation：Attempt → SessionBinding → Claim/Handoff | `B1AgentParticipationAdapter`、adapter tests、OpenCode live test | **Aligned（边界内）** | 参与链已实现；明确不授予 Authority。 |
| Worker execution：Task/Revision/Execution/Session/恢复 | `CanonicalWorkerExecutionCertificationTests`、typed repositories | **Aligned（Legacy Worker path）** | 旧 Worker execution typed lifecycle 可恢复，不能据此宣称所有 B1 Agent execution 都经过同一条路径。 |
| Workspace Baseline / Execution Delta | `WorkspaceSnapshot`、Migration026、worker verification tests | **Aligned（路径级）** | 能按 baseline hash 归因文件变化；只覆盖可观察文件路径。 |
| Completion Verification | `WorkerCompletionVerifier`、`WorkerSessionRouter`、verification tests | **Partial** | deliverable existence 与 scope 可检查；acceptance content 固定为 `NotVerifiable`，不是完整机器验收。 |
| Handoff continuity / agent replacement | B1 adapter continuation tests；OpenCode live test | **Partial** | OpenCode live path 本次通过；Codex live continuation 需另开环境变量，本次未重验。 |
| Authority 与 Execution Approval 分离 | B1 adapter 拒绝自动 approval；Legacy approval tests | **Aligned** | Provider action approval 不等于 Project authority。 |
| Single Instance / Window 与 Host 解耦 | `Program.cs`、`SingleInstanceCoordinator`、coordinator tests | **Aligned** | Mutex + named pipe 激活路径存在；本次未做人工双进程 UI smoke。 |
| Host lifecycle observability | Runtime status/stop contracts；文档 Phase 5 | **Partial / Frozen** | Stop/Interrupt 有实现；Steer 默认 NotSupported，AskUser 依赖 provider capability；Steer 异常曾发生且原因未定。 |
| Provider-neutral runtime | Codex runtime、OpenCode ACP runtime、`IAgentRuntime` | **Partial** | Codex 为 primary，OpenCode 已接入；Claude/Kimi/DSH 等仍是目标，不是 Alpha acceptance。 |
| Provenance / evidence | Claims、Handoffs、EvidenceRefs、typed execution identity | **Partial** | 来源链可记录；外部 locator、commit、test claim 不由系统独立证明。 |
| Token efficiency / semantic drift reduction | 分层 context 设计与文档 | **Gap / Not measured** | 是设计目标，没有 benchmark，不能宣称固定节省比例或已证明降低 drift。 |

## 关键发现

### A-01 [P1] B1 Agent path 与 Worker Verification path 尚未统一

`B1AgentParticipationAdapter` 的职责是创建 Attempt、绑定外部 Session、运行 Agent、记录非权威 Handoff；其代码注释明确写明没有 AuthorityDecision 写入路径（[B1AgentParticipationAdapter.cs](/C:/Users/pupet/Documents/ChatGPT/AI%20Game%20Workbench/src/Workbench.App/Continuity/B1AgentParticipationAdapter.cs:31)）。

而 `WorkerCompletionVerifier` 由 `WorkerSessionRouter` 的旧 Worker 路径调用（[WorkerSessionRouter.cs](/C:/Users/pupet/Documents/ChatGPT/AI%20Game%20Workbench/src/Workbench.App/Worker/WorkerSessionRouter.cs:714)），两者不是同一个端到端管线。

**影响：** 技术说明中“Phase 3 Worker execution + verification PASS”只能代表 Legacy Worker execution 的验收，不应扩展解释为“所有 B1 Agent participation 都已完成独立验证”。

**建议：** 后续明确选择其一：

- 将 B1 Agent completion verification 作为单独 Application service 接入；或
- 在导师材料中把“B1 participation”和“Legacy Worker verification”拆成两个能力，不称为统一闭环。

### A-02 [P1] 默认 live-provider 测试存在“静默通过”语义

`OpenCodeLiveB1AcceptanceTests` 在环境变量不是 `1` 时直接 `return`，但测试仍会被 xUnit 计为 Passed（[OpenCodeLiveB1AcceptanceTests.cs](/C:/Users/pupet/Documents/ChatGPT/AI%20Game%20Workbench/tests/Workbench.App.Tests/Continuity/OpenCodeLiveB1AcceptanceTests.cs:11)）。

因此默认全套的 `Passed` 数字不证明 OpenCode/DeepSeek 实际运行。本次显式设置 `WORKBENCH_RUN_OPENCODE_LIVE=1` 后，专项测试确实通过 1/1；两者必须在报告中分开写。

**建议：** 把未启用 live provider 的情况改成显式 Skip/Requires，或拆成不进入默认 suite 的 integration test，避免“绿色但未执行”。

### A-03 [P1] “跨 Agent 接力”证据是条件化的

专项测试默认执行 OpenCode/DeepSeek；只有设置 `WORKBENCH_RUN_CODEX_LIVE=1` 才会创建 Codex runtime 并执行第二次 continuation（[OpenCodeLiveB1AcceptanceTests.cs](/C:/Users/pupet/Documents/ChatGPT/AI%20Game%20Workbench/tests/Workbench.App.Tests/Continuity/OpenCodeLiveB1AcceptanceTests.cs:58)）。

本次审计确认了 OpenCode live path；没有在当前环境中重新确认 Codex live continuation。因此“OpenCode → Codex 已真实跑通”应标为历史/条件化证据，而不是本次 fresh audit 的无条件结论。

### A-04 [P2] Completion Verification 目前是“路径/范围检查”，不是语义验收

`WorkerCompletionVerifier` 对目标文件存在性和 changed paths 做确定性检查，但 acceptance-content 永远记录为 `NotVerifiable`（[WorkerCompletionVerifier.cs](/C:/Users/pupet/Documents/ChatGPT/AI%20Game%20Workbench/src/Workbench.App/Worker/WorkerCompletionVerifier.cs:140)）。

这与“Agent 自报完成不等于事实”原则一致，但意味着 `PASS` 只能理解为部分检查通过；不能把它称作完整 deliverable verification。

### A-05 [P2] 当前工作树可复现性低于已发布版本

当前工作树包含大量未提交 source/test/generated artifact 变化；`docs/validation/current-working-tree-20260904.md` 也明确标记 dirty working tree。当前结果不能直接归属于 `v0.1.0-alpha.20260827` 或 sealed HEAD。

建议每次对外验收同时记录：commit/ref、dirty 状态、环境变量、provider executable/version、数据库路径及专项日志。

### A-06 [P2] Runtime capability 仍是 provider-specific

`IAgentRuntime` 对 Steer 与 RespondToQuestion 提供默认 `NotSupportedException`（[IAgentRuntime.cs](/C:/Users/pupet/Documents/ChatGPT/AI%20Game%20Workbench/src/Workbench.Runtime/Runtime/IAgentRuntime.cs:33)）。这与文档中 Phase 5 的 `AskUser = capability limitation`、`Steer = inconclusive/frozen` 一致，但与“通用 Agent surface”目标尚未完全对齐。

## Fresh verification

本次审计执行：

```powershell
dotnet build .\AI.Game.Workbench.sln --no-restore --verbosity minimal
dotnet test .\tests\Workbench.Core.Tests\Workbench.Core.Tests.csproj --no-build --no-restore --verbosity minimal
dotnet test .\tests\Workbench.Runtime.Tests\Workbench.Runtime.Tests.csproj --no-build --no-restore --verbosity minimal
dotnet test .\tests\Workbench.Project.Tests\Workbench.Project.Tests.csproj --no-build --no-restore --verbosity minimal
dotnet test .\tests\Workbench.App.Tests\Workbench.App.Tests.csproj --no-build --no-restore --verbosity minimal
dotnet test .\tests\Workbench.Storage.Tests\Workbench.Storage.Tests.csproj --no-build --no-restore --verbosity minimal
$env:WORKBENCH_RUN_OPENCODE_LIVE='1'
dotnet test .\tests\Workbench.App.Tests\Workbench.App.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~OpenCodeLiveB1AcceptanceTests' --verbosity minimal
```

结果：

- Build：**通过，0 warning，0 error**。
- Core：**128/128 passed**。
- Runtime：**67/67 passed**。
- Project：**28/28 passed**。
- App：**543/543 passed**。
- Storage：**307/307 passed**。
- 组件测试合计：**1,073 passed**。
- OpenCode live 专项：**1/1 passed**，使用 `WORKBENCH_RUN_OPENCODE_LIVE=1`；这证明 OpenCode live path，不证明 Codex continuation。

本次直接运行 solution 级 `dotnet test` 时，在若干项目输出后未正常收尾，已停止挂起进程；因此本报告将**组件级结果**作为 fresh evidence，不把一次未收尾的 solution 级命令写成完整通过。

## 最终判定

| 层级 | 判定 |
|---|---|
| 架构意图与核心 B1 不变量 | **基本对齐** |
| Authority / Accepted State / restart recovery | **对齐** |
| Manual project-world slice | **对齐** |
| OpenCode B1 participation | **已验证，但范围受限** |
| Legacy Worker execution + path/scope verification | **对齐，但不是 B1 Agent 统一闭环** |
| 完整多 Agent、跨 Provider 接力 | **部分对齐，条件化验证** |
| 运行时 Steer / AskUser / lifecycle observability | **部分对齐/冻结** |
| 完整语义验收与 Token benchmark | **未完成/未测量** |

**审计结论：当前项目可以诚实地称为“已落地 Project Continuity + Authority 基座的 Windows-first Alpha/working-tree reference implementation”；不能称为“所有目标 Agent 工作流已经统一接入并完成通用运行时闭环”。**

## 建议的下一步验收门槛

1. 将 live-provider 测试改为显式 Skip 或独立 integration suite，消除静默通过。
2. 为 B1 Agent participation 增加独立的 completion verification contract，或者在文档中明确它不属于 Worker verification。
3. 在同一份可保存日志中重跑 OpenCode → Codex continuation，并记录 provider/model/session binding 数量。
4. 为 acceptance criteria 增加结构化检查类型；在此之前统一使用 `NotVerifiable`，禁止把路径 PASS 写成整体 PASS。
5. 产出一次 clean commit/tag 验收包，隔离 generated artifacts，并固定数据库、Provider、环境变量和测试版本。
