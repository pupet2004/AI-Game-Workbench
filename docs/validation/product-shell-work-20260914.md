# Product Shell Work Validation

Date: 2026-09-14

## Scope

Work answers four user questions:

1. What is the Agent doing?
2. Which Agent is doing it?
3. What step is it on?
4. What should I do when it stops or finishes?

The primary Worker card now presents:

- task title;
- provider and model summary;
- product status: Working, Waiting for your decision, Needs attention,
  Ready to start, Completed, or Stopped;
- current plan step and progress;
- Stop, Continue, Open work session, and Review result actions where relevant.

Execution state, runtime identifiers, provenance, and Authority wording are
secondary details. They remain available without defining the first-read
experience.

## Authority Guarantees

- A completed Worker result is shown as waiting for a decision only when a
  reviewable canonical handoff exists.
- Accepted or non-reviewable legacy results are not presented as pending
  decisions.
- Failed or interrupted work explicitly says that accepted project state has
  not changed.
- Stop and Continue use the existing Worker host/router paths.
- Review continues to converge on the existing Review Queue and
  Preview -> Confirm decision path.

## Automated Tests

Work-focused tests:

```text
30 passed, 0 failed
```

Full App test suite:

```text
669 passed, 5 skipped, 0 failed
```

The final full App run was rebuilt without shared compilation and passed
serially after closing the local inspection process.

## Visual Verification

The real desktop Work pane was checked against the certified counter project.
The current card surface shows DeepSeek, the task title, Waiting for your
decision, the user-facing explanation, progress, and Review result. Technical
details are collapsed.

## Certification Boundary

This phase certifies the Work surface language and state projection. It does
not add new Worker execution semantics, automatic retry policy, or a new
provider-specific path.
