# Workbench 核心连续性闭环演示脚本

这是一份可以反复执行的现场演示稿。照着顺序操作即可，不需要手写数据库，也不要直接调用 Service。

## 一、这次演示证明什么

演示一条真实连续性链：

```text
真实 Leader 对话
  → 发现项目变化
  → Evolution Candidate
  → Governance Preview
  → 用户明确接受
  → Truth 或 Library 持久化
  → Worker 完成 / FinalReport / Handoff
  → durable Summary
  → 重启或换脑后由新 Leader 恢复
```

本演示不证明：语义粒度最优、Token 消耗最低、Provider 平行能力、Benchmark 或产品已经完成。

## 二、启动方式

### 每次重新开始一轮

1. 关闭已经打开的 Workbench。
2. 双击桌面的 `AI Game Workbench - Core Continuity Demo.lnk`。
3. 等待 Workbench 打开《零刻》项目。

这个快捷方式会从冻结 baseline 创建一个新的隔离 run，不会修改正式 Workbench 数据。

### 继续同一轮

如果只是中途关闭窗口、想继续同一轮，不要重新双击 fresh Demo 入口。使用命令行：

```powershell
.\tools\run-core-continuity-demo.ps1 -RunDirectory "<本轮 run 目录>" -Resume
```

同一轮内的 Candidate、Proposal、Library、Worker、Handoff 和 Summary 都应该保留。

### 重要区别

- `AI Game Workbench.lnk`：普通 Workbench，使用正式数据。
- `AI Game Workbench - Core Continuity Demo.lnk`：演示入口，每次创建 fresh 隔离副本。

## 三、开始前检查

打开窗口后先不要发送消息，确认：

- 项目名称是《零刻》；
- Leader 面板可以输入消息；
- 能看到 Leader、Work、Library 等面板；
- 没有上一次演示遗留的 Pending Proposal；如果有，说明没有启动 fresh run；
- 不要点击“开始新的”或清理任何项目数据。

## 四、阶段 1：确认新 Leader 能恢复项目

这一阶段拆成两个短回合。先展示项目当前是什么，再展示项目最近怎么变化。不要把 Evolution 放进第一轮，避免一次查询同时展开所有来源。

### A. Current World：Truth + Library

#### 发给 Leader 的提示词

```text
先告诉我这个项目当前有哪些正式 Truth，以及有哪些长期 Library 内容。

只回答当前 Project World：
1. AcceptedProjectState 中有哪些正式规则？
2. Library 中有哪些长期内容？

不要展开最近 Worker、Summary、Evolution 或旧聊天历史，也不要修改任何正式状态。
```

#### 预期

Leader 应该快速回答：

- 当前正式 Truth；
- 当前 Library 内容；
- Truth 与 Library 的区别。

向观众强调：这是“项目现在是什么”，不是项目历史。此时只观察，不接受任何变化。

### B. Recent Continuity：Worker + Summary + Evolution

#### 发给 Leader 的提示词

```text
现在再告诉我项目最近发生了什么：
1. 最近完成过哪些 Worker 工作？请指出 Artifact、FinalReport 和 Handoff。
2. 最近有哪些 durable Summary？它们分别总结了什么？
3. 最近有哪些重要 Evolution 或治理变化？这些变化为什么发生？

请区分 Worker 结果、Summary、待治理 Candidate、Library 变化和已经进入正式 Truth 的变化。不要修改任何状态。
```

#### 预期

Leader 应该从恢复上下文回答：

- 最近 Worker 的 Artifact / FinalReport / Handoff；
- durable Summary 及其来源；
- 最近的 Evolution、治理状态和变化原因；
- 哪些已经接受，哪些仍然只是 Candidate 或 Proposal。

向观众强调：这是“项目最近怎么变成这样”。这一轮结束后再进入 Authority Candidate，不要在恢复阶段接受任何变化。

## 五、阶段 2：制造 Authority Candidate

### 发给 Leader 的提示词

```text
我确定一条新的正式规则：每次时间旅行必须在出发前生成一个全局唯一、不可复用的因果编号；同一个编号只能对应一次实际穿越，不能复制到另一条穿越上。这条规则仍然只用于标识和追踪因果链，不参与风险评分，也不能阻止任何人的时间旅行。

请正常回答，并在确实需要治理时提出 Evolution Candidate。不要直接修改正式状态，等待用户确认。
```

### 预期

Leader 的回答区域或 Candidate 区应出现一条新的 Evolution Candidate。检查它是否包含：

- Object；
- Before；
- After；
- Impact；
- Route，通常是 Authority / Truth；
- Reason；
- Source 或当前消息来源。

Candidate 出现后，确认 Accepted Project State 仍然没有变化。Candidate 只是提议，不是 Truth。

### 点击什么

1. 在 Leader 面板的 **Evolution Candidates** 区找到这条 Candidate。
2. 点击该 Candidate 下方的 **准备治理草案**（英文界面可能显示 `Prepare governance draft`）。
3. 检查出现的 **Governance draft preview / 治理草案预览**。
4. 确认标题、Before、After、Impact、Route 和 Source 都合理。
5. 对 Authority Candidate，点击 **Create Authority Confirmation**。

此时仍然不应该直接改变正式 Truth，只会生成待确认的 Authority Confirmation。

## 六、阶段 3：接受 Authority 变化

### 检查待确认卡片

在 Leader 面板找到 **Authority Confirmation** 区。卡片应明确显示：

```text
待确认的项目事实（尚未进入 AcceptedProjectState）
```

确认它的内容就是刚才的“因果编号”规则。

### 点击什么

1. 点击 **接受**。
2. 等待状态消息刷新。
3. 不要重复点击。

### 预期

- Authority Confirmation 消失或显示已接受；
- AcceptedProjectState 出现新的因果编号唯一性正式规则；
- Accepted contribution 的文字应该是“当前正式规则”，不能仍然写“待用户确认”；
- provenance / source 中能看到 Candidate 或其来源引用；
- Library 不应因为这一步被污染。

如果 Candidate 在点击“接受”之前就改变了 Truth，立即停止并记录为失败。

## 七、阶段 4：制造并接受 Library Candidate

### 发给 Leader 的提示词

```text
下一章固定加入一种“时间旅行者黑话”：这是时间旅行者群体长期使用的文化细节，后续章节会反复出现，但它不是世界规则，也不改变时间旅行的物理机制。

请把它作为需要长期记住的创作内容提出 Candidate，但不要把它写入正式 Truth，等待用户接受。
```

### 预期

Leader 应识别为 Content / Library，而不是 WorldRule / Authority。Candidate 应说明：

- 这是可长期复用的创作内容；
- 不改变时间旅行物理规则；
- route 是 Library 或 Content；
- source 能回到当前 Candidate。

### 点击什么

1. 在 Candidate 区点击 **准备治理草案**。
2. 检查 Preview 中的对象、内容、分类、来源。
3. 点击 **Create Library Proposal**。
4. 转到右侧或下方的 **Library** 面板。
5. 在 **Pending Proposal / 待处理提案** 列表中单击这条提案，使其成为选中项。
6. 检查 Proposed Content、Proposed Overview、Materials / Sources。
7. 点击 **接受**。

### 预期

- Proposal 从 `Pending` 变成 `Accepted`；
- Library Object / Timeline Node 生成；
- Material Ref / Source Ref 指向 durable Candidate identity；
- 这条内容出现在 Library；
- AcceptedProjectState / Truth 不出现“时间旅行者黑话”作为正式世界规则。

如果 UI 显示已接受，但重新加载后仍是 Pending，停止演示并记录为失败，不要用数据库手工修复。

## 八、阶段 5：创建 Worker 工作

### 发给 Leader 的提示词

```text
根据当前已接受的因果编号设定，写一个约 500 字的小场景草稿。

要求：
- 场景中体现因果编号只负责标识和追踪因果链；
- 不改变已经接受的正式规则；
- 完成后返回 Artifact、FinalReport 和交接说明；
- 这是一个有边界的 Worker 任务，请不要自行确认任何新的 Truth。
```

### 预期

Leader 应提出一个明确的 Worker 工作，而不是把 Worker 当作 Authority。根据当前 UI，可能需要：

1. 在 Leader 的 Worker 草案或任务确认区确认任务；
2. 选择当前可用的 Worker / Runtime；
3. 点击 **确认 Worker** 或同等的任务确认按钮；
4. 等待 Work 面板出现 Worker 卡片。

如果出现 Runtime approval，请只按现场需要批准当前 Worker 运行，不要改变项目治理设置。

## 九、阶段 6：检查 Artifact、FinalReport、Handoff 和 Summary

### 点击什么

1. 转到 **Work** 面板。
2. 找到刚才创建的 Worker 卡片。
3. 点击 Worker 卡片或 **展开**，查看任务详情。
4. 确认出现 Artifact 或输出文件信息。
5. 展开 **Handoff / 交接** 区，查看完成结果、已验证内容、未解决问题和 provenance / source。
6. 如有 **查看详情**，点击查看完整 FinalReport。

### 预期

必须同时看到或能恢复：

- Artifact；
- Worker FinalReport；
- Handoff；
- Worker 来源和任务身份；
- durable Summary。

Summary 应在 Worker 完成后自动出现，不需要 Leader 额外说一句“请生成 Summary”。Summary 是连续性辅助信息，不是 Truth，也不能绕过 Authority 改变正式状态。

可在 Leader 的恢复信息、Summary 区或 Evolution 视图中检查 Summary identity、内容、source type、source id 和创建时间。

## 十、阶段 7：查看 Evolution / Library 分层

打开 Evolution、Library Overview 或相应的信息视图，向观众说明四类内容：

| 类型 | 应看到的内容 | 权威性 |
|---|---|---|
| Truth | 因果编号正式规则 | Accepted Project State |
| Library | 时间旅行者黑话 | 长期内容投影，不是 Truth |
| Work | 场景草稿、FinalReport、Handoff | 执行结果与证据 |
| Summary / Evolution | 最近阶段变化及来源 | 恢复与导航辅助 |

重点展示：Library 内容没有污染 Truth，Worker 结果也没有自动变成 Truth。

## 十一、阶段 8：换脑并验证恢复

### 点击什么

1. 回到 Leader 面板顶部。
2. 点击当前 Leader 名称或 **换脑**。
3. 在弹出的 Leader 列表中选择另一个可用 Leader。
4. 如果出现两个选项：需要延续当前工作时点 **继续上一次**；只需要新 Leader 重新读取项目时点 **开始新的**。
5. 等待新 Leader 完成启动，不要依赖旧聊天继续解释项目。

### 发给新 Leader 的提示词

```text
这是一次新的 Leader 接管。请只根据当前恢复到的 Project World、AcceptedProjectState、Library、Worker 结果、Summary 和 Evolution 信息回答：

1. 现在有哪些正式规则？
2. 最近确定了哪些不是正式规则的创作内容？
3. 最近 Worker 做了什么？Artifact、FinalReport 和 Handoff 分别是什么？
4. 项目最近为什么发生这些变化？
5. 因果编号这条 Truth 和时间旅行者黑话这条 Library 内容，分别来自哪个 Candidate 或来源？

不要假设旧聊天仍然存在，也不要把 Library 或 Summary 直接当成 Truth。
```

### 预期

新 Leader 应正确区分：

- Truth：已接受的因果编号规则；
- Library：时间旅行者黑话；
- Worker：刚完成的场景草稿及其 Artifact / FinalReport / Handoff；
- Summary：最近阶段认知；
- Evolution / provenance：变化来自哪个 durable Candidate、何时进入治理、如何被接受。

## 十二、现场判定

### 通过条件

本轮至少确认：

1. 新 Leader 能恢复项目，而不是依赖旧 transcript；
2. 正常聊天能产生 Candidate；
3. Candidate 在用户接受前不改变 Truth；
4. Authority 接受后 Truth 持久化；
5. Library Proposal 从 Pending 变为 Accepted；
6. Library 变化不污染 Truth；
7. Worker 产生 Artifact、FinalReport 和 Handoff；
8. Worker 完成自动产生 durable Summary；
9. Candidate provenance 能追到 Authority / Library；
10. 换脑后新 Leader 能恢复上述全部内容。

### 立即停止条件

- Candidate 未经接受直接改变 AcceptedProjectState；
- Library UI 显示接受但持久状态仍为 Pending；
- Library 内容出现在 Truth；
- Worker 没有 FinalReport、Handoff 或 Summary；
- 新 Leader 只能复述旧聊天，不能从恢复上下文回答；
- Demo 触碰正式数据库或正式《零刻》项目目录。

失败时只记录现象、所在阶段、界面状态和时间，不手写数据库，不删除 run，不伪造成功。

## 十三、演示结束与下一次复跑

1. 正常关闭 Workbench。
2. 保留本轮 `artifacts\\demo-runs\\run-*` 目录，便于复盘。
3. 下一次重新双击 `AI Game Workbench - Core Continuity Demo.lnk`。
4. 新一轮会重新复制冻结 baseline；上一轮的变化不会污染下一轮。

复跑契约：

```text
冻结 baseline → fresh run → 对话 / 治理 / Worker / 换脑
                         ↓
                 本轮变化全部保留

再次 fresh run → 回到同一个冻结起点
```
