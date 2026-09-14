# Canonical Acceptance Spine Certification

Date: 2026-09-14

Status: Passed for the deterministic provider-independent certification path and
the three-round live Codex Worker path.

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
`LiveCodexAcceptanceSpineTests.Real_codex_worker_completes_three_accepted_counter_rounds`
passed with `WORKBENCH_RUN_CODEX_WORKER_LIVE=1` on September 14, 2026.

It uses one real temporary Git workspace and confirms across three rounds that
Codex:

1. Changes the real `src/counter.js` file from `+1` to `+2` to `+3`.
2. Returns a structured FinalReport with a Proposed State Contribution on each
   round.
3. Persists typed execution, verification evidence, Completion, Claims, and
   Handoff on each round.
4. Leaves AcceptedProjectState unchanged until the explicit User Accept
   decision.
5. Updates AcceptedProjectState and Summary only after each decision.
6. Closes and reopens the database between rounds, after which Leader boot
   context reads all prior accepted statements.
7. Creates the next B1 delegation only after the prior round is accepted.

This is the three-round live-provider gate. The deterministic certification
remains the faster provider-independent `+1 -> +2 -> +3` regression.

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

## Successor Assignment Gate

`GuidedDecisionRequest` accepts an optional, user-supplied successor
Assignment contract. When the disposition is `Accepted`, the
`GuidedDecisionService` creates the successor through a second
`B1AuthorityCommandService.DelegateAssignmentAsync` call, replacing the
accepted Assignment while preserving its Responsibility and assignee.

The successor path is deliberately gated:

- an empty contract creates no successor;
- Reject and Revise create no successor;
- Completion, Claims, Handoffs, and AcceptedProjectState are still governed by
  the original decision first;
- the successor is an Authority Decision, never a side effect of Completion or
  Summary projection.

An explicit successor plan now creates the Task and initial TaskRevision, then
dispatches the Worker through `CanonicalWorkerLaunchService` and
`WorkerSessionRouter`. The launch accepts an explicit AssignmentRef, so a
successor remains addressable when other Assignments are current. If the
runtime is unavailable after the Authority commit, the result is
`FailedAfterAuthorityCommit` and a durable `SuccessorDispatchFailed` task event
is recorded once the Task exists. Retrying the same request reuses the
persisted successor Authority Decision and Task instead of creating another
successor.

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
- Product-shell and UI workflow completion.
