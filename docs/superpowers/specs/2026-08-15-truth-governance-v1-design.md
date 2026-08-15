# Truth Governance V1 — Design Specification

Status: **Approved for implementation planning**
Date: 2026-08-15
Product: AI Game Workbench
Intended repository path: `docs/superpowers/specs/2026-08-15-truth-governance-v1-design.md`

## 1. Scope

Truth Governance V1 defines how AI Game Workbench preserves the minimum durable project continuity needed for a future Logical Leader to correctly understand the current project without rereading raw Agent history.

This specification covers:

- Project Summary / Continuity Journal;
- Project Library Objects;
- Intent Head and Implemented Head;
- Truth Delta and authority/evidence rules;
- Evolution promotion;
- provenance / Source references;
- Agent Session traceability;
- New Leader recovery and read strategy;
- conflict, uncertainty, supersession, retention, and cleanup;
- token-economy constraints that govern all of the above.

This is a behavioral and information-architecture contract. It does not prescribe concrete SQL tables, migrations, classes, UI layout, provider adapters, or implementation tasks.

## 2. Product Goal

The Project must survive Agent replacement, Session termination, model changes, and provider changes.

The system is project-first rather than agent-first:

> **Agent does the work. Workbench owns the handoff and continuity.**
>
> **The project survives the agents.**

Truth Governance V1 exists to preserve the smallest sufficient representation of:

1. what the Project is now;
2. why important decisions or constraints exist;
3. what evidence or original scene supports them;
4. what responsibility is currently unresolved.

It must not become a second transcript system, a generic AI memory platform, or a continuously-running summarization agent.

## 3. Non-Goals

Truth Governance V1 does **not** build:

- a full Wiki replacement;
- generic cross-project personal memory;
- embeddings/vector RAG as a requirement;
- a complete Agent transcript store;
- automatic Session summaries for every Session;
- periodic background summarization;
- a permanent candidate-memory inbox;
- a full event-sourcing architecture;
- a project-history narrative generated at boot;
- automatic rewriting of approved user Intent from code or Worker claims;
- a mechanism that injects the full Library, all Summaries, and raw Sessions into every Agent;
- provider runtime replacement or R1–R5 migration implementation.

Provider/runtime refactoring, persistence migrations, legacy-memory removal, and data slimming are later implementation concerns informed by this contract.

## 4. Core Information Model

Workbench separates long-term continuity into four semantic layers plus provenance:

```text
Agent Session / Raw Scene
        ↓
Project Summary / Continuity Journal
        ↓
Project Library Current Truth
        ↓
Project Library Evolution

Source refs connect all layers to evidence / original scenes.
```

The responsibilities are intentionally distinct:

> **Session 留现场。**
> **Summary 记原因。**
> **Current Head 记现在。**
> **Evolution 记值得长期知道的变化。**

Equivalent product shorthand:

> **Library 记变化。Summary 记原因。Source 给路径与证据。Session 留现场。**

No layer should duplicate the full body of a lower layer merely for convenience.

## 5. Information Lifecycle

Raw Agent activity is not automatically durable Project Memory.

Only meaningful boundary events are eligible for Leader governance, such as:

- a formal user decision;
- a Worker Final Report that the Leader must review;
- a NeedsLeaderDecision / AskUser resolution;
- a project-design discussion that reaches a real decision;
- a failure or discovery that materially changes future decisions;
- a stage boundary or Leader rollover requiring consolidation.

For an important boundary that the Leader is already required to understand, the same Leader cognition may derive:

```text
Leader Decision
├─ Review Decision       (when applicable)
├─ Summary Delta
├─ Truth Delta
└─ Evolution Delta / Candidate
```

These are products of one understanding pass, not separate LLM calls.

Most raw events should die below Summary. Most Summary entries should never become Current Truth. Most Current Head changes should never become Evolution nodes.

## 6. Token Constitution

All Truth Governance behavior is constrained by three rules:

> **Understand once.**
> **Store only the delta.**
> **Read the highest-density sufficient layer.**

### 6.1 Understand once

If the Leader has already read and understood an Assignment result, user decision, or evidence package for its primary duty, governance must reuse that same understanding. It must not start a second model pass merely to ask whether the information deserves Summary or Truth promotion.

### 6.2 Store only the delta

Summary is appended as sparse semantic entries. Current Heads are updated directly. Existing Summary or Library text is not repeatedly regenerated because a new event occurred.

### 6.3 Highest-density sufficient layer

The default read order is:

```text
Relevant Current Truth
        ↓ only if insufficient
Relevant Summary entries
        ↓ only if insufficient
Specific Source metadata
        ↓ only if insufficient
Specific raw Session / Git / Test / Document
```

A higher-density layer replaces lower-density context by default; it does not accompany it automatically.

### 6.4 No second read for governance

A structured governance result must be durable enough to resume after crash. Recovery applies any unapplied delta idempotently instead of rereading the original Final Report or conversation and asking an LLM to decide again.

## 7. Project Summary / Continuity Journal

### 7.1 Purpose

Summary is a sparse durable project decision journal written for future Leaders.

It does **not** answer “what happened today?” It answers:

> **Which past information will materially affect future correct project decisions, and why?**

Summary is explicitly not a shortened transcript, work diary, or流水账.

### 7.2 Grouping and time

Summary entries are grouped by Project and Project-local calendar date for presentation. Storage may be entry-based rather than one mutable prose document.

Every entry records at least:

- `occurred_at`: when the decision/change actually occurred;
- `created_at`: when the entry was written;
- Project timezone;
- semantic type;
- concise statement;
- concise reason / significance;
- Source references.

The project timeline uses `occurred_at`, not the later write time.

At least minute-level display precision is required for normal human navigation.

### 7.3 Eligible entry types

A Summary entry should normally be one of:

- **Decision** — a formal decision and why it was made;
- **Change** — a material direction/state change and why it matters;
- **Constraint** — a durable condition future work must respect and why;
- **RejectedPath** — a route future agents should not repeat and why;
- **Unresolved** — a genuinely open issue whose uncertainty affects future work.

### 7.4 Admission test

The Leader asks:

> **If this sentence is deleted, is a future Leader meaningfully more likely to make a wrong decision, repeat a known dead end, or misunderstand why current Truth exists?**

If no, do not write it.

### 7.5 Explicit exclusions

Do not write ordinary process chatter such as:

- ran N tests;
- edited N files;
- build passed;
- Worker reported PASS;
- opened a Session;
- ran `git status`;
- temporarily tried A then B then C;
- ordinary bug-fix steps;
- routine tool output;
- model speculation;
- copied Final Report bodies;
- copied diffs or logs.

A failed attempt enters Summary only if the failure changes future decision-making or prevents a meaningful repeated dead end.

### 7.6 Append semantics

Summary is append-oriented semantic history. Later decisions do not silently rewrite earlier history.

If A was adopted at 10:12 and superseded by B at 13:42, preserve both decisions as separate entries if both passed the admission test. Do not edit the 10:12 entry to pretend B had always been the decision.

Allowed maintenance is limited to non-semantic repair such as typo/format fixes, Source repair, and exact duplicate/idempotency cleanup. Periodic AI “re-summary of summaries” is prohibited by default.

### 7.7 Authority

The Leader may append Summary without a separate user confirmation because Summary has no authority to change Current Truth.

However Summary must not:

- convert inference into fact;
- silently change user Intent;
- resolve uncertainty that remains unresolved;
- represent Worker claims as verified engineering fact.

## 8. Project Library Object

### 8.1 Purpose

A Library Object is a stable long-lived project concept with an independently meaningful current state.

Object creation requires all three:

1. **Stable identity** — future agents can refer to the same concept;
2. **Independent current state** — the concept can meaningfully have its own current Intent and/or Implemented state;
3. **Long-term reuse value** — misunderstanding its current state can cause a wrong future Assignment or project decision.

Examples include Agent Runtime, Leader Review, Boss chase mechanic, economy system, art direction, save architecture.

A button width, one bug, one commit, one Worker, one test failure, or a local variable is not an Object.

### 8.2 Minimal shape

```text
Object
├─ Name
├─ Short Description
├─ Intent Head        (optional when applicable)
├─ Implemented Head   (optional when applicable)
├─ Alignment          (derived)
├─ Evolution
└─ Source refs
```

`Short Description` identifies the concept only. It must not contain history, reasons, test status, or a long design explanation.

## 9. Current Head

### 9.1 Purpose

A Current Head is the minimum sufficient expression of what is currently valid for one dimension of an Object.

It is not a document.

### 9.2 Head types

- **Intent Head** — what the Object should be according to authorized project intent;
- **Implemented Head** — what the Object demonstrably is in the current implementation / observable project state.

These heads never overwrite one another.

### 9.3 Status values

A present Head has exactly one of:

- `KNOWN`
- `UNRESOLVED`
- `UNVERIFIED`
- `CONFLICTED`

There is no generic `UNKNOWN` record. Ordinary unknown facts stay absent from Library. A high-value unknown is represented only when falsely assuming an answer would materially harm project understanding or subsequent work.

### 9.4 Content granularity

A Head contains only current effective state:

- one concise statement; or
- a small set of atomic rules when one sentence would lose necessary meaning.

The normal compression target is one statement or approximately 2–7 atomic rules, not a hard storage limit.

A Head must not contain:

- formation history;
- reasons;
- prior alternatives;
- future plans;
- narrative discussion;
- copied evidence bodies.

If a Head needs many rules to be understandable, first reconsider whether the Object is too broad and should be decomposed.

### 9.5 Minimal fields

Conceptually:

```text
Head
├─ status
├─ statement / atomic rules
├─ effective_at
└─ source refs
```

Every Current Head must have traceable provenance. **No sourceless Current Truth.**

## 10. Intent Authority

Intent means approved project intent, not what code happens to do.

Intent `KNOWN` may be established or superseded only by:

- an explicit user decision; or
- a Leader decision that is inside previously granted authority.

The following cannot silently supersede Intent:

- Worker suggestion;
- code behavior;
- tests;
- a newer Final Report;
- Leader inference outside its authority.

If the Leader believes Intent should change but lacks authority, the result is `AUTHORITY_REQUIRED`, not an automatic Head update.

A user decision made in the current interaction does not require a redundant second approval merely to record that decision in Library.

## 11. Implemented Evidence

Implemented Truth is evidence-first.

### 11.1 Direct evidence

Evidence capable of supporting `KNOWN`, when relevant to the claim, includes:

- runtime behavior;
- automated tests;
- smoke verification;
- directly verifiable file/asset/project state;
- Git anchor plus reproducible or directly inspectable behavior/state.

### 11.2 Derived or claimant evidence

The following are useful Source material but do not normally establish `KNOWN` alone:

- Worker Final Report;
- Worker self-reported PASS;
- narrative implementation explanation;
- diff/stat without the acceptance-relevant verification;
- build success when behavior, not compilation, is the claim.

They usually support `UNVERIFIED` until the missing direct evidence exists.

Exception: if the acceptance claim is itself purely structural and the evidence directly proves that structure exists, a verifiable file/Git inspection may be sufficient without runtime evidence.

## 12. Alignment

Alignment is a derived projection, never independent authoritative Truth.

Possible values:

- `ALIGNED`
- `DRIFT`
- `NOT_COMPARABLE`

When both Intent and Implemented Heads are `KNOWN` and semantically comparable, Workbench/Leader may derive `ALIGNED` or `DRIFT`.

If a relevant Head is `UNRESOLVED`, `UNVERIFIED`, or `CONFLICTED`, the default result is `NOT_COMPARABLE`, not false certainty about Drift.

Because Alignment is derived, it naturally changes when Heads change and cannot become a stale third truth source.

## 13. Truth Delta

Truth Governance runs only at meaningful boundaries the Leader already needs to understand.

The logical outcomes are:

- `NO_CHANGE`
- `AUTO_UPDATE`
- `AUTHORITY_REQUIRED`
- `UNVERIFIED`
- `CONFLICTED`

`UNRESOLVED` may be written when a high-value decision is explicitly still open.

### 13.1 NO_CHANGE

Default result. No Library mutation and no Proposal.

Most Assignment PASS results should end here.

### 13.2 AUTO_UPDATE

Used when authority/evidence is sufficient for the relevant Head.

Examples:

- a user has just formally decided an Intent change;
- direct engineering evidence establishes an Implemented change;
- a Workbench-owned authoritative state transition deterministically changes a Workbench state.

Workbench-owned runtime state should be updated mechanically where possible; it does not require an LLM to rediscover a state transition the application already performed.

Not every Workbench-owned state belongs in Project Library. It enters Library only if it is necessary to the Project’s durable current world model.

### 13.3 AUTHORITY_REQUIRED

Created only when a genuine Truth Delta is identified but the Leader lacks authority to make the intended change.

This is the only normal path that creates a Truth Proposal.

### 13.4 UNVERIFIED

Used when a concrete high-value claim exists but evidence is insufficient. If the uncertainty does not materially affect future project decisions, do not create a Library Head merely to record ignorance.

### 13.5 CONFLICTED

Used when credible Sources materially disagree and the Leader cannot safely resolve them from current evidence/authority.

The Leader must not select whichever Source is newer or more convenient without a justified evidence/authority basis.

## 14. Truth Proposal

Truth Proposal is a short-lived authority gate, not a permanent candidate-memory pool.

Lifecycle:

```text
AUTHORITY_REQUIRED
        ↓
Pending Proposal
        ↓
Accepted / Modified / Rejected
        ↓
Consumed
        ↓
large proposal payload disposable
```

After decision, retain only thin governance metadata required for continuity/idempotency/audit, such as Proposal identity, Object, decision, occurred time, and decision Source.

Result distribution:

- accepted/modified current state → Current Head;
- important reason → Summary;
- original discussion → Session / Source;
- rejected proposal → not Truth; write Summary only if the rejection itself materially constrains future decisions.

## 15. Summary Delta and Truth Delta Are Independent

A Truth change does not automatically require a Summary entry.

A routine factual update may update a Head with direct Source and no explanatory Summary.

A Summary entry does not automatically require a Truth change. For example, a durable rejected path can matter to future reasoning even when the Current Head remains unchanged.

When a change has an important rationale, Summary is the preferred explanatory Source for Library/Evolution. It is not a mandatory intermediary for every Head mutation.

## 16. Evolution

### 16.1 Purpose

Evolution records only changes with long-term explanatory value.

A Current Head update serves **accuracy**. Evolution serves **historical meaning**. They are distinct operations.

### 16.2 Node shape

A normal Evolution node contains:

- precise `occurred_at`;
- Object;
- concise `Before → After`;
- Git anchor when relevant;
- Source references.

It does **not** contain the reason. The reason belongs in Summary or another Source.

### 16.3 Promotion rule

Do not create an Evolution node for every Head update.

Leader may:

- promote immediately when long-term explanatory value is obvious and the change is already authorized/evidenced; or
- defer the decision and consider the change during stage/phase consolidation.

Short churn can therefore collapse from `A → B → C → D` into `A → D` if B/C have no future explanatory value. If C was an important directional turn, retain `A → C → D`.

Duration is not the criterion; explanatory value is.

### 16.4 Consolidation

Stage consolidation receives bounded references to Head changes plus only the relevant Summary entries. It must not reread an entire month of transcripts or regenerate Library from scratch.

## 17. Source / Provenance

### 17.1 Principle

Workbench owns traceability, not transcripts.

Source is a compact locator/provenance record, not an evidence warehouse.

Typical Source kinds include:

- SummaryRef;
- GitRef;
- TestEvidenceRef;
- DocumentRef;
- AssetRef;
- AssignmentRef;
- AgentSessionRef;
- UserDecisionRef;
- WorkbenchStateTransitionRef.

The exact persistence shape is an implementation concern, but Source must contain enough stable identity to locate or describe the evidence without copying its full body.

### 17.2 Preferred trace

For an explanatory Library change, the common path is:

```text
Library change
    ↓
Summary
    ↓
Source
    ↓
Session / Git / Test / Document / Asset
```

Direct evidence Sources may also be attached to Current Heads or Evolution nodes. Summary is the primary explanation layer, not an exclusive provenance requirement.

### 17.3 Source loss

Loss of a lower-level Source does not automatically invalidate an already-legitimate Current Truth.

> **Source loss degrades provenance, not Truth. Evidence conflict changes Truth confidence.**

If a Provider Session becomes unavailable, provenance may become partially degraded. Current Truth changes only when new counter-evidence, authority conflict, or inability to establish the original legitimacy warrants it.

## 18. Agent Session Trace

Workbench does not persist native Agent transcript bodies as Project Truth.

It retains only the identity/lifecycle data needed for navigation, recovery, and source traceability, for example:

- Provider;
- ExternalSessionId;
- Project;
- occurred/start time;
- optional Agent/model label;
- working directory when required operationally;
- Git anchor;
- necessary internal correlation IDs.

Human-visible labels prioritize natural navigation, for example:

```text
2026-08-15 13:06
JADEFIXIO
Codex
Git d84bd37
```

Internal SessionId / AssignmentId / EventId remain machine identifiers and are not the primary UI language.

Project-local time is a first-class navigation spine across Summary, Evolution, Git, and Session trace.

## 19. Handoff

Handoff is an operational relay baton, not long-term memory.

Lifecycle:

```text
Active → Delivered/Selected → Consumed → Disposable
```

Handoff answers only:

> **What was the immediately previous Leader/Agent doing, where did it stop, and what must continue next?**

It must not duplicate durable Project Truth or become a permanent sequence of historical session summaries.

Leader rollover does not create a new long-lived Summary merely because a Session changed.

## 20. New Leader Recovery

### 20.1 Goal

A new Logical Leader must recover enough context to correctly own the current responsibility, not reconstruct the entire project history.

### 20.2 Default Boot Context

Boot should remain thin and normally contain:

- Project Identity;
- Persistent Logical Leader responsibility;
- Active Assignment / pending decision / unresolved authority gate;
- current Git anchor;
- relevant Current Truth Heads;
- relevant known conflicts/drift;
- active unconsumed Handoff if one exists;
- bounded Source references for drill-down.

### 20.3 Relevance

Current responsibility determines which Library Objects are relevant. Workbench must not inject the full Library by default.

The Library primarily serves Leader judgment. Assignment contracts serve Worker execution.

### 20.4 Progressive drill-down

```text
Current responsibility
        ↓
Relevant Current Heads
        ↓ sufficient? yes → STOP
Relevant Summary entries
        ↓ sufficient? yes → STOP
Specific Source metadata
        ↓ sufficient? yes → STOP
Specific raw Source / Session
```

Session is an exception path for audit, ambiguity, conflict, debugging, or precise semantic reconstruction. It is not the normal Leader recovery path.

### 20.5 No Project History Summary

Do not automatically generate or inject a prose “project history summary” at boot. It would mix current truth, expired history, reasons, and temporary state while duplicating Library/Summary/Evolution.

### 20.6 Evolution reading

Evolution is not part of default Boot unless historical evolution is directly relevant to the current responsibility. Read it when the Leader needs to understand how an Object reached its current state.

## 21. Worker Context Boundary

Workers do not perform full Project recovery.

A Worker receives the minimum correct world for the active Assignment:

- Goal;
- Acceptance;
- Scope;
- OutOfScope;
- hard constraints;
- relevant Current Truth;
- relevant Drift/conflict only when task-relevant;
- BaseCommit / workspace identity;
- optional Source refs when required.

Do not inject full Library, all Summary, Leader history, or unrelated project evolution.

> **Library is Leader memory. Assignment is Worker world.**

## 22. Uncertainty and Conflict Lifecycle

### 22.1 UNVERIFIED

`UNVERIFIED` exists only while the unresolved evidence gap matters to current project understanding.

It may transition to:

- `KNOWN` when evidence arrives;
- another current state if the claim proves false;
- removal from Library if the uncertainty becomes irrelevant.

### 22.2 UNRESOLVED

`UNRESOLVED` is a current decision state, not permanent history. Once authorized resolution occurs, replace it with the resolved Head. Preserve the historical dispute in Summary only if it has future explanatory value.

### 22.3 CONFLICTED

`CONFLICTED` never expires merely because time passes. It requires new evidence, reproduction, authority resolution, or explicit correction before transition.

Resolving a conflict should create Summary only if the conflict/resolution materially affects future reasoning.

> **Uncertainty is current state, not permanent history.**

## 23. Supersession

“Newer” does not mean “more authoritative.”

Supersession occurs only within the same Truth dimension and according to its governing basis:

- Intent → newer authorized Intent decision;
- Implemented → newer/more direct/reliable engineering evidence;
- Workbench-owned state → formal Workbench transition.

Cross-type mismatch never supersedes. Intent and Implemented disagreement produces derived Drift when both are KNOWN and comparable.

Old Head bodies do not need to remain indefinitely as parallel current revisions. Long-term historical value is preserved through Evolution, Summary, and Sources when warranted.

## 24. Retention and Cleanup

### 24.1 Long-lived

- current Object identity and Current Heads;
- high-value Evolution nodes;
- admitted Summary entries;
- thin Source references;
- necessary Assignment acceptance identity / outcome identity;
- thin Review / authority decision records required for continuity and audit.

### 24.2 Short-lived / consumable

- Truth Proposal full payload after decision;
- Handoff body after consumption;
- high-value uncertainty only until no longer current/relevant;
- temporary governance work products once their canonical result is safely persisted.

### 24.3 Provider-owned

- native Agent transcript bodies and provider-specific raw session history.

### 24.4 Cleanup principle

> **Filter before storage, not clean after accumulation.**

The system should not preserve low-value material on the assumption that a future AI cleanup pass will fix it. Admission should be strict up front; post-hoc semantic cleanup should be rare.

## 25. Canonical Result / Duplication Principle

Truth Governance must not encourage the same Final Report, Handoff, transcript, or evidence body to be copied across multiple persistence surfaces.

Long-term persistence should converge toward:

```text
canonical result / authoritative state
+
thin references
+
necessary immutable acceptance / decision identity
```

This spec does not define the R4 migration, but future data slimming must preserve continuity while eliminating redundant bodies where safe.

## 26. Failure and Recovery Semantics

| Condition | Required behavior |
|---|---|
| Leader decision produced; Summary applied; Truth not applied before crash | Resume from durable decision and apply only missing Truth Delta idempotently; no second LLM read. |
| Truth applied; Evolution promotion not decided | Current Head remains authoritative; Evolution can be considered later without changing current truth. |
| Source unavailable | Mark provenance degraded as needed; do not invalidate Truth solely due to source loss. |
| Worker claim conflicts with current Implemented evidence | Do not overwrite KNOWN; use CONFLICTED or remain with stronger authoritative evidence as justified. |
| Code differs from approved Intent | Preserve both Heads; derive DRIFT when both are KNOWN/comparable. |
| Leader infers an Intent change without authority | AUTHORITY_REQUIRED; create Proposal/AskUser; do not mutate Intent. |
| High-value claim lacks direct evidence | UNVERIFIED, or omit from Library if uncertainty is not project-relevant. |
| Old uncertainty becomes irrelevant | Remove it from Current Library; Summary remains only if it independently passed the admission threshold. |

All governance mutations require stable event/decision identities sufficient for idempotent application and restart recovery. Exact schema is deferred.

## 27. Token-Economy Observability

Implementation should leave room to measure whether continuity is actually saving context rather than adding governance tax.

Useful metrics may include:

- **Continuity Compression Ratio** — durable high-density context created relative to raw information considered;
- **Boot Context Size** — tokens injected for Leader recovery;
- **Drill-down Rate** — how often Library was insufficient and Summary/raw Sources were required;
- **Context Reuse Savings** — estimated raw context avoided by reusing Current Truth / Summary;
- **Governance Re-read Rate** — should approach zero for already-understood boundary events.

These are observability goals, not V1 acceptance thresholds. They exist to falsify a bad design: if governance consumes more context than it prevents over long-lived projects, the design must be revisited.

## 28. Testable Behavioral Invariants

- Session boundaries do not automatically create durable Summary entries.
- Summary contains no routine work-log entries solely because an event occurred.
- Summary admission is based on future decision value, not event importance alone.
- Summary is append-oriented and not recursively re-summarized by default.
- Current Head contains current state only; no history, rationale, or next-step plan.
- Every Current Head has traceable Source provenance.
- Intent and Implemented Heads never overwrite one another.
- Worker claims cannot silently become Implemented KNOWN.
- Approved user Intent cannot be silently superseded by code, tests, or newer Worker output.
- Alignment is derived and never persisted as an independent authoritative Truth source.
- Most PASS results can legally produce `NO_CHANGE` and no Summary entry.
- Truth Proposal exists only for authority-gated Truth changes, not as a universal candidate pool.
- Current Head update does not automatically create Evolution.
- Evolution nodes do not contain reasons; reasons live in Summary/Source.
- New Leader Boot does not inject full Library, all Summary, or raw Session history by default.
- Read strategy stops at the first sufficient density layer.
- Worker context is task-minimal rather than project-complete.
- Source loss degrades provenance rather than automatically invalidating established Truth.
- CONFLICTED does not time out into a guessed answer.
- Handoff is consumable and does not become long-term project memory.
- Governance recovery applies stored deltas idempotently rather than asking the LLM to reinterpret the same source.
- Duplicate large bodies are not required for continuity.

## 29. Relationship to Existing / Future Work

This design changes how later architecture work should interpret existing persistence:

- Persistent Project identity and Logical Leader responsibility remain core continuity.
- Current effective Assignment contract/revision identity is core; historical rich revision bodies are not automatically core forever.
- Review/Authority decisions remain continuity-critical, but their long-term representation should be thin and typed rather than duplicated prose or fragile serialized-payload search.
- existing legacy memory/session/library structures should not be expanded merely because they exist;
- later kernel/provider/migration work must preserve authoritative Project state locally even if all external Providers are unavailable;
- provider-native Sessions and replaceable runtime capabilities remain operational dependencies, not owners of Project Truth.

Concrete migration from current schema and legacy M1.5/v7 structures is outside this spec and belongs to the later implementation plan / architecture migration sequence.

## 30. Success Criteria

Truth Governance V1 is successful when a fresh Leader can enter a long-lived Project and, without reading historical transcripts, recover:

1. what Project it owns;
2. what responsibility is currently active;
3. what relevant project facts and intent are currently valid;
4. where Intent and implementation are known to differ;
5. what unresolved/conflicted facts must not be guessed;
6. why a specific important current state exists, by drilling into only the relevant Summary;
7. the original evidence/scene only when deeper audit is actually necessary.

The durable information footprint must stay materially smaller and denser than the raw history it replaces.

## 31. Design Summary

The complete V1 model is:

```text
Raw Agent/User Scene
        ↓ meaningful boundary only
Persistent Logical Leader
        ↓ one understanding pass
Summary Delta + Truth Delta (+ Review when applicable)
        ↓
Project Summary                  Project Library
why / constraints / decisions    current Intent + Implemented
        │                              │
        └──────── Source refs ─────────┘
                       │
                 Evolution
              meaningful changes
                       │
                 drill down only
                       ↓
          Session / Git / Test / Docs
```

The governing product rules are:

> **Understand once. Store only the delta. Read the highest-density sufficient layer.**
>
> **Library 记变化。Summary 记原因。Source 给路径与证据。Session 留现场。**

---

## Self-Review

### Placeholder scan

No implementation-affecting `TBD` / `TODO` remains. Concrete schema, class names, migrations, UI controls, exact retention jobs, and token thresholds are deliberately deferred to planning because they do not change the behavioral contract.

### Internal consistency

- Summary is the preferred explanation layer but is **not required for every Head update**, avoiding a contradiction between “Truth change can have no Summary” and provenance requirements.
- Current Head and Evolution are explicitly separated: Head updates immediately for accuracy; Evolution may promote immediately only when long-term value is obvious or later at bounded consolidation.
- `UNVERIFIED` / `UNRESOLVED` / `CONFLICTED` are current states, while old uncertainty is not forced into permanent history.
- Source loss and evidence conflict are intentionally different: source loss affects provenance; contradictory evidence may affect Truth status.
- Workbench-owned state may update mechanically without LLM, but only durable project-world facts need Library representation.

### Scope check

The design is large but cohesive: every section belongs to one continuity problem — how durable Project Truth is created, read, traced, and retired. Provider runtime replacement, schema migration, UI redesign, generic memory, RAG, and data migration are explicitly outside scope and should be separate implementation-plan phases.

### Ambiguity check

The following choices are explicit:

- no automatic per-Session Summary;
- no generic UNKNOWN rows;
- no universal Proposal/candidate pool;
- no silent Intent overwrite from implementation;
- no automatic Evolution node per Head change;
- no periodic recursive AI cleanup/summarization;
- no full-Library boot injection;
- no default raw Session read during recovery;
- no requirement that Summary intermediate every Truth update;
- no use of “newer source” alone as supersession authority.

No unresolved product-level ambiguity remains that should block implementation planning after user review.
