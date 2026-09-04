# Current Working-Tree Validation

Recorded: **2026-09-04**
Repository: `C:\Users\pupet\Documents\ChatGPT\AI Game Workbench`
Sealed `HEAD`: `cc1f70b0ed904341994e64fbf4143af1c5b8ff3b`
Working tree: **dirty; committed source/test changes plus retained generated artifacts and local experiment files**

## Fresh verification

Commands run on 2026-09-04:

```powershell
dotnet build .\AI.Game.Workbench.sln --no-restore --verbosity minimal
dotnet test .\AI.Game.Workbench.sln --no-restore --verbosity minimal
```

Build result: **0 warnings, 0 errors**.

Test result: **1,073 passed, 0 failed, 0 skipped**.

| Test project | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Workbench.Core.Tests | 128 | 0 | 0 |
| Workbench.Runtime.Tests | 67 | 0 | 0 |
| Workbench.Project.Tests | 28 | 0 | 0 |
| Workbench.App.Tests | 543 | 0 | 0 |
| Workbench.Storage.Tests | 307 | 0 | 0 |
| **Total** | **1,073** | **0** | **0** |

## Interpretation boundaries

- This is a current local working-tree result, not a tagged or committed release result.
- The default full-suite command does not prove live OpenCode execution. `OpenCodeLiveB1AcceptanceTests` is gated by `WORKBENCH_RUN_OPENCODE_LIVE=1`; use the dedicated WEIQI3 demo command and preserve its environment and output when claiming live-provider evidence.
- Agent participation remains non-authoritative: Agent output is recorded as Claims/Handoffs and requires a separate Authority Decision before Accepted Project State changes.
- The historical Alpha records at `v0.1.0-alpha.20260827` report earlier counts and remain valid only as historical snapshots.
