# Alpha Release Candidate Smoke

Date: 2026-09-15
Status: Passed

## Checks

- Full .NET test suite: `700 passed, 5 skipped`
- Release publish: passed
- Self-contained Windows x64 ZIP: generated
- Per-user installation layout: previously certified
- Provider authentication: live OpenCode / OpenCode Zen credential
- Target model: `deepseek/deepseek-v4-flash` live probe passed
- Product loop: previously certified through Worker -> Review -> Accept
- Restart continuity: previously certified

## Release artifact

```text
AI.Game.Workbench-alpha-final-win-x64.zip
```

The ZIP is intentionally kept out of Git history and should be attached to
the GitHub Release as a release asset.

## Boundaries

This is an Alpha release candidate, not a clean-machine or upgrade
certification. Windows x64 is the supported target. Unity integration,
automatic updates, crash export, and upgrade migration remain outside this
release.
