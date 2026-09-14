# Product Loop Certification

目标：证明普通用户可以从 Workbench Project Home 走完一次真实项目变化的接受闭环。

## Fixture

```text
demos/product-loop-godot-counter
```

Baseline：

```text
Score button: +1
Reset: not implemented
High score: not implemented
```

## Round 1

任务：

```text
把 Score button 从每次 +1 改为每次 +2。
只修改声明范围内的 Godot 文件。
启动 Godot 并验证按钮行为。
```

Workbench 必须显示：

```text
Proposed Change: Score button now adds 2
Evidence: Godot launched, scene loaded, validation passed
Changed files: within declared scope
Status: Pending Review
```

在 Accept 之前：

- `AcceptedProjectState` 仍然是 baseline。
- Summary 不得提前写入 accepted change。
- Completion、Diff、Evidence、Claim 和 Handoff 必须可见且可恢复。

Accept 之后：

- Accepted State 显示 `Score button now adds 2`。
- Summary 显示该 accepted change。
- 如果有 successor assignment，下一轮工作应基于该状态。

## Restart Gate

1. 完全退出 Workbench 桌面进程。
2. 重新启动 `Workbench.App.exe`。
3. 从 Project Home 打开同一个 Godot 项目。
4. 不使用终端、SQLite 或 CLI rescue。
5. Overview 必须显示：

```text
Accepted: Score button now adds 2
Pending: none
Next work: Reset
```

6. 新 Leader session 的请求上下文必须包含上一轮 accepted state。

## Rounds 2 and 3

Round 2：

```text
增加 Reset 按钮。
```

Round 3：

```text
增加 High Score 记录。
```

三轮完成标准：

```text
baseline +1
  → accepted +2
  → accepted Reset
  → accepted High Score
```

## Certification Boundary

这份认证验证的是 Product Loop 和 Godot vertical integration，不改变
Continuity Kernel 或 Acceptance Spine 的领域模型。

验证脚本：

```powershell
pwsh -NoLogo -NoProfile -File .\tools\validate-product-loop-demo.ps1
```

如果本机没有安装 Godot，脚本仍会验证 fixture 文件和项目引用；安装 Godot
后，脚本会额外执行 headless smoke test。

