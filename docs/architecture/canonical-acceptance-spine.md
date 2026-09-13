# Canonical Acceptance Spine

Status: Phase 1 certified
Recorded: 2026-09-13

Certification record: `docs/validation/canonical-acceptance-spine-certification-20260913.md`

The Workbench has one path by which an Agent-produced change can become
accepted project truth:

```text
Task / Leader Request
        |
        v
Worker Execution
        |
        v
Completion
        |
        +--> Diff / Result / Evidence / Verification
        |
        v
Claim / Proposed Change
        |
        v
Authority Decision
     /       \
  Accept   Reject / Revise
    |
    v
AcceptedProjectState
    |
    v
Summary / Continuity / Next Session
```

## Ownership Rules

### Completion

Completion is the durable fact that a Worker execution reached a terminal
result and produced a result package. It may contain a final report,
validation summary, evidence references, execution identity, and provenance.

Completion does **not** write `AcceptedProjectState`.

### Evidence

Evidence records provenance and verification observations. It can support a
Claim or Authority Decision, but it is never accepted truth by itself.

### Claim and Handoff

A Claim describes a result or proposed change. A Handoff groups Claims and
evidence for governance. Neither transfers authority and neither changes the
accepted project state.

### Authority Decision

An Authority Decision is the only command that can establish, replace, or
remove accepted project contributions and accepted assignment state. The
decision is append-only and is the source for the accepted projection.

### AcceptedProjectState

`AcceptedProjectState` is a projection of Authority history. It is read-only
to execution, completion, evidence, Claim, Handoff, Summary, and Library
services.

## Legacy Compatibility Rule

Legacy Task, Review, and Worker UI paths may remain during migration, but their
completion output must enter this spine through an adapter. Legacy completion
or review rows must not write accepted state directly.

The adapter boundary is:

```text
Legacy Worker / Review
        |
        v
Canonical Completion Package
        |
        v
Canonical Claim / Handoff
        |
        v
Existing Authority Command
```

## Phase 1 Gate

The first certification must prove a real project can complete three accepted
iterations, such as:

```text
+1 -> +2 -> +3
```

For every iteration:

1. A Worker changes the project files.
2. Completion, diff, verification, and evidence are persisted.
3. A pending Claim is visible.
4. The project remains unchanged in `AcceptedProjectState` before acceptance.
5. A user accepts, rejects, or requests revision through Authority.
6. The accepted projection and Summary update only after that decision.
7. Workbench exits and restarts.
8. A new Leader session reads the accepted result and continues from it.

No database edits, CLI rescue path, or manual state repair are allowed in this
certification.
