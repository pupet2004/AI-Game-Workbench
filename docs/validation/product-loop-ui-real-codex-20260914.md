# Product Loop UI Real Codex Three-Round Validation

Date: 2026-09-14

## Result

**Passed: the desktop Workbench completed three consecutive real Codex Worker
acceptance loops against one Godot project and one Workbench database.**

The run used:

- The real local Codex CLI app-server provider.
- A disposable Git copy of `demos/product-loop-godot-counter`.
- A disposable Workbench SQLite database.
- The Avalonia desktop UI driven through Windows UI Automation.

## Certified Rounds

```text
Round 1: The score button now adds 2 per click.
Round 2: The counter has a Reset button.
Round 3: The counter records the highest score.
```

The same ProjectId, project directory, and database were reused for all three
rounds. Rounds 1 and 2 also created the next Assignment through the real UI
successor field:

```text
Round 1 -> Add a Reset button to the counter.
Round 2 -> Add high score tracking to the counter.
```

## Certified Path

```text
Desktop Project Overview
  -> Open workspace
  -> Confirm / Start Worker
  -> real Codex edits the declared Godot files
  -> Godot headless validation
  -> Completion Package
  -> Verification Evidence
  -> Claim / Handoff
  -> Preview Decision
  -> Confirm Decision
  -> AcceptedProjectState
  -> force-stop Workbench
  -> restart Workbench
  -> accepted state recovered
```

Observed final Godot state:

```text
CLICK_INCREMENT = 2
ResetButton exists and resets score to 0
HighScoreValue exists and tracks high_score
```

The final isolated database contained three records for each per-round
canonical artifact:

```text
WorkerExecution                  3
Verification Evidence            3
Evidence Record                  3
Canonical Worker Completion      3
Claims                           9
Canonical Handoff                3
Authority Decisions              6
Accepted State Contributions     3
Project Summary                  3
```

Certification artifacts from the passing run:

```text
Project:
artifacts/local/product-loop-real-codex-20260914-192518-005/counter

Database:
artifacts/local/product-loop-real-codex-20260914-192518-430.db
```

Accepted statements progressed after each restart:

```text
1 -> The score button now adds 2 per click.
2 -> The counter has a Reset button.
3 -> The counter records the highest score.
```

The restart assertion showed one accepted statement, no pending handoffs, and
the accepted statement:

```text
The score button now adds 2 per click.
```

## Harness

```powershell
pwsh -NoLogo -NoProfile -File .\tools\verify-product-loop-ui-real-codex.ps1 `
  -ExecutablePath .\src\Workbench.App\bin\Debug\net10.0-windows\Workbench.App.exe `
    -SeedDllPath .\artifacts\local\product-loop-ui-seed\ProductLoopUiSeed.dll
```

## Boundary

This record certifies the real desktop Worker path for three complete rounds,
including Completion, Evidence, Claim, Handoff, UI Authority Accept, successor
Assignment creation for rounds 1 and 2, and process-level restart recovery.

It does not certify a live Leader session writing the next request after each
restart; the next-round task drafts were prepared by the certification seed.
