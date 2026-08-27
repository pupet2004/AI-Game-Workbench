# WEIQI3 Cross-Agent Demo

Demo target: show one Project continuing across two Agent runtimes.

## One-line Story

`Leader -> Worker -> Handoff -> Decision -> replace Agent -> continue Project`

## Preconditions

- Windows checkout of AI Game Workbench.
- Codex and OpenCode available locally.
- A disposable or explicitly chosen project directory.
- Do not treat the demo project folder as a release artifact; the demo must not rely on hidden chat history.

## Run

```powershell
$env:WORKBENCH_RUN_OPENCODE_LIVE = '1'
$env:WORKBENCH_RUN_CODEX_LIVE = '1'
$env:WORKBENCH_LIVE_PROJECT_PATH = 'C:\Users\pupet\Documents\ChatGPT\立围'
dotnet test .\tests\Workbench.App.Tests\Workbench.App.Tests.csproj --no-build --filter FullyQualifiedName~OpenCodeLiveB1AcceptanceTests
```

## Narration

1. Open the existing Project and show the current Project World.
2. Leader creates a bounded Worker Assignment.
3. OpenCode/DeepSeek performs the work and emits runtime activity.
4. Workbench records a Handoff; it is visible as returned work, not accepted truth.
5. Codex takes over the same Assignment/Attempt context.
6. User reviews the result and makes a Guided Decision.
7. Restart Workbench services.
8. Show that Accepted Project State and routing history are still present.

## Evidence To Capture

- Test output showing `1 passed` for the live acceptance test.
- Screenshot of the Agent picker showing both Codex and OpenCode.
- Screenshot of Handoff before Decision and Accepted State after Decision.
- Restart/reload view proving recovery.

## Success Condition

The audience should be able to answer yes to one question:

> Can a different Agent continue the same Project without replaying the original Session?

The demo is successful when the answer is visibly yes and no Agent output bypasses the Handoff/Decision authority path.
