# Product Loop Release Gate

Date: 2026-09-14

## Status

The Product Loop release gate is defined and passed on September 14, 2026.

```text
Godot fixture validation       passed
Real Codex desktop loop        passed
Acceptance rounds              3
Process restarts               3
Accepted statements            1 -> 2 -> 3
Full automated test suite      643 passed, 5 skipped, 0 failed
```

## Gate

Run from the repository root:

```powershell
pwsh -NoLogo -NoProfile -File .\tools\verify-product-loop-release-gate.ps1 `
  -GodotExecutablePath "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\GodotEngine.GodotEngine_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.7.2-stable_win64_console.exe"
```

The gate composes:

```text
validate-product-loop-demo.ps1
  -> verify-product-loop-ui-real-codex.ps1
```

The live verifier uses one disposable Godot project and one disposable
Workbench database. It performs:

```text
real Codex Worker
  -> Completion
  -> Evidence
  -> Claim
  -> Handoff
  -> UI Review
  -> Authority Preview
  -> explicit Accept
  -> force-stop
  -> process restart
  -> AcceptedProjectState recovery
```

## Kernel Freeze

The following are frozen contracts. Product-shell work must not add alternate
write paths or new domain concepts to them:

- Completion is a Worker execution fact.
- Evidence is proof, not Truth.
- Handoff carries context and does not transfer Authority.
- Authority Decision is the only acceptance decision.
- AcceptedProjectState is the authoritative project state.
- Summary is written after the Authority decision.
- Recovery reconstructs state from persisted records.

Kernel changes are allowed only for:

- bug fixes;
- invariant violations;
- recovery failures;
- certification regressions.

## Current Boundary

Certified:

- real Codex Desktop Worker execution;
- Godot file changes and headless validation;
- UI Review, Preview, and Accept;
- successor Assignment creation in the first two rounds;
- three process-level restart recoveries.

Still outside this gate:

- a live Leader session generating the next Task after restart;
- final product-shell information architecture;
- packaged installation and first-run experience;
- a general Godot adapter beyond the certification fixture.

