# Product Loop Godot Counter

这是 Workbench 产品主闭环的最小 Godot 项目夹具。

## Baseline

```text
Score button: +1
Reset: not implemented
High score: not implemented
```

打开 `project.godot` 即可运行。项目不依赖外部资源，所有 UI 都由 Godot 控件组成。

## Product Loop Rounds

Workbench 的产品认证按三轮进行：

1. 将点击增量从 `+1` 改为 `+2`。
2. 增加 Reset 按钮。
3. 增加 High Score 记录。

每轮都必须经过：

```text
Worker execution
  -> changed files
  -> verification evidence
  -> Pending Review
  -> Accept
  -> Accepted Project State
  -> restart
  -> next round
```

## Expected Evidence

第一轮至少应证明：

- Godot 项目可以启动。
- Main scene 可以加载。
- Score button 可以点击。
- Worker 只修改声明范围内的 Godot 文件。
- Workbench 在 Accept 前不更新 Accepted Project State。
- Accept 后显示 accepted change。

Godot 不是 Workbench kernel 的一部分。它只是用于验证产品入口和真实项目文件的 vertical integration fixture。

