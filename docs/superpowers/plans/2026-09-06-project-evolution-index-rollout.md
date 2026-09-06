# Project Evolution Index Rollout Plan

状态：**Phase 3.2 压力测试完成；治理自动提交仍未开启**  
日期：**2026-09-06**

## 当前 checkpoint

- Phase 1 架构审计：完成。
- Phase 2-A 确定性只读投影：完成。
- Phase 2-B 使用验证：完成。
- Phase 3 Candidate Detector：完成实验验证。
- Phase 3.1 Governance Boundary Hardening：完成；Candidate 与 Worker 执行链隔离，路由冲突进入人工复核。
- Phase 3.2 Governance Gateway 压力测试：完成但部分通过。真实 GPT-5.6-sol 能识别 state-aware 世界规则修订，且没有产生执行副作用；外部参考和弱内容意见仍存在误升级或路由冲突。
- Signal / Candidate 收敛矩阵：完成；评价、头脑风暴、措辞讨论和状态复述大多不升级，外部参考与执行措辞仍是误报重点。新增同轮 Candidate 与治理命令的隔离规则，要求治理命令在后续明确请求中单独生成。

完整实验过程见：[Project Evolution Index 实验总记录](../validation/project-evolution-index-experiment-record-20260906-07.md)。

当前下一步：保留失败样本，收紧分类规则并重复压力测试。在语义路由稳定前，不自动提交 Library Proposal 或 Authority Confirmation。

## 总原则

Project Evolution Index 是解释和导航层，不是新的事实层、权威层、记忆层或事件存储系统。

本计划统一的是 Evolution 的展示和查询，不统一底层身份、持久化 ownership 或 authority semantics。

任何需要以下动作的方案必须停止并报告：

- 新建 Event Store 或 Evolution 专用事实表；
- 把 Legacy Worker / Task / Summary 改写成 B1 身份；
- 改变 AuthorityDecision 或 AcceptedProjectState 语义；
- 改变 Library Proposal / Library Projection ownership；
- 新造第二套 Memory、Continuation 或 Truth 模型；
- 让 Leader 或 Worker 输出直接写入项目现实。

## Phase 1：架构审计与数据源映射（当前阶段）

### 目标

确认现有 Workbench 是否已经具备生成 Evolution Index 只读投影的能力，并明确最小投影边界。

### 严格限制

- 不修改生产代码；
- 不新增数据库表、字段或迁移；
- 不修改任何 Workbench 数据库；
- 不创建 Event Store；
- 不修改 B1 Authority、AcceptedProjectState 或 Projector；
- 不修改 Library persistence semantics；
- 不修改 Legacy Leader / Worker / Task 生命周期；
- 不新增 Leader Candidate Structured Output；
- 只输出审计报告和映射结果。

### 审计问题

1. 当前有哪些持久化数据可以生成 Evolution Record？
2. 哪些来源属于 B1 Project World？
3. 哪些来源属于 Library 当前投影？
4. 哪些来源属于 Legacy Worker / Task / Handoff？
5. 哪些来源属于 Artifact / Evidence / Session？
6. 当前哪些变化类型可以确定性生成？
7. 哪些变化目前缺少稳定的对象 ID、变化类型或来源定位？
8. 最小只读聚合层应放在哪个 Application / Query 边界？
9. 哪些看似方便的实现会破坏现有语义边界？

### 预期输出

```text
ProjectEvolutionIndex（只读查询/聚合）
    ↓
现有数据源
    ├─ B1 AuthorityDecision / AcceptedProjectState
    ├─ B1 Claim / Handoff / Evidence
    ├─ Library Object / Timeline Node / Material Ref / Proposal
    ├─ Legacy Task / TaskRevision / Worker events / Completion
    ├─ Artifact references
    └─ Leader epoch / Session locators
```

审计报告必须按以下类别标记每项：

- AVAILABLE AND DIRECTLY PROJECTABLE
- AVAILABLE BUT NEEDS ADAPTER
- AVAILABLE ONLY AS SOURCE LOCATOR
- NOT AVAILABLE
- SEMANTICALLY UNSAFE TO MERGE

### Phase 1 通过门槛

只有同时满足以下条件，才能进入 Phase 2：

- 已明确每个 Evolution Record 的真实来源；
- 已明确哪些记录是确定性系统事件；
- 已明确哪些内容只能作为 Candidate；
- 已证明不需要新增持久化模型；
- 已确定一个只读查询边界；
- 已列出所有不可合并的 B1 / Legacy 身份；
- 已有一个可用的《零刻》或《玉牌劫》样例映射。

否则停止在 Phase 1，不进入开发。

## Phase 2：最小只读 Project Evolution View

### 前置条件

Phase 1 审计通过，并且明确允许的查询边界。

### 目标

增加一个只读 Project Evolution View，用现有数据生成可理解的变化导航，不增加新的事实来源。

### 允许范围

- 新增只读 query / aggregation service；
- 复用现有 repositories 和 read models；
- 为每条结果展示 Object、Change、Status、Source；
- 对 B1、Library、Legacy Worker 分别显示真实来源类型；
- 只做最小 Demo UI 或现有 View 的展示接入；
- 增加确定性 projection tests 和展示测试。

### 禁止范围

- 新增 Evolution 持久化表；
- 修改现有记录的写入路径；
- 将所有来源压成一个统一实体身份；
- 让 Library 接受自动变成 AcceptedProjectState；
- 让 Worker Completion 自动变成 B1 Handoff；
- 让 Session 成为项目事实来源。

### 最小 Demo

打开《零刻》项目后，至少能够看到：

```text
Authority
四条核心约束已确认
Source: AuthorityDecision

Library
Chapter 1 已进入 Library
Source: Library Object / Timeline Node

Worker
chapter-01.md 已产生
Source: Worker FinalReport / Handoff
```

## Phase 3：受限 Leader Evolution Candidate

### 前置条件

Phase 2 已通过真实项目恢复和展示验证，且只读索引被证明能减少错误延续。

### 目标

让 Leader 仅提交结构化 Evolution Candidate，不直接写入项目现实。

### Candidate 最小契约

```text
Object
ObjectKind
ChangeKind
Before（可选）
After（可选）
Status = Observed | Candidate
SourceRef
```

### 约束

- 必须绑定对象；
- 必须有来源定位；
- 只能产生 Observed / Candidate；
- 不允许直接产生 Accepted、LibraryUpdated 或 Truth；
- 关联现有 Library Proposal 或 Authority proposal 时，复用现有确认路径；
- 每轮数量和字段长度有硬上限；
- 不复制完整 Session 或对话文本。

## 阶段间停止条件

在任一阶段，如果实现需要：

```text
B1 / Legacy lifecycle convergence
persistence identity rewriting
new authority semantics
second memory or continuation model
automatic truth promotion
```

立即停止，返回阻塞报告，不自行扩大范围。

## 最终验收

Project Evolution Index 只有在以下结果成立时才算完成：

1. 新 Leader 能看到当前状态及其形成路径；
2. 被拒绝或被替代的方向不会被误当成当前事实；
3. 所有结果都能追溯到现有 Source；
4. 默认上下文只使用短索引，完整材料按需展开；
5. B1、Library、Legacy、Worker、Session 的身份保持不变；
6. 没有 Evolution Index 时现有功能仍然可用。
