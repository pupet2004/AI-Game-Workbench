# No-seed Continuation: Diagnostic History

Date: 2026-09-15

## Status

Update at 19:20 on 2026-09-15: a fresh three-round UI run with normal tray
exits passed. See `first-run-ui-three-rounds-20260915.md` for the fixed build,
evidence and limits. Automatic verification did not pass all rounds; that
remaining defect is explicitly recorded there.

The following sections retain the earlier incomplete diagnostic sequence,
not the status of the later passing run.

The first no-seed round remains certified as documented in
`first-run-ui-no-seed-20260915.md`. Rounds 2 and 3 are NOT certified.
The intended remaining rounds are Reset, then High Score, preserving +2.
They are not three increment changes.

The continuation uses the original isolated project/database:
`artifacts/local/first-run-ui-20260915-155930-859/`.
The Reset Worker has now completed and was accepted through UI. The diagnostic
continuation is still incomplete; its second-round harness did not finish.
The database was inspected read-only for diagnosis, never repaired.

## Findings And Changes

- A restored Leader locks the initial model selector and may display its
  persisted model ID rather than the discovered friendly label.
- Opening Change Brain could query an empty runtime registry, clearing model
  options. The picker now reconnects a missing runtime before discovery.
- An empty catalog for an existing session now fails without clearing the
  recovered selection. The existing error handling remains in use.
- Two regression tests cover reconnect-without-session-creation and retention
  of an existing selection when no catalog is available.
- App suite after these changes: 684 passed, 5 live tests skipped, 0 failed.
  Three existing xUnit1031 warnings remain.

The updated application displayed the full model list during the live retry.
That retry stopped because the verifier matched a same-name text control
without SelectionItemPattern. The verifier now explicitly selects a visible,
enabled matching option supporting SelectionItemPattern.

## Evidence

- `round-2-20260915-165231-389/`: empty picker failure, screenshot and transcript.
- `round-2-20260915-171244-928/`: model discovery recovered; verifier selection
  failed before Switch Brain. Screenshot and transcript retained.
- Updated App DLL SHA-256:
  `F96F05F77364D437F97FDA0E683F1372E5FA4BA2C51211580CB9EB9C44644449`.
- `tools/verify-first-run-ui-continue.ps1` is an unfinished certification
  harness, not evidence that its assertions have passed.

The proposed continuation checks include a UI-created fresh Leader, its
read-only accepted-state answer, task confirmation, unchanged accepted state
before Accept, exact reviewed contribution recovery after restart, and
source-pattern checks. Full gameplay behavior still needs independent testing.

## Live Crash And Recovery

After the user requested renewed approval, the desktop retry ran:
`round-2-20260915-171616-775/`.
The new Leader correctly described the accepted +2 change and its Authority
Decision. Confirming the Reset draft then crashed the application at
2026-09-15 17:16:38 local time. Windows .NET Runtime event 1026 reported
`B1CommandException: The effective Revision already has a disposition.`
The exception escaped canonical launch preparation in `ConfirmDraftAsync`.

The fix keeps ordinary canonical launch non-authoritative. A separate
user-confirmed draft adapter delegates subsequent work through the existing
Authority command service when the prior revision has a disposition. The
delegation records no accepted contribution. Retry can recover that decision
by the full task revision contract; linked retries remain on their original
assignment. Preparation failures are shown without losing the draft or
falling back to an ungoverned Worker.

App regression after the fix: 688 passed, 5 live skipped, 0 failed.
Three pre-existing xUnit1031 warnings remain.

`round-2-20260915-174054-079/` restored the persisted Reset draft through UI,
started the real DeepSeek Worker, modified the real Godot files and reached
pending Review. The desktop did not crash. The verifier then failed because
its accepted-state extraction traversed a UIA parent containing unrelated
Overview text. The retained screenshot shows the original +2 contribution
and one Reset proposal waiting for review. No Accept was performed.

The Overview statement now has a dedicated automation identifier, and
continuation comparisons use only those statement controls. The harness
also checks process liveness while waiting and supports explicit UI-only
recovery of an existing draft or pending review. These recovery modes do not
make an interrupted run a clean certification.

## Next Run

`round-2-20260915-174612-724/` opened the pending Reset Review, previewed and
confirmed Accept. The UI then displayed three accepted statements, not two:
the Worker had proposed two distinct Reset contributions. The harness's
one-contribution-per-round assumption failed after the successful Accept.
No state repair was attempted.

Review now exposes each proposed statement as a separate visible item.
The revised harness compares those exact items against the complete World
state, rather than splitting joined prose or reading Overview's three-item
preview. Contribution counts are derived from the reviewed items, not rounds.

The fresh attempt `artifacts/local/first-run-ui-20260915-175153-533/` passed
round 1 on App hash
`D2776E1FE482FF28C7264326F8B0347B8AF1CD1C2EBBF5ADCD656C0D317FC429`.
Round 2 reached Review but had no proposed contributions: ACP concatenated
progress prose before an explicit JSON FinalReport, and the Worker parser
only accepted an entire JSON document. No second-round Accept occurred.
Its Review screenshot retains the unparsed report and the explicit
`NotVerifiable` machine-verification status.

The parser was hardened to accept one explicit terminal JSON report
after progress prose, using structured JSON parsing. It does not infer facts
from prose and rejects ambiguous, nonterminal, nested and invalid reports.
This attempt is failed, not a three-round certification.

Run `tools/verify-first-run-ui.ps1 -IncludeContinuation` on a fresh database
with one fixed App binary. Since round 1 and earlier diagnostic attempts use different
App builds and include failed attempts, this sequence cannot be called a clean,
uninterrupted three-round release gate. A fresh full rerun is still required.

No new commit, tag, or release certification was created. The original
Workbench window is not restored by this stopped run.

## Later Runs

- `first-run-ui-20260915-175847-108`: Leader request failure before Worker;
  no completed round.
- `first-run-ui-20260915-180231-813`: all three UI work/acceptance rounds
  passed, using process termination for restart boundaries. Independent
  Godot behavior checks also passed.
- `first-run-ui-20260915-191244-531`: normal-exit round 1 passed; round 2
  accepted Reset but the verifier failed to open the hidden tray popup.
  Failure cleanup was forced and is not a certified normal exit.
- The helper now checks whether the hidden-tray popup is already open
  before toggling its button. Three isolated normal-exit smoke runs passed
  in `tray-exit-smoke-20260915-191557`.
- `first-run-ui-20260915-191638-564`: complete fresh three-round run,
  six normal tray exits, no forced cleanup or UI retries. Its separate
  certification report documents the automatic verification failures.
