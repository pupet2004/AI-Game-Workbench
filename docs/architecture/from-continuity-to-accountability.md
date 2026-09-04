# From Continuity to Accountability

Status: **Non-normative research horizon**

Recorded: **2026-08-23**

Implementation status: **Historical research-horizon note. No accountability-platform implementation, roadmap, schema, migration, or product commitment is authorized by this document.** Current continuity implementation status is tracked in [Current Working-Tree Validation](../validation/current-working-tree-20260904.md).

## 1. Purpose

Workbench was designed to keep long-running AI Projects stable across disposable Agents, Sessions, Providers, models, and runtimes. While solving that continuity problem, it acquired several primitives that are also useful for responsibility and accountability: durable identity, bounded delegation, attributable Claims, explicit Authority Decisions, accepted-state projection, evidence locators, and recoverable history.

That overlap is worth preserving, but it must not rewrite the product narrative.

> **Workbench is first a lightweight long-running AI Project workbench. It is not currently an Agent accountability platform.**

The current product objective remains:

> **Long-term stability, low token cost, reliable continuity, convenient multi-CLI and multi-Agent routing, durable Project state, and bounded handoffs that do not silently distort accepted meaning.**

This note records how the current continuity architecture is accountability-friendly, where the approved product direction may strengthen provenance, and which more ambitious accountability questions remain research only.

## 2. Three Deliberately Separate Horizons

The discussion must remain split into three horizons so that future possibilities do not become accidental backlog.

| Horizon | Meaning | Commitment level |
|---|---|---|
| Implemented foundation | Sealed R5-B1 governance, attribution, persistence, and recovery semantics that exist on `master`. | Implemented and governed by the sealed R5-B1 documents. |
| Product target | The lightweight Leader/Worker continuity experience explored by this note; portions of bounded Agent participation and source-preserving Library navigation now exist in the current working tree. | This note remains non-normative; current implementation claims must come from the canonical repository's validation record. |
| Accountability research horizon | Stronger auditability, integrity, dependency, invalidation, recoverability, and organizational responsibility models. | Research questions only; no implementation is implied. |

An idea appearing in the third horizon does not become a Workbench feature requirement. It must later prove that it solves a real continuity or recovery problem without turning Workbench into a governance platform.

## 3. Implemented Accountability-Friendly Foundation

R5-B1 already preserves enough structure to answer important Project-internal questions without replaying a transcript.

| Existing primitive | Current semantic responsibility | What it does not establish |
|---|---|---|
| `UserPrincipal` | External authenticated person that may hold Project bootstrap authority. | It is not automatically a Project LogicalActor. |
| `LogicalActor` | Durable Project-local responsibility-bearing identity and eligible Claimant. | RoleKind does not grant authority; Provider or Session identity does not replace it. |
| `Responsibility` | Long-lived obligation, expected outcome, and maximum delegable authority boundary. | It does not store a mutable current owner. |
| `Assignment` | Immutable delegation of bounded work to one LogicalActor. | It is not runtime execution status. |
| `Revision` | Immutable effective contract for work under an Assignment. | It does not rewrite earlier Attempts or Claims. |
| `Attempt` | A specific effort under one effective Revision. | It does not imply started, running, failed, or completed execution. |
| `SessionBinding` | Optional connectivity and provenance binding for an Actor working through an external Session. | Session, model, runtime, and Provider do not become responsibility identities. |
| `Claim` | Immutable attributable assertion made by a UserPrincipal or LogicalActor. | It is not accepted Project state and is not verified merely by being recorded. |
| `EvidenceRef` | Typed provenance locator associated with a Claim. | It is not proof, independent verification, or authority. |
| `Handoff` | Immutable bounded collection of Claims produced during an Attempt. | It does not transfer Responsibility or Authority, complete work, or accept its Claims. |
| `AuthorityDecision` | Persisted, attributable, validated bounded Project change. | It is not a generic transaction, ACL grant, or workflow language. |
| `AcceptedProjectState` | Deterministic projection of persisted AuthorityDecision effects. | It is not a Summary, transcript interpretation, or automatic truth engine. |

This supports a modest but defensible statement:

> **Workbench contains a Project-internal responsibility and provenance substrate created in service of continuity.**

It does not support the broader claim that Workbench has solved Agent accountability. Legal liability, organizational policy, external identity assurance, regulatory compliance, insurance, non-repudiation, and responsibility for irreversible real-world actions remain outside the current system.

## 4. Current Provenance Doctrine

Accountability should remain a cross-cutting quality of existing Project objects, not a new product module or a second domain model.

### 4.1 Authoritative state has one required origin

The current hard invariant is:

> **No Accepted Project State mutation without one persisted, attributable `AuthorityDecision`.**

The authoritative chain is:

```text
optional Claim / Handoff / EvidenceRef
  -> considered context or provenance
     -> AuthorityDecision
        -> accepted-state effects
           -> AcceptedProjectState
```

An authorized principal may author a contribution directly. Therefore `SourceClaimRef`, considered references, and EvidenceRefs are optional when no honest source exists. The system must never manufacture a Claim or EvidenceRef merely to make a chain look more complete.

### 4.2 Mechanical facts should be recorded mechanically

Workbench should not spend additional model tokens restating facts that it or an adapter already observes. Examples include:

- Project, Actor, Responsibility, Assignment, Revision, Attempt, Claim, Handoff, and Decision identities;
- stored routing selections and commit sequence;
- timestamps generated at persistence boundaries;
- opaque Session, file, commit, CI run, artifact, or test-run locators;
- command and Decision relationships already present in the domain model.

Observation strength must remain honest. A commit hash read from Git, a test-process exit code observed by an adapter, a locator supplied by an external system, and an Agent statement that “tests passed” are not the same kind of knowledge. R5-B1 currently stores provenance without automatically verifying it.

### 4.3 Semantic deltas should reuse necessary cognition

When a Leader has already understood a result or Decision as part of normal work, it may emit a small Summary delta as a by-product of that same cognition. Workbench should not require a second LLM pass to reconstruct why the primary action happened.

Summary remains a non-authoritative continuity journal:

```text
AuthorityDecision
   -> changes AcceptedProjectState

SummaryDelta
   -> records a compact, source-typed continuity explanation
   -> never changes AcceptedProjectState
```

If a Summary says `Decided`, it should reference the real Decision. Otherwise it must preserve the weaker source semantics: discussed, proposed, claimed, observed, or unresolved. Compression improves readability and token efficiency; it never upgrades authority.

### 4.4 Library is navigation, not higher truth

A future source-preserving Library may index Decisions, accepted contributions, Claims, Handoffs, Summaries, evidence, artifacts, and Legacy context by time, type, responsibility, Assignment, or material. Indexing makes information easier to find but does not merge source kinds into one Truth layer.

The safe reading order is progressive:

```text
Mandatory Authority Packet
  -> highest-authority current Project state

Optional Context Packet
  -> bounded Summary / Library / Handoff context

Source drill-down, only when needed
  -> Claim / EvidenceRef / Session / Git / test / artifact / Legacy source
```

The highest-density layer is not automatically the highest-authority layer.

### 4.5 Trace on demand, not causal precomputation

Workbench should preserve durable pointers between important objects so that a user or Agent can drill down when something looks wrong. It should not continuously ask a model to build and maintain a complete causal graph of the Project.

The practical target is a thin provenance spine:

```text
Current accepted state
  -> owning AuthorityDecision
     -> optional considered Claim / Handoff / EvidenceRef
        -> optional Session / Git / test / artifact / external source locator
```

Summary and Library are lateral continuity and navigation views. They may reference the chain, but they do not sit between AuthorityDecision and AcceptedProjectState.

### 4.6 B1 Handoff and Session continuity are different histories

The B1 Assignment Handoff is durable, immutable, and attributable work history. It is not a disposable Session-summary cache and must not be deleted merely because a conversation-retention window expires.

Legacy Leader brain handoffs, transcript-derived recovery material, and future Session context packets may have separate retention policies because they belong to the Context Lane. Sharing the word “handoff” does not give them identical authority, ownership, or cleanup semantics.

## 5. Working Product Target

The accountability-friendly foundation should serve the original product goal rather than replace it. The currently discussed target experience is:

```text
Project Home
  -> durable Leader identity and current Project World
     -> bounded Assignment routed to a Worker / CLI / Agent
        -> optional replaceable SessionBinding
           -> typed Claims + bounded Handoff
              -> explicit Leader/User authority decision when required
                 -> recoverable AcceptedProjectState
                    -> compact Summary / Library navigation
```

The desired product qualities are:

- a new Leader or Worker can take over without replaying months of conversation;
- the mandatory authority packet is small, explicit, and trustworthy;
- optional context is labeled by source semantics and loaded only as needed;
- routine provenance is recorded without extra LLM interpretation;
- a Worker result returns through typed Claims and Handoff rather than copy/paste;
- Project policy can distinguish decisions requiring user approval from bounded decisions delegated to a Leader;
- Agent and CLI adapters remain replaceable and do not force Provider semantics into the Kernel;
- Summary and Library improve token economy and navigation without becoming authoritative state;
- retention of disposable conversation context cannot delete authoritative history or durable work attribution.

These are target semantics, not claims about the current UI. R5-B1 implemented and certified the domain/application continuity spine; the current product UI and Legacy Agent workflows are not yet the complete experience described above.

## 6. Future Accountability Research Horizon

The following topics are legitimate extensions of the discussion. They are intentionally recorded as hypotheses and questions rather than required objects, tables, services, or roadmap items.

### 6.1 Evidence integrity and observation strength

Possible future work may distinguish:

- a locator supplied by a claimant;
- an artifact directly observed by a trusted adapter;
- immutable content addressed by hash;
- independently reproduced validation;
- externally attested or signed evidence.

Questions include how evidence changes, disappears, or becomes unverifiable, and whether the Project should record observation method and integrity metadata. None of these upgrades a Claim automatically; acceptance remains an authority decision.

### 6.2 Delegation, revocation, and authority history

R5-B1 provides a closed capability and locality model sufficient for the continuity spine. A more complex environment might need explicit revocation, time-bounded delegation, multiple human principals, organizational roles, joint approval, separation of duties, or external policy integration.

Those features would be a new governance design. They must not enter by expanding RoleKind, adding wildcard capabilities, or treating Session ownership as authority.

### 6.3 Artifact and semantic-history linkage

Git already explains what changed in versioned files. Workbench may eventually help answer why it changed by linking commits, diffs, builds, test runs, documents, and other artifacts to Assignments, Claims, Handoffs, EvidenceRefs, and Decisions.

The link should remain bidirectional for navigation, not claim that Git history is Project authority or that an Agent-reported commit automatically proves an accepted result.

### 6.4 Dependency, invalidation, and blast-radius analysis

A future system might record that a Decision, accepted contribution, requirement, artifact, or result depended on another bounded source. When an upstream Decision is superseded or evidence is invalidated, it could identify downstream items that may need review.

The conservative semantic is `PotentiallyInvalidated` or `NeedsReview`, not automatic deletion or reversal. Designing this safely would require a separately approved dependency vocabulary, scope model, concurrency semantics, and human/authority workflow. Workbench currently has none of those commitments.

### 6.5 Recoverability and compensation metadata

For consequential actions, future research may ask:

- Is the action reversible?
- Is there a known rollback target?
- Does recovery require a compensating action rather than rollback?
- Is the effect irreversible outside Workbench?
- Who has authority to initiate recovery?

This is potentially valuable because accountability should improve recovery, not merely produce a detailed history. It is not part of the current Assignment, Attempt, Handoff, or AuthorityDecision model.

### 6.6 Runtime, model, tool, and environment provenance

SessionBinding already separates durable Actor identity from external connectivity. Future adapters may retain bounded model, runtime, tool, permission-profile, environment, and execution timestamps when they materially help reproduce or investigate work.

This information remains execution provenance. Provider, model, runtime, Agent instance, and tool identities do not become Claimants or deciding authorities merely because they are recorded in greater detail.

### 6.7 Immutable audit view or event history

It may become useful to query important lifecycle events—Decision creation, supersession, delegation, Claim submission, evidence observation, routing changes, or recovery actions—through a unified audit view.

This does not currently require or authorize a universal Event Ledger, whole-system event sourcing, a new canonical event table, or duplication of existing immutable histories. A later design must first prove that a query projection over existing records is insufficient.

### 6.8 Organizational, legal, and regulatory accountability

Project-internal attribution is only one input to real accountability. Legal responsibility may depend on employment, contract, jurisdiction, foreseeability, control, identity assurance, external harm, regulation, insurance, and organizational procedure.

Workbench must not claim that an internal provenance chain answers those questions. At most, it may produce structured records that a separately governed organizational or legal process can consider.

## 7. Explicit Non-Commitments

This research note does not authorize or require:

- a `ProjectCausalLedger` domain object or database;
- a universal immutable Event Ledger;
- whole-system event sourcing;
- a general Truth Object or Truth dependency graph;
- automatic causal inference or dependency discovery by an LLM;
- automatic invalidation propagation;
- mandatory EvidenceRefs for every AuthorityDecision;
- automatic Claim verification;
- a generic ACL, IAM, policy language, or organization model;
- legal or regulatory accountability claims;
- a second model pass that audits every normal Leader action;
- retention of every transcript, token, tool call, or runtime event;
- conversion of Legacy history into synthetic B1 authority history;
- a new Product Slice, R6 direction, schema migration, implementation plan, or cleanup authorization.

The phrase “responsibility/accountability provenance substrate” describes an architectural affordance. It is not a promise that all accountability problems have been solved.

## 8. Admission Criteria for Any Future Extension

An accountability-related proposal may enter architecture design only if it answers all of these questions:

1. **Continuity value:** Which concrete long-running Project failure, recovery problem, or trust ambiguity does it solve?
2. **Existing-model insufficiency:** Why are current AuthorityDecision, Claim, Handoff, EvidenceRef, SessionBinding, Summary, Library, Git, and source links insufficient?
3. **Token cost:** What additional LLM reading or writing is required in ordinary use, and why can the fact not be captured mechanically?
4. **Authority safety:** Can the extension change AcceptedProjectState, or merely improve provenance and navigation? Any state change must preserve the sealed authority path.
5. **Semantic honesty:** Does it distinguish claimant assertion, adapter observation, evidence locator, independent verification, and accepted Project state?
6. **Recovery action:** Does the additional record enable a concrete diagnosis, rollback, compensation, review, or handoff outcome?
7. **Scope containment:** Can it remain a bounded projection, adapter, or optional view instead of expanding the Kernel?
8. **Retention:** Can raw context expire without breaking durable attribution or accepted-state recovery?
9. **Legacy safety:** Does it avoid retroactively manufacturing B1 identity or authority from Legacy data?
10. **Independent authorization:** Has it received a new architecture decision rather than entering as a field, UI convenience, or migration side effect?

Failing these questions means the idea remains research material.

## 9. Research Judgment

Continuity and accountability are related but not identical:

```text
Continuity asks:
  How can a new participant inherit the Project without replaying everything?

Accountability asks:
  Why should that inherited state be trusted, and how can a failure be traced or recovered?
```

Workbench currently solves the first problem through durable identity, bounded work, attributable authority, accepted-state projection, and layered recovery. Those same structures provide useful provenance for the second problem.

The appropriate strategy is therefore:

> **Keep Workbench lightweight and continuity-centered. Preserve thin, honest provenance links as a cross-cutting invariant. Investigate stronger accountability only when a concrete recovery or trust problem justifies the additional domain and token cost.**

This preserves the research insight without allowing it to inflate the current product.
