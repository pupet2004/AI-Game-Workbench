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

当前已通过一个非 UI 的真实 Provider gate：

```text
Real Codex
  → real Godot project copy
  → CLICK_INCREMENT 1 → 2
  → Godot headless validation
  → Completion / Evidence / Claim / Handoff
  → explicit Accept
  → database reopen
  → AcceptedProjectState recovered
```

证据记录：

```text
docs/validation/godot-product-loop-live-20260914.md
```

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

如果 Godot 没有注册到当前 shell 的 PATH，可以显式传入可执行文件：

```powershell
pwsh -NoLogo -NoProfile -File .\tools\validate-product-loop-demo.ps1 `
  -GodotExecutablePath "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\GodotEngine.GodotEngine_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.7.2-stable_win64_console.exe" `
  -RequireGodot
```

Workbench 也支持直接打开项目：

```powershell
dotnet run --project .\src\Workbench.App\Workbench.App.csproj -- --project .\demos\product-loop-godot-counter
```

发布后的桌面程序使用同样的参数：

```powershell
.\Workbench.App.exe --project .\demos\product-loop-godot-counter
```

桌面进程级重启 harness：

```powershell
pwsh -NoLogo -NoProfile -File .\tools\verify-product-loop-process-restart.ps1
```

该 harness 会构建 Workbench，使用同一个项目路径启动两次，并在两次启动
之间强制结束第一个进程。它只验证进程级启动/退出/重启，不伪造 Worker、
Accept 或 UI 操作结果。

如果本机没有安装 Godot，脚本仍会验证 fixture 文件和项目引用；安装 Godot
后，脚本会额外执行 headless smoke test。
