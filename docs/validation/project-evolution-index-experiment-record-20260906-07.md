# Project Evolution Index 实验总记录

日期：2026-09-06 至 2026-09-07  
范围：Workbench Project Evolution Index、Leader Evolution Candidate、Governance Gateway  
状态：实验轮完成；代码和文档已提交；自动治理提交仍未开启。

## 实验问题

Workbench 要解决的不是“Agent 能不能记住更多聊天”，而是：

> Agent 能不能知道项目从状态 A 经过哪些合法变化，成为当前状态 B。

因此 Evolution Index 被定义为解释和导航层，不是新的 Memory、Truth、Summary、Event Store 或事实拥有者。

核心分层保持为：

```text
Truth     项目不能违背的正式约束
Library   项目当前有什么
Evolution 项目为什么变成现在这样
Source    为什么相信这次变化
Session   交互和执行发生在哪里
```

## Phase 1：架构审计

目标是确认现有 Workbench 是否已经拥有生成确定性 Evolution 投影所需的数据。

审计结论：

- B1 Authority / AcceptedProjectState 可提供正式约束和决策来源；
- Library Object、Overview、Timeline Node、Material Ref 可提供当前项目材料；
- Task、TaskRevision、Worker event、Handoff、Artifact locator 可提供执行和完成来源；
- Leader epoch / session 可提供交互定位；
- 不需要新增 Evolution 表、Event Store、Memory 表或 Object Registry；
- 最小实现应放在 Application 层的只读 query/aggregation service。

审计报告：[Phase 1 Architecture Audit](./project-evolution-index-phase1-audit-20260906.md)

## Phase 2-A：确定性只读投影

实现了 `ProjectEvolutionIndexQuery`，从现有来源生成短导航记录：

```text
Authority
Library
Worker / Task
Artifact reference
```

它保留每个来源的真实身份，不把 Legacy Worker、Library、B1 Authority 压成统一 Event 身份。

验证重点：

- AuthorityDecision 只投影为 Authority 记录；
- Library projection 不自动变成 AcceptedProjectState；
- Worker FinalReport / Handoff 保留 Worker 来源；
- Workspace artifact 不覆盖持久化 Library 状态；
- 查询是只读且有记录上限；
- 没有新增持久化。

## Phase 2-B：项目使用验证

使用《零刻》现有项目状态验证新 Leader 的恢复能力。

Evolution Timeline 能够帮助 Leader 快速回答：

- 当前正式约束是什么；
- Chapter 1 是否完成；
- `chapter-01.md` 来自哪里；
- Library 内容是否只是 Proposal / Recommendation；
- Worker 结果是否已经交接。

同时确认当前来源仍无法稳定表达：

- 被拒绝或被替代的完整语义链；
- 删除的 Material Ref；
- Library Overview 的完整 before/after；
- 尚未进入任何正式命令的讨论变化。

结论：确定性投影足以支撑第一版导航，但不是完整 Event Sourcing。

报告：[Phase 2-B Usage Validation](./project-evolution-index-phase2b-validation-20260906.md)

## Phase 3：Candidate Detector

Leader Structured Output 增加了受限的 `evolution_candidates` 数组。

Candidate 必须包含：

```text
object
object_kind
change_type
before
after
impact_class
route_hint
reason
source_ref
```

约束：

- 必须绑定对象；
- 必须有来源定位；
- 最多三个；
- 只表示观察到的可能变化；
- 不进入 Truth、Library 或 AcceptedProjectState；
- 不触发治理；
- 不允许把 Worker、Task、Assignment、Artifact 确定性变化伪装成语义 Candidate。

第一次真实实验验证了 GPT-5.6-sol 可以从项目上下文和用户输入中抽取对象、before/after、影响范围和建议路径。

## Phase 3.1：治理边界加固

早期真实测试发现，弱内容意见可能同时产生 Candidate 和 Legacy `draft_proposal`，从而误创建 Worker Task Draft。

修复内容：

1. Candidate 与 Worker Draft 通道分离；
2. 同一轮存在 Candidate 时，不物化 Legacy Worker Draft；
3. `impact_class` 与 `route_hint` 使用确定性策略矩阵校验；
4. 冲突路由进入人工复核，不静默改写；
5. Candidate 不允许直接创建 Task、Assignment、WorkerExecution 或 Worker Session；
6. Boot Context 和 Leader Skill 明确 Candidate 不等于执行请求；
7. 同轮出现 Candidate 时，`authority_confirmation` 和 `memory_commands.library_proposal` 也不进入治理入口，必须等待后续明确请求。

最后一条是在后续压力测试中补上的保护：模型可能把角色或内容变化错误地同时输出为 Authority Confirmation；应用层现在会丢弃该同轮治理命令，只保留 Candidate 和提示。

## Phase 3.2：真实 Governance Gateway 压力测试

测试使用真实本机 Codex app-server 和 GPT-5.6-sol。每个案例使用独立临时数据库和项目目录，测试结束检查：

```text
Task
TaskRevision
WorkerExecution
WorkerSessionStarted
AuthorityDecision
Library Object
```

不得出现未授权变化。

第一轮四案例：

| 案例 | 结果 |
|---|---|
| 无限计算器控制梦境的头脑风暴 | 空 Candidate |
| 时间礼仪课感觉太少 | 识别为 Content，但 route 为 NoGovernance，策略拦截 |
| 参考《沙丘》的政治氛围 | 误判为 WorldRule + Authority |
| 已接受单一时间线后提出平行宇宙 | 正确识别为 WorldRule + Authority |

四个案例均没有产生 Task、WorkerExecution 或 WorkerSessionStarted。

详细报告：[Phase 3.2 Pressure Test](./project-evolution-index-phase3-2-pressure-test-20260906.md)

## Signal / Candidate 收敛实验

随后运行了 20 个真实案例，覆盖：

- 明确世界规则修改；
- 已有状态下的反向修改；
- 角色职业修改；
- 内容增加；
- 架构边界修改；
- 外部作品引用；
- 内容评价；
- 赞扬；
- 头脑风暴；
- 措辞润色；
- 明确修改文件；
- 明确创建 Worker 任务；
- Worker 完成报告；
- 当前状态复述。

主要结果：

- 明确世界规则修订可稳定生成 `WorldRule` Candidate；
- 带有 AcceptedProjectState 时，模型可以识别真正的 state-aware revision；
- 评价、赞扬、头脑风暴、措辞讨论和状态复述大多为空 Candidate；
- 明确创建 Worker 任务会进入原有 Worker Draft 通道，且不会启动 Worker；
- 外部参考的判断不稳定，有时为空，有时误升级为 Content；
- 明确“修改文件”有时会被错误带入 Candidate，但路由冲突会被拦截；
- Candidate 与错误的同轮 Authority Confirmation 现在不会显示待确认入口。

实验中还发现过一次测试夹具问题：Codex runtime 仍持有临时项目目录，导致目录清理失败。将 runtime 改为每个案例独立创建并在目录销毁前释放后，测试正常完成。另一个夹具误报是把明确创建 Task 的合法 Worker Draft 当成副作用，随后改为允许该案例创建一个 Draft，但仍要求不得启动 Worker。

详细报告：[Signal / Candidate Convergence](./project-evolution-index-signal-candidate-convergence-20260906.md)

## 当前结论

已经验证：

- 确定性 Evolution Projection 可行；
- 强模型可以发现对象绑定的状态变化；
- State-aware Candidate 可行；
- Signal、Candidate、治理建议和执行链可以隔离；
- 路由冲突可以由应用层阻止；
- 模型犯错时不会直接污染 Project World。

仍未解决：

- 外部灵感引用与项目事实变化的稳定区分；
- 明确执行措辞与 Candidate 的稳定区分；
- `CharacterOrObject`、`Content`、`Architecture` 的统一路由准确率；
- Candidate 的去重、过期、拒绝和持久化策略；
- Candidate 到真实 Library Proposal / Authority Confirmation 的自动关联。

因此当前产品边界冻结为：

```text
Project World
    ↓
Deterministic Evolution Index
    ↓
Leader Evolution Candidate
    ↓
Impact Classification
    ↓
Governance Route Suggestion
```

到此停止。`Governance Route Suggestion` 仍是只读建议，不自动提交 Library Proposal，不自动写入 Authority，不自动创建 Worker。

## 代码与文档变更

核心代码：

- `src/Workbench.App/ProjectWorld/ProjectEvolutionIndexQuery.cs`
- `src/Workbench.App/Leader/LeaderGovernanceRouting.cs`
- `src/Workbench.App/Leader/LeaderDraftProposalBuilder.cs`
- `src/Workbench.App/Leader/LeaderBootContextBuilder.cs`
- `src/Workbench.App/ViewModels/Panes/LeaderPaneViewModel.cs`
- `src/Workbench.App/Views/ProjectWorldExplorerView.axaml`
- `src/Workbench.App/Views/Panes/LeaderPaneView.axaml`
- `skills/workbench-leader/SKILL.md`

主要测试：

- `tests/Workbench.App.Tests/ProjectEvolutionIndexQueryTests.cs`
- `tests/Workbench.App.Tests/LeaderGovernanceRoutingTests.cs`
- `tests/Workbench.App.Tests/Worker/LeaderDraftProposalTests.cs`
- `tests/Workbench.App.Tests/LeaderBootContextBuilderTests.cs`
- `tests/Workbench.App.Tests/LeaderPaneViewTests.cs`
- `tests/Workbench.App.Tests/ProjectWorldExplorerViewModelTests.cs`

## 最终验证

- focused tests：53 通过；
- 完整 `Workbench.App.Tests`：574 通过、3 跳过、0 失败；
- solution build：0 警告、0 错误；
- `git diff --check`：通过；
- 真实 GPT-5.6-sol 压力测试：完成，使用隔离临时项目和数据库；
- 生产数据库：未修改；
- 未新增 Event Store、Evolution 表、Memory 表、Truth 表或 Object Registry；
- 未提交或推送任何外部系统变更。
