# Canonical Source and Documentation Export Policy

Status: **Current repository policy**
Effective: **2026-09-04**

## Canonical source

`C:\Users\pupet\Documents\ChatGPT\AI Game Workbench` is the single canonical Workbench repository. It owns the implementation, tests, migrations, release records, validation evidence, and current project status.

Public repository: `https://github.com/pupet2004/AI-Game-Workbench`

The repository's sealed `HEAD` at the start of this policy is `cc1f70b0ed904341994e64fbf4143af1c5b8ff3b` (2026-08-29). The working tree currently contains additional uncommitted implementation, test, and artifact changes. Claims about the current implementation must identify whether they refer to that sealed `HEAD` or to the current working tree.

## Desktop documentation export

`C:\Users\pupet\Desktop\workbench` is a documentation export package. It is not an implementation checkout and is not a release or validation authority. Its Markdown files may be regenerated from the canonical repository; local edits there must be treated as export changes until copied back into the canonical repository.

The export package retains historical Architecture Preview and whitepaper material. Historical test counts and dates must not be read as current implementation claims.

## Current validation reference

The current working-tree verification record is [Current Working-Tree Validation](validation/current-working-tree-20260904.md). Release-facing documents for `v0.1.0-alpha.20260827` remain historical records of that Alpha snapshot.
