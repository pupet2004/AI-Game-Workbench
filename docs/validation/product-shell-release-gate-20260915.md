# Product Shell Release Gate Rerun

Date: 2026-09-15

## Result

`ProductLoopReleaseGate=Passed`

Application baseline: `7e36cfe` (`Complete product shell first-run experience`).
App and ProductLoopUiSeed were rebuilt together from that source into
`artifacts/local/gate-current-20260915`: 0 warnings, 0 errors.

The verifier working tree additionally excludes generated `.godot` / `.git`
directories when copying the fixture, captures Worker-start and failure
screenshots, and reports review-wait progress every 30 seconds. No application,
Kernel, Authority, or accepted-state behavior was changed for this rerun.

## Environment

- Provider: OpenCode.
- Model: `deepseek/deepseek-v4-flash`.
- Godot: `4.6.3.stable.official.7d41c59c4`.
- Executable:
  `E:\Godot_v4.6.3-stable_win64.exe\Godot_v4.6.3-stable_win64_console.exe`.
- Desktop and provider execution ran with explicit permission outside the
  restricted sandbox, without overriding `APPDATA` or `LOCALAPPDATA`.
- A fresh disposable project and database were used.

## Observed Rounds

| Round | Proposed change | Accepted count before Accept | After forced restart |
| --- | --- | --- | --- |
| 1 | Score button adds 2 | 0 | Count 1, +2 statement recovered |
| 2 | Reset button | 1 | Count 2, Reset statement recovered |
| 3 | High Score tracking | 2 | Count 3, High Score statement recovered |

Each round started the real Worker through desktop UI, reached pending review,
used Accept -> Preview -> Confirm, checked the changed project files, terminated
Workbench, and reopened it against the same database. After each restart the
Overview showed no pending action and no running Agents. Successor assignments
were visible after rounds 1 and 2.

The original fixture passed static and headless validation. A separate bounded
headless launch of the final modified project also returned exit code 0.

## Local Evidence

- Launch transcript: `artifacts/local/product-shell-deepseek-20260915-130134.log`.
  PowerShell did not capture the inherited child-process stdout in this file;
  it is not a complete gate log. The round results and final Passed marker were
  observed in the command execution output, which exited with code 0.
- Database: `artifacts/local/product-shell-deepseek-20260915-130134.db`.
- Project: `artifacts/local/product-loop-real-codex-20260915-130136-534/counter`.
- Screenshots: `artifacts/local/product-shell-deepseek-20260915-130134.screenshots`.
  Each round has Worker-start, Review Queue, change, and preview captures.
  A capture reflects the current scroll position, not the entire page.

## Earlier Attempts

The earlier restricted runs encountered Godot user-directory access errors,
NuGet network denial, and an interrupted Codex review wait. Their incomplete
results are not successful certifications.

The same Godot 4.6.3 installation passed this authorized rerun without user
directory redirection. Earlier evidence therefore does not justify classifying
the failure as a general Godot 4.6.3 headless defect.

The earlier seed console output did not match the current source's output
fields. This rerun rebuilt both binaries instead of relying on that artifact.
It does not establish the precise cause of the interrupted Codex attempt.

## Certification Boundary

This is a seeded product-loop regression, not a clean-install first-user test.
ProductLoopUiSeed still prepares governance and task records. It cannot prove
that an unfamiliar user can create the first project/task exclusively via UI.

Review displays the Worker's Godot validation report but also reports
`NotVerifiable` for some structured scope/acceptance checks. A successful
headless exit and file-pattern checks do not prove every interactive gameplay
behavior or full machine verification of the proposed change.

Next: a fresh-configuration Create/Open -> Configure -> First work -> Review ->
Accept walkthrough without seed or database repair, including failure recovery.
