# Project Evolution Index — Phase 1 Architecture Audit

日期：**2026-09-06**  
范围：只读架构审计与数据源映射  
结论：**具备最小只读投影条件；暂不进入实现**

## 1. 审计结论

现有 Workbench 已经拥有生成第一版 Project Evolution Index 所需的大部分数据源：

```text
B1 Authority / Accepted State
Library projection / proposal
Legacy Task / Worker events / completion
Artifact and material locators
Leader epoch / session locators
```

当前缺少的是一个跨来源的只读查询投影：

```text
Object → Change → Status → Source → Current projection
```

因此第一版不需要新增 Event 表、Event Store、Memory 层或事实模型。最小实现边界应是 Application 层的只读 query/aggregation service，复用现有 repositories 和 read models。

## 2. 来源映射

| 来源 | 当前入口 | 可生成的 Evolution 信息 | 分类 |
|---|---|---|---|
| B1 AuthorityDecision | `B1ProjectionService` / `B1AuthorityRepository.LoadProjectStateAsync` | `AuthorityDecisionRecorded`、正式约束被接受、decision provenance | AVAILABLE AND DIRECTLY PROJECTABLE |
| B1 AcceptedProjectState | `B1ProjectionService.GetAcceptedProjectStateAsync` | 当前 Truth / accepted contribution | AVAILABLE AND DIRECTLY PROJECTABLE |
| B1 Claim | `B1AuthorityRepository` project-state load | Claim 类型、声明对象、来源 binding/evidence | AVAILABLE BUT NEEDS ADAPTER |
| B1 Handoff | `B1AuthorityRepository` project-state load | B1 Handoff、Attempt、Claim、Evidence 关系 | AVAILABLE BUT NEEDS ADAPTER |
| B1 Evidence | `B1AuthorityRepository` / evidence records | 证据 locator、digest、来源关系 | AVAILABLE BUT NEEDS ADAPTER |
| Library Object / Overview | `ProjectLibraryEvolutionRepository` | 当前 Library Object、Overview 更新 | AVAILABLE AND DIRECTLY PROJECTABLE |
| Library Timeline Node | `ProjectLibraryEvolutionRepository` | Timeline Node 创建/更新、当前节点内容 | AVAILABLE AND DIRECTLY PROJECTABLE |
| Library Material Ref | `ProjectLibraryEvolutionRepository.GetMaterialReferencesAsync` | Artifact/source locator、label | AVAILABLE AND DIRECTLY PROJECTABLE |
| Library Proposal | `ProjectLibraryProposalService` | ProposalCreated、ProposalAccepted、ProposalRejected、关联 source session | AVAILABLE AND DIRECTLY PROJECTABLE |
| Legacy Task / TaskRevision | `TaskRepository` / `TaskRevisionRepository` | Assignment-like work item、revision、status change | AVAILABLE BUT NEEDS ADAPTER |
| Legacy Worker task events | `TaskEventRepository.ListForProjectAsync` | WorkerStarted、Progress、FinalReport、Handoff、Stopped | AVAILABLE AND DIRECTLY PROJECTABLE |
| Worker execution | `WorkerExecutionRepository` | execution identity、provider/session locator、execution state | AVAILABLE BUT NEEDS ADAPTER |
| Artifact | Library material refs、completion payload、filesystem locator | artifact produced / referenced；不能仅凭文件内容推断项目事实 | AVAILABLE ONLY AS SOURCE LOCATOR |
| Leader epoch / Agent session | `LeaderSessionEpochRepository` / `LeaderMessageRepository` | Session boundary、rotation、handoff locator、conversation source | AVAILABLE ONLY AS SOURCE LOCATOR |
| R5-A Project Summary | `ProjectSummaryRepository.QueryAsync` | 非权威 SummaryDelta、变化提示、source refs | AVAILABLE BUT NEEDS ADAPTER |
| Legacy Daily Summary | `DailySummaryRepository` | 可变日总结、上下文来源 | AVAILABLE BUT NEEDS ADAPTER |
| Manual project activity | `ProjectActivityRepository` / `ProjectMemoryService` | 手动活动记录 | AVAILABLE BUT NEEDS ADAPTER |
| Leader semantic Candidate | 当前仅有 `leader_messages.text`，无 Evolution Candidate 契约 | 需要 Phase 3 的受限 Structured Output | NOT AVAILABLE |

## 3. 当前《零刻》样例证据

当前主库中的《零刻》Project ID：

```text
db5333e2-573d-432f-b457-1fc3b063a122
```

已存在并可直接投影：

- 2 条 B1 AuthorityDecision；
- 4 条 AcceptedProjectState contribution；
- 1 个 Library Object；
- 1 个 Library Timeline Node；
- 1 个 `chapter-01.md` Material Ref；
- Legacy Worker FinalReport / WorkerToLeaderHandoff 事件；
- 多个 Task / TaskRevision / Worker execution 记录；
- 4 个 Leader epoch 和对应 Session locator。

该样例中当前没有：

- B1 Claim；
- B1 Handoff；
- B1 Evidence record；
- Project Summary entry；
- Daily Summary；
- Leader semantic `summary_delta` 记录。

因此《零刻》可以证明“Authority + Library + Worker/Handoff + Artifact locator”的确定性演化索引，但不能证明 R5-A Summary 或 B1 Claim/Handoff 适配器的实际样例覆盖。

## 4. 可确定生成的第一版记录

只读索引第一版可以安全生成：

```text
AuthorityDecisionRecorded
AcceptedStateProjected
LibraryObjectCreated
LibraryOverviewUpdated
LibraryTimelineNodeCreated
LibraryProposalCreated
LibraryProposalAccepted
LibraryProposalRejected
AssignmentCreated（Legacy Task adapter label）
TaskRevisionCreated
WorkerSessionStarted
WorkerFinalReportReceived
WorkerToLeaderHandoff
ArtifactReferenced
LeaderEpochStarted
LeaderEpochRotated
```

其中 `LibraryProposalAccepted → LibraryProjectionUpdated` 只能在现有 Proposal 状态和 Library projection 同时可见时作为派生关系展示；不能声称底层存在一个未持久化的统一事件。

以下内容不能在 Phase 1 自动生成正式 Evolution Record：

- 对自由对话中隐含语义变化的判断；
- 没有明确 Object / Before / After 的讨论；
- “重要性”或“项目意义”的自由推断；
- 未经用户确认的 Accepted、Truth 或 LibraryUpdated 状态。

## 5. 身份和权威边界

只读索引必须保留来源标签：

```text
SourceKind = B1 | Library | LegacyWorker | Task | Artifact | Session | Summary
```

以下关系不能被压成统一身份：

```text
Legacy Worker Completion ≠ B1 Handoff
Library Proposal Accepted ≠ AuthorityDecision
Library Projection ≠ AcceptedProjectState
Leader Session ≠ Project identity
Summary ≠ Truth
Artifact file ≠ persisted project state
```

## 6. 推荐最小实现边界

建议将第一版查询层放在 Application 层，例如：

```text
Workbench.App.ProjectWorld.ProjectEvolutionIndexQuery
```

它只依赖现有只读入口，返回展示用 DTO：

```text
ProjectEvolutionRecord
    ObjectKey / ObjectId
    ObjectKind
    ChangeKind
    Status
    Before / After（可选）
    OccurredAt
    SourceKind
    SourceLocator
    RelatedLibraryObjectId（可选）
    RelatedAuthorityDecisionId（可选）
    DetailAvailable
```

第一版不应把 DTO 放进 B1 Core，因为 Evolution Index 不属于 Project World 的事实拥有者；也不应放进 Memory namespace，因为它不是第二套记忆系统。

## 7. 可能破坏架构的方案

以下方案不允许进入 Phase 2：

1. 新建 `evolution_events` 表，把所有来源复制进去；
2. 将 Task / Worker Completion 转换成 B1 Assignment / Handoff；
3. 将 Library Proposal acceptance 直接写成 AuthorityDecision；
4. 让 Leader semantic Candidate 直接更新 Library 或 AcceptedProjectState；
5. 把完整聊天记录作为 Evolution 内容保存；
6. 让 Session 或 Provider 成为项目事实来源；
7. 用一个新的 Summary / Continuation / Memory 模型替代现有来源；
8. 用关键词直接决定状态持久化，而没有 Object、Change 和 Source 校验。

## 8. Phase 1 门槛判断

Phase 1 可以通过，理由是：


- 每个主要确定性来源已有可读入口；
- 《零刻》已有可验证的 Authority、Library、Worker/Handoff、Artifact locator 样例；
- 现有身份和权威边界足以阻止错误合并；
- 最小实现可以保持只读，不需要新增持久化；
- Leader semantic Candidate 可以延后到 Phase 3。

进入 Phase 2 前仍需明确两件事：

1. 第一版 UI 只展示确定性派生记录，还是同时展示 R5-A Summary（默认应暂不展示）；
2. “对象”如何从 Legacy Task / Artifact locator 映射到稳定 ObjectKey，而不伪造 B1 或 Library ID。

在这两点明确前，不开始代码实现。
