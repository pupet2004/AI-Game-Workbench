# Project Evolution Index Design

状态：**概念冻结，分阶段实现**  
日期：**2026-09-06**

## 1. 目的

Workbench 的长期问题不是 Agent 有没有更多聊天记忆，而是新的 Agent 能不能理解：

```text
项目当前是什么状态？
它经过哪些合法变化才变成现在这样？
这些变化由谁提出、由谁确认、依据在哪里？
```

Project Evolution Index（项目演化索引）是解决这个问题的导航层。

它把现有 Project World、Library、Worker、Handoff、Authority 和 Source 连接成一张可查询的项目变化地图，让不同 Agent 可以在同一个持续发展的项目世界中工作。

Evolution Index 不是新的 Truth、Memory、Summary 或 Event Store。它不拥有任何项目事实，也不改变现有记录的身份和权威语义。

## 2. 核心产品定义

Workbench 不是一个带记忆的 Agent 系统，而是一个允许 Agent 临时参与其中的项目世界系统。

在这个项目世界中：

| 层 | 回答的问题 | 典型内容 | 权威性 |
|---|---|---|---|
| Truth | 哪些规则不能随意违反？ | 单一时间线、核心主题约束 | 最高，仅由 AuthorityDecision 支持 |
| Library | 项目现在有什么？ | 角色、物件、章节、当前设定、材料引用 | 当前项目视图，不自动等于 Truth |
| Evolution Index | 项目为什么变成现在这样？ | 初始设定、修订、淘汰、接受、替代、来源 | 导航索引，不拥有事实 |
| Source | 为什么相信这次变化？ | User input、Claim、Handoff、Artifact、Evidence、Session | 原始证据或定位 |
| Session | 谁在什么时候进行了一次交互或执行？ | Leader session、Worker session、Attempt | 交互/执行载体，不是项目事实 |

Truth 与 Library 是并行的项目视图，不是简单的 `Library → Truth` 晋升链。Evolution Index 横向连接它们的形成过程。

## 3. 不变量

### 3.1 Evolution Index 不拥有任何东西

Evolution Index 不拥有：

- AcceptedProjectState；
- Library Object 或 Timeline Node；
- Assignment、Revision、Attempt；
- Claim、Handoff 或 AuthorityDecision；
- Worker、AgentSession 或 Provider 身份。

它只引用这些记录，并提供面向恢复和导航的查询结果。

### 3.2 Agent 输出不是项目现实

Leader 或 Worker 可以观察、提出、解释和提交证据，但不能直接宣布项目状态已经改变。

正式状态改变仍然使用现有路径：

```text
Authority proposal
    → named authority command
    → validated AuthorityDecision
    → AcceptedProjectState projection
```

```text
Library proposal
    → user acceptance
    → Library projection update
```

Library 接受不自动产生 AuthorityDecision；AuthorityDecision 也不要求 Library 同步写入。

### 3.3 身份不合并

Evolution Index 可以把以下来源放在同一张结果视图中：

- B1 Claim / Handoff / AuthorityDecision；
- Legacy Task / Worker event / completion；
- Library Proposal / Library projection；
- Artifact / Evidence / Session locator。

但它不能把这些来源重写成同一种身份。Legacy Worker Completion 仍然是 Legacy Worker Completion，B1 Handoff 仍然是 B1 Handoff。

## 4. Evolution Record 的语义

Evolution Record 不是聊天摘要，而是一个可命名的状态变化或状态变化候选。

最小记录应包含：

```text
Object
ObjectKind
ChangeKind
Before（可选）
After（可选）
Status
OccurredAt
SourceRef
```

可选的扩展字段：

```text
Reason
AffectedObjects
RelatedProposal
RelatedDecision
RelatedLibraryObject
RelatedArtifact
```

推荐的状态生命周期：

```text
Observed
    ↓
Candidate
    ↓
Proposed
    ↓
Accepted
    ├→ Library projection
    └→ AuthorityDecision → AcceptedProjectState

Candidate / Proposed → Rejected
Accepted → Superseded
```

状态含义：

- **Observed**：系统或 Leader 观察到可能存在变化，但还不能形成稳定的结构化变化。
- **Candidate**：已经有明确对象和前后差异，但尚未提交为产品状态变更。
- **Proposed**：已经关联到 Library Proposal 或 Authority proposal，等待对应的确认路径。
- **Accepted**：底层 Library 或 Authority 正式记录已经接受该变化。
- **Rejected**：提案或方向被明确否定。
- **Superseded**：该变化曾经成立，但后来被新的变化替代。

“用户探索了一个可能方向”通常最多是 Observed；“以后把林砚设定为调查员”才可以形成 Candidate。

## 5. Object 约束

Evolution Candidate 必须绑定一个可命名对象。没有对象的泛化感想不进入项目演化索引。

第一阶段允许的 ObjectKind 可以包括：

```text
WorldRule
Character
Location
Item
PlotArc
Chapter
Requirement
ArchitectureComponent
CodeModule
LibraryObject
Assignment
Artifact
```

对象身份优先使用现有持久化 ID；当对象尚未拥有正式 ID 时，可以使用受约束的稳定逻辑键，但必须标记为未解析对象，不能伪装成 B1 或 Library ID。

## 6. 事件来源

Evolution Index 使用双来源模型。

### 6.1 系统来源：确定性变化

由现有持久化状态或明确命令生成，不需要模型判断：

```text
AssignmentCreated
RevisionCreated
AttemptStarted
WorkerCompleted
ArtifactProduced
HandoffRecorded
ProposalCreated
ProposalAccepted
ProposalRejected
LibraryProjectionUpdated
AuthorityDecisionRecorded
AcceptedStateProjected
```

这些记录应当来自现有 B1、Library、Task、Worker、Artifact 和 Authority 读模型。第一阶段不新增统一事件表。

### 6.2 语义来源：变化候选

Leader 可以从当前对话或 Worker 结果中提交受限的 Candidate，但它只描述“可能发生的变化”，不直接写入正式状态。

建议使用结构化字段：

```json
{
  "object": "无限计算器",
  "object_kind": "WorldRule",
  "change_kind": "ConstraintRevision",
  "before": "可能干涉人的选择",
  "after": "只预测时空稳定性风险",
  "reason": "用户明确收窄职责边界",
  "status": "Candidate",
  "source_ref": "LeaderMessage:532"
}
```

模型判断的不是“这件事重要不重要”，而是：

> 是否存在一个可命名的对象，以及一个未来工作可能需要知道的状态差异？

## 7. Leader 的三层职责

Leader 不是一个会自动写日志的 Agent，而是一个项目状态协调器。

### 7.1 工作脑（Workspace）

负责：

- 探索和推理；
- 讨论方案；
- 试错和修正；
- 生成普通回答；
- 组织当前回合的工作。

工作脑产生的完整讨论默认留在 Session 中。

### 7.2 观察脑（Evolution Detector）

负责检测：

- 明确的对象状态变化；
- 用户对既有方向的否定；
- 指向未来行为的规则变化；
- Worker 结果导致的项目状态变化；
- 可能影响后续工作的方案淘汰或替代。

观察脑只生成 Observed 或 Candidate，不改变 Truth 或 Library。

### 7.3 管理脑（Project Governance）

负责：

- 提交 Library Proposal；
- 请求 Authority Decision；
- 组织 Assignment、Worker 和 Handoff；
- 选择恢复材料；
- 解释 Candidate 当前处于什么状态。

这三层是 Leader 的职责分工，不要求实现成三个独立 Agent。

## 8. 触发规则

### 应该生成 Candidate 的情况

1. 用户明确表达持续性意图：

   ```text
   以后、不再、改成、保持、统一、定义为、从现在开始
   ```

2. 明确改变对象属性：

   ```text
   林砚不是审核员，是调查员。
   ```

3. 明确淘汰方案：

   ```text
   A 方案不要了。
   ```

4. Worker 或系统状态改变了项目可见结果：

   ```text
   Chapter 1 完成，产生 chapter-01.md。
   ```

### 不应该生成 Candidate 的情况

- 普通赞同或否定但没有对象状态差异；
- 纯粹的头脑风暴；
- 临时措辞调整；
- 对同一个已记录变化的重复描述；
- 只存在于模型内部的推理；
- 没有来源定位的自由总结。

关键词只能作为提示，不能单独触发持久化。

## 9. Token 与数据分层

Evolution Index 采用三级读取深度：

### Level 0：Index

默认注入 Leader 上下文的短记录：

```text
对象、变化类型、前后值、状态、时间、来源 ID
```

### Level 1：Detail

需要解释时再读：

```text
变化原因、影响对象、被否定的方案、关联 Proposal/Decision
```

### Level 2：Source

需要核查时才展开：

```text
Session、完整 Handoff、Artifact、Evidence、原始消息
```

硬性限制：

- 每个 Leader 回合的 Candidate 数量有上限；
- Candidate 只保存短字段，不复制完整对话；
- 同一对象和同一变化应去重；
- Detail 和 Source 按需读取；
- 没有 Object / Change 的内容不进入 Index。

## 10. 与当前 Workbench 的映射

当前系统已经拥有大部分底层记录：

| Evolution 需要的能力 | 当前来源 |
|---|---|
| 正式项目规则 | B1 AuthorityDecision、AcceptedProjectState |
| 当前项目状态 | Project Library Object、Overview、Timeline Node、Material Ref |
| 执行结果 | Task、TaskRevision、WorkerExecution、task_events |
| 完成声明 | Worker FinalReport、Legacy WorkerToLeaderHandoff、B1 Handoff |
| 变化证据 | Claim、Evidence、Artifact、Material Ref |
| 交互定位 | Leader epoch、Leader message、AgentSession |
| 旧摘要机制 | R5-A Project Summary、Daily Summary |

当前缺少的不是这些记录本身，而是一个按以下维度连接它们的查询投影：

```text
Object → Change → Status → Source → Current projection
```

## 11. 与现有 B1、Legacy、Library 的关系

Evolution Index 不替换现有架构。

### B1

B1 继续拥有：

- Claim、Handoff；
- AuthorityDecision；
- AcceptedProjectState；
- Assignment、Revision、Attempt；
- 权威验证和投影。

Evolution Index 只读取并导航这些记录。

### Legacy

Legacy Leader、Worker、Task、Completion、Summary 和 Session 继续保持原有身份。

它们可以作为 Evolution 的来源，但不能因为进入 Index 就自动变成 B1 记录。

### Library

Library 继续表示项目当前可检索的状态和有来源的演化内容。

Library Proposal 接受后，Evolution Index 可以把对应变化标为 Accepted；但这不意味着它进入 AcceptedProjectState。

## 12. 推荐实现顺序

### Phase 0：语义验证

使用《零刻》和《玉牌劫》人工整理一批变化，验证：

- 哪些内容值得出现在 Level 0；
- 哪些内容只适合 Detail；
- 哪些变化会防止新 Leader 重复犯错；
- 哪些 Candidate 实际上只是讨论噪音。

### Phase 1：只读 Evolution Index

新增查询/聚合层，从现有记录生成确定性索引：

```text
B1 records
Library records
Worker/task events
Artifact references
```

这一阶段不新增持久化表，不改变任何现有写入语义。

### Phase 2：受限语义 Candidate

在 Leader Structured Output 中增加可选 Candidate 数组，要求：

- 必须绑定 Object；
- 必须有 ChangeKind；
- 必须有 SourceRef；
- 只能写 Observed / Candidate；
- 不能直接写 Accepted、Truth 或 LibraryUpdated。

### Phase 3：明确的 Proposal 关联

把 Candidate 关联到现有 Library Proposal 或 Authority proposal，仍然复用现有确认和权限路径。

### Phase 4：评估是否需要持久化

只有当只读投影和实际恢复验证证明当前来源不足，才考虑增加 Evolution 专用持久化。新增持久化必须说明：

- 为什么现有记录无法表达；
- 新记录的拥有者是谁；
- 如何避免重复保存聊天；
- 如何保持 B1 / Legacy 身份分离。

## 13. 验收标准

Evolution Index 只有在以下情况成立时才算有价值：

1. 新 Leader 能看到当前 Library 状态及其形成原因；
2. 被拒绝或被替代的方向不会被重新当成新建议；
3. Candidate 不会自动污染 Library 或 AcceptedProjectState；
4. 每条变化都能追溯到现有 Source；
5. 默认上下文只注入短索引，不注入完整聊天；
6. B1、Legacy、Library、Worker 的底层身份保持不变；
7. 没有 Evolution Index 时，项目现有功能仍然可用；
8. Leader 可以在需要时从 Level 0 展开到 Level 1 和 Level 2。

## 14. 最终产品表述

Workbench 是：

> **面向 Agent 的项目演化地图，让不同 Agent 在同一个持续发展的项目世界中协作。**

其中：

```text
Truth     = 项目不能违背的少量最高约束
Library   = 项目当前是什么
Evolution = 项目为什么变成现在这样
Source    = 为什么相信这次变化
Session   = 交互和执行发生在哪里
```

Agent 不需要拥有项目记忆。它需要能够读取项目历史，并沿着合法的状态迁移继续工作。
