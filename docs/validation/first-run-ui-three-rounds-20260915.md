# First-use UI Three-round Certification

Date: 2026-09-15 (Asia/Shanghai)

## Result

`FirstRunUiThreeRounds=Passed`

The normal-exit run completed from 19:16:38 to 19:20:29 using one fixed
application binary and a previously nonexistent Workbench database.
It completed +2, Reset, and High Score through the desktop UI, including
Review/Accept and normal tray Exit followed by reopening after each round.

This certifies the first-use UI execution/acceptance/continuity path. It
does NOT certify all-green automatic verification: Workbench reported
`NotVerifiable` in round 1 and `Failed` in rounds 2 and 3. Those messages
were visible in Review and remained in stored evidence. The UI test
explicitly accepted despite them. See the outstanding issue below.

## Fixed Inputs

- Baseline commit: `7e36cfe` plus the current uncommitted changes.
- App DLL SHA-256:
  `52E833B910A8420956C6A754F3BDA536F4D6F50F579381670A8C55FE82E0398C`.
- Provider: OpenCode / `deepseek/deepseek-v4-flash`.
- Godot executable:
  `E:\Godot_v4.6.3-stable_win64.exe\Godot_v4.6.3-stable_win64_console.exe`.
- Evidence:
  `artifacts/local/first-run-ui-20260915-191638-564/`.
- Round 2: `round-2-20260915-191755-546/`.
- Round 3: `round-3-20260915-191901-027/`.

Only a Godot source fixture and its independent Git baseline were prepared
outside the UI. No Workbench governance, accepted state, tasks, claims or
completions were seeded. Git, Godot and authenticated OpenCode were already
installed. This is clean Workbench configuration, not a clean machine.

## Observed Path

1. Empty project list; project registration through the native folder picker.
2. Agent unavailable, invalid executable configuration, repair in Settings,
   and successful discovery.
3. First-work preview, edit invalidation, renewed preview, user confirmation.
4. Leader proposes +2; Worker starts only after UI confirmation.
5. Worker completion leaves accepted state unchanged; Review shows proposals.
6. Accept, Preview, Confirm; all reviewed statements appear in World.
7. Normal tray Exit; reopen recent project; identical accepted statements.
8. Fresh Leader reads the prior accepted state; Reset follows the same path.
9. Another fresh Leader reads +2 and Reset; High Score follows the same path.
10. Final normal exit, reopen, compare World, and normal exit for cleanup.

Every accepted-state comparison reads complete World items, not the
three-item Overview preview. Each round proposed two statements, yielding
six final accepted contributions. No statement was inferred from the round
number or by splitting prose.

The normal-exit records are stored in each round's `ui-exits.jsonl`.

| Round | Exit After Accept | Exit After Recovery Check | Exit Codes |
| --- | --- | --- | --- |
| +2 | PID 30340 | PID 12212 | 0, 0 |
| Reset | PID 26196 | PID 8648 | 0, 0 |
| High Score | PID 26184 | PID 29004 | 0, 0 |

No failure cleanup, external state repair, resume mode, or Leader UI retry
was used in this passing run. Workers may run their own tools; that is not
an external CLI rescue.

## Corroboration

Read-only SQLite inspection after all UI actions found:

- Three canonical completions, each `Governed`.
- Six Authority decisions: initial responsibility, three acceptance
  decisions, and two user-confirmed successor delegations.
- Six accepted contributions exactly matching the recorded proposals.
- Three acceptance Summary entries, each linked to the same decision as
  its completion and containing the corresponding accepted statements.

Acceptance decisions:

- +2: `c84200f2-58cf-4fe1-90e6-ac75a9bc7e18`.
- Reset: `ad3b394c-f1b8-4b12-9d4b-b4d9d4024c3e`.
- High Score: `6a3cfe34-87dd-445e-afb3-23a5a851704a`.

`read-only-persistence-audit.log` retains these checks. No SQL writes were
used to advance or repair the project.

Independent Godot signal-driven behavior checks passed on the final files:
score/high-score `0/0 -> 2/2 -> 4/4 -> 0/4 -> 2/4 -> 6/6`.
See `godot-behavior.log`. This was post-run corroboration, not evidence
injected into Review. Only `scripts/main.gd` and `scenes/main.tscn` differ
from the tracked source baseline.

The fixed App build previously passed 693 tests with 5 live tests skipped.
This turn changed only certification scripts and documentation; the App
hash remained unchanged. The scripts passed PowerShell syntax checks and
the actual desktop walkthrough.

## Outstanding Verification Issue

The stored scope/deliverable checks for rounds 2 and 3 treated
`--headless --check-only --script scripts\main.gd` as a required file path.
They also reported generated `.godot` files and the intended source files
as unexpected paths. These diagnostics are inconsistent with the intended
bounded task and require investigation in the free-text target/scope
extraction path. They must not be relabeled Passed or hidden.

Worker-declared Godot success, post-run independent behavior success, and
Workbench's own verification result are separate evidence. This run proves
that the user can inspect evidence and decide; it does not prove the
automatic verifier correctly classified these tasks. Fixing that
classification and repeating the run is the next quality gate.

Other limits:

- High score survives Reset during play, not a Godot process restart.
- The fixture still displays its original "Baseline: one point per click"
  caption, since the bounded changes did not request that text edit.
- Legacy work-history `Reviewing` labels still appear in Leader context
  alongside canonical Accepted state.
- This is UI automation, not an unfamiliar human usability study.
- Installer, first-time provider authentication, alternate desktop layouts
  and providers were not tested by this run.
- The older seeded release gate was not rerun against these source changes.

## Reproduction

Exit other Workbench instances first. Run from the repository root:

```powershell
pwsh -NoLogo -NoProfile -File tools/verify-first-run-ui.ps1 `
  -ExecutablePath src/Workbench.App/bin/Debug/net10.0-windows/Workbench.App.exe `
  -GodotExecutablePath 'E:\Godot_v4.6.3-stable_win64.exe\Godot_v4.6.3-stable_win64_console.exe' `
  -IncludeContinuation
```

The tray helper uses visible UI controls and clicks the native Exit menu.
It never substitutes process termination for a successful normal exit.
Forced termination is explicitly labeled failure cleanup and cannot
produce a passing round.

Earlier failed attempts, including the hidden-tray toggle problem in
`first-run-ui-20260915-191244-531`, remain separate evidence. This report does
not combine their partial successes into the passing run.

No commit or release tag was created.
