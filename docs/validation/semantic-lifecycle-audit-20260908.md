# Semantic Lifecycle 审计

状态：**审计完成，最小 Candidate 生命周期修复已实现**  
日期：**2026-09-08**

## 结论

当前 Workbench 已经具备多个彼此分离的持久化对象，但“工作集”和“历史证据”的边界还没有在 Evolution Candidate 路径中真正执行。

准确结论是：

> **Summary 的语义边界基本成立；Candidate 的持久化和 provenance 已成立；Candidate 的 active/history 生命周期尚未闭合。**

因此目前不能把系统描述成“LLM 按变化严重程度自动写入 Truth/Library”。更准确的运行模型是：

```text
LLM 识别结构化变化
        ↓
Evolution Candidate（非权威观察）
        ↓ 用户明确打开治理路径
Library Proposal / Authority Confirmation
        ↓ 用户接受
Library / AcceptedProjectState
```

LLM 可以判断、分类和建议路由，但不能自行改变正式项目现实。

## 当前真实对象边界

| 对象 | 当前语义 | 当前生命周期执行情况 |
|---|---|---|
| Conversation | 原始交互历史 | 持久化，按 Session/Epoch 保留 |
| Project Summary | 阶段性、稀疏的连续性压缩 | 有持久化和 recovery；不是权威 |
| Evolution Candidate | 对对象状态差异的非权威观察 | 有持久化，但当前读取仍混入全部历史 |
| Library Proposal | Library 治理事务 | Pending/Accepted/Rejected 已存在 |
| Authority Confirmation | Truth 治理草案 | UI 临时草案，接受后进入 Authority |
| Library | 当前长期项目知识投影 | 持久化 |
| AcceptedProjectState | 当前正式项目现实 | 只由 Authority Decision 投影 |
| Candidate provenance | 变化来源证据 | 已可指向 durable Candidate identity |

## 已确认的正确边界

### 1. Summary 不是 Candidate

`LeaderSummaryAdmissionInstruction` 已明确要求 Summary 是稀疏的长期理由压缩，而不是 routine activity log；只读、审计、查询请求不得生成 Summary。Leader 回合在有必要时写入 `summary_deltas`，Worker FinalReport 也可以通过 Summary consumer 进入 durable Summary。

因此当前不能把 Summary 解释为“每天自动生成的 Evolution Candidate”。两者分别回答：

```text
Summary   = 这一阶段以后，下一位 Agent 需要知道什么？
Candidate = 当前 Canonical Project World 可能发生了什么 Delta？
```

### 2. Candidate 不能直接改变 Truth 或 Library

Leader skill 和 structured response contract 都把 Candidate 定义为 observation only。Leader 回合中如果出现 Candidate，会阻止同一回合隐式生成 Authority 或 Library governance command；后续必须通过显式治理路径继续。

这条边界符合：

```text
Agent 发现变化 ≠ 项目已经接受变化
```

### 3. Library 和 Truth 是并行的 canonical projection

Library Proposal 接受后进入 Library，不自动成为 Truth；Authority Confirmation 接受后进入 AcceptedProjectState，不要求同步写入 Library。系统没有把所有长期内容压成一个统一 Memory 层。

## 已确认的生命周期缺口（本轮已修复）

### P0：Evolution Candidate 有状态字段，但没有完整状态迁移（已修复）

`ProjectEvolutionCandidateStatus` 当前只有：

```text
Observed
GovernancePending
Accepted
Rejected
```

审计时发现运行路径只在 Leader turn 完成时以 `Observed` 保存 Candidate。现已增加受限状态迁移，并在 Authority/Library 治理边界接入 `GovernancePending`、`Accepted`、`Rejected`。

结果是：

```text
Candidate 被接受后仍可能看起来像当前 Candidate
Candidate 被拒绝后仍可能看起来像当前 Candidate
Candidate 进入 Proposal 后仍可能保持 Observed
```

这不是 provenance 问题，而是 working-set lifecycle 没有闭合。

### P0：恢复和上下文读取没有区分 Active Set 与 History（已修复）

审计时以下读取路径调用的是无状态过滤的 `ListAsync`，现已改为 active-only 查询：

- `LeaderPaneViewModel.RestoreEvolutionCandidatesAsync`；
- `ProjectContinuityMaterialService.BuildInitialBundleAsync`；
- `ProjectEvolutionIndexQuery`；
- `ProjectMemoryApi.ListEvolutionCandidatesAsync`。

`ProjectEvolutionCandidateRepository.ListActiveAsync` 现在只返回 `Observed` / `GovernancePending`。新 Leader 不再恢复已 Accepted/Rejected Candidate；Evolution Index 保留完整历史作为审计/provenance 视图。

需要明确拆成两种查询：

```text
ListActiveAsync      → 恢复、治理选择、Leader 当前工作集
ListHistoryAsync     → provenance、审计、Evolution 展开
```

历史不应删除，但默认不应进入 Project Context。

### P1：Proposal / Decision 与 Candidate 没有统一的 resolve 关联（已修复）

Library Proposal payload 可以携带 `EvolutionCandidate` material reference；Authority Decision 可以携带 `ConsideredRef.EvolutionCandidate`。这已经足够支持来源追踪，但目前缺少一个确定性动作：

```text
ProposalAccepted / AuthorityDecisionCommitted
        ↓
关联 Candidate.status = Accepted
```

Library Proposal 和 Authority Confirmation 的显式拒绝现在会把 Candidate 标记为 `Rejected`；未解决的 Candidate 才会继续留在 Active Working Set。

### P1：当前模型没有表达完整的终态历史

当前 schema 没有：

- `resolved_at`；
- `resolved_by`；
- `related_proposal_id`；
- `related_decision_id`；
- `Superseded` / `Merged` / `Dismissed` 状态。

这不必在本轮立即扩展。第一阶段只需要保证 active/history 分离和 Accepted/Rejected 的确定性迁移；只有真实场景出现替代、合并或撤回需求时，才增加新的状态和关联字段。

## 对“变化严重程度自动写入”的准确回答

当前系统**不是**：

```text
LLM 判断无变化 / 低风险 / 高风险
↓
低风险自动写 Library
高风险请求用户写 Truth
```

当前系统是：

```text
LLM 判断是否存在对象绑定的状态差异
↓
没有差异：普通回答；必要时产生稀疏 Summary
有长期但非正式差异：Evolution Candidate → Library Proposal
有正式项目现实差异：Evolution Candidate → Authority Confirmation
↓
用户接受后才写入 Library 或 AcceptedProjectState
```

`route_hint` 只是建议，不是自动执行权限。低风险也不能绕过治理直接改 Truth；低风险、无变化或普通对话可以不生成 Candidate，或只留下必要的 Summary/Session 证据。

## 最小修复边界

本审计不建议现在引入新的 Agent、队列、重试 daemon、复杂严重度策略或自动低风险写入。

本轮只实现四件事：

1. 为 Candidate Repository 增加 active/history 查询和受限状态迁移；
2. Library Proposal 接受/拒绝时解析 Candidate material ref，并更新 Candidate 状态；
3. Authority Decision 成功提交后，根据 `ConsideredRef.EvolutionCandidate` 更新 Candidate 状态；
4. Leader recovery、initial continuity bundle 和 Evolution Index 默认只读取 active candidates，历史只在 provenance/audit 查询中展开。

Summary 先保持现状：它是稀疏、非权威、按阶段/结果边界产生的连续性压缩，不改造成 Candidate，也不把所有普通聊天写成 Summary。

## 验收标准

最小修复后的生命周期验收条件：

1. 新 Candidate 初始为 `Observed`，不改变 Truth 或 Library；
2. Candidate 进入治理路径后变为 `GovernancePending`；
3. Library Proposal 接受后 Candidate 变为 `Accepted`，拒绝后变为 `Rejected`；
4. Authority Decision 成功提交后关联 Candidate 变为 `Accepted`；
5. 已解决 Candidate 仍可通过 History/provenance 查询；
6. 新 Leader 默认只收到少量 active Candidate，而不是全部历史；
7. 已接受或已拒绝 Candidate 不会在下一轮被重新当成待处理变化；
8. Summary 仍不具备 Truth/Library 写权限；
9. 原始 Source、Candidate ID、Proposal/Decision ID 仍可反向追踪。

## 最小修复结果

本轮已实现：

- `ListActiveAsync`：恢复和当前工作上下文只返回 `Observed` / `GovernancePending`；
- `ListAsync`：保留完整 Candidate History，供 Evolution Index、审计和 provenance 使用；
- Library Proposal 创建时，关联的 Candidate 进入 `GovernancePending`；
- Library Proposal 接受/拒绝时，关联 Candidate 进入 `Accepted` / `Rejected`；
- Authority Confirmation 草案提交时，关联 Candidate 进入 `GovernancePending`；
- Authority Decision 成功提交时，关联 Candidate 进入 `Accepted`；
- Authority Confirmation 明确拒绝时，关联 Candidate 进入 `Rejected`；
- Leader recovery 和 continuity bundle 默认不再把已解决 Candidate 作为当前工作项恢复。

本轮没有改动 Summary 生命周期。Summary 仍然只按阶段/结果边界产生，并通过 recent bounded query 进入恢复上下文；后续可单独设计 Recent/History 归档策略。

## 审计判定

```text
Semantic Lifecycle: PARTIAL
Core Authority Boundary: PASS
Summary Boundary: PASS
Candidate Durability: PASS
Candidate Active/History Separation: PASS
Candidate Resolution Wiring: PASS
```

当前最准确的产品表述是：

> **Project World 会长期增长；Evolution Working Set 会在治理终点退出当前上下文，但历史证据仍可审计和追溯。**
