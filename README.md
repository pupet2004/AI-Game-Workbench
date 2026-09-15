# AI Game Workbench

> **AI sessions end. Projects should not forget.**

AI Game Workbench is a Windows-first workspace for long-running AI projects.
Agents can change, sessions can end, and providers can be replaced without
losing track of what the project has actually accepted.

```text
Agent work
    -> completion and evidence
    -> review
    -> authority decision
    -> accepted project state
    -> continuity for the next session
```

Workbench is not another chat window and it is not an automatic truth engine.
It gives project changes a visible review boundary: Agent work can propose a
change, but only an explicit acceptance decision changes the project's
accepted state.

## Download

The current build is an **Alpha release candidate for Windows x64**.

- [Windows Alpha package](https://github.com/pupet2004/AI-Game-Workbench/releases)
- [Validation evidence](docs/validation/)
- [Known limitations](docs/alpha-known-limitations.md)

The package is self-contained. Extract it and run `Workbench.App.exe`, or run
`install-windows.ps1` for a per-user installation with Start menu and desktop
shortcuts. Uninstalling removes the application but preserves project data in
`%LOCALAPPDATA%\AI Game Workbench`.

## The product loop

1. Open a project.
2. Workbench checks the project, Git, engine, local data, and Agent provider.
3. A Leader or Worker performs bounded work.
4. Workbench records completion, changed files, and available evidence.
5. You review the proposed change.
6. Accept, request revision, or reject.
7. Only acceptance updates the accepted project state.
8. A later session resumes from that state, not from a stale transcript.

## What is certified

- Canonical Acceptance Spine
- Product Loop with real Codex and OpenCode
- DeepSeek model execution through OpenCode
- Godot project changes
- Overview, Work, Review, and first-use UI
- Reject and revision retention
- Normal exit and process restart recovery
- Verification scope classification
- Per-user Windows distribution
- Read-only environment readiness detection
- Provider-owned OpenCode authentication

The latest validation records are in [`docs/validation`](docs/validation/).
The default test suite deliberately skips live-provider tests unless their
explicit environment switch is enabled.

## Providers and editors

Workbench currently exercises Codex and OpenCode as replaceable execution
providers. Godot is the first tested project adapter. Provider credentials
remain owned by the provider; Workbench only starts the provider's login flow
and probes the runtime again afterward.

## Documentation

- [Why Workbench](docs/why-workbench.md)
- [Alpha whitepaper](docs/whitepaper/workbench-alpha-whitepaper.md)
- [Canonical Acceptance Spine](docs/architecture/canonical-acceptance-spine.md)
- [Alpha architecture](docs/architecture/workbench-alpha-architecture.md)
- [Known limitations](docs/alpha-known-limitations.md)
- [Validation records](docs/validation/)
- [Release notes](docs/releases/alpha-product-loop-20260915.md)

## Build locally

```powershell
dotnet test --no-restore
pwsh -NoLogo -NoProfile -File .\tools\publish-windows.ps1 -Version alpha
```

The publish script writes the self-contained ZIP to `artifacts\release`.

## License

See [LICENSE](LICENSE).
