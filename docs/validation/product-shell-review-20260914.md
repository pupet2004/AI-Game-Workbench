# Product Shell Review Validation

Date: 2026-09-14

## Scope

Review is the product surface where a completed Worker change becomes a
decision for the project owner:

```text
Review Queue
  -> completed change
  -> verification summary
  -> evidence count
  -> Accept / Request revision / Reject
  -> Preview
  -> Confirm Decision
```

The UI does not expose Claim, Handoff, AuthorityDecision, or
AcceptedProjectState as the primary user vocabulary. Submission and deciding
identity are available behind a collapsed provenance section. Advanced
contribution, revision, and successor-assignment controls remain available
behind an advanced section.

## Authority Guarantees

- Product actions only select a decision and open the existing Preview path.
- Preview is non-persistent.
- Any change to a decision field invalidates the previous preview.
- Confirm can only commit the exact request that was previewed.
- Accept is the only product action that establishes the proposed project
  contribution.
- Reject and Request revision retain the Worker submission and evidence while
  leaving AcceptedProjectState unchanged.
- Completion and Evidence do not write AcceptedProjectState.

## Automated Tests

Review-focused tests:

```text
34 passed, 0 failed
```

The full solution regression suite after Review changes:

```text
663 passed, 5 skipped, 0 failed
```

## Visual Verification

The real desktop verifier captures:

- Review Queue;
- Review change;
- decision preview.

The captured Review change surface shows the intended first-read order:
the completed change, verification result, evidence count, and the three
product decisions. Provenance and advanced controls are collapsed by default.

## Real Desktop Certification

Command path:

```text
Godot 4.6.3
OpenCode
deepseek/deepseek-v4-flash
```

Result:

```text
ProductLoopReleaseGate=Passed
Round 1: accepted count 0 before Accept; +2 recovered after restart.
Round 2: accepted count 1 before Accept; Reset recovered after restart.
Round 3: accepted count 2 before Accept; High Score recovered after restart.
```

The run used one disposable Godot project, one Workbench database, three
real Worker rounds, three explicit UI decisions, and three forced Workbench
process restarts. It also captured Review Queue and Review change screenshots
under:

```text
artifacts/local/product-loop-real-codex-20260914-232602-352.screenshots
```

## Certification Boundary

This phase certifies the Review product surface against the existing
Acceptance Spine. It does not add a Diff viewer or a full provenance browser.
Those remain secondary expansion work after the primary Review loop is stable.
