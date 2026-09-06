# Project Evolution Index — Phase 3.2 Governance Gateway Pressure Test

日期：2026-09-06  
状态：真实 GPT-5.6-sol 压力测试完成；未修改生产数据库。

## 目的

验证 Leader 在带有 Project World 上下文时，能否：

- 识别可能影响未来工作的语义变化；
- 给出 `impact_class` 和 `route_hint`；
- 在边界案例中保持 Candidate 与执行链隔离；
- 对已有 AcceptedProjectState 做 state-aware revision 判断。

测试使用每个案例独立的临时项目和临时数据库，通过本机 Codex app-server 运行真实 GPT-5.6-sol。测试结束后核对 Task、WorkerExecution 和 `WorkerSessionStarted` 数量未增加。

## 案例与结果

| 案例 | 输入性质 | Candidate | 模型分类 | 路由 | 副作用 |
|---|---|---:|---|---|---|
| brainstorm | “如果无限计算器可以控制梦境，好像很酷” | 0 | — | — | 无 |
| weak-change | “第一章这里感觉时间礼仪课写得有点少” | 1 | `Content` | `NoGovernance`（与策略冲突） | 无 |
| dune-reference | “这个世界观可以参考《沙丘》的政治氛围” | 1 | `WorldRule` | `AuthorityConfirmation` | 无 |
| reverse-world-rule | 已接受“世界只有一条闭合时间线”后，提出改成平行宇宙 | 1 | `WorldRule` | `AuthorityConfirmation` | 无 |

## 解释

### 通过的部分

- 纯头脑风暴没有进入 Candidate。
- 带有 AcceptedProjectState 的反向设定能够被识别为对现有世界规则的修订；这验证了 Evolution Detection 必须读取当前项目状态，而不能只分析单句文本。
- 所有案例都没有创建 Task、TaskRevision、WorkerExecution 或 `WorkerSessionStarted`；Candidate 通道与 Worker 执行通道的硬隔离有效。
- `Content + NoGovernance` 的不一致没有被系统静默改写。`LeaderGovernanceRouteSuggestionBuilder` 将其标记为人工复核，说明 Route Policy Validator 正在发挥保护作用。

### 暴露的模型边界

- 弱内容变化被识别出来，但模型给出的 `route_hint=NoGovernance` 与 `impact_class=Content` 不一致。应用层正确阻止了自动治理草稿；这是模型路由判断失败，不是副作用失败。
- 外部作品参考被误判为 `WorldRule + AuthorityConfirmation`。当前模型仍可能把“参考某种氛围”当成项目规则变化。该样本应保留，作为后续 Leader Skill / 分类规则的回归案例；本轮不通过修改模型输出伪造成功。

## 后续收敛结果

随后运行了 20 个案例的 Signal / Candidate 收敛矩阵。评价、赞扬、头脑风暴、措辞讨论和当前状态复述大多保持空 Candidate；明确创建 Worker 任务的请求只创建了 Legacy Worker Draft，未启动 Worker。外部作品引用仍有不稳定误升级，明确“修改文件”的执行措辞也可能被错误带入 Candidate。

当模型同时输出 Candidate 和 `authority_confirmation` 时，应用层现已强制隔离：不显示 Authority 待确认草稿，也不保存 Library Proposal，要求后续明确请求单独进入治理流程。详见 [Signal / Candidate 收敛报告](./project-evolution-index-signal-candidate-convergence-20260906.md)。

## 判定

Phase 3.2 **部分通过**：

1. Candidate Detection：通过；
2. State-aware revision detection：通过（反向时间线案例）；
3. Candidate 与执行链隔离：通过；
4. Route Policy 安全拦截：通过；
5. 语义路由稳定性：未完全通过；存在误分类和外部参考误升级。

因此当前系统可以继续作为只读 Governance Gateway 实验使用，但还不应自动提交 Library Proposal 或 Authority Confirmation。下一步应先收紧“外部参考”和“明确执行措辞”的判定规则，再重复同一矩阵。

## 夹具边界

- 测试没有使用《零刻》生产数据库；所有状态均在隔离临时数据库中创建。
- 反向时间线案例显式预置了已接受的 Authority contribution：`世界只有一条闭合时间线，不存在平行宇宙。`
- 测试只观察现有项目状态，不新增 Evolution 表、Event Store、Memory 表或 Object Registry。
