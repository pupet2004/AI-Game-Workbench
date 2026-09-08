# AI Game Workbench

> **The project persists. Agents don't have to.**

AI Game Workbench is a Windows-first reference implementation for long-running AI project continuity. A Project World keeps accepted state, decisions, handoffs, evidence, and routing history durable while Leaders, Workers, models, sessions, and runtimes remain replaceable.

```text
Codex / OpenCode / other Agents
              |
       Workbench Relay
              |
       Project World
       |      |       |
  Accepted  Decisions Handoffs
    State              |
                   Sources / Evidence
```

Current release track: **Alpha** (`v0.1.0-alpha.20260827`, historical release snapshot). The real WEIQI3 cross-Agent acceptance path has been exercised locally through a dedicated gated integration run; the default full-suite test count does not imply live-provider execution.

The canonical source is this repository. See [Canonical Source and Documentation Export Policy](docs/CANONICAL_SOURCE.md) and the [Current Working-Tree Validation](docs/validation/current-working-tree-20260904.md) record for the current local state.

## Documentation

- [Alpha whitepaper](docs/whitepaper/workbench-alpha-whitepaper.md)
- [Architecture](docs/architecture/workbench-alpha-architecture.md)
- [WEIQI3 demo script](docs/demo/weiqi3-cross-agent-demo.md)
- [Alpha known limitations](docs/alpha-known-limitations.md)
- [Release record](docs/releases/v0.1.0-alpha.20260827.md)
- [Current working-tree validation](docs/validation/current-working-tree-20260904.md)
- [Core continuity governance loop live verification](docs/validation/core-continuity-governance-loop-live-verified-20260908.md)
- [Repeatable core continuity demo](docs/demo/core-continuity-demo.md)

## Windows Alpha Package

Build the self-contained Windows package from a checkout:

```powershell
pwsh -NoLogo -NoProfile -File .\tools\publish-windows.ps1 -Version alpha
```

The resulting ZIP is in `artifacts\release`. Extract it and run `Workbench.App.exe`; no .NET runtime, Node.js, Codex, OpenCode, or other Agent installation is required for Manual mode. Enable an Agent only when needed in `Settings`, where an optional executable path can override the detected local installation.

## Local Native Surface

To run the development build with the Native Agent Surface enabled, run the helper from any PowerShell directory:

```powershell
& 'C:\Users\pupet\Documents\ChatGPT\AI Game Workbench\tools\run-native-surface.cmd'
```

This helper resolves the project path and sets `WORKBENCH_NATIVE_AGENT_SURFACE=1` automatically.
If Workbench is already open, close the existing window first so the development build can replace its DLLs.

M0 — Empty Office
Completed

M1 — Leader Lives
SEALED

Completed:
M1-01
M1-02
M1-02A
M1-03
M1-04
M1-05A
M1-05B
M1-05C

M1.5 — Project Memory

M1.5A — Memory Foundation
Completed

M1.5B — Session-Derived Memory Intelligence
Completed

## Current Working-Tree Milestone — 2026-09-04

The current canonical working tree extends the earlier Alpha baseline with:

- provider-neutral B1 Agent participation and an OpenCode participation path;
- B1 accepted-state and Project Library overview/category/time projections;
- Worker completion normalization, workspace baseline checks, and completion verification records;
- single-instance activation coordination and additional navigation/recovery coverage.

These changes are verified locally and sealed in the pinned Phase F.1 release-candidate commit. Fresh provider acceptance remains a separate next gate. See [Current Working-Tree Validation](docs/validation/current-working-tree-20260904.md) and the Phase F.1 reconciliation report for the exact test/build result and boundaries.
