# Agent Native Surface Fake Relay Spike

Date: 2026-08-26  
Scope: throwaway implementation spike; no production UI or runtime changes.

## Result

The Host-owned Relay contract is viable in a standalone Windows WebView2 host. The browser surface is a projection: it sends user commands and renders events, while the Fake Relay retains session identity, Assignment/Attempt metadata, transcript, approval correlation, and Handoff governance.

The experiment lives under `spikes/agent-native-surface-fake-relay/` and is not part of `AI.Game.Workbench.sln`.

## Implemented checks

| Check | Result | Proof |
|---|---|---|
| DOM selection across messages | Manual UI check | Transcript uses normal DOM nodes with `user-select: text`; the surface includes a selection test and instructions to drag from adjacent blank space across messages. |
| Streaming | Passed | Fake Relay emits ordered `assistant_delta` events. |
| Approval round-trip | Passed | UI sends `requestId` and allow/deny; Relay correlates and emits `approval_resolved`. |
| Stop / interrupt | Passed | Capability is advertised and the command produces `turn_interrupted`. |
| Steer | Passed | Capability is advertised and the command produces `steer_acknowledged`. |
| Attachment token | Passed | UI sends file metadata only; Relay returns a host-issued `attachment-token-*`. |
| Transcript reload | Passed | Host keeps transcript state and re-sends it after a new `ready`. |
| Assignment / Attempt metadata | Passed | Metadata is initialized by Host and rendered read-only by the surface. |
| Result -> Handoff | Passed | Completion emits `handoff_created` with `authorityDecisionCreated: false`. |
| Kill/restart continuity | Protocol passed | A second page boot receives the same Host-owned session and transcript. A real process kill/restart test remains for the next pass. |

## Run

```powershell
dotnet run --project .\spikes\agent-native-surface-fake-relay\Host\AgentNativeSurfaceSpike.csproj
```

Headless protocol self-test:

```powershell
dotnet run --project .\spikes\agent-native-surface-fake-relay\Host\AgentNativeSurfaceSpike.csproj -- --self-test
```

The self-test passed for protocol, approval, streaming, attachment, steer, interrupt, Handoff, and restart continuity.

## Deliberate limitations

- No real Codex, `assistant-ui`, `agent-ui`, SQLite, project files, provider credentials, or production runtime is connected.
- The host uses WebView2 WinForms only to validate the browser/relay contract. It does not yet prove Avalonia control hosting.
- WebView2 runtime availability and actual user drag selection still require a manual Windows run.
- The Fake Relay is intentionally small and is not a production event schema.

## Decision

Continue to a **Real Codex Relay Spike** only after the manual WebView2 run confirms native selection and page reload behavior. Do not modify the production Leader Pane yet.

## Real Codex Relay pass

The same throwaway host now has an opt-in `--real-codex` mode. It reuses the existing C# `CodexAgentRuntime` as the sole Codex owner, creates one read-only session, and projects provider-neutral events into the browser surface. The browser never starts Codex directly.

Verified at compile time and by the existing runtime contracts:

- session creation and transcript reload use `IAgentRuntime`;
- streaming, approval, tool, error, completion, and interrupt events map through the Relay;
- Assignment/Attempt metadata remains Host-owned;
- completion emits a non-authoritative Handoff projection;
- steer is reported unavailable because the current Codex runtime contract does not expose it.

The local environment completed the Real Codex smoke test successfully on August 26, 2026. The smoke used the existing `CodexAgentRuntime`, injected the Workbench Leader Skill, completed a no-tool read-only turn, observed streaming and completion, and projected a non-authoritative Handoff. Production integration remains deferred until the interactive WebView2 checks (native drag selection, approval UI, attachment UX, and page reload) are completed manually.
