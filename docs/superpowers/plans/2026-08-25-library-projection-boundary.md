# Library Projection Boundary — Implementation Plan

Status: **SEALED — Phase 7 completed**  
Date: **2026-08-25**

Governing design:

- [Library Projection Boundary Design](../specs/2026-08-25-library-projection-boundary-design.md)
- [R5-B1 Baseline](../../architecture/r5-b1-baseline.md)
- [Memory Continuity And Evolution Library](../specs/2026-08-14-memory-continuity-library-design.md)
- [Product Slice 1 — Manual Project World](../specs/2026-08-24-product-slice-1-manual-project-world-design.md)

## Objective

Make the existing Library the primary user browsing surface for accepted Project changes while preserving R5-B1 authority boundaries.

The work must connect:

```text
AuthorityDecision
  -> AcceptedProjectState
  -> explicit Library projection
  -> Library Object / Timeline
  -> user browsing and trace expansion
```

It must not create a new B1 authority scope, make Library a state writer, or infer B1 facts from Legacy prose, Summary, Handoff, or Claim display.

## Locked implementation rules

1. `LibraryObject` remains an application projection identity, not a B1 contribution scope.
2. A committed `AuthorityDecision` is never rolled back because a Library projection fails.
3. A Library projection never changes `AcceptedProjectState`.
4. Automatic Decision-to-Object guessing is prohibited.
5. Ambiguous or missing object association remains visible at Project Decision level.
6. Summary, Handoff, Claim, Evidence, and Legacy records remain labeled by their own kind.
7. Existing Legacy Library data remains readable and separately marked.
8. Library projections are rebuildable and may be repaired independently from B1 authority history.

## Phase 0 — Repository and contract audit — **COMPLETED**

Read-only audit before edits:

- locate current B1 projection/read models and authority decision queries;
- locate `ProjectLibraryEvolutionRepository`, proposal service, and Library UI entry points;
- identify the current Project Home / Project World navigation seam;
- identify existing material-reference types and continuity labels;
- document whether current Library rows are Legacy, evolution records, or both in each path.

Deliverable: a short implementation map in the task notes or plan update. No code changes.

## Phase 1 — Define the projection application contract — **COMPLETED**

Add a provider-neutral Application contract for an explicit Library projection. The contract should carry:

- Project identity;
- target Library Object or explicit create-object information;
- accepted Decision reference;
- optional source Claim, Handoff, Summary, Evidence, Git, or test references;
- current overview or timeline-node mutation;
- expected Object/Node revision when updating;
- an idempotency key or equivalent duplicate-submission guard.

Validation must reject:

- cross-Project references;
- missing Decision reference for content labeled accepted;
- ambiguous target identity;
- stale revisions;
- attempts to write B1 accepted state;
- Legacy references presented as B1 authority without an explicit current-time bridge.

The contract must support Project-level Decision display when no Object projection is safe.

## Phase 2 — Build read-side mapping from B1 to Library — **COMPLETED**

Implement a read-side mapper that can present:

- current accepted contributions;
- their Decision attribution and Project commit order;
- explicit Library Object/Timeline projections;
- unresolved Project-level Decisions with no Object mapping;
- expandable provenance references.

The mapper must not synthesize Library Objects from arbitrary contribution text. It may join only explicit persisted projection references or deterministic mappings already approved by the contract.

Add tests for:

- one Decision mapped to one Object;
- one Decision mapped to multiple Objects through explicit projections;
- one Project-level Decision with no Object;
- ambiguous mapping staying Project-level;
- superseded accepted contribution retaining historical trace;
- missing external evidence remaining an unavailable reference.

## Phase 3 — Connect explicit projection proposals — **COMPLETED**

Reuse the existing Library proposal and confirmation model where possible.

Implement the flow:

```text
Decision / accepted-state view
  -> explicit projection proposal
  -> user review
  -> Accept or Edit+Accept
  -> atomic Library transaction
```

The transaction may update Object, Overview, Timeline Node, and material references together. It must not create or modify an AuthorityDecision.

If the proposal is stale or invalid:

- keep it pending or report the conflict according to the existing proposal contract;
- write no partial Library state;
- leave AcceptedProjectState unchanged.

## Phase 4 — Make Library the primary browse surface — **COMPLETED**

Update the B1 Project World presentation so the normal user path is:

```text
Project Home
  -> Project World
  -> Library / accepted overview
  -> Object timeline
  -> optional trace expansion
```

The first view should prioritize:

- current Object overview;
- accepted changes over time;
- decision date and Decision reference;
- related materials only when expanded.

Keep Governance, Work, Decisions, and Legacy Context available, but do not make Handoff, Summary, Claim, or raw Session the default browsing surface.

## Phase 5 — Legacy coexistence and labeling — **COMPLETED**

Verify that:

- imported or existing Legacy Library entries remain visible;
- Legacy entries are marked as Legacy Context;
- browsing Legacy data creates no B1 effects;
- a current B1 Decision can reference Legacy evidence only through an explicit current-time action;
- B1 Library projections and Legacy Library records cannot be mistaken for one another.

Add regression tests around opening, browsing, and adopting Legacy projects.

## Phase 6 — Recovery and independence verification — **COMPLETED**

Verify the asymmetric recovery guarantee:

```text
AuthorityDecision history -> AcceptedProjectState
```

and independently:

```text
Library projection records -> Library browse view
```

Test that:

- deleting/rebuilding a materialized Library read cache does not alter B1 state;
- a failed Library write does not alter B1 state;
- restarting preserves Object IDs, Timeline IDs, Decision references, and ordering;
- missing external files or commits remain visible as unavailable references;
- B1 state can still recover when Library records are absent.

## Phase 7 — Acceptance and seal — **COMPLETED / SEALED**

Acceptance requires all of the following:

1. ✅ A user can open a B1 Project and browse current accepted Object state from Library.
2. ✅ A Decision without safe Object attribution remains visible at Project level.
3. ✅ An explicit proposal can create or update a Library Object/Timeline atomically.
4. ✅ Library content always shows its authority/provenance labels.
5. ✅ Library cannot write AcceptedProjectState.
6. ✅ Legacy Library remains readable and separate.
7. ✅ Restart produces the same B1 state and Library projection identities.
8. ✅ Existing certification suites remain green with no new warnings.

Implementation and verification are complete for this boundary. No further work is implied by this plan.

## Explicit non-goals for this plan

- no new B1 contribution scope;
- no automatic Summary-to-Library or Summary-to-State conversion;
- no automatic Decision-to-Object semantic classifier;
- no graph database, embeddings, RAG, or causal ledger;
- no Agent Gateway or provider integration;
- no installer/package work;
- no Legacy deletion or forced migration;
- no background summarization or cleanup scheduler.
