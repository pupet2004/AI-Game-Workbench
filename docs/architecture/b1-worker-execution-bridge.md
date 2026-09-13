# B1 / Worker Execution Bridge

Status: normative Phase B contract
Recorded: 2026-09-04

## Purpose

Workbench keeps responsibility continuity and concrete agent execution as separate semantic domains, connected by durable typed attribution. The bridge is provenance, not authority.

## Domain Identities

- `LogicalActor`: durable project role identity.
- `Responsibility`: obligation, expected outcome, and delegable authority boundary.
- `Assignment`: authority-created Responsibility-to-LogicalActor binding.
- `AssignmentRevision`: versioned Assignment work contract.
- `Attempt`: one participation attempt for an Assignment/effective Revision.
- `SessionBinding`: B1 continuity/provenance binding from an Attempt to an external provider session.
- `WorkerTask`: concrete execution work item.
- `TaskRevision`: versioned Worker execution contract.
- `WorkerExecution`: one concrete runtime execution of a TaskRevision.
- `AgentSession`: runtime/host session identity.
- `WorkerCompletionVerification`: deterministic execution evidence.

## Typed Relations

### B1WorkerTaskLink

`Assignment + AssignmentRevision -> WorkerTask + TaskRevision`.

Cardinality: one Assignment may link to `0..N` Worker Tasks; each canonical linked Worker Task belongs to `0..1` Assignment. A Task with no link is legacy/compatibility history.

### B1WorkerExecutionLink

`Attempt -> WorkerExecution` with relation kind `Initial`, `Retry`, `ProviderReplacement`, or `Continuation`.

Cardinality: one Attempt may link to `0..N` WorkerExecutions. One WorkerExecution may link to at most one Attempt. Retry/provider replacement stays under the same Attempt when continuity is preserved; a new Attempt requires an explicit B1 decision/routing action.

### B1WorkerSessionLink

`SessionBinding -> AgentSession` with provider/account/model and opaque external session provenance.

Cardinality: one WorkerExecution has at most one current session link in this implementation; a new provider execution uses a new WorkerExecution link. SessionBinding and AgentSession remain distinct identities.

### B1WorkerExecutionEvidence

One canonical verification evidence row per linked WorkerExecution. It stores the B1 EvidenceRef plus Worker Task/Revision, Attempt, result, serialized deterministic checks, and timestamp. It is non-authoritative.

## Lifecycle and Recovery

Canonical attribution is created before runtime execution starts. Worker execution may then create a session link when runtime and external session identity are available. Repository reload restores links by ProjectRef. Existing Worker rows remain valid without links.

`Task=Working` with latest `WorkerExecution=Interrupted` is valid: logical work remains open while a concrete execution stopped.

## Verification and Authority

```text
WorkerExecution
  -> WorkerCompletionVerification
  -> B1WorkerExecutionEvidence
  -> Claim / Handoff (non-authoritative)
  -> GuidedDecision
  -> AuthorityDecision
  -> AcceptedProjectState
```

`Passed`, `Failed`, and `NotVerifiable` are preserved exactly. Free-text acceptance remains `NotVerifiable`. Evidence/Handoff creation never creates an AuthorityDecision or mutates AcceptedProjectState.

## Historical Data Policy

**NO RETROACTIVE GUESSING.** Historical Worker Task/WorkerExecution rows are `LEGACY UNLINKED` unless an explicit user/system action supplies the relationship. No time, text, session, provider, or user-name similarity may be used for automatic backfill.

## Invariants

1. Attempt != WorkerExecution.
2. SessionBinding != AgentSession.
3. Assignment != WorkerTask.
4. Cross-project links are rejected by foreign keys and application validation.
5. A WorkerExecution cannot belong to multiple Attempts.
6. Verification is evidence, not authority.
7. Legacy unlinked history remains loadable and reviewable.

## Product Path

The bridge is production-callable through `B1WorkerExecutionBridgeService` and is used by `WorkerSessionRouter` when a start request carries B1 Assignment/Revision/Attempt refs and a typed WorkerExecution identity. The normal Leader draft confirmation path now resolves a unique current B1 delegation, creates/reuses its Attempt through the routing application service, and supplies the typed refs before Worker runtime execution when a typed workspace-backed execution identity is available. Git supplies commit and branch provenance when present; non-Git projects use explicit `unversioned` / `worktree` provenance and filesystem snapshots. Ambiguous or non-governed starts remain explicitly `LEGACY / UNLINKED` for compatibility.

## Non-goals

No provider parity, active execution recovery, Steer recovery, semantic LLM judging, token benchmark, Library temporal redesign, or broad Legacy deletion is implied by this contract.
