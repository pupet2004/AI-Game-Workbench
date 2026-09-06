# Zero Hour Demo Evidence 2026-09-05

## Scope

This validation covers the demo presentation milestone only. It does not claim B1/Legacy lifecycle convergence, provider equivalence, semantic content verification, or active execution recovery.

## Current demo-safe path

1. Open a project and inspect the Project World entry surface.
2. Show Accepted Project State and active assignment information.
3. Use the existing supported Leader/Worker path for bounded work.
4. Present completion through the read-only `HandoffDisplayModel` surface. B1 Handoff and Legacy Worker Completion retain distinct source kinds.
5. Search Library using the visible search field. Results retain separate labels for Accepted Fact, Proposal, Summary, and Library projection.
6. Reopen Project World and show the `RecoveryViewModel` assembled from AcceptedProjectState, assignment projection, and handoff projection.

## Verification run

- App project build: passed with zero errors.
- App test project: 544 passed, 3 skipped.
- Skipped tests are live-provider tests and remain separate from deterministic evidence.
- `git diff --check`: no whitespace errors after cleanup.

## Remaining presentation gaps

- The demo still requires an operator to choose the supported Worker path.
- Artifact and changed-path details depend on the source path supplying those records.
- Recovery display currently presents assignment references and source categories; it does not invent chapter prose or semantic completion claims.
- A fresh live-provider run is still required before making an L4 live-validation claim.
