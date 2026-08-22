# R5 Boundary Reconciliation — Architecture Boundary Decision

Status: **Architecture approved; written specification pending review**
Date: 2026-08-22
Product: AI Game Workbench

## 1. Problem Statement

AI Game Workbench exists because external Agent sessions are isolated, replaceable, and disposable while a Project must remain continuous.

The missing product capability is not another execution engine. Codex, Claude, ACP-compatible agents, Codeg, human operators, and future Agents may already own worktrees, Git, tests, review execution, subagents, sandboxing, runtime permissions, background work, and execution strategy. Workbench must not reimplement those capabilities merely because one integration exposes them incompletely.

The product problem is to preserve responsibility when a physical session dies, route bounded handoffs between independently hosted Agent sessions, and ensure that only attributable authority decisions change the Project's accepted state.

The governing product definition is:

> **Workbench turns disposable Agent sessions into a continuous project by preserving responsibility, routing bounded handoffs, and governing accepted project state.**

Conversation is therefore not a permanent domain object. Session history may provide context or provenance, but Project continuity must survive without replaying it.

## 2. Product Boundary

Workbench owns:

- durable Project identity;
- logical actors and their role kinds;
- durable responsibilities and concrete assignments;
- assignment revisions or attempts;
- bindings between logical actors, assignments, and external sessions;
- bounded, attributable handoffs and claims;
- references to external evidence or scenes;
- authority decisions and the accepted Project state derived from them;
- compact, non-authoritative recovery and navigation views;
- application flows for dispatch, routing, rebinding, recovery, and escalation;
- replaceable gateways to external Agent sessions.

Workbench does not own:

- how an Agent plans or performs work;
- worktree, branch, commit, merge, or other Git execution;
- shell, file editing, builds, tests, or review execution;
- subagent orchestration, background queues, or execution scheduling inside an Agent;
- sandbox implementation, OS security, or provider runtime permissions;
- automatic verification of an external Agent's claims;
- native Agent transcript storage or lifecycle semantics beyond the connection information needed for routing and traceability.

The governing rule is:

> **Agent capabilities belong to the Agent. Workbench preserves responsibility, routes sessions and handoffs, and governs accepted Project state.**

## 3. Domain Vocabulary

### 3.1 Project

The durable boundary within which responsibilities, assignments, authority, and accepted state have meaning.

### 3.2 LogicalActor and RoleKind

A `LogicalActor` is a durable identity that may receive Assignments, exercise explicitly granted authority, and be rebound to different external sessions over time.

`Leader`, `Worker`, and `Reviewer` are `RoleKind` values, not three permanent singleton conversations. A Project normally has one current Leader Responsibility fulfilled through an active Assignment. An Assignment may have its own Worker actor, and a Reviewer actor may be created temporarily for a review responsibility.

A LogicalActor is not a Codex thread, Claude session, transcript, model, or provider account.

### 3.3 Responsibility

A durable Project obligation consisting of an expected outcome and the authority boundary governing its fulfillment and acceptance. Responsibility does not store a current owner and outlives any particular Assignment or SessionBinding.

### 3.4 Assignment

A concrete delegation of all or part of a Responsibility's fulfillment to a LogicalActor. It defines the assignee, bounded goal, constraints, references, and expected handoff, but not the Agent's execution strategy.

The actor or actors currently responsible for fulfillment are derived from active Assignments. Even a long-lived Leader Responsibility is connected to its current LogicalActor through an explicit Assignment; there is no second Responsibility owner field with competing precedence.

### 3.5 Revision and Attempt

A `Revision` records an authorized material change to an Assignment contract. An `Attempt` records another effort to fulfill the same effective contract. Rework should create a Revision or Attempt as appropriate rather than silently redefining Responsibility.

### 3.6 SessionBinding

A replaceable association between a LogicalActor or active Assignment and an opaque external session reference. It describes connectivity and traceability, not responsibility or truth.

Session death, provider replacement, or session rollover creates, replaces, or closes a binding. It does not create a new LogicalActor or silently change Assignment identity, Responsibility, Authority, or Accepted Project State.

### 3.7 Claim

An attributable statement produced by an Agent, human, tool, or external system. Completion reports, review outcomes, test reports, and statements such as "tests passed" are Claims until an authorized decision gives them Project meaning.

### 3.8 Handoff

A bounded transfer object that carries the result and continuation information needed by the receiving responsibility. A normal Handoff contains the semantic equivalent of:

```text
AssignmentRef
ResultClaim
ChangedRefs[]
ValidationClaims[]
UnresolvedIssues[]
RecommendedNextStep
SourceSessionRef
EvidenceRefs[]
```

A Handoff does not copy a complete transcript, tool log, diff, test log, or shell history. A consumed Handoff is not automatically long-term Project memory. When an Authority Decision cites it, its bounded content or a stable provenance reference remains traceable.

### 3.9 EvidenceRef

A compact locator or provenance reference to an external scene or artifact. It is not an evidence warehouse, and its presence does not make the referenced Claim true.

### 3.10 AuthorityDecision

An attributable decision by a user or a LogicalActor holding the required authority. `Accept`, `Reject`, and `RequestRevision` are authority outcomes. An application-level `AskUser` or escalation leaves the relevant state unresolved until an authorized decision occurs.

### 3.11 AcceptedProjectState

The current authoritative Project view derived from attributable Authority Decisions. It is not a mutable transcript, Agent report, or Summary document.

### 3.12 Summary and Transcript

`Summary` is a non-authoritative, compact continuity view produced or retained for recovery and navigation. It may be derived from or reference Claims, Handoffs, Authority Decisions, or other bounded Project context, but it is not the authoritative projection and is not required to be mechanically recomputable from Accepted Project State.

The existing R5-A path remains valid: normal Leader cognition may emit a sparse append-only `SummaryDelta` that Workbench persists mechanically. This decision neither converts Summary into an Accepted Project State projection nor requires existing Summary entries to be deleted and recomputed.

`Transcript` is provider-owned contextual scene. It may be useful for audit or semantic reconstruction, but it must never be authoritative Project state.

### 3.13 TransientInteraction

A provider/runtime interaction required to continue a live session, such as a permission request or clarification request. Routing or answering it does not change Accepted Project State. A requested change to Assignment scope, Responsibility, or Authority is not merely transient and must enter the appropriate Revision or Authority flow.

### 3.14 Relationships

```text
Project
├─ LogicalActors[]
│  └─ RoleKind
├─ Responsibilities[]
│  └─ Assignments[]
│     ├─ Revisions / Attempts[]
│     ├─ SessionBindings[]
│     └─ Handoffs[]
├─ Claims / EvidenceRefs[]
├─ AuthorityDecisions[]
├─ AcceptedProjectState
└─ Summary
```

## 4. Authority and State Model

The authoritative state flow is:

```text
Responsibility
      ↓ concrete delegation
Assignment
      ↓ temporary connectivity
SessionBinding
      ↓ external work
Claim + bounded Handoff
      ↓ authorized judgment
AuthorityDecision
      ↓ attributable projection
AcceptedProjectState
```

External output never changes Accepted Project State directly:

```text
AcceptedProjectState
    ← AuthorityDecision
        ← Claim / Handoff / EvidenceRef
```

A Worker completion Claim and a Reviewer approval Claim have the same non-authoritative status until evaluated by the authority responsible for the relevant Project decision. A Reviewer owns review responsibility; it is not a truth machine.

Authority Decisions must preserve attribution and references sufficient to explain which Claim, Handoff, evidence, or prior decision they considered. Later decisions may supersede earlier accepted state, but they must not rewrite the historical decision chain as if the earlier decision never existed.

The information layers have these fixed meanings:

```text
Transcript          contextual scene; non-authoritative
Claim / Handoff     attributable statement or transfer; non-authoritative
Summary             produced or retained continuity aid; non-authoritative
AuthorityDecision   attributable governance action
AcceptedProjectState
                    authoritative projection of decisions
```

Summary may point to the authoritative chain, but Summary text alone cannot accept a Claim, resolve uncertainty, revise an Assignment, or change Accepted Project State.

## 5. Session Gateway Boundary

`IAgentSessionGateway` is the conceptual connection boundary between Workbench and an external Agent/session host. It is not an execution framework.

Its minimum semantic operations are:

```text
CreateSession
ResumeSession
Send
Observe
RespondInteraction
Cancel
```

The gateway may expose opaque session references, message or lifecycle events, transient interactions, final responses, and failures. Provider-specific capabilities remain inside the Adapter and must not leak into the core domain as universal execution requirements.

Expected adapters may include:

```text
CodexSessionGateway
ClaudeSessionGateway
AcpSessionGateway
ManualSessionGateway
CodegSessionGateway
```

Gateway semantics are limited to answering:

> How does Workbench communicate with, observe, and resume this external session?

They do not answer:

> How should the Agent perform the Assignment?

Session creation followed by persistence or routing failure may require best-effort cancellation or explicit uncertain state, but gateway failure must not fabricate a Project result or change accepted state. Resume failure may lead to a new binding; it must not silently reuse a different external identity as though it were the original session.

The hard rebinding rule is:

> **Rebinding a session changes connectivity only; it must never silently change responsibility, authority, assignment identity, or accepted state.**

## 6. Eight Architecture Invariants

1. **Physical session death must not destroy logical responsibility.**

2. **Changing Agent or provider must not require migration of the Workbench core domain model.**

3. **Workbench does not need to know how an Agent performs work.**

4. **Project recovery must not require replaying complete transcripts.**

5. **Every handoff must be bounded, attributable, and traceable.**

6. **Agent capabilities belong to the Agent. Workbench routes sessions and preserves continuity.**

7. **External Agent output is a Claim. Accepted Project State is an attributable projection of Authority Decisions.**

8. **Session history may provide context, but it must never be authoritative Project state.**

These invariants constrain all later repository, ownership, migration, and implementation decisions.

## 7. Core / Application / Adapter / Delegated Classification

Every audited module or responsibility must first be classified by the semantics it owns.

### CORE

Durable Project semantics and rules: LogicalActor, RoleKind, Responsibility, Assignment identity and effective revision, Claim attribution, Authority Decision, Accepted Project State, and the invariants connecting them.

### APPLICATION

Use-case coordination without performing Agent work: assignment dispatch, session rebinding, bounded handoff routing, interaction forwarding, recovery, escalation, and projection orchestration.

### ADAPTER

Replaceable integration with an external system: Codex, Claude, ACP, Manual, Codeg, persistence, or UI-specific translation. An Adapter is outside the Kernel but may still be necessary product infrastructure.

### DELEGATED

Capabilities owned by the selected Agent or external runtime: worktrees, Git execution, tests, builds, sandboxing, runtime permissions, review execution, subagents, background queues, merge execution, and execution strategy.

Runtime permission policy, enforcement, and semantics are `DELEGATED`. Forwarding a provider permission request through a Session Gateway is `ADAPTER`, while coordinating its presentation and response is `APPLICATION`; neither means Workbench owns the permission policy.

The audit decision sequence is:

1. Does it express durable Project meaning or enforce a domain invariant? Classify it as `CORE`.
2. Does it coordinate responsibilities, sessions, handoffs, recovery, or authority without doing the external work? Classify it as `APPLICATION`.
3. Does it translate between Workbench and a replaceable external system? Classify it as `ADAPTER`.
4. Does it perform, reproduce, or judge work already owned by an Agent/runtime? Classify that responsibility as `DELEGATED`.

Classification precedes retention decisions. `Not CORE` does not mean `delete`; for example, an ACP gateway is a legitimate Adapter. A mixed module may require later separation rather than wholesale retention or deletion.

## 8. Manual Gateway Acceptance Scenario

Manual operation is a mandatory architecture acceptance scenario, not a fallback afterthought.

```text
Responsibility
      ↓
Assignment created in Workbench
      ↓
Human performs work outside Workbench without an Agent API
      ↓
Human returns a bounded Handoff, Claims, and references
      ↓
Authorized Leader or user decides
      ↓
AcceptedProjectState updates
      ↓
Next Responsibility remains routable
```

The architecture passes this scenario only if:

- no Codex, Claude, ACP, or Codeg capability is required to preserve Assignment identity;
- the returned Claims remain non-authoritative until an Authority Decision;
- the authority chain and accepted state remain complete and attributable;
- project recovery does not require an external transcript;
- a later Agent session can take over the next responsibility from bounded Project context.

Failure of this scenario indicates that Agent execution capability has leaked into the Workbench Kernel.

## 9. Explicit Non-goals

This decision does not design or authorize:

- an execution engine or provider-neutral execution abstraction;
- Workbench-owned worktree, Git, build, test, merge, or review execution;
- a sandbox, OS security boundary, or runtime permission system;
- automatic claim verification or autonomous acceptance of Agent output;
- transcript replication, full-history memory, or replay-based recovery;
- a permanent singleton Worker or Reviewer conversation;
- provider capability normalization beyond session communication;
- an Ownership Matrix;
- a schema migration, API/class design, UI redesign, implementation plan, or production-code change;
- immediate deletion, freezing, or refactoring of any existing repository module.

Codeg may be a useful aggregation Adapter, but it is optional and cannot become a prerequisite of the core domain or the Manual Gateway acceptance scenario.

## 10. Consequences for Repository Audit

This Architecture Boundary Decision is the governing interpretation for R5 Boundary Reconciliation. Where earlier design language treats a long-lived Leader Session or Conversation as the permanent identity, it must now be read as a durable LogicalActor and Responsibility with replaceable SessionBindings. Historical implementation facts and already-approved minimal slices remain historical; this decision changes how later architecture work classifies them.

Earlier shorthand such as `Summary 记原因` remains valid only as a recovery and navigation aid. The authoritative explanation for accepted state is the attributable chain from Accepted Project State to Authority Decision and then to cited Claim, Handoff, or EvidenceRef. Summary never becomes an authority source.

The later repository audit must:

1. inventory existing modules without changing them;
2. identify the responsibility each module currently owns;
3. classify each responsibility as `CORE`, `APPLICATION`, `ADAPTER`, or `DELEGATED`;
4. flag mixed modules whose responsibilities cross those boundaries;
5. evaluate retention, separation, freezing, delegation, or removal only after classification;
6. treat incomplete Agent execution features as possible boundary violations, not automatic Workbench feature gaps;
7. preserve necessary connection Adapters even though they are not part of the Kernel;
8. verify proposed future architecture against the Manual Gateway acceptance scenario and all eight invariants.

This document does not itself decide the ownership or fate of individual repository modules. It supplies the constitution under which that separate audit will be performed.
