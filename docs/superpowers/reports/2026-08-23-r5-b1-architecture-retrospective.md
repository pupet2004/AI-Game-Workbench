# R5-B1 Architecture Retrospective

Status: **Post-implementation architectural record**

R5-B1 Manual Continuity Spine is implemented, merged, verified, and sealed. This retrospective explains why the architecture reached its final shape and records the lessons that should constrain later work. It is not a new specification, implementation plan, R6 design, feature backlog, or authorization to change production.

The normative sources remain:

- [R5-B1 Manual Continuity Spine — Design Specification](../specs/2026-08-22-r5-b1-manual-continuity-spine-design.md)
- [R5-B1 Final Seal](2026-08-23-r5-b1-final-seal.md)
- [R5-B1 Baseline](../../architecture/r5-b1-baseline.md)

## 1. Background: The Original Problem

Before R5-B1, Workbench had many useful continuity-adjacent mechanisms: Leader epochs, conversations, transcripts, Summary, tasks, revisions, review results, completion packages, Memory, Library, runtime routing, and recovery paths. The fundamental gap was not a lack of Agent capability. It was the absence of durable continuity primitives that remained meaningful when execution infrastructure disappeared.

The implicit continuity chain was too close to:

```text
Session
  -> Conversation
  -> Summary
  -> Next Agent
```

That chain could preserve context, but it could not reliably answer the Project-governance questions:

- Who is durably responsible?
- What bounded work was delegated, and under which contract revision?
- Who claimed that something happened?
- Who had authority to accept, reject, revise, delegate, or establish Project state?
- Why does the current accepted state exist?
- Can the same state be recovered without replaying a transcript or trusting a generated Summary?

Sessions end. Agents, Providers, models, and runtimes change. A Summary describes or compresses context but is not an attributable authority record. The core question became:

> If no Agent is continuously running, does a long-running AI Project still possess durable identity, responsibility, decisions, and current accepted state?

R5-B1 answers yes.

## 2. Boundary Corrections That Shaped R5-B1

R5-B1 became possible only after several ownership and identity corrections.

### 2.1 Execution capability was not the missing Kernel

Codex, Claude, and other Agents already provide worktrees, background activity, Git review, merge workflows, subagents, tests, permissions, and execution strategy in different forms. Workbench did not need to rebuild a provider-neutral execution engine merely because those capabilities were incomplete or prompted indirectly.

The durable Workbench responsibility became:

> Turn disposable Agent sessions into a continuous Project by preserving responsibility, routing bounded handoffs, and governing accepted Project state.

Execution remains delegated. Workbench keeps only the domain and application semantics necessary to connect execution results to durable Project governance.

### 2.2 Conversation and Session were demoted from durable identity

The permanent objects are Project-local responsibility-bearing identities and governance records, not conversations. `LogicalActor`, `Responsibility`, `Assignment`, `AuthorityDecision`, and Accepted Project State survive session loss. A `SessionBinding` is an optional, replaceable connectivity and provenance record.

Rebinding changes connectivity only. It cannot silently change responsibility, authority, Assignment identity, Revision, Claim, Handoff, or accepted state.

### 2.3 Responsibility, Assignment, and SessionBinding were separated

The earlier vocabulary risked placing ownership in multiple locations. R5-B1 resolved this:

- `Responsibility` is the durable obligation, expected outcome, and maximum delegable authority boundary.
- `Assignment` is an immutable delegation of bounded work to one LogicalActor.
- `Revision` is the immutable effective contract under which an Attempt operates.
- `SessionBinding` records how an Actor was externally connected during an Attempt.

Current fulfillment is derived from active authoritative delegation and Revision state, not from a mutable owner field or a live Session.

### 2.4 Summary was explicitly made non-authoritative

Summary remains useful for cognition, navigation, and recovery context. It may be append-only and need not be mechanically recomputable from Accepted Project State. But it cannot accept a Claim, create authority, or modify Project state.

The final authority chain is:

```text
optional Claim / Handoff / EvidenceRef
  -> considered context or provenance
     -> AuthorityDecision
        -> accepted-state effect
           -> Accepted Project State
```

Considered references are optional. An authorized principal may author a bounded accepted-state effect without a prior Claim; the effect still belongs to and is attributable to the AuthorityDecision.

Not:

```text
Accepted Project State
  <- Summary
```

### 2.5 Manual continuity became the architecture proof

The decisive acceptance scenario removed every execution convenience:

- no Agent API;
- no SessionBinding;
- no transcript;
- no Summary;
- no Provider or runtime;
- no Git/worktree dependency;
- no review execution.

If a human could establish responsibility, perform work externally, submit bounded Claims and a Handoff, make an attributable decision, restart tomorrow, and recover the same Project state, then continuity belonged to Workbench rather than to an Agent session.

That scenario became the R5-B1 vertical slice and prevented early Gateway work from pulling execution assumptions back into the Kernel.

## 3. Final Outcome: The Continuity Spine

R5-B1 established this durable chain:

```text
Project
  -> governance root
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

Each part answers a distinct question:

| Primitive | Question answered |
|---|---|
| Project bootstrap governance | Where does the non-circular root of trust come from? |
| LogicalActor | Which durable Project identity bears responsibility or makes Claims? |
| Responsibility | What long-lived obligation and maximum authority boundary exist? |
| Assignment | Which Actor received this bounded delegation? |
| Revision | Which immutable contract version governs the work? |
| Attempt | Which effort is being made under that Revision? |
| Claim | Who asserts what, with which provenance or evidence? |
| Handoff | What bounded work context and Claims were handed over? |
| AuthorityDecision | Who authoritatively caused which bounded Project effects? |
| Accepted Project State | What does the Project currently acknowledge? |
| Projection | How is current state recovered mechanically from durable history? |

The result is not a Memory subsystem. It is a Project-governance and continuity spine to which Manual work and future external execution adapters may submit the same bounded inputs.

## 4. Implemented Invariants

### 4.1 Authority invariant

> Accepted Project State can originate only from persisted, attributable `AuthorityDecision` effects.

Chat history, transcript, Summary, Agent output, runtime events, compatibility records, and direct repository writes cannot independently create accepted state.

The implemented write path is:

```text
Named Application command
  -> Authority evaluator
  -> fully validated bounded decision
  -> Project sequence CAS
  -> atomic repository transaction
  -> persisted AuthorityDecision
  -> pure projection
```

### 4.2 Attribution invariant

Every important assertion or state change retains distinct responsibility:

- a Claim identifies its Claimant;
- a Handoff references immutable Claims without accepting them;
- evidence preserves provenance but does not verify truth;
- an AuthorityDecision identifies its deciding authority and considered references;
- every accepted contribution belongs to the AuthorityDecision that created it.

Therefore:

```text
Claim != Authority
Handoff != Acceptance
Evidence != Decision
Routing selection != Authority
Summary != Accepted State
```

### 4.3 Identity invariant

UserPrincipal and LogicalActor are responsibility-bearing principals. Session, Provider, model, runtime, tool, and Workbench itself are not Claimants or authority identities.

Assignment delegation and Revision identity are immutable. A material contract change creates a new Revision; responsibility transfer creates a new Assignment. Historical Attempt, Claim, Handoff, and SessionBinding attribution is never rewritten.

### 4.4 Recovery invariant

The system supports:

```text
shutdown
  -> restart
  -> reload persisted authority history and routing state
  -> rebuild projection
  -> recover structurally equivalent Accepted Project State
```

Recovery does not require Session, transcript, Summary, Provider, runtime, or a mutable projection cache.

### 4.5 Authority-locality invariant

Authority uses a closed capability set. Responsibility contracts define a maximum delegable boundary; each effective Assignment Revision explicitly delegates a subset, defaulting to none. Capabilities do not amplify through newly created identities or effects.

RoleKind does not grant authority. Assignment-derived authority is responsibility-local unless an explicitly delegated Project-level capability permits the Project-level effect. Every effect is validated independently before any effect is persisted.

### 4.6 Routing invariant

Stored routing history and effective continuation are separate projections. Stored selections use expected-old CAS and are never inferred from timestamps. A Revision change or authoritative disposition may make a stored route ineffective without rewriting history or assigning execution status.

### 4.7 Legacy invariant

Legacy data remains readable under its original meaning. No compatibility mechanism may synthesize historical B1 identity, authority, Claims, Handoffs, or accepted state.

## 5. Necessary Complexity

R5-B1 demonstrated that not all complexity is overdesign. Some complexity is the minimum cost of preventing ambiguous authority, rewritten history, and accidental ownership expansion.

### 5.1 Why AuthorityDecision exists

Writing Assignment, Revision, disposition, or contribution rows directly would record state without recording why the state changed or who owned the change. `AuthorityDecision` is the attributable boundary around a bounded world change.

Its compound shape is deliberately limited. One real user decision may atomically disposition one Assignment, activate or replace its contract under approved combinations, establish or delegate bounded identities, and create zero or more accepted contributions. It is not a generic transaction language.

Atomicity prevents half-decisions such as accepting an Assignment while losing its accepted contributions, or activating a Revision after only some authority checks have passed.

### 5.2 Why Claim and accepted contribution are separate

An authority may adopt a proposal verbatim, author a revised assertion while retaining the Claim unchanged, or author a new contribution without a prior Claim. This preserves honest provenance:

```text
Actor claimed X
  -> Authority considered X and decided Y
  -> Project currently accepts Y
```

The history never pretends that the Actor originally claimed Y.

### 5.3 Why Revision and Attempt are separate

A material Revision changes the Assignment contract; an Attempt records effort under one immutable effective Revision. Keeping the identities separate prevents work performed under R1 from being silently reinterpreted as work performed under R2. Historical work remains usable evidence, but a stale Revision cannot be dispositioned as current.

### 5.4 Why stored and effective routing are separate

The Application must preserve what was explicitly selected while also answering whether that selection is still valid under current authoritative state. Collapsing the two would either erase history or continue stale work. Separate projections make CAS, recovery, and invalidation mechanical rather than interpretive.

### 5.5 Why scopes and capabilities are closed

Closed `Project | Responsibility | Assignment` contribution scopes and a short capability enum prevent B1 from becoming a general Truth platform or ACL engine. No wildcard, inheritance, deny language, arbitrary resource path, custom scope string, or role template was needed.

### 5.6 Why Legacy was not automatically converted

Old Task completion, review approval, AutoProceed, epoch ownership, Memory, Summary, and completion payloads are not semantically equivalent to B1 governance records. Automatic conversion would manufacture historical authority that never existed. Preserving old records and requiring explicit current-time crossing is safer than producing a convenient but false history.

## 6. Legacy and B1 Coexistence

The final relationship is:

```text
Legacy world
  - readable
  - inspectable
  - independently meaningful
  - referencable as bounded context/evidence
        |
        | explicit, attributable, one-way crossing
        v
B1 continuity world
  - Claim
  - Handoff
  - AuthorityDecision
  - Accepted Project State
```

It is not a convert-everything migration. The two eras coexist:

- Legacy retains historical behavior and compatibility semantics.
- B1 begins a new governance history when a new Project is created with bootstrap authority or an eligible pre-B1 Project is explicitly adopted once.
- Adoption establishes only the governance root. It does not adopt Legacy state, create a Decision, or infer an owner from a Leader, Session, setting, review, or AutoProceed record.
- Later B1 actions may reference Legacy locators as evidence or context, but only named current-time commands can create B1 authority effects.
- B1 writes do not rewrite bounded Legacy rows.

This parallel landing zone preserves production data while leaving later retirement decisions evidence-driven and separately authorized.

## 7. Implementation and Certification Lessons

### 7.1 Additive landing before retirement

The Call/Data Audit showed that no old bridge was safe to remove before new semantics had a real persistence and recovery destination. Migration020 therefore added a parallel B1 landing zone without deleting or reinterpreting Legacy data. The result validates the earlier audit conclusion: build the new bridge before retiring the old one.

### 7.2 Validate before persistence

Implementation tests exposed places where schema constraints would otherwise have become the first domain validator, including duplicate supersession targets. Moving these checks into the evaluator preserved the intended boundary: the evaluator decides whether a Decision is legal; the repository atomically persists a Decision already known to be legal; database constraints remain the last mechanical defense.

### 7.3 Persistence must round-trip the complete decision

Task 9 exposed a missing persistence field for Revision activation source provenance. The correct response was to complete Migration020 and repository round-trip semantics before adding higher-level commands. Authoritative history is only recoverable if every validated field survives persistence.

### 7.4 Manual certification prevented execution leakage

The zero-Session flow forced the Application composition and recovery projection to work without fake runtime telemetry or a Gateway. If Manual continuity had required a Provider or runtime object, that would have shown that execution still owned Project continuity.

### 7.5 Certification includes engineering hygiene

The final integration audit found that passing certification tests still left empty temporary parent directories and that unconstrained test concurrency made SQLite cleanup nondeterministic on Windows. A narrow test-only correction made cleanup ownership explicit, serialized the Storage test assembly, retained bounded App concurrency, and proved Task 14 added no new temporary directories. Reproducible verification is part of a trustworthy architecture seal.

## 8. Product Positioning

R5-B1 changes the center of gravity of Workbench.

Earlier framing:

> A workbench that helps AI develop games.

Sealed framing:

> **AI Game Workbench preserves continuity, attributable decisions, and recoverable accepted state for long-running AI projects across disposable agents, sessions, providers, and runtimes.**

Chinese:

> **AI Game Workbench 是一个为长期 AI 项目保存连续性、可追溯决策和可恢复已接受状态的工作系统，使项目能够跨越一次性 Agent、Session、Provider 与 Runtime 持续存在。**

The change is from an AI development tool centered on execution to AI Project continuity infrastructure centered on durable responsibility and governance. Workbench may connect to execution, but execution no longer defines the Project's identity or accepted state.

## 9. Explicit Non-Goals

R5-B1 does not make Workbench responsible for:

- Agent intelligence, training, marketplace, prompting strategy, or subagent strategy;
- execution engines, sandboxing, worktree/Git orchestration, tests, merge workflows, provider runtime, or runtime permission policy;
- a generic ACL, enterprise IAM, wildcard resource language, or role-derived privilege system;
- automatically determining whether every Claim is true;
- a general Truth Object platform or semantic conflict-resolution engine;
- transcript or Summary as authoritative state;
- automatic conversion, cleanup, migration, or retirement of Legacy semantics;
- Agent Gateway, provider adapter, external runtime integration, or UI work not separately designed and approved.

These exclusions are ownership boundaries, not claims that the capabilities are unimportant. They state which layer is responsible and prevent every execution or product gap from being reclassified as a Kernel requirement.

## 10. Future Admission Criteria

Any later proposal must pass these questions before it can modify the R5-B1 baseline.

### 10.1 Continuity

Would accepted state, durable identity, or recovery become dependent on a live Session, Provider, runtime, transcript, or Summary?

If yes, reject or redesign the proposal.

### 10.2 Authority

Can the proposal modify accepted state without a persisted, attributable AuthorityDecision produced through a named validated command?

If yes, reject or redesign the proposal.

### 10.3 Attribution

Can the system still answer who claimed, who decided, which Responsibility and Assignment were involved, and which immutable Revision governed the work?

If no, reject or redesign the proposal.

### 10.4 Legacy compatibility

Does the proposal infer B1 meaning or authority from Legacy state, or silently rewrite Legacy records?

If yes, reject or require a separate explicit architecture and migration decision.

### 10.5 Domain ownership

Does a proposed fact have a clear owner, authority source, persistence model, concurrency rule, and restart/recovery behavior?

If no, it is not ready to become a durable Workbench domain fact. Adding a table or field is not sufficient.

### 10.6 Delegated execution

Is Workbench being asked to own execution merely because an Agent or Provider exposes the capability imperfectly?

If yes, first prove why durable Project semantics require that ownership rather than an Application or Adapter connection.

### 10.7 Change control

Does the proposal introduce a new scope kind, authority capability, claimant kind, command shape, automatic bridge, Accepted Project State writer, or durable identity?

If yes, it requires a new explicit architecture decision. It cannot enter through an implementation convenience.

## 11. Retrospective Judgment

R5-B1 succeeded because it stopped treating continuity as remembered conversation and started treating it as attributable, recoverable Project governance.

The enduring lesson is:

> A long-running AI Project must continue to know who is responsible, what was claimed, who decided, and what is currently accepted even when every Agent session and execution environment is disposable.

This retrospective closes the architectural record of R5-B1. It selects no later release direction and authorizes no further implementation.
