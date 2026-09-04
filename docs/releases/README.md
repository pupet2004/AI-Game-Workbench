# Release and Artifact Index

This index distinguishes historical release packages from current working-tree validation. It does not authorize a release by itself.

## Historical Alpha

| Package / record | Date | Meaning |
|---|---|---|
| `v0.1.0-alpha.20260827` | 2026-08-27 | Timestamped Alpha snapshot; verification record reports 1,017 tests. |
| `AI.Game.Workbench-alpha-b1` through `alpha-c3` | 2026-08-26 | Earlier iterative build packages; historical only. |
| `AI.Game.Workbench-alpha-win-x64` | 2026-08-30 | Later local package; not a versioned release record. |
| `AI.Game.Workbench-round3-readiness-20260902` | 2026-09-02 | Readiness package; not a tagged release. |

## Current canonical state

The 2026-09-04 canonical working tree has a fresh build/test validation of **1,068 passed, 0 failed, 0 skipped** with **0 build warnings and 0 build errors**. That result is explicitly a dirty working-tree result and is not currently attached to a new versioned release package.

Use [Current Working-Tree Validation](../validation/current-working-tree-20260904.md) for the reproducible local status. Do not describe a ZIP as the current release until it has a commit identifier, validation record, and artifact manifest.

## Release hygiene still required

- choose one package as the next release candidate;
- record the exact source commit and clean/dirty state;
- record build, full test, and gated live-provider evidence separately;
- generate a manifest with SHA-256 and platform/runtime details;
- then publish or archive the package according to the chosen release policy.
