# ZERO_HOUR_DEMO_CLOSURE_REPORT

Date: 2026-09-05

## Result

**PARTIAL: presentation wiring is closed for the selected Worker card path; the complete 5–10 minute UI walkthrough is not yet proven.**

The selected path now reads the existing `WorkerToLeaderHandoff` event, maps it through `HandoffDisplayModelFactory.FromLegacyWorkerHandoff`, and renders a Handoff card on the corresponding Worker card. Legacy completion remains Legacy; no B1 Handoff or AuthorityDecision is created by this display path.

## Steps and evidence

| Step | Status | Evidence |
|---|---|---|
| Open/create Project World | READY | Existing Project World Explorer and Project entry status path |
| Leader delegates bounded work | MANUAL OPERATOR STEP | Existing Leader/Worker delegation UI; no new wiring added |
| Real Worker/provider execution | READY in live acceptance path | OpenCode/DeepSeek live test executed with `WORKBENCH_RUN_OPENCODE_LIVE=1` |
| Worker completion persists | READY in live acceptance path | Existing Worker execution and Handoff event path |
| Visible Handoff card | READY for loaded Worker cards | `WorkPaneViewModel` reads latest handoff and renders `HandoffDisplayModel` |
| Produce `chapter-01.md` | BLOCKED / NOT PROVEN | The live acceptance test uses a concise no-tools verification prompt; it does not create the Zero Hour chapter artifact |
| Retain/search Library knowledge | READY as presentation path; MANUAL OPERATOR STEP | Library search labels Accepted Fact, Proposal, Summary, and projection separately |
| Rotate/reopen Leader | READY in existing recovery structures; MANUAL OPERATOR STEP | Existing Leader epochs and boot context; no fresh UI walkthrough performed |
| Recovery display | READY as presentation path | `RecoveryViewModel` assembled from existing Project World projections |
| Full first-time-user walkthrough | BLOCKED | No automated UI driver or fresh manual capture was available in this run |

## Live provider evidence

- Provider path: OpenCode with DeepSeek, followed by Codex continuation.
- OpenCode/DeepSeek B1 participation test: **1/1 passed** with `WORKBENCH_RUN_OPENCODE_LIVE=1`.
- OpenCode → Codex continuation: **1/1 passed** with both `WORKBENCH_RUN_OPENCODE_LIVE=1` and `WORKBENCH_RUN_CODEX_LIVE=1`.
- The live test proves Attempt, SessionBinding, Handoff, Guided Decision, Accepted State persistence, and restart projection for the test fixture.
- It does not prove the Zero Hour chapter artifact or the complete visible UI walkthrough.

## Deterministic verification

- Full `Workbench.App.Tests`: **544 passed, 3 skipped** before the closure wiring; the closure-specific Worker/Handoff filter passed **89, 2 skipped**.
- Solution build: **0 errors, 0 warnings**.
- The live-provider test remains explicitly skipped in the deterministic suite and must be run separately with the environment variables above.

## Remaining manual steps

1. Open the app with a disposable Zero Hour project.
2. Ask Leader to delegate Chapter 1 with the stated constraints.
3. Start the supported Worker/provider path.
4. Confirm `chapter-01.md` exists.
5. Confirm the Worker card shows the Handoff result and provenance.
6. Retain “time permit” as Proposal and search it alongside the Accepted timeline rule.
7. Rotate/reopen Leader and confirm the Recovery display describes completed and remaining work.

## Remaining blockers

- The current live acceptance fixture is verification-only and does not create a chapter artifact.
- No fresh UI walkthrough evidence was captured in this pass.
- The Handoff card currently displays the Legacy Worker handoff result, validation summary, and provenance; artifact and changed-path detail still depend on the completion source exposing those fields.

## Commit readiness

**Not ready for the Demo-ready commit yet.** The presentation wiring is small and tested, but the milestone should remain uncommitted until the disposable Zero Hour UI walkthrough produces `chapter-01.md` and the operator confirms the Handoff, Library search, and recovery screens in sequence.
