# Agent Native Surface Spike

Date: 2026-08-26
Scope: Codex only. No UI refactor.

## Result

The current Workbench can relay a Codex App Server session, but it cannot embed or reuse the Codex Desktop interaction surface from this repository.

## Five checks

1. **Mature Codex surface in Leader** — Not proven. The repository has an `IAgentRuntime` protocol adapter, not an embeddable Codex Desktop control or host API.
2. **Input, copy/select, image, steer, stop** — Text input, stop, streaming events, and approval UI exist. Image drop and steer are not represented by `IAgentRuntime`; transcript selection is still a local UI concern.
3. **Workbench observes structured events** — Proven for the existing adapter. `CodexAgentRuntime.SendAsync` emits provider-neutral `AgentEvent` values and `LeaderPaneViewModel` consumes them.
4. **Turn belongs to Assignment / Attempt** — Proven only in the separate B1 participation path. `B1AgentParticipationAdapter` creates/selects an `Attempt`, binds the external session, and records a Handoff. The current Leader pane path does not carry those refs.
5. **Result reaches Claim / Handoff / Authority** — Not proven for the current Leader path. B1 participation records a non-authoritative Handoff; the Leader pane currently persists legacy Leader messages and Summary metadata, with no automatic Claim or AuthorityDecision.

## Decision

Do not replace the Leader UI yet. The next spike, if pursued, should be a narrow relay contract around an existing `AgentSession` plus `AssignmentRef`/`AttemptRef`, and should explicitly test steer and attachment events before any surface embedding work.
