# AI Game Workbench Alpha

## Theme

This Alpha release turns the continuity kernel into a usable Windows product:

```text
Install
  -> check environment
  -> authenticate a provider
  -> work
  -> review
  -> accept
  -> restart
  -> continue
```

## Included

- Windows x64 self-contained distribution package
- Per-user installer and uninstall script
- Overview, Work, Review, and first-use product shell
- Canonical Acceptance Spine
- Read-only environment readiness detection
- Provider-owned OpenCode authentication
- Codex and OpenCode execution paths
- DeepSeek live model validation through OpenCode
- Godot product-loop validation
- Process restart and accepted-state recovery

## Known limitations

- Windows x64 is the supported distribution target.
- Provider authentication is currently implemented for OpenCode.
- Free-text acceptance criteria may remain `NotVerifiable` when no machine
  evidence exists.
- Godot is the first project adapter; Unity is not certified.
- Upgrade, crash export, and clean-machine certification remain future release
  work.

## Evidence

See [`docs/validation`](../validation/) for the individual gates, including
the [Provider Authentication Gate](../validation/provider-authentication-gate-20260915.md).
