# Project Evolution Index — Signal / Candidate Convergence

日期：2026-09-06  
状态：真实 GPT-5.6-sol 收敛实验完成；临时夹具已删除。

## 实验目的

验证 Leader 是否能把以下几类输入区分开：

- 明确的项目状态变化；
- 外部灵感或作品引用；
- 评价反馈；
- 头脑风暴；
- 措辞讨论；
- 明确执行请求；
- 系统事件；
- 当前状态复述。

每个案例在独立临时项目和数据库中运行。除明确执行请求创建 Worker Task Draft 的案例外，测试要求 Task、TaskRevision、WorkerExecution、`WorkerSessionStarted`、AuthorityDecision 和 Library Object 不增加。

## 实际结果

| 案例 | Candidate | 模型输出 | 治理安全 | 备注 |
|---|---:|---|---|---|
| 明确世界规则修改 | 1 | `WorldRule / AuthorityConfirmation` | 通过 | 正常候选 |
| 已接受单一时间线改成平行宇宙 | 1 | `WorldRule / AuthorityConfirmation` | 通过 | state-aware 成功 |
| 角色职业修改 | 1 | `CharacterOrObject / AuthorityConfirmation` | 人工复核 | impact 与 route 冲突 |
| 内容增加场景 | 1 | `Content / AuthorityConfirmation` | 人工复核 | impact 与 route 冲突 |
| 架构边界修改 | 1 | `WorldRule / AuthorityConfirmation` | 通过 | 仍有 impact 误分类 |
| 《沙丘》参考 | 0 | — | 通过 | 本次正确压成无候选 |
| 《赛博朋克2077》参考 | 1 | `Content / AuthorityConfirmation` | 人工复核 | 外部参考误升级 |
| 《星际穿越》参考 | 1 | `Content / AuthorityConfirmation` | 人工复核 | 外部参考误升级 |
| “时间礼仪课感觉有点少” | 0 | — | 通过 | 评价未升级 |
| “林砚感觉有点弱” | 0 | — | 通过 | 评价未升级 |
| “剧情推进太快” | 0 | — | 通过 | 评价未升级 |
| 赞扬 | 0 | — | 通过 | 无变化 |
| 外星文明头脑风暴 | 0 | — | 通过 | 无变化 |
| 无限计算器控制梦境头脑风暴 | 0 | — | 通过 | 无变化 |
| 平行宇宙问题 | 0 | — | 通过 | 无变化 |
| 措辞润色讨论 | 0 | — | 通过 | 无变化 |
| “修改 chapter-01.md” | 1 | `Content / NoGovernance` | 人工复核 | 执行请求被错误带入 Candidate，但未执行 |
| “创建 Worker 任务修改文件” | 0 | 无 Candidate，生成 Worker Draft | 通过 | 明确执行请求允许创建 1 个 Task/TaskRevision，未启动 Worker |
| Worker 完成报告 | 0 | — | 通过 | 系统事件不由模型生成 |
| 当前规则复述 | 0 | — | 通过 | 无变化 |

## 关键发现

### Signal 与 Candidate 的分界初步成立

评价、赞扬、头脑风暴、问题和措辞讨论在本轮大多没有生成 Candidate。当前不需要把临时 Signal 持久化；可以继续把它作为后续诊断概念，而不是新的事实层。

### 外部参考仍是主要误报源

“参考《沙丘》”有时被正确压成空 Candidate，但“参考《赛博朋克2077》”和“参考《星际穿越》”仍被误判为项目变化。说明模型需要明确的 `ExternalReference / Inspiration` 抑制规则，但该概念不应加入 `impact_class`，因为它描述的是“没有形成项目变化”。

### 执行请求必须独立评估

“请创建 Worker 任务”没有生成 Candidate，并只创建了一个 Legacy Worker Draft；这是合法的执行通道行为。相反，“请修改文件”被模型同时标成 Content Candidate，路由冲突被应用层拦截。后续应在 Skill 中进一步说明：明确执行意图优先进入 `draft_proposal`，不应被当成 Evolution Candidate。

### 同轮治理输出已被隔离

当 Candidate 与 `authority_confirmation` 同时出现时，应用层现在不显示 Authority 待确认草稿，也不保存 Library Proposal。真实复跑的角色案例结果为：

```text
CharacterOrObject / AuthorityConfirmation
SAFE=False
MANUAL=True
AUTH_PENDING=False
```

这保证模型的错误路由不会绕过 Governance Gateway。

## 判定

本轮 **Signal / Candidate 分界部分通过**：

- 真正的状态变化仍能被捕获；
- 评价、头脑风暴和状态复述大多不会升级；
- 明确执行请求可进入独立 Worker Draft 通道；
- Candidate、治理草稿和执行链之间没有产生越权副作用；
- 外部参考与执行措辞仍需要进一步收紧。

因此暂不新增 Signal 持久化，也不接入自动 Library / Authority 提交。下一轮应优先增加外部灵感抑制规则和执行意图优先级，并用相同矩阵回归。
