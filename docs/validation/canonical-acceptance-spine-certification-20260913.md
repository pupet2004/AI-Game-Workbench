# Canonical Acceptance Spine Certification

Date: 2026-09-13

Status: Passed for the deterministic provider-independent certification path.

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

- Real-provider code-changing Worker demonstration.
- Crash-time workspace/execution reconciliation.
- Automatic B1 successor task creation and dispatch.
- Product-shell and UI workflow completion.
