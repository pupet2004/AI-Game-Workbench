# AI Game Workbench Alpha

**The project persists. Agents don't have to.**

Version `alpha-product-loop`
Updated 2026-09-15

> This paper describes the current Alpha product direction. Validation claims
> are backed by the dated records in [`docs/validation`](../validation/).

## Abstract

AI work often outlives the model, provider, session, and interface that started it. AI Game Workbench explores a small answer to that problem: make the Project World durable, make Agent participation bounded, and keep accepted meaning separate from conversation output.

Workbench is not the intelligence of a project. It is the continuity substrate around replaceable intelligence.

## Thesis

The central design claim is simple:

> **A project should not have to restart merely because its agent changed.**

The system therefore treats Project, Assignment, Attempt, Claim, Handoff, Authority Decision, and Accepted Project State as durable concepts. Sessions, models, providers, and runtimes are connectivity and execution details.

## Semantic Separation

Workbench keeps three lanes distinct:

```text
Authority  -> what the project formally accepts
Context    -> what helps a participant understand the project
Execution  -> what agents and tools are doing now
```

Claims and Handoffs can propose useful work. Only an attributable Authority Decision changes Accepted Project State. Summary and Library views reduce re-comprehension cost but never gain authority through repetition or compression.

## Replaceable Participation

A bounded Assignment may be performed by a Worker using one runtime and continued by another Agent later. The runtime adapter translates provider events into provider-neutral Workbench events. The host preserves Assignment, Attempt, SessionBinding, transcript recovery, and approval correlation.

The user can therefore follow a path such as:

```text
Leader understands the Project
  -> Worker performs a bounded Assignment
  -> Handoff returns Claims and evidence
  -> user or Leader makes an Authority Decision
  -> another Agent continues the Project
```

## Evidence From Alpha

The current Alpha has completed the product loop from installation and
environment readiness through provider-owned authentication, Agent work,
review, acceptance, and process restart recovery. It has been exercised with
real Codex, OpenCode, DeepSeek, and Godot workflows as well as deterministic
certification fixtures.

Earlier, on 2026-08-27, the real `立围` project completed a local cross-Agent acceptance run:

1. OpenCode with DeepSeek completed a bounded Worker assignment.
2. Workbench recorded a non-authoritative Handoff.
3. Codex continued the same Assignment from the prior Attempt.
4. A Guided Decision accepted the final result.
5. Workbench services restarted and recovered the accepted state.

The project files were not modified by the acceptance test; the existing `README.md` change was pre-existing.

## What This Alpha Is

- A working Windows-first continuity reference implementation.
- A replaceable Agent routing and relay experiment.
- A concrete demonstration that a Project World can outlive an Agent session.
- A foundation for further dogfooding and independent review.

## What This Alpha Is Not

- A promise of legal, organizational, or regulatory accountability.
- A full IDE, terminal, Git client, or browser automation suite.
- A universal drop-in replacement for Codex, OpenCode, Claude, or Kilo UI.
- An automatic truth, verification, or causal-inference engine.

## Closing

Workbench's intended unit of persistence is the Project, not the chat window. A Session may end. A model may change. The Project continues.
