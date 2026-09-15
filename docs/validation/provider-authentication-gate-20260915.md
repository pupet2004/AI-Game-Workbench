# Provider Authentication Gate

Date: 2026-09-15
Status: Passed

## Scope

This gate verifies the provider-owned authentication path without moving
credentials into Workbench:

```text
OpenCode installed
  -> user starts provider login
  -> OpenCode stores its own credential
  -> Workbench reconnects and probes
  -> deepseek/deepseek-v4-flash is usable
```

## Evidence

- OpenCode login was started by the user from the Workbench authentication action.
- The provider's interactive flow completed with `Done`.
- `opencode providers list` reported one OpenCode Zen credential.
- Workbench's OpenCode runtime connected successfully.
- The live OpenCode B1 probe completed successfully with
  `deepseek/deepseek-v4-flash`.
- The live probe used a prompt that forbids tools and file changes.

## Boundary checks

- Workbench does not read or persist the provider credential.
- A successful login process is followed by a runtime re-probe.
- Authentication failure does not set the UI to Ready.
- Unsupported providers do not start a process.
- The OpenCode command is provider-owned: `opencode providers login`.

## Result

```text
ProviderCredentialPresent = Passed
RuntimeReady              = Passed
TargetModelUsable         = Passed
ProviderAuthenticationGate = Passed
```
