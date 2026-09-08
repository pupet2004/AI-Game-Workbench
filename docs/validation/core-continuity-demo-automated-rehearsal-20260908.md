# Core Continuity Demo 自动彩排记录

日期：2026-09-08  
类型：Automated rehearsal，不等同于人工现场 Demo 验收

## 本次彩排目标

验证可复跑 Demo runner 是否能从冻结基线创建隔离演示轮次，并在关闭 Workbench 后通过 resume 继续同一轮。彩排不把自动启动结果冒充为真实 GPT 对话、真实 UI 点击或完整现场演示。

## Run 信息

- Run directory：`artifacts/demo-runs/run-20260908-222058`
- Database：`artifacts/demo-runs/run-20260908-222058/workbench.db`
- Project：`artifacts/demo-runs/run-20260908-222058/零刻`
- Runtime：`artifacts/local/demo-bootstrap/Workbench.App.exe`
- Project ID：`db5333e2-573d-432f-b457-1fc3b063a122`
- Baseline：`artifacts/demo-baseline`

## 自动彩排结果

| 阶段 | 自动动作 | 结果 | 状态 |
|---|---|---|---|
| Frozen baseline | 复制 baseline project 与 SQLite 数据库 | 完成 | PASS |
| Fresh run | 创建独立 `run-20260908-222058` | 完成 | PASS |
| Project relocation | 数据库中的《零刻》保持原 Project ID，并指向本轮目录 | 完成 | PASS |
| Runtime launch | 使用 `demo-bootstrap` 启动真实 Workbench.App | 完成 | PASS |
| Process restart | 关闭 Workbench.App | 完成 | PASS |
| Resume | 使用同一 run directory 重新启动 | 完成 | PASS |
| Isolation | 未写入正式 Workbench 数据库或正式《零刻》目录 | 已检查 | PASS |

## 尚未由自动 runner 验证的部分

以下步骤需要人工在 Workbench UI 中使用真实 GPT-5.6 完成，不能由当前 PowerShell runner 的启动/恢复检查替代：

1. Current World 与 Recent Continuity 两轮 Leader 查询；
2. 正常对话生成 Authority Candidate；
3. Governance Preview 与 Authority Accept；
4. 正常对话生成 Library Candidate、Proposal Accept 与 provenance 检查；
5. Worker、Artifact、FinalReport、Handoff 与自动 Summary；
6. 换脑后新 Leader 的四问恢复验证。

这些步骤的现场操作顺序和提示词见：`docs/demo/core-continuity-demo.md`。

## 判定

**自动彩排：PASS。**

这证明 Demo runner 已具备可重复的启动、隔离、重定位和 resume 能力。  
**完整人工 Demo：待执行。** 只有在真实 GPT-5.6、真实 UI 操作、治理接受、Worker 完成和换脑恢复全部按脚本跑完后，才能记录为现场 Demo PASS。
