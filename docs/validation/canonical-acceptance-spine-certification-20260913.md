# Canonical Acceptance Spine Certification

Date: 2026-09-13

Status: Passed for the deterministic provider-independent certification path and
the single-round live Codex Worker path.

## Certified Path

The certified path is:

```text
Worker Execution
  -> Completion
  -> Evidence / Verification
  -> Claim / Handoff
  -> Authority Decision
  -> AcceptedProjectState
  -> Summary / Continuity
  -> Restart
  -> New Leader Session
```

The `WorkerSessionRouter` adapts Legacy Worker starts into the canonical path
when a governed project, typed execution identity, and current assignment can
be resolved. Completion never writes `AcceptedProjectState`; only the
Authority command does.

## Tiny Counter

The certification runs three accepted changes:

```text
+1 -> +2 -> +3
```

Each round proves:

1. The Worker changes the real project file.
2. Typed execution, filesystem baseline, FinalReport, verification evidence,
   Completion, Claims, and Handoff are persisted.
3. `AcceptedProjectState` and Summary remain unchanged before the decision.
4. Accept updates the accepted contribution and Summary.
5. Reject leaves the proposed contribution out of accepted state.
6. Revise activates a successor revision without accepting the proposal.
7. Completion, Evidence, Claims, Handoff, and WorkerExecution remain durable
   after Reject and Revise.
8. The process is restarted and a new Leader epoch is created.
9. The new Leader prompt contains the prior accepted statement and
   `CURRENT AUTHORITY STATE (USER-ACCEPTED)`.

No database edits, CLI rescue path, or manual state repair are used.

## Live Codex Gate

The live gate
`LiveCodexAcceptanceSpineTests.Real_codex_worker_change_requires_acceptance_and_survives_restart`
passed with `WORKBENCH_RUN_CODEX_WORKER_LIVE=1`.

It uses a real temporary Git workspace and confirms that Codex:

1. Changes the real `src/counter.js` file from `+1` to `+2`.
2. Returns a structured FinalReport with a Proposed State Contribution.
3. Persists typed execution, verification evidence, Completion, Claims, and Handoff.
4. Leaves AcceptedProjectState unchanged until the explicit User Accept decision.
5. Updates AcceptedProjectState and Summary only after that decision.
6. Releases and reopens the database, after which Leader boot context reads the
   accepted `+2` statement.

This is a single-round live-provider gate. The deterministic certification
remains the three-round `+1 -> +2 -> +3` regression.

## Crash Recovery Gate

`WorkerSessionRouter.ReconcileInterruptedExecutionsAsync` runs when the
Workspace loads a project. It reconciles stale in-progress executions left by
a process crash by:

1. Capturing the workspace delta against the persisted execution baseline.
2. Moving the execution to `Interrupted`.
3. Moving a still-`Working` task to `NeedsLeaderDecision`.
4. Recording a durable `WorkerExecutionCrashReconciled` event.
5. Leaving Completion, Claims, Handoffs, and AcceptedProjectState untouched.

The reconciliation is idempotent because terminal `Interrupted` executions are
not selected again.

## Race Coverage

A regression test covers a Legacy task transition reaching `Reviewing` before
the router processes the FinalReport. The linked canonical Completion still
bridges to Claim and Handoff, while the Legacy task remains `Reviewing`.

## Verification

Relevant commits:

- `177b990` Leader continuity after restart
- `0391f26` rejection/revision retention and Leader rollover
- `c6d2810` Legacy transition race routed through the canonical spine

Solution test result:

```text
1133 passed, 4 skipped, 0 failed
```

The skipped tests are existing live-provider checks. This certification does
not claim that every real external provider has completed the same path.

## Remaining Gates

- Three consecutive real-provider rounds without state drift.
- Automatic B1 successor task creation and dispatch.
- Product-shell and UI workflow completion.
