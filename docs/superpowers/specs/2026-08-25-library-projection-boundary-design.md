# Library Projection Boundary Design

Status: **Implemented and sealed — 2026-08-25**
Date: **2026-08-25**

Governing records:

- [R5-B1 Baseline](../../architecture/r5-b1-baseline.md)
- [Workbench Global Architecture](../../architecture/workbench-global-architecture.md)
- [Product Slice 1 — Manual Project World](./2026-08-24-product-slice-1-manual-project-world-design.md)
- [Memory Continuity And Evolution Library](./2026-08-14-memory-continuity-library-design.md)

## 1. Purpose

The Library is the place a normal user should browse to understand a Project. This record defines how that user-facing Library relates to B1 authority without turning Library Objects into a new authority model.

The central rule is:

> **B1 owns accepted Project facts. Library owns a readable, navigable projection of selected facts and their references.**

The Library is therefore not a second truth store, not a replacement for `AcceptedProjectState`, and not a writer to it.

## 2. User-facing model

The default user path is:

```text
Library Object
  -> current overview
  -> accepted changes over time
  -> reason / decision reference
  -> supporting handoff, claim, summary, evidence, or source
```

Most users should not need to open a Handoff, Summary, Claim, or AuthorityDecision. Those remain expandable trace material for review, debugging, or disputed history.

The Library may also provide Project-level Decisions that do not map safely to one Object.

## 3. Non-negotiable boundaries

1. `LibraryObject` is not a B1 contribution scope. R5-B1 currently permits only `Project`, `Responsibility`, and `Assignment` contribution scopes.
2. A Library write cannot change `AcceptedProjectState`.
3. An `AuthorityDecision` cannot be fabricated from a Library paragraph, Summary, Handoff, Legacy row, or timestamp.
4. A Summary is not promoted merely because it is concise, recent, or displayed in the Library.
5. A Claim remains an attributable input. It may be shown beside a Library change, but it is not accepted by display.
6. Legacy Library data remains Legacy context unless a user explicitly records a current-time B1 Claim or Decision.
7. If an application cannot determine which Object a Decision affects without guessing, it must keep the event at Project level.

## 4. Three layers of Library information

### 4.1 Accepted current view

The authoritative source is the deterministic B1 projection:

```text
persisted AuthorityDecision history
  -> AcceptedProjectState
```

The Library may render accepted contributions as a current object overview or as a Project-level accepted statement. It must preserve the originating `AuthorityDecisionRef`.

### 4.2 Evolution view

The Library Object and Timeline Node are application records used for navigation and readability. They may contain a concise user-facing explanation of a change, but they do not replace the accepted contribution or its decision.

An Object overview can be revised without rewriting the underlying authority history. Timeline nodes are append-only history except for the existing explicit optimistic-revision update operation.

### 4.3 Trace view

When the user expands a Library change, the application may resolve references in this order:

```text
AuthorityDecision
  -> considered Handoff / Claim
  -> Summary Journal
  -> Evidence / file / commit / test / session
```

References are links to canonical records or external locators. Bodies are not copied into provenance tables merely to make the Library self-contained.

## 5. What can appear in an Object timeline

A change may appear in an Object timeline only when an explicit application projection operation supplies all of the following:

- the target Object identity;
- the user-facing change text;
- the effective local date;
- the relevant `AuthorityDecisionRef` when the change is presented as accepted;
- optional `SourceClaimRef`, `HandoffRef`, `SummaryRef`, and evidence/material references;
- the expected Object/Node revision where an existing record is updated.

The projection operation may be initiated by a user or by an explicit Leader proposal subject to the existing Library confirmation policy. It is not an automatic database trigger on every Decision.

Allowed timeline content includes:

- a newly accepted Project fact that the user explicitly associates with an Object;
- an accepted Assignment outcome that clearly describes an Object change;
- a revision of an existing Object overview or timeline node with preserved source references;
- a material implementation change where the authority event and evidence are both explicit.

The timeline must not contain low-value execution noise such as every file edit, tool call, or intermediate conversation turn.

## 6. What remains Project-level

The following remain visible in a Project Decision view rather than being guessed into an Object timeline:

- governance or authority changes;
- a decision affecting multiple unrelated Objects;
- a contribution whose scope is Project-level but has no explicit Object association;
- a rejected or revision-required work result with no accepted object change;
- a Decision whose target Object was deleted, renamed ambiguously, or cannot be resolved deterministically;
- Legacy history that has not been explicitly re-established in B1.

Project-level visibility is not a failure. It is the safe result when object attribution is unknown.

## 7. Decision-to-Library mapping

The mapping is intentionally one-way and application-owned:

```text
AuthorityDecision
  -> accepted contribution(s)
  -> optional explicit Library projection proposal
  -> Library Object / Timeline Node
  -> user browse view
```

There is no reverse path:

```text
Library prose -X-> AcceptedProjectState
```

The Library proposal may quote or summarize an accepted effect, but the accepted effect remains authoritative only because of its B1 Decision.

## 8. Provenance shown to users

The normal Object view should be compact:

```text
Enemy Scaling

Current:
Dynamic scaling by player progression

Changed:
2026-08-25

Accepted by:
Decision D19
```

Expandable detail may show:

```text
Decision D19
  considered Handoff H7
  included Claim C12
  summarized in Summary S183
  supported by commit abc123 and test run T4
```

The UI must label each item by kind. A Summary or Evidence reference must never be rendered as if it were an accepted statement.

## 9. Overview semantics

`Current Overview` is a convenience projection for fast reading. It is not the complete accepted state and is not independently authoritative.

Therefore:

- an Overview may be empty even when accepted Project facts exist;
- an Overview may lag until a user accepts a Library projection proposal;
- changing an Overview cannot delete or supersede a B1 contribution;
- a rebuild or deletion of Library projections must not change B1 accepted state;
- the UI should provide a link to the underlying accepted Decision whenever an Overview claims current acceptance.

## 10. Legacy coexistence

Legacy Library records remain readable under a clearly marked Legacy Context. They may be displayed beside B1 material only with distinct labels and source boundaries.

Opening or browsing a Legacy record does not:

- create a B1 Object;
- create a Claim or Handoff;
- create an AuthorityDecision;
- modify AcceptedProjectState.

Explicit adoption establishes only the B1 bootstrap root. Any current B1 interpretation must be recorded now through the normal Claim/Decision path.

## 11. Recovery and failure

The recovery guarantee is asymmetric by design:

```text
AuthorityDecision history -> AcceptedProjectState
```

must always rebuild identically.

Library projections may be rebuilt, repaired, or temporarily unavailable without changing accepted state. A failed Library proposal or stale Object/Node revision writes nothing to the Library and never rolls back a committed B1 Decision.

## 12. Implementation admission gate

Before implementation, a proposal must answer:

1. What exact accepted contribution or Project Decision is being projected?
2. Who or what supplied the Object association: user, explicit Leader proposal, or deterministic existing mapping?
3. Which canonical references will be stored?
4. How will the UI distinguish accepted state from Summary, Claim, Handoff, Evidence, and Legacy context?
5. What happens when mapping is ambiguous or missing?
6. How does restart rebuild the same Library view without granting Library authority?

Until these answers are concrete, do not add a new B1 scope kind, automatic Decision-to-Object mapper, Summary-to-State conversion, or reverse Library writer.

## 13. Decision

This record freezes the boundary for future design:

> **The Library is the user's primary browsing surface, but B1 remains the sole authority source. Library Objects and Timelines are explicit, rebuildable application projections with traceable references. Ambiguity stays at Project level instead of being guessed.**
