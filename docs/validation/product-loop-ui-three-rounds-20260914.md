# Product Loop UI Three-Round Validation

Date: 2026-09-14

## Scope

This validation record is superseded by the live-provider record
`product-loop-ui-real-codex-20260914.md`, which runs the same three rounds with
the real Codex Desktop Worker. The live run used one Godot project and one
isolated Workbench database:

```text
Round 1: Score button now adds 2 per click.
Round 2: The counter has a Reset button.
Round 3: The counter records the highest score.
```

Each round used the real desktop sequence:

```text
Seed Pending Handoff
  -> Project Overview
  -> Review pending
  -> Review Handoff
  -> Preview what will change
  -> Confirm Decision
  -> force-stop Workbench
  -> restart Workbench
  -> recover AcceptedProjectState
```

Rounds 1 and 2 also fill the real successor Assignment field through UIA:

```text
Round 1 -> Add a Reset button to the counter.
Round 2 -> Add high score tracking to the counter.
```

## Result

```text
Round 1: real Codex Worker + UI Accept + restart passed.
Round 2: real Codex Worker + UI Accept + restart passed.
Round 3: real Codex Worker + UI Accept + restart passed.
```

The same ProjectId was used for all three rounds. The final restart showed
three accepted statements and no pending Handoffs.

## Boundary

Certified:

- UI Review and Authority Preview were exercised three times.
- UI Confirm committed each decision.
- Accepted statement count progressed `1 -> 2 -> 3`.
- Accepted decisions survived three process-level restarts.
- Accepted decisions in rounds 1 and 2 created the next Assignment through the
  real UI successor field.

Not certified by this record:

- A live Leader session wrote the next request after each restart; the
  certification seed prepared the next round's task draft.

The seed writes only governance and Worker task preparation data. It never
writes `AcceptedProjectState`; each accepted contribution was committed by the
desktop UI Authority path.
