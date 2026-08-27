# Agent UI Surface Library Spike

Date: 2026-08-26  
Scope: research only; no production UI or runtime changes.

## Executive conclusion

The direction is technically viable, but the two candidates serve different layers:

- **assistant-ui** is the stronger long-term React surface. It is MIT, composable, supports custom runtimes, streaming, attachments, Markdown/code rendering, tool UI, approvals, selection, and queue/steer primitives.
- **nyosegawa/agent-ui** is the stronger Codex App Server reference implementation. Its packages are MIT and split into React UI, protocol-neutral core, and a Codex transport/normalizer. The Codex package exposes thread/turn clients including `turn/start`, `turn/interrupt`, `turn/steer`, approvals, transcript-oriented item normalization, skills, resources, and stdio/WebSocket transports.

**Recommendation:** use `assistant-ui` as the eventual visual foundation, borrow the Codex protocol mapping ideas from `agent-ui`, and place a Workbench-owned relay between the Avalonia host and the browser surface. Do not replace the current Leader pane until the relay contract is proven.

## Workbench Skill boundary

`workbench-leader` and `workbench-worker` are Workbench product capabilities, not project assets.
They describe what an agent is inside Workbench and what authority boundary applies to its role;
they do not contain project facts, project workflow, or provider-specific operating procedures.

The canonical sources live at the repository-level `skills/` directory. `Workbench.App` packages
these files as application content, and `WorkbenchSkillCatalog` loads them when building Leader,
Worker, and continuation prompts. A future browser surface must preserve this host-owned injection
boundary: the surface may render or edit user-facing conversation, but it must not decide which
Workbench Skill applies or replace the host's role/Assignment/Attempt metadata.

This also means the eventual relay contract should carry role metadata separately from project
context. The Skill establishes the participant's Workbench identity; the Project World establishes
the project-specific facts and accepted state.

## Repository and host findings

The current desktop app is **Avalonia**, not WPF. It currently has no WebView package or browser-based frontend. The existing Leader UI is XAML in `src/Workbench.App/Views/Panes/LeaderPaneView.axaml`; the current provider abstraction is `IAgentRuntime` and the Codex implementation is `CodexAgentRuntime`.

Avalonia's current WebView documentation provides `NativeWebView`, JavaScript execution through `InvokeScript`, and a bidirectional `WebMessageReceived` bridge. On Windows it uses WebView2; WebView2 is preinstalled on Windows 11, while Windows 10 may require the runtime to be installed or bundled. This is enough for a local embedded React surface, subject to adding and validating the WebView package in a separate implementation spike.

## Candidate assessment

### assistant-ui

Strengths:

- MIT license.
- React primitives rather than one monolithic application.
- Custom backend/runtime support, including external-store and transport hooks.
- Existing UX primitives for streaming, auto-scroll, Markdown, code highlighting, attachments, keyboard interaction, accessibility, tool calls, inline human approval, selection actions, and queue-item steer.
- Provider-neutral enough to support Codex first and OpenCode/Claude later if Workbench defines a common event model.

Gaps for Workbench:

- No native Avalonia host integration.
- No direct Codex App Server ownership in the base package.
- Workbench must implement the runtime/transport adapter, lifecycle, persistence observation, and governance metadata.
- A UI primitive named “steer” does not by itself guarantee provider support; the relay must expose a real steer operation or mark it unavailable.

### nyosegawa/agent-ui

Strengths:

- MIT license.
- Explicit local-first Codex App Server focus.
- Host-owned routing, persistence, credentials, and process lifecycle boundary.
- Protocol-neutral core state plus a Codex-specific transport/normalizer.
- Directly models richer transcript items such as reasoning, plans, command execution, file changes, tool calls, images, web search, sub-agent activity, usage, and approval/server-request state.
- Codex clients explicitly expose thread start/resume, turn start, interrupt, steer, approval responses, skills, and resources.

Gaps and risks:

- Smaller and more Codex-specific than assistant-ui.
- Its React packages are not a drop-in Avalonia control.
- The browser-side Codex transport would duplicate or compete with the existing C# `CodexAgentRuntime` unless one side becomes the sole owner.
- The package's normalized model is richer than the current Workbench `AgentEvent` contract, so an adapter or contract expansion is required.

## Capability matrix

| Capability | assistant-ui | agent-ui | Workbench status | Spike implication |
|---|---|---|---|---|
| User input | UI/runtime primitives | React composer + host controllers | Existing | Relay maps submit to one host turn |
| Browser text selection | Native DOM | Native DOM | Current XAML is limited | Strong reason to move transcript surface |
| Streaming | Supported by runtime adapters | Codex event normalizer | `AgentTextDelta` exists | Preserve ordered event sequence |
| Markdown/code | Supported through message parts/components | Bundled Markdown/transcript styles | Current UI displays plain selectable text | UI replacement benefit is high |
| Attachments/images | Attachment primitives/adapters | Codex input/resource types | Not in `IAgentRuntime` | Requires host-owned file token contract |
| Tool/command rendering | Generative/tool UI | Rich Codex item kinds | Only coarse `AgentToolEvent` | Need richer non-authoritative activity events |
| Approval | Inline approval/tool patterns | Codex server-request/approval state | Existing approval response path | Relay must preserve request correlation |
| Stop/interrupt | Runtime-dependent cancel/stop | Explicit `turn/interrupt` | Existing `StopAsync` | Map to current session/turn identity |
| Steer | Queue/steer primitive | Explicit `turn/steer` | No generic API | Add capability-gated relay operation |
| Transcript | External/history adapters | Transcript-first normalized state | `GetTranscriptAsync` exists | Host remains persistence observer |
| Assignment/Attempt identity | Host-defined | Host-defined | B1 path has it; Leader path does not | Relay envelope must carry both refs |
| Claim/Handoff/Authority | Host-defined | Host-owned boundary | Existing B1 path, not current Leader pane | UI must never commit authority directly |
| OpenCode/Claude future reuse | Good with common adapter | Mostly Codex-specific | Provider-neutral runtime exists | assistant-ui should own the visual contract |

## Proposed Workbench relay

The browser surface should never connect directly to SQLite, project files, or provider credentials. The Avalonia host remains the authority and lifecycle owner.

Suggested host-to-surface contract:

```text
SurfaceSession
  project_id
  role = leader | worker
  assignment_id?
  attempt_id?
  agent_session_id
  provider
  capabilities: streaming, approval, interrupt, steer, attachments, transcript

SurfaceCommand
  send_turn(text, attachments[])
  interrupt(turn_id)
  steer(turn_id, text)
  respond_approval(request_id, option_id)
  load_transcript(cursor?)

SurfaceEvent
  sequence
  project_id
  assignment_id?
  attempt_id?
  agent_session_id
  turn_id?
  event_type
  payload
  occurred_at
```

The relay should fan out the same provider-neutral events that Workbench observes for persistence, status, and audit. UI rendering is a projection; it must not become a second authority path.

## Mapping from the current code

- `CodexAgentRuntime.SendAsync` already owns Codex App Server request/notification correlation and emits provider-neutral events.
- `IAgentRuntime` already exposes send, approval response, stop, status, and transcript operations.
- `B1AgentParticipationAdapter` already demonstrates the desired Assignment -> Attempt -> SessionBinding -> Handoff chain, but the ordinary Leader pane does not carry those refs.
- `LeaderPaneViewModel` currently owns presentation, persistence, structured-response parsing, Summary admission, approval presentation, and stop. A browser surface should receive presentation/activity events while these governance decisions stay in the host.
- `WorkerSessionRouter` already has explicit task/session routing and handoff persistence, which is a useful relay test fixture.

## Replacement scope if the relay succeeds

The first replacement should be narrow:

1. Replace the Leader transcript and composer area in `LeaderPaneView.axaml` with a hosted web surface.
2. Keep Workbench-native project header, model/account selection, rotation state, draft confirmation, approval governance status, and Project World panels outside the surface until equivalent browser behavior is proven.
3. Adapt the existing C# runtime rather than introducing a second Codex process.
4. Add a relay event log/observer without allowing the surface to write Accepted Project State.

The current transcript text-selection issue should remain deferred until this experiment is complete.

## Main risks

- **Two owners for Codex:** do not let a Node/browser Codex transport and the C# runtime both control one session.
- **Event loss or reordering:** include monotonic sequence numbers and replay/load semantics.
- **Approval mismatch:** preserve provider request IDs and Workbench approval IDs separately, with an explicit correlation map.
- **Security boundary:** restrict navigation, use a local origin/token, validate every WebMessage, and expose file access only through explicit host commands.
- **Attachment lifetime:** browser `File` objects are not durable project evidence; the host must assign scoped temporary/material references.
- **Authority leakage:** tool results, transcript text, and UI actions must remain Claims/Handoffs/activity until Workbench governance accepts anything.
- **Packaging:** local static web assets, WebView2 runtime availability, cache invalidation, crash recovery, and offline behavior need an installer-level test.

## Decision

Proceed to a second, implementation-only spike with a throwaway WebView host and a fake relay before touching the production Leader pane. The minimum proof should cover:

1. DOM selection across messages.
2. Streaming event rendering.
3. Approval round-trip.
4. Stop and steer capability reporting.
5. Attachment token round-trip.
6. Transcript reload.
7. Assignment/Attempt metadata observation.
8. A final result reaching the existing non-authoritative Handoff path without bypassing Authority.

If this proof passes, stop investing in the current transcript renderer and replace only that surface. If it fails, the relay/event contract still becomes useful for a future native transcript implementation.
