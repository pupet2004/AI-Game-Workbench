# WORKBENCH PHASE F
# Release Hygiene + Pinned Reference Candidate

Validation date: **2026-09-04**
Repository: `<PROJECT_ROOT>`
Starting HEAD: `fd026407031a295870c1a8493706e1c7a3e7457a`

## 1. Result

**STATUS: BLOCKED BEFORE COMMIT**

The Phase B/C source candidate is identifiable and deterministic gates are available, but a clean pinned release candidate cannot be honestly created from this working tree without deciding how to handle unrelated repository state. No commit, tag, push, live provider run, or frozen-database mutation was performed.

## 2. Original Dirty Tree Summary

- `197` porcelain entries at the Phase F audit point.
- `14` tracked build/publish artifacts modified by local builds.
- `1` tracked nested repository entry: `spikes/agent-shell-reuse/upstream/kilocode`.
- Numerous untracked screenshots, videos, logs, WebView/provider state, publish directories, acceptance fixtures, and historical reports.
- The nested repository itself reports broad deletions and untracked files; the parent repository also reports a missing `.gitmodules` mapping for `spikes/agent-shell-reuse/upstream/cline`.

## 3. Cleanup Classification

| Category | Representative paths | State | Action |
|---|---|---|---|
| `REQUIRED_PRODUCTION_SOURCE` | `src/Workbench.App/**`, `src/Workbench.Core/Continuity/B1WorkerBridgeModels.cs`, `src/Workbench.Storage/Continuity/**` | tracked/untracked | candidate `COMMIT` |
| `REQUIRED_TEST` | `tests/**/CanonicalWorkerLaunchServiceTests.cs`, `tests/**/B1WorkerBridgeRepositoryTests.cs`, live-test skip changes | tracked/untracked | candidate `COMMIT` |
| `REQUIRED_MIGRATION` | `src/Workbench.Storage/Migrations/Migration027B1WorkerBridge.cs`, `MigrationRunner.cs` | tracked/untracked | candidate `COMMIT` |
| `REQUIRED_NORMATIVE_DOC` | `docs/architecture/workbench-global-architecture.md`, `docs/architecture/b1-worker-execution-bridge.md` | tracked/untracked | candidate `COMMIT` |
| `REQUIRED_PUBLIC_VALIDATION_DOC` | `docs/validation/workbench-phase-f-release-hygiene-20260905.md` | new | candidate `COMMIT` after review |
| `GENERATED_BUILD_OUTPUT` / `GENERATED_PUBLISH_OUTPUT` | `artifacts/Workbench.App/**`, `artifacts/release/**` | tracked/untracked | tracked changes restored; new output `IGNORE` |
| `ARCHIVE_ZIP` | `artifacts/**/*.zip` | untracked/tracked | `IGNORE`; preserve existing evidence |
| `SCREENSHOT_VIDEO` | `artifacts/**/*.{png,jpg,mp4,mp3}`, `round5-screen.png` | untracked | `IGNORE`; preserve |
| `LOG_DIAGNOSTIC` | `artifacts/**/*.log` | untracked | `IGNORE`; preserve |
| `WEBVIEW_PROVIDER_PROFILE` | `artifacts/**/Workbench.App.exe.WebView2`, `artifacts/**/skills` | untracked | `IGNORE`; preserve |
| `TEMP_DATABASE` / `ACCEPTANCE_RUNTIME_FIXTURE` | `*.db`, `artifacts/acceptance-*`, `acceptance-lab-*` | untracked | `IGNORE`; frozen fixture remains read-only |
| `UNKNOWN` / unrelated | nested `kilocode` repository, `tools/html-video/`, `docx_qa_en_20260825/` | mixed | `REVIEW_REQUIRED`; no deletion |

## 4. Cleanup Actions Actually Taken

- Added narrow ignore rules for generated/internal validation material and nested tool runtime state.
- Restored only the 14 tracked build/publish files modified by local builds from `HEAD`.
- Did not use `git clean -fdx`, `git reset --hard`, batch-delete `artifacts/`, or delete unknown files.
- Did not modify the nested `kilocode` repository.

## 5. Secret / Privacy Scan

Candidate source/docs were scanned for API keys, access tokens, refresh tokens, passwords, cookies, authorization/bearer material, private keys, credentials, and secret values. **No secret value was found.** Existing public documentation mentions credential-dependent live tests but does not contain credentials. Candidate report paths use `<PROJECT_ROOT>` / `<USER_HOME>` placeholders.

## 6. Phase B/C Source Completeness

Present in source candidate: `B1WorkerTaskLink`, `B1WorkerExecutionLink`, `B1WorkerSessionLink`, `B1WorkerExecutionEvidence`, Migration 027, `B1WorkerExecutionBridgeService`, `CanonicalWorkerLaunchService`, canonical Leader launch wiring, Verification-to-B1 evidence projection, GuidedDecision evidence consumption, explicit live-provider skip semantics, canonical launch certification tests, and bridge architecture documentation.

## 7. Deterministic Gates

Previously recorded on this working tree:

- Build: `0 warnings, 0 errors`, exit code `0`.
- Solution tests: `1,074 passed, 4 skipped, 0 failed`, natural exit, exit code `0`, about `2m` wall time (Core 128, Runtime 66/1, Project 28, App 542/3, Storage 310).
- Live provider execution: **NOT RUN**; default live tests explicitly skipped.

A post-commit gate is **not applicable** because no commit was safely created.

## 8. Frozen Fixture Integrity

The historical fixture was checked read-only before Phase F and remains unchanged: schema `26`; Task `2ef8fb29-3a87-4225-85db-f86045bad771` = `Working`; WorkerExecution `0e5d9e8c-eceb-4462-85f7-13ee683cd419` = `Interrupted`; task events `199`; AuthorityDecision `4`; AcceptedContribution `5`. No migration, resume, retry, replay, repair, reclassification, or deletion was performed.

## 9. Staging / Commit / Publish / Hashes

- Intended explicit staging manifest (not executed):
  - `.gitignore`
  - `src/Workbench.App/Continuity/B1WorkerExecutionBridgeService.cs`
  - `src/Workbench.App/Continuity/CanonicalWorkerLaunchService.cs`
  - `src/Workbench.Core/Continuity/B1WorkerBridgeModels.cs`
  - `src/Workbench.Storage/Continuity/B1WorkerBridgeRepository.cs`
  - `src/Workbench.Storage/Migrations/Migration027B1WorkerBridge.cs`
  - modified canonical wiring under `src/Workbench.App/{Leader,Services,ViewModels,Worker}`
  - `src/Workbench.Storage/Database/MigrationRunner.cs`
  - bridge/certification/migration tests under `tests/**`
  - `docs/architecture/workbench-global-architecture.md`
  - `docs/architecture/b1-worker-execution-bridge.md`
  - `docs/validation/workbench-phase-f-release-hygiene-20260904.md`
- Exact staging manifest: prepared conceptually, **not staged**, because unrelated nested-repository state prevents a clean candidate review.
- Pinned commit SHA: **NONE**.
- Working tree: **DIRTY**.
- Fresh publish from pinned SHA: **NOT CREATED**.
- Artifact SHA-256 values: **PENDING**.
- Alpha tag: **NOT CREATED**.
- Push: **NO**.

## 10. Alpha Live Protocol Preparation

Not sealed. The next protocol must reference a real pinned commit SHA, fresh publish hashes, and the isolated schema-27 Alpha fixture. It must run fresh OpenCode, canonical Leader-to-Worker, typed provenance, Verification-to-B1 evidence, authority boundary, GuidedDecision, Codex continuation, graceful restart, and cold Leader continuity in that order. No historical frozen fixture or old publish output may be used.

## 11. Remaining Blockers

1. Resolve/explicitly preserve the unrelated nested `kilocode` repository state and repair the missing parent `.gitmodules` mapping decision.
2. Re-run the exact staging review after blocker resolution.
3. Create a local pinned commit, then fresh-publish from that SHA and calculate hashes.
4. Only in a later approved phase: fresh live acceptance and final Alpha seal.

## 12. Phase F Questions

| Question | Answer |
|---|---|
| A. Pinned commit exists? | **NO** |
| B. SHA | **NONE; current HEAD remains `fd026407031a295870c1a8493706e1c7a3e7457a`** |
| C. Working tree clean? | **NO** |
| D. Commit contains all Phase B/C implementation? | **NO COMMIT TO ASSERT**; source completeness is **YES** |
| E. Deterministic build/test from pinned commit? | **NO PINNED COMMIT**; working-tree gate previously passed |
| F. Solution naturally exits? | **YES** in recorded working-tree run |
| G. Default live tests skipped? | **YES** |
| H. Fresh publish from commit? | **NO** |
| I. Artifact hashes | **PENDING** |
| J. Migration 027 release check | **PASS in isolated deterministic evidence; no frozen DB touched** |
| K. Frozen fixture unchanged? | **YES** |
| L. Pinned Alpha reference candidate? | **NO** |
| M. Only fresh live acceptance remains? | **NO**; pinning/clean-tree/publish blockers remain |
| N. `git push` executed? | **NO** |

**MANDATORY STOP:** Phase F stops before commit, tag, push, live providers, Phase 5, Host recovery, Library temporal work, and token benchmarking.
