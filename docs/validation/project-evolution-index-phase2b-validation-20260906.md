# Project Evolution Index — Phase 2-B Usage Validation

日期：2026-09-06
状态：只读验证完成；未修改代码、数据库或持久化语义。

## 结论

Phase 2-A 的确定性投影已经足够支撑《零刻》当前的项目演化导航 Demo，但它证明的是“已有事实可以被重新排列和解释”，不是“系统已经拥有完整历史事件溯源”。

推荐结论：**通过 Phase 2-B 使用验证，暂不进入 Leader Evolution Candidate。**

原因：

- 新 Leader 可以从持久化 Authority、Library、Worker/Handoff 和 Artifact locator 看到当前项目状态；
- Workspace JSON 不属于索引来源，因此不会覆盖当前 Workbench 持久化状态；
- B1、Library、Legacy Worker 的身份仍然分离；
- 当前投影对“当前记录”和“确定性完成结果”有效，但对被替代/被删除/讨论中方案等语义历史仍然没有稳定记录；
- 这些缺口应先通过使用案例确认，再决定是否设计 Candidate，而不是现在自动补写历史。

## 《零刻》实际持久化证据

项目：`零刻`

Project ID：`db5333e2-573d-432f-b457-1fc3b063a122`

项目目录：`C:\Users\pupet\Desktop\零刻`

主库（只读打开）：`C:\Users\pupet\AppData\Local\AI Game Workbench\workbench.db`

| 来源 | 数量 | 关键证据 |
|---|---:|---|
| B1 AuthorityDecision | 2 | `3d1e8e19-1fc0-4790-9e4b-bb0fcda4519e`、`a67afeb1-9026-46d4-a1cd-d506b7e75be6` |
| AcceptedProjectState contribution | 4 | 四条核心约束均由 `a67afeb1-9026-46d4-a1cd-d506b7e75be6` 接受 |
| Library Object | 1 | `ff69d083-c2d0-491a-8103-b95ce49bdd67` |
| Timeline Node | 1 | `93b5a9e9-064e-4011-9cbf-8cd35423dcde` |
| Material Ref | 1 | `Artifact / chapter-01.md` |
| Legacy Task | 3 | Chapter 1、Chapter Delivery、历史 Authority Confirmation Worker |
| WorkerFinalReportReceived | 2 | Chapter 1 执行、Chapter Delivery 执行 |
| WorkerToLeaderHandoff | 3 | 两个完成交接、一个旧 Authority Worker 交接 |
| WorkerSessionStarted | 3 | 包含历史错误路由产生的 Authority Worker |
| WorkerExecution | 0 | 当前《零刻》没有 `worker_executions` 持久记录 |

### 当前 Project World 事实

Authority 的四条正式约束：

1. 从“零刻”开始，人类可以进行时间穿梭。
2. 故事最终的核心冲突来自自由与选择权；主角的反抗不以爱情为主要原因。
3. 无限计算器只预测行为对时空稳定性的风险，不控制人的思想。
4. 世界只有一条闭合时间线，不存在平行宇宙。

Library 当前投影：

- Topic：`Chapter 1 交付与未接受生活化设定`
- Timeline 内容明确包含：时间层通勤、时间礼仪课、社区时间服务、风险色标、无限计算器；并明确标注为 Proposal / Recommendation，不是 AcceptedProjectState；
- Material Ref：`chapter-01.md`。

Legacy Worker FinalReport：

- 第一章已完成，生成 `chapter-01.md`；
- Chapter Delivery 任务生成了 `library-object-chapter-01-delivery.json`，其中 `library_update_status=pending_confirmation`；
- 该 JSON 是 Worker 产生的工作区 artifact，不是当前 Library 投影的事实来源。

## 使用验证结果

### 1. 新 Leader 是否能快速理解？

**部分通过，足以支撑当前 Demo。**

只看 AcceptedProjectState、Library 投影、Worker FinalReport/Handoff 和 Artifact Ref，可以回答：

- 哪些是正式约束；
- Chapter 1 是否完成；
- `chapter-01.md` 在哪里；
- 生活化设定属于 Library 的非权威材料；
- 哪些 Worker 结果已经交接。

仍然缺少：

- 被替代版本的完整变化链；
- 被删除 Material Ref 的历史；
- 讨论中但未形成持久状态的语义转变；
- 每次 Library Overview 修改的 before/after。

这些不是 Phase 2-A 的失败，而是“当前来源没有保存该历史”这一事实。

### 2. Evolution Index 是否减少误解？

**通过。**

它把以下三种身份并列展示，同时不混淆：

- Authority：四条 AcceptedProjectState 约束；
- Library：Chapter 1 当前投影和生活化设定；
- Legacy Worker/Handoff：完成声明与证据来源。

它不会读取 `library-object-chapter-01-delivery.json`，因此不会把 artifact 内的 `pending_confirmation` 覆盖成当前 Library 状态。持久化 Workbench Library 是当前 Library 状态来源；Authority 仍只由 AuthorityDecision / AcceptedProjectState 定义。

注意：开发库中仍有一条历史上错误路由的 `Authority Confirmation / 零刻核心世界约束` Legacy Worker Task。确定性索引显示它，是因为它确实存在于历史表中；这不是索引把它提升为 Authority，也不是 Phase 2-B 修改它。

### 3. 哪些变化必须进入项目演化索引？

下面的矩阵用于后续人工评估，不是 Candidate 自动规则。

| # | 变化案例 | Phase 2-A 确定性状态 | 结论 |
|---:|---|---|---|
| 1 | AuthorityDecision 接受四条核心约束 | 可直接投影 | 必须记录 |
| 2 | AcceptedProjectState contribution 被新决策 supersede | 可从 Authority 关系读取，但当前 UI 未展开完整替代链 | 必须记录 |
| 3 | 新建 Library Object | 可直接投影 | 必须记录 |
| 4 | 新建 Library Timeline Node | 可直接投影 | 必须记录 |
| 5 | Library Timeline Node 当前内容更新 | 只能看到当前节点和 revision，缺少 before 内容 | 需要历史细节时再扩展 |
| 6 | 增加 Material Ref | 可直接投影为 ArtifactReferenced | 必须记录 |
| 7 | 删除 Material Ref | 当前表只保留现状 | 当前不可确定记录 |
| 8 | Worker FinalReportReceived | 可直接从 TaskEvent 投影 | 必须记录 |
| 9 | WorkerToLeaderHandoff（FinalReport） | 可直接从 TaskEvent 投影 | 必须记录 |
| 10 | WorkerToLeaderHandoff（NeedsLeaderDecision） | 可直接从 TaskEvent 投影 | 必须记录为待决工作 |
| 11 | Assignment / Task 创建 | 可投影 Task 与当前状态 | 必须记录 |
| 12 | Assignment 状态多次转换 | 当前只稳定显示当前 Task 状态 | 需要历史时再扩展 |
| 13 | TaskRevision 创建 | 当前可显示 current revision locator | 需要历史时再扩展 |
| 14 | Artifact locator 被写入 Library Material Ref | 可直接投影 | 必须记录 |
| 15 | 工作区文件内容被外部修改 | 没有 Workbench 持久事件 | 只能作为 Source，不可推断变化 |
| 16 | Library Proposal 创建但尚未接受 | 当前 Evolution Query 尚未纳入 Proposal reader | 不进入确定性当前索引 |
| 17 | Library Proposal 接受并写入 Library | 可由 Library Object/Node 结果观察到，但没有独立 Proposal 记录 | 必须记录结果，Proposal 作为来源细节 |
| 18 | Library Proposal 被拒绝 | 当前查询层没有拒绝历史结果 | 不可直接记录 |
| 19 | 用户讨论“可能修改无限计算器” | 没有结构化持久状态 | 只留在 Session，不能进入 v0.1 |
| 20 | 用户确认“以后无限计算器只预测风险”但尚未提交 Authority | 若未产生系统命令则不可确定 | 未来 Candidate / Proposal 输入，不是 Accepted |

## 当前索引的边界

当前只读 Query 位于：

`src/Workbench.App/ProjectWorld/ProjectEvolutionIndexQuery.cs`

它读取：

- `B1AuthorityRepository.LoadProjectStateAsync`；
- `ProjectLibraryEvolutionRepository` 的 Object、Timeline Node、Material Ref；
- `TaskRepository` 的当前 Task 状态；
- `TaskEventRepository` 的 Worker FinalReport 和 Worker Handoff。

它不读取：

- Workspace JSON artifact；
- Leader 聊天全文；
- Proposal pending/rejected 历史；
- Summary；
- 新的 Evolution 表。

记录上限：`200` 条。

这使它适合做 Level 0 的短导航，但不等于完整 Event Sourcing。

## Phase 2-B 验收判定

| 验收项 | 结果 |
|---|---|
| 当前状态可理解 | 通过 |
| Authority / Library / Legacy 身份不混淆 | 通过 |
| Artifact 不覆盖持久化 Library 状态 | 通过 |
| Worker/Handoff 可追溯 | 通过 |
| 被拒绝/被替代方向完整保留 | 部分；当前来源本身不足 |
| 不新增事实层 | 通过 |
| 不需要 Candidate 才能展示当前 Demo | 通过 |
| 可直接进入 Phase 3 Candidate | 暂不建议；先积累案例与人工评估 |

## 推荐下一步

1. 冻结 Phase 2-A 代码和语义，不增加自动 Candidate。
2. 使用《零刻》和《玉牌劫》继续人工标注 20～50 个真实变化案例。
3. 观察哪些缺口会导致新 Leader 重复犯错。
4. 只有当这些案例形成稳定规则后，再设计受限 `Observed/Candidate` Structured Output。
5. 如果未来需要补充被替代/删除历史，先证明现有来源无法表达，再单独审计持久化 ownership。

本报告没有执行任何数据库写入、迁移、修复或代码实现。
