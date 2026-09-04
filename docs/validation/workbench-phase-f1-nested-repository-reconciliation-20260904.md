# WORKBENCH PHASE F.1
# Nested Repository Reconciliation + Pinned Reference Candidate

Validation date: **2026-09-04**
Repository: `<PROJECT_ROOT>`
Starting HEAD: `fd026407031a295870c1a8493706e1c7a3e7457a`

## 1. Kilocode Current Git Reality

- Parent index entries were mode `160000` gitlinks for both `spikes/agent-shell-reuse/upstream/cline` and `spikes/agent-shell-reuse/upstream/kilocode`.
- `HEAD` contained both gitlinks, but the parent repository has no `.gitmodules` now and no historical `.gitmodules` entry was found.
- The `kilocode` clone is an independent repository at `ed3380ea494156cd36107d3ac95acaf08bf28b7f` (`v7.5.0`), branch `main`, tracking `origin/main`.
- Its apparent whole-tree deletion was an index-only anomaly. After a mixed reset to its own `HEAD`, one real deleted `.scm` file was found and restored from `HEAD`; the clone is now clean.
- The `cline` clone is an independent clean repository at `c0c37a1587defec8d0e901f053312d0721bec0fe` (`cli-v3.0.60`), branch `main`.

## 2. Historical Ownership Evidence

The parent history shows both gitlinks were introduced by commit `f4f399f1e138d13f353a23fb838c329f2dce68fd` (`feat: seal alpha continuity and native agent surface`) as part of the agent-shell-reuse spike. No parent commit contains `.gitmodules`, and no parent build or test project references either clone as a dependency.

## 3. Production Dependency Audit

Production source, solution projects, tests, runtime composition, and build scripts do not import or compile files from either upstream clone. The tracked spike documentation describes them as audited upstream snapshots and explicitly separates reusable shell research from Workbench authority/runtime ownership.

## 4. Ownership Decision

| Field | Decision |
|---|---|
| Current Git form | Historical parent gitlinks without submodule metadata |
| Intended ownership | **C. LOCAL-ONLY SPIKE CLONE** |
| Confidence | **HIGH** |
| Local unique changes exist | `kilocode`: one deleted `.scm` file was present; `cline`: none observed |
| Release relevance | **NON_REQUIRED** |
| Evidence | Independent upstream remotes/commits, spike-only path, no product dependency, no `.gitmodules`, and explicit spike documentation |

## 5. Unique Local Changes Preservation

Before any parent-tree mutation, external preservation was created under `<LOCAL_APP_DATA>\AI Game Workbench\validation-archive\`. It contains Kilo and Cline git bundles, nested HEAD/branch/origin/status records, a Kilo cached-index patch, and a compressed Kilo working tree. The Kilo cached-index patch SHA-256 is recorded in the archive metadata; raw bundles and archives are internal-only and are not staged.

## 6. Fix Applied

- Rebuilt the Kilo nested index with `git reset --mixed HEAD` (nested working files preserved).
- Restored the one real deleted `.scm` file from the nested `HEAD` using long-path support.
- Removed both non-reproducible parent gitlinks from the parent index with `git rm --cached` while leaving the local clone directories in place.
- Added exact ignore rules for only the two upstream clone paths.
- Updated the spike document to state that both clones are external, local-only references and not Alpha source identity.
- No nested `.git` metadata was deleted; no upstream source was vendored.

## 7. Parent Repository State

The parent now has an explainable ownership model: Workbench source owns `src/`, `tests/`, `docs/`, migrations, and project files; upstream spike clones remain filesystem-local and ignored. No `.gitmodules` file is invented.

## 8. Submodule State

No formal submodules remain in the Alpha source candidate. `git submodule status` is not used as release metadata because the parent has no `.gitmodules`; the two local clone repositories are independently clean and pinned by their documented commit IDs.

## 9. Deterministic Verification

After the ownership fix, the deterministic gate must be rerun. Required result: build `0 warnings / 0 errors`; solution tests `0 failed`, natural exit; live-provider tests explicitly skipped/not run.

## 10. Frozen Fixture Integrity

The historical fixture remains read-only and unchanged: schema `26`; Task `2ef8fb29-3a87-4225-85db-f86045bad771` = `Working`; WorkerExecution `0e5d9e8c-eceb-4462-85f7-13ee683cd419` = `Interrupted`; events `199`; AuthorityDecision `4`; AcceptedContribution `5`.

## 11. Staging Manifest

Explicit staging is limited to `.gitignore`, the Phase B/C source and tests, Migration 027, bridge/canonical architecture docs, spike ownership documentation, and public-safe validation reports. No `artifacts/`, binaries, logs, WebView state, databases, videos, archives, or nested clone contents are staged.

## 12. Secret / Binary Review

Candidate text files were scanned for credential-bearing material and user-specific absolute paths. No secret value was found. Generated binary/runtime material remains ignored or unstaged.

## 13. Pinned Commit

Pending staged review and local commit.

## 14. Final Git Status

Pending commit. Nested `cline` and `kilocode` repositories are clean after reconciliation.

## 15. Fresh Publish

Pending pinned commit. Publish output must be outside the source tree under `<LOCAL_APP_DATA>\AI Game Workbench\release-candidates\<short-sha>\publish`.

## 16. Artifact Hashes

Pending fresh publish from the pinned commit.

## 17. Post-Commit Tests

Pending pinned commit; no live provider may run in this phase.

## 18. Migration 027

Isolated schema-27 migration evidence is PASS; representative schema-26 to schema-27 checks use isolated copies only. The frozen historical database is not touched.

## 19. Final Manifest

Will be updated after commit, fresh publish, SHA-256 calculation, and post-commit deterministic verification.

## 20. Remaining Alpha Blockers

Fresh publish, hashes, and post-commit verification remain. Fresh OpenCode/Codex/DeepSeek acceptance, Alpha tag, and push are explicitly deferred to the next approved phase.

## Final Questions

- A. `kilocode` type: **LOCAL-ONLY SPIKE CLONE**.
- B. Evidence: historical spike gitlinks without `.gitmodules`, independent upstream clone, no product dependency, explicit spike docs.
- C. Unique local work: **YES**, one deleted `.scm` file; preserved externally and restored.
- D. Handling: external internal archive, then restore nested clone to its pinned `HEAD`.
- E. Parent ownership consistent/explainable: **YES**, after removing the two accidental gitlinks and adding exact ignore rules.
- F. Pinned commit: pending.
- G. Commit SHA: pending.
- H. Working tree clean: pending commit.
- I. Dirty nested repository: **NO**.
- J. Fresh publish from commit: pending.
- K. Artifact hashes: pending.
- L. Post-commit suite: pending.
- M. Frozen fixture unchanged: **YES**.
- N. Pinned Alpha reference candidate: pending.
- O. Remaining blockers: commit/publish/hash/post-commit gates, then later fresh live acceptance.
- P. Live provider run: **NO**.
- Q. `git push`: **NO**.

**MANDATORY STOP after Phase F.1 completion:** no live provider, tag, push, Phase 5, Host recovery, or Library temporal work.
