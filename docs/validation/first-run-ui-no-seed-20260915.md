# No-seed First-use Desktop Certification

Date: 2026-09-15

Later evidence: `first-run-ui-three-rounds-20260915.md` records the fresh
three-round normal-exit UI run and its remaining verification limitations.
This document retains the original one-round result below.

## Result

`FirstRunUiLoop=Passed`

One complete fresh-Workbench-database run passed from 15:59:30 to 16:00:31
(Asia/Shanghai). It used real OpenCode / `deepseek/deepseek-v4-flash`, real
Godot project files, UI task confirmation, UI Review/Accept, and process
termination/relaunch. No seed executable, application-service test shortcut,
database repair, or provider CLI rescue was used to advance this run.

Baseline commit: `7e36cfe`, plus the uncommitted changes described below.
This is not a new certified release tag.

## What Was Clean

- The Workbench database did not exist before launch.
- The first screen was the empty project list.
- Project registration and Agent settings were created through the UI.
- Only a source fixture was prepared externally: a copy of the Godot Counter
  with an independent Git baseline. It starts with a +1 score button.
- No AcceptedProjectState, task, governance, claim, or completion was seeded.
- Git, Godot and an authenticated OpenCode installation already existed.
  "Clean configuration" refers to Workbench, not the whole machine.

## Observed Path

1. Launch with only `--database`, not `--project`.
2. Create Project through the native folder picker.
3. Observe Agent unavailable; enable OpenCode and save an invalid executable
   path through Settings.
4. Return to setup, observe failure, reopen Settings, clear the bad override,
   save the actual Godot path, and observe models available.
5. Enter the first work, preview, edit it, observe preview invalidation,
   preview again, and confirm entry into Overview.
6. Observe zero accepted statements and no running Agents.
7. Select DeepSeek in Work; ask the real Leader for a bounded Worker proposal.
8. Observe a visible confirmation button. The source still contains +1.
9. Confirm the task through UI; Worker changes the real source to +2 and
   reports a structured result with proposed changes and validation.
10. Before Accept, Overview still shows zero accepted statements and one
    change waiting for review.
11. Open Review and read the actual proposed contribution. Accept, preview,
    and Confirm Decision through the existing Authority UI.
12. Overview shows one accepted statement matching the reviewed proposal.
13. Terminate the test process, relaunch with the same database, and open the
    project using its recent-project entry, not startup project injection.
14. Overview again shows the identical accepted statement, no pending action,
    and no running Agents.

The accepted statement in this run was:

```text
scripts/main.gd: CLICK_INCREMENT changed from1 to2, so each score button press adds2.
```

Independent post-run inspection found the sole tracked source diff was
`CLICK_INCREMENT: int = 1` becoming `CLICK_INCREMENT: int = 2`.
A bounded headless launch of that final project exited 0. This was corroboration
after the UI run, not a repair or substitute for the Worker's evidence.

## Fixes Exercised

- OpenCode ACP forwards the existing requested output schema as prompt
  instructions; this path does not claim native schema enforcement.
- Worker dispatch requests the existing handoff shape, including
  `ProposedChanges` and `ValidationSummary`. Prose-only legacy results still
  cannot manufacture proposed facts.
- Leader instructions distinguish explicitly requested task preparation from
  a semantic change observation. Observation-only turns still cannot create
  Worker tasks. Every actual dispatch still requires confirmation.
- Long task details scroll within a bounded area; the native confirmation
  button remains outside that scroll area and visible.
- The verifier compares the actual reviewed statement with accepted state,
  rather than demanding one particular English paraphrase.

No Kernel object, Authority write path, or accepted-state projection rule was
added or changed.

## Evidence

Local evidence root:
`artifacts/local/first-run-ui-20260915-155930-859/`

- `walkthrough.log`: stage results, reviewed proposal, executable hash,
  `FirstRunUiLoop=Passed`, and completed transcript.
- `workbench.db`: resulting database.
- `counter/`: actual isolated project and Git baseline.
- `01-empty-home.png` through `11-restarted-overview.png`: UI captures,
  including `08-completed-not-accepted.png`, `09-review.png`,
  `10-accept-preview.png`, and `10b-accepted-overview.png`.
- `godot-independent.log`: successful independent final-project launch.

App used: `src/Workbench.App/bin/Debug/net10.0-windows/Workbench.App.exe`.
Godot used:
`E:\Godot_v4.6.3-stable_win64.exe\Godot_v4.6.3-stable_win64_console.exe`.

SHA-256 at certification:

```text
Workbench.App.dll:
C8A0704EE12F0BCA49F92C15FC4F88DF801D1D0BD794F82324B84A7D75F9680B
Workbench.Runtime.dll:
8E8917037BDF727F3413C25E60EA32B282BDAE3CAE943A7DEA1F6F25F170F2A4
Copied Leader skill:
06692587CFA922D10783C4C5205912D786B8F9A92D477D1CA1948CA710BA3AFE
Verifier at the passing run:
91EE16924F308F437D741FB30C087D7EA3038D857A27B60E1A57A8D688C81A4E
```

After the passing run, only single-instance preflight/error reporting was
added to the verifier. The App binaries and acceptance checks are unchanged.

Read-only SQLite inspection performed after the UI run found one canonical
completion, one accepted contribution, and one Summary entry. There are two
Authority decisions: setup responsibility establishment and the subsequent
assignment acceptance. The completion, contribution, and Summary all reference
acceptance decision `c8c6ed45-3568-4309-aa95-a542c9354860` (commit sequence 2).
The Summary `result_id` is that decision ID. SQLite was opened with `mode=ro`;
these reads did not advance the workflow.

## Regression And Failed Attempts

- App suite: 682 passed, 5 live-provider tests skipped, 0 failed.
- Existing warnings: three xUnit1031 warnings in WorkPaneViewModelTests.
- Runtime suite previously passed with the schema-forwarding fix:
  70 passed, 1 live test skipped.
- PowerShell syntax validation passed.
- The additional seeded three-round Release Gate was not executed: build
  permission review timed out twice. Earlier gate evidence must not be
  represented as a rerun of these changed binaries.

Earlier unsuccessful attempts remain local evidence, not certifications:

| Evidence directory suffix | Failure |
| --- | --- |
| `133510-125` | Worker prose lacked proposed contribution; accepting work established no fact |
| `134926-046` | Leader produced an observation candidate, so no task was created |
| `153939-613` | Existing global app instance rejected the second process before DB creation |
| `155410-392` | UI Accept succeeded, but verifier searched inside the heading instead of the state section |
| `155702-824` | UI Accept succeeded, but verifier rejected a grammatical paraphrase |

Each attempt used a different fresh database/project. The final run did not
resume or repair any failed attempt.

## Certification Boundary

This certifies one no-seed business workflow driven through desktop controls,
with real provider execution and accepted-state recovery. UI automation is
external orchestration; it is not a study with an unfamiliar human.

The harness performs process termination outside the UI. Normal closing only
minimizes Workbench; normal tray Exit was not certified. An already running
instance must be exited before testing because the application is global
single-instance. No original-project database was reset or edited.

Existing limitations remain:

- Provider login, installation, clean-machine setup, three no-seed rounds,
  and a new Leader session reading the accepted state after restart were not
  exercised by this one-round run.
- Workbench reports `NotVerifiable` for free-text scope/acceptance checks;
  Worker-reported validation is evidence, not independently established truth.
- Screenshots include a Windows tray popup in the lower-right corner. Core
  state and decision text remain visible, but this is not a polished visual
  signoff or mobile/small-window certification.
- Native and embedded task previews can appear together; some streamed text
  loses spaces around numbers. These UX/provider issues remain open.
