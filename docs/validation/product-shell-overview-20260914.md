# Product Shell Overview Validation

Date: 2026-09-14

## Scope

This change introduces the first Product Shell entry surface for an opened
project:

- `Overview`
- `Work`
- `Review`
- `World`
- `History`
- `Settings`

The Overview surface presents accepted project state, current work, Agent
status, pending decisions, recent accepted change, and the next user action.
The detailed projection and provenance tools remain available under `World`.

## Semantic Checks

- Accepted state is read from the Authority projection.
- Completion and Evidence do not write accepted state.
- A persisted `Running` execution is not presented as live after restart unless
  an attached hosted session has an active turn.
- Accepted or rejected assignment revisions are not shown as current work.
- Recent change is derived from an Authority decision with accepted
  contributions, not from an unaccepted Summary entry.
- Overview `Continue`, `Review`, and individual handoff review entry points
  converge on the Review Queue.
- No automatic Leader execution is introduced by this shell change.

## Automated Tests

Targeted Product Shell tests:

```text
19 passed, 0 failed
```

These include current-work ordering, restart status semantics, Authority
disposition labels, accepted-state timing, summary isolation, and Review Queue
routing.

## Real Desktop Certification

Command:

```powershell
pwsh -NoLogo -NoProfile -File .\tools\verify-product-loop-ui-real-codex.ps1 `
  -ExecutablePath .\src\Workbench.App\bin\Debug\net10.0-windows\Workbench.App.exe `
  -SeedDllPath .\artifacts\local\product-loop-ui-seed\ProductLoopUiSeed.dll
```

Result:

```text
Real Codex desktop Worker three-round product loop passed.
```

The isolated run used one Godot project and one Workbench database:

```text
ProjectPath=artifacts/local/product-loop-real-codex-20260914-213614-385/counter
DatabasePath=artifacts/local/product-loop-real-codex-20260914-213614-824.db
```

Round results:

```text
Round 1: accepted count remained 0 before Accept; +2 recovered after restart.
Round 2: accepted count remained 1 before Accept; Reset recovered after restart.
Round 3: accepted count remained 2 before Accept; High Score recovered after restart.
```

Each round used the real Codex desktop Worker, real Godot project files, UI
Review, explicit Accept, forced Workbench process termination, and a new
Workbench process.

## Remaining Boundary

This certifies the Overview shell against the existing Product Loop. It does
not certify live Leader drafting, automatic next-task execution, or a full
first-run user study. Those remain Product Shell follow-up work.
