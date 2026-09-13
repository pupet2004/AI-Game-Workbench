# Real Provider Acceptance

Date: 2026-09-13

## Result

**PASSED: local Provider lifecycle and cross-Provider Acceptance Spine are
certified.**

The machine-local providers used for this run were:

- Codex CLI `0.154.0`, standalone `codex.exe app-server --stdio`
- OpenCode `1.17.7`, local `opencode.cmd`

No user project was used. The OpenCode acceptance test created a disposable
temporary project.

## Live Evidence

| Path | Result |
|---|---|
| Codex app-server lifecycle | 1 passed |
| Codex Leader UI, two turns in one session | 1 passed |
| OpenCode/DeepSeek -> Codex continuation -> Guided Decision -> restart | 1 passed |

The cross-Provider path proved:

```text
OpenCode execution
      -> Attempt / SessionBinding
      -> Handoff
      -> Codex continuation
      -> Guided Decision
      -> AcceptedProjectState
      -> restart projection
```

## Test Gate

Live tests now use a conditional `LiveFact` attribute. They remain skipped in
the deterministic suite unless their explicit environment variable is set, but
they execute normally when enabled. This avoids both false passes and a
permanent attribute-level skip that could never be overridden.

Codex tests support both forms:

```text
codex.exe app-server --stdio
node codex.js app-server --stdio
```

## Commands

```powershell
$env:WORKBENCH_RUN_CODEX_INTEGRATION = '1'
$env:WORKBENCH_CODEX_EXECUTABLE = "$env:LOCALAPPDATA\Programs\OpenAI\Codex\bin\codex.exe"
$env:WORKBENCH_CODEX_CWD = (Get-Location).Path
dotnet test .\tests\Workbench.Runtime.Tests\Workbench.Runtime.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~CodexAppServerIntegrationTests"
```

```powershell
$env:WORKBENCH_RUN_OPENCODE_LIVE = '1'
$env:WORKBENCH_RUN_CODEX_LIVE = '1'
dotnet test .\tests\Workbench.App.Tests\Workbench.App.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~OpenCodeLiveB1AcceptanceTests"
```

## Boundary

This certifies the real Provider execution boundary and its integration with
the Acceptance Spine. It does not yet certify a full first-time-user UI
walkthrough or a real game-engine project artifact.
