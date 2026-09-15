# AI Game Workbench Alpha

Version: `alpha-product-loop`  
Updated: 2026-09-15

## Abstract

AI projects outlive individual models, providers, sessions, and interfaces.
AI Game Workbench is a Windows-first continuity and acceptance layer for that
reality. It keeps project state durable, Agent work bounded, and accepted
meaning separate from conversation output.

The central claim is:

> **A project should not have to restart merely because its Agent changed.**

## 1. Problem

Chat transcripts preserve conversation, not project truth. Memory and summaries
reduce context loss, but repetition does not grant authority. Git records
artifact history, but does not by itself record which proposed meaning a user
accepted. Handoffs carry context without transferring authority.

Long-running AI work therefore needs a project-level layer that answers:

> What has this project formally accepted, and what should the next session
> treat as its starting point?

## 2. Design Principles

Workbench separates three lanes:

```text
Authority  -> what the project formally accepts
Context    -> what helps a participant understand the project
Execution  -> what Agents and tools are doing now
```

The key boundaries are:

```text
Completion      != Acceptance
Evidence        != Truth
Handoff         != Authority transfer
Summary         != Authority
```

Only an attributable Authority Decision changes Accepted Project State.

## 3. Project World Model

The durable Project World contains the identity, responsibilities,
assignments, attempts, claims, evidence, decisions, accepted state, summaries,
and recovery information needed to continue work.

Sessions, models, providers, runtimes, and editor windows are replaceable
participants or connections. They may produce useful output, but they do not
become the project's formal state merely by producing it.

## 4. Canonical Acceptance Spine

The canonical path is:

```text
Task / Assignment
    -> Worker execution
    -> Completion package
    -> diff, evidence, and verification
    -> Claim / proposed change
    -> Authority queue
    -> Accept, Reject, or Revise
    -> Accepted Project State
    -> Summary and next-session continuity
```

Completion records remain durable when a proposal is rejected or revised.
Projection services summarize authority history; they do not make decisions.

## 5. Continuity and Recovery

After an accepted decision, a later Leader or Worker reads the accepted state
and continues from it. Normal process exit, service/database reopen, and
runtime recovery are treated as separate concerns. Recovery restores durable
state without promoting incomplete execution into accepted truth.

## 6. Product Implementation

The Alpha presents the kernel through a small product shell:

- **Overview** answers what the project is now.
- **Work** answers what the Agent is doing.
- **Review** answers what change awaits a decision.
- **World** exposes accepted project facts.
- **History** explains how the current state was reached.
- **Settings** controls providers and runtime configuration.

Codex and OpenCode are replaceable execution providers. Godot is the first
tested vertical adapter. Provider credentials remain provider-owned; Workbench
starts the provider's authentication flow and probes the runtime afterward.

## 7. Evaluation and Certification

The current Alpha has been exercised with:

- deterministic acceptance fixtures;
- real Codex and OpenCode participation;
- DeepSeek model execution through OpenCode;
- Godot project changes;
- first-use UI without seeded Workbench state;
- Review Accept, Reject, and Revision paths;
- normal process exit and restart recovery;
- verification scope classification;
- provider-owned authentication.

The release-candidate application test run covered the `Workbench.App` suite:
`700 passed, 5 skipped, 0 failed`. Live-provider tests are opt-in. The dated
records in [`docs/validation`](../validation/) state the conditions and
boundaries of each result.

## 8. Limitations

This Alpha is Windows x64 focused. Godot is the first certified project
adapter; Unity is not certified. Free-text acceptance criteria may remain
`NotVerifiable` when no machine evidence exists. Upgrade migration, automatic
updates, crash export, and clean-machine certification are outside this
release.

Workbench is not a full IDE, terminal, Git client, browser automation suite,
or automatic truth and causal-inference engine.

## 9. Related Systems

Workbench complements, rather than replaces, chat interfaces, Agent runtimes,
editors, Git, and project-specific tools. Those systems handle conversation,
execution, editing, artifact history, or domain work. Workbench supplies the
project-level acceptance and continuity layer that connects their outputs over
time.

## 10. Conclusion

The intended unit of persistence is the Project, not the chat window. An Agent
may finish, a provider may change, and a process may restart. The next
participant can still begin from the same accepted project truth.
