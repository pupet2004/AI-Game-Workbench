# Agent Native Surface Spike: WebView + Fake Relay

This is a throwaway implementation spike. It is intentionally outside the production solution and does not reference `Workbench.App`.

## What it proves

1. Native DOM text selection across transcript messages.
2. Streaming assistant deltas.
3. Approval request/response correlation.
4. Interrupt and steer commands.
5. Host-issued attachment tokens; no browser path is sent to the Host.
6. Transcript reload from Host-owned state.
7. Immutable Assignment/Attempt/session metadata projection.
8. Final result becoming a non-authoritative Handoff.
9. Page reload continuity while the Host process remains alive.

## Run

From the repository root:

```powershell
dotnet run --project .\spikes\agent-native-surface-fake-relay\Host\AgentNativeSurfaceSpike.csproj
```

The host uses Microsoft WebView2. Windows 11 normally has the WebView2 runtime installed; if the runtime is unavailable, the host reports the initialization error and the browser surface cannot start.

Opening `Web\index.html` directly in Chrome/Edge is only a standalone DOM preview. It intentionally shows sample messages for selection testing, but it has no Host bridge and therefore cannot exercise Relay commands.

## Protocol boundary

The browser sends only `ready` and user-facing `command` envelopes. The Fake Relay owns transcript, session identity, Assignment/Attempt metadata, approval correlation, and Handoff creation. The browser never receives a project file path or an authority-writing command.

The default mode deliberately does not use real Codex, `assistant-ui`, `agent-ui`, the production Avalonia app, SQLite, or provider credentials. A separate `--real-codex` mode now exercises the existing C# `CodexAgentRuntime` through the same Relay surface, still without touching the production Leader Pane.

## Real Codex mode

```powershell
dotnet run --project .\spikes\agent-native-surface-fake-relay\Host\AgentNativeSurfaceSpike.csproj -- --real-codex
```

Headless real-session smoke test (no WebView window):

```powershell
dotnet run --project .\spikes\agent-native-surface-fake-relay\Host\AgentNativeSurfaceSpike.csproj -- --real-codex-smoke
```

The Real Relay starts the local Codex App Server through the existing Workbench runtime, creates one read-only session, and maps provider-neutral events to the browser. It reports `steer: false` because the current `IAgentRuntime`/Codex adapter does not expose steer; the surface must not claim that capability until the runtime contract supports it.

Optional environment variables:

- `WORKBENCH_CODEX_EXECUTABLE` plus `WORKBENCH_CODEX_ENTRY` for the Node-based Codex launcher.
- `CODEX_CLI_PATH` for the standalone `codex.exe` launcher.
- `WORKBENCH_CODEX_CWD` for the session working directory.
- `WORKBENCH_CODEX_MODEL` to prefer a specific model id.
