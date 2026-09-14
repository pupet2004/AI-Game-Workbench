# Godot Product Loop Live Validation

Date: 2026-09-14

## Result

**Passed: real Codex changed a real Godot project and the change completed the
canonical acceptance path.**

The gate used:

- Codex app-server from the local Codex installation.
- Godot Engine 4.7.2 console executable.
- A disposable Git copy of `demos/product-loop-godot-counter`.
- A disposable Workbench SQLite database.

## Certified Path

```text
Godot project baseline
  -> real Codex Worker
  -> scripts/main.gd changed from CLICK_INCREMENT 1 to 2
  -> Godot headless validation
  -> Completion Package
  -> Verification Evidence
  -> Claim / Handoff
  -> explicit Accept
  -> AcceptedProjectState
  -> database reopen
  -> accepted contribution recovered
```

The live test is:

```text
tests/Workbench.App.Tests/Continuity/LiveCodexGodotProductLoopTests.cs
```

It is enabled with:

```powershell
$env:WORKBENCH_RUN_CODEX_GODOT_PRODUCT_LOOP_LIVE = '1'
$env:WORKBENCH_GODOT_EXECUTABLE = '<path-to-Godot-console-executable>'
$env:WORKBENCH_CODEX_EXECUTABLE = '<path-to-codex.exe>'
dotnet test .\tests\Workbench.App.Tests\Workbench.App.Tests.csproj `
  --no-build --no-restore -m:1 `
  --filter "FullyQualifiedName~LiveCodexGodotProductLoopTests"
```

Observed result:

```text
1 passed, 0 failed, 0 skipped
```

## Boundary

This record proves the real Provider and real Godot artifact boundary at the
service level. The human-scale desktop walkthrough is certified separately in
`product-loop-ui-real-codex-20260914.md`.
