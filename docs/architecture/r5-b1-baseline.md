# R5-B1 Baseline

Status: **Sealed architecture baseline**

## Purpose

AI Game Workbench preserves continuity, attributable decisions, and recoverable accepted state for long-running AI Projects across disposable Agents, Sessions, Providers, and runtimes.

R5-B1 provides durable Project governance. It does not provide an execution engine.

## References and Precedence

- Normative source: [R5-B1 Manual Continuity Spine — Design Specification](../superpowers/specs/2026-08-22-r5-b1-manual-continuity-spine-design.md)
- Verification record: [R5-B1 Final Seal](../superpowers/reports/2026-08-23-r5-b1-final-seal.md)
- Explanatory record: [R5-B1 Architecture Retrospective](../superpowers/reports/2026-08-23-r5-b1-architecture-retrospective.md)

This baseline is a concise restatement of the sealed design. It does not expand the domain model. If wording conflicts, the sealed Design Specification or a later explicit architecture decision governs. The Final Seal proves implementation and verification status; the Retrospective explains design history but is not normative.

## Durable Spine

```text
Project governance root
  -> LogicalActor
  -> Responsibility
  -> Assignment
  -> immutable Revision
  -> Attempt
  -> Claim + bounded Handoff
  -> AuthorityDecision
  -> Accepted Project State
  -> deterministic recovery projection
```

## Core Invariants

1. Accepted Project State originates only from persisted, attributable `AuthorityDecision` effects.
2. External Agent output and non-authoritative human submissions are Claims. Authority-authored effects exist only inside a valid AuthorityDecision.
3. Claim, Handoff, EvidenceRef, routing selection, transcript, and Summary are non-authoritative.
4. LogicalActor, Responsibility, Assignment, Revision, Attempt, Claim, Handoff, Decision, and accepted contribution history are durable and attributable.
5. Assignment delegation is immutable. Responsibility transfer creates a new Assignment.
6. Revision contracts are immutable. Material contract change creates a new Revision, and every Attempt remains bound to one Revision.
7. SessionBinding is optional connectivity/provenance. Rebinding changes connectivity only.
8. Stored routing history and effective continuation are distinct. Routing never creates authority or execution status.
9. Authority is closed, capability-based, locality-aware, maximum/subset constrained, and non-amplifying.
10. Every effect is validated before persistence; bounded decisions commit atomically under Project sequence concurrency.
11. Current accepted state and effective routing are mechanically rebuildable after restart without Session, transcript, Summary, Provider, or runtime.
12. Legacy records retain their original semantics and never automatically become B1 authority history.

## Identity and Attribution

- `UserPrincipalRef` and `LogicalActorRef` are the only R5-B1 Claimant kinds.
- Session, Provider, model, runtime, tool, and Workbench itself are never Claimants.
- RoleKind does not grant authority.
- A Handoff's primary Assignment ResultClaim belongs to the Assignment's immutable assignee LogicalActor.
- Evidence records provenance only; it does not verify truth or grant authority.

## Authority and State

- Project governance starts from one pre-existing bootstrap UserPrincipal at Project creation or one explicit eligible pre-B1 adoption.
- No entity or effect created by a Decision may authorize its own creation.
- Authority changes state only through the sealed named Application command shapes.
- The repository persists fully validated Decisions and is not an alternative domain validator or direct Accepted Project State writer.
- Accepted contributions have exactly one closed scope: `Project`, `Responsibility`, or `Assignment`.
- Contributions coexist by default. Only explicit, same-scope supersession changes current projection; history remains append-only.

## Recovery Guarantee

The following must remain valid:

```text
shutdown
  -> restart
  -> reload durable history
  -> rebuild projection
  -> same Accepted Project State
```

Recovery must not require:

- a live Agent or Session;
- transcript replay;
- Summary as authority;
- Provider/model availability;
- runtime execution;
- Git/worktree state;
- review execution.

## Legacy Boundary

- Legacy state remains independently readable and inspectable.
- New Projects receive bootstrap authority at creation.
- An eligible pre-B1 Project may be explicitly adopted exactly once while it has no B1 governance history.
- Adoption establishes only the B1 root of trust.
- Legacy context may cross into B1 only through explicit, attributable, current-time Claim recording or named authority commands.
- No migration, mapper, startup path, Summary, review, AutoProceed result, epoch, task state, Memory record, Library record, or completion payload may synthesize B1 authority or accepted state.
- B1 writes do not silently rewrite Legacy records.

## Explicit Non-Goals

R5-B1 does not own:

- Agent intelligence, training, marketplace, or execution strategy;
- execution engine, sandbox, tests, worktree/Git, merge workflow, or runtime permission policy;
- generic ACL, enterprise IAM, wildcard authority, or role templates;
- automatic Claim verification or general Truth resolution;
- transcript or Summary authority;
- automatic Legacy conversion, retirement, or deletion;
- Agent Gateway, provider adapter, external runtime integration, or UI work without separate approval.

## Admission Gate for Later Work

A later change must be rejected or redesigned if it:

- makes durable identity, accepted state, or recovery depend on Session/runtime infrastructure;
- bypasses a persisted attributable AuthorityDecision;
- loses Claimant, deciding authority, Responsibility, Assignment, or Revision attribution;
- infers B1 authority from Legacy state;
- adds a durable fact without explicit ownership, authority, persistence, concurrency, and recovery semantics;
- turns an execution capability gap into Kernel ownership without proving a durable Project requirement;
- adds a scope kind, capability, Claimant kind, command shape, automatic bridge, Accepted Project State writer, or durable identity without a new architecture decision.

## Change Control

This baseline may be changed only by an explicit architecture decision followed by its own written specification, implementation plan, verification, and seal. Feature implementation alone cannot weaken these invariants.
