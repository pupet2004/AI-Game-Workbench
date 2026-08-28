# Theoretical Foundations of the Continuity Harness

**Status:** Working theory note for the Architecture Preview  
**Date:** 2026-08-28  
**Scope:** This note clarifies the design lineage and boundaries of Workbench. It does not claim that Workbench has empirically validated the benefits described here.

## 1. Position

Workbench is best understood as a **continuity harness for long-running agentic projects**.

The term *harness* refers to the surrounding structure that makes agent work inspectable, repeatable, and recoverable: role separation, task routing, context selection, verification, and feedback. Workbench applies that idea at the project level. It treats the project as the durable subject while agents, models, sessions, and tools remain replaceable participants.

This is a framing choice, not a claim that Workbench originated harness engineering. The relevant mechanisms have important precedents in agent orchestration, context engineering, progressive disclosure, compaction, and hierarchical memory. Workbench's proposed contribution is their combination around a governed project state.

## 2. Three Dimensions of Continuity

Workbench treats continuity as three related but distinct dimensions:

### Execution continuity

Work should be able to continue when a participant or session ends.

`Assignment -> Attempt -> Result -> Handoff -> Next participant`

Leader, Worker, and Review roles are an instance of role-separated execution. A Handoff is a task-local continuation checkpoint. It records what was attempted, what was produced, what remains uncertain, and what the next participant should inspect or do.

### Semantic continuity

The project should remain understandable without replaying every prior interaction.

`Library / Accepted context -> Summary -> Session / Source`

This is a progressive-disclosure relationship, not a truth hierarchy. Higher layers are intended to be denser and easier to scan; lower layers preserve more detail and provenance. The appropriate stopping point depends on the task.

### Authority continuity

The project should retain a stable answer to the question: what has the project formally accepted as true?

`Claim -> Authority Decision -> Accepted Project State`

This is the part that should not be collapsed into memory. Context can help a participant understand a project, but only an authority decision can change the accepted project state.

## 3. Two Orthogonal Axes

The semantic and authority structures are independent:

| Axis | Main question | Workbench structures |
| --- | --- | --- |
| Semantic continuity | What information is sufficient for this task? | Library, Summary, Handoff, Session, Source |
| Authority continuity | What is formally accepted as project reality? | Claim, Decision, Accepted Project State |

A Library entry may be more compact than its source without being more authoritative. A Handoff may be more useful for resuming a task without becoming project truth. Keeping these axes separate is a central design constraint.

## 4. Minimum Sufficient Context

Workbench uses **Minimum Sufficient Context** as its context-selection principle:

> For a given task, provide the smallest context set that preserves sufficient decision-relevant information for the next action.

This is an optimization target and a reading protocol, not a measured token-saving result. It allows a participant to begin with a dense project view and descend into decisions, Handoffs, Sessions, or source artifacts only when the current task requires more fidelity.

The principle does not imply that less context is always better. Removing information can create ambiguity, hide uncertainty, or erase a relevant exception. Sufficiency must therefore be evaluated against the task and the required level of confidence.

## 5. Handoff and Summary Semantics

### Handoff: task-local continuation checkpoint

A Handoff is not a copy of conversation history and not an authority record. It is a structured execution artifact that may contain:

- the attempted task and its scope;
- completed and incomplete work;
- results and supporting evidence;
- pending or proposed Claims;
- unresolved questions and risks;
- references to relevant Accepted Project State elements;
- suggested next actions; and
- links back to the originating Session or source artifacts.

The appropriate size of a Handoff depends on the task. No fixed compression ratio is assumed.

### Summary: decision-oriented semantic compaction

A Summary is not intended to describe every event in a Session. It should preserve the information that a future participant is likely to need: decisions, constraints, rejected alternatives, important failures, open questions, and the reasons for significant changes.

Summaries remain derived context. They should cite their sources and must not silently acquire authority by being shorter or easier to read.

## 6. Relation to Prior Work

Workbench draws on several existing lines of work:

- **Agentic harness engineering:** role separation, orchestration, verification, feedback, and enforceable boundaries.
- **Context engineering and progressive disclosure:** treating context as finite, selecting high-signal information, and retrieving detail when needed.
- **Compaction and handoff control:** reducing or filtering prior history at participant boundaries.
- **Hierarchical memory:** organizing information across levels of abstraction and retrieving at the level appropriate to a task.

These precedents support the plausibility of the individual mechanisms. They do not by themselves establish the Workbench architecture or its long-term benefits.

Workbench's specific architectural emphasis is the integration of these mechanisms with **Authority Continuity**: contextual memory helps participants work, while Accepted Project State defines what the project formally accepts.

## 7. Engineering Principle: Store the Delta

`Store only the delta` is a Workbench engineering principle, not a claim of direct authorship by any prior system. It follows from a practical combination of structured progress records, decision logs, compaction, and progressive disclosure:

- preserve durable changes and their reasons;
- keep full-fidelity sources available for inspection;
- avoid treating repeated restatements as new authority; and
- let future participants retrieve detail when the delta is insufficient.

The principle may be revised if real projects show that a different retention strategy produces better recovery or fidelity.

## 8. Claims and Future Tests

The following are hypotheses for future validation, not current findings:

1. `Handoff + Accepted Project State` may allow a new participant to resume with less historical replay than a cold-start session.
2. Authority-separated context may reduce accidental decision drift.
3. Decision-oriented compaction may preserve more decision-relevant information than generic conversational summarization.
4. Role-separated execution may improve reviewability and recovery even when raw task speed is unchanged.
5. Continuity mechanisms may become more valuable as project duration, participant turnover, and accumulated decisions increase.

These hypotheses require controlled comparisons, real project use, and longitudinal evidence.

## 9. Boundary

Workbench does not claim to have invented agent harnesses, hierarchical memory, context engineering, or compaction. It also does not claim that Accepted Project State solves project correctness, agent alignment, or organizational governance.

The narrower claim is architectural: a long-running project can be represented with separate execution, semantic-context, and authority structures, and those structures can be connected without allowing contextual artifacts to silently become project truth.

## Selected Reading

- OpenAI, *Harness engineering: leveraging Codex in an agent-first world*.
- Anthropic, *Effective context engineering for AI agents*.
- OpenAI Agents SDK, *Handoffs*.
- Packer et al., *MemGPT: Towards LLMs as Operating Systems*.
- Sarthi et al., *RAPTOR: Recursive Abstractive Processing for Tree-Organized Retrieval*.
- H-MEM and HiGMem, recent work on hierarchical memory for long-term agent reasoning.

The selected reading list is a map of adjacent ideas, not a claim of exhaustive coverage or direct equivalence.
