# Workbench R5-B1 Final Seal

## Result

`R5_B1_SEALED`

R5-B1 Manual Continuity Spine is merged into `master` and sealed. The implementation establishes a durable Project-governance spine that remains valid without Agent sessions, transcripts, Summary, runtime, Git, worktree, or execution infrastructure.

## Git Baseline

- Base `master`: `9d5e4c56f48cddda6ff44aabad6b8f4bfc45c774`
- Feature branch: `codex/r5-b1-task-1`
- Feature HEAD: `2ffe16a02987809e46f1ef502ff807b9dee0ddec`
- Merge commit: `23b2eae9645de67595a23b70a2b14994d2774f32`
- Merge strategy: non-squash, `--no-ff`
- Preserved feature commits: 18
- Branch at seal: `master`
- Final HEAD: the report commit containing this document

## Task Inventory

| Task | Responsibility | Commit(s) |
|---|---|---|
| 1 | Durable identities | `ed1ed9b` |
| 2 | Claims and commands | `191d425` |
| 3 | Current-state projection | `0ea793e` |
| 4 | Authority evaluation | `fede772` |
| 5 | Additive Migration020 landing zone | `2422a7f` |
| 6 | Project governance bootstrap and adoption | `6038c7c` |
| 7 | Attempt and SessionBinding routing | `ea0d679` |
| 8 | Claims and Handoffs | `256f022` |
| 9 | Atomic authority persistence | `ce75877` |
| 10 | Responsibility establishment and delegation commands | `cab01a6` |
| 11 | Bounded compound decisions | `b81e58e` |
| 12 | Durable recovery projection | `48e78a3`, `d8a2020` |
| 13 | Zero-Session Manual continuity | `a7148b3`, `8d1bf7f` |
| 14 | Legacy one-way coexistence | `2613000`, `05aff80` |
| Integration hygiene | Deterministic test cleanup and bounded test concurrency | `2ffe16a` |

## Architecture Guarantees

- `LogicalActor`, `Responsibility`, `Assignment`, immutable `Revision`, and `Attempt` provide durable Project identity and responsibility continuity.
- `SessionBinding` is optional connectivity/provenance. Rebinding cannot change responsibility, assignment identity, authority, or accepted state.
- Claims and Handoffs are immutable and non-authoritative. The primary Assignment result remains attributable to its immutable assignee LogicalActor.
- Accepted Project State is rebuilt only from persisted, attributable `AuthorityDecision` effects.
- Authority validation happens before persistence. Repository commit uses Project sequence CAS and one SQLite transaction; bounded compound decisions are all-or-nothing.
- Authority capabilities are closed, locality-aware, maximum/subset constrained, and non-amplifying. Role, provider, model, runtime, and session identity confer no authority.
- Accepted contributions use explicit `Project | Responsibility | Assignment` scopes. Current projection uses explicit same-scope supersession; history remains append-only.
- Stored routing selections remain distinct from effective continuation projections and never confer authority or execution status.
- The public authority surface remains the six sealed named commands; no generic Decision/effect builder or general ACL language was added.
- Summary remains a non-authoritative continuity aid and is not an Accepted Project State writer.

## Manual Continuity Certification

The certified end-to-end path is:

```text
Project bootstrap UserPrincipal
  -> LogicalActor + Responsibility + Assignment + Revision
  -> Manual Attempt
  -> Claims + bounded Handoff
  -> attributable AuthorityDecision
  -> AcceptedProjectState
  -> restart and next-day recovery
```

The certification passes with:

- SessionBindings: `0`
- Agent API/runtime dispatch: `0`
- transcript dependency: `0`
- Summary dependency: `0`
- Provider/model dependency: `0`
- Git/worktree dependency: `0`
- review-execution dependency: `0`

## Legacy Coexistence

- Legacy epoch, task/revision, review/AutoProceed, completion/event, Memory/Synthesis, Daily Summary, Library, and R5-A Summary records retain their original semantics and remain independently readable.
- Migration or compatibility code does not synthesize B1 LogicalActors, Responsibilities, Assignments, Claims, Handoffs, AuthorityDecisions, or Accepted Project State from Legacy history.
- An eligible pre-B1 Project enters B1 governance only through explicit one-time adoption by an authenticated UserPrincipal.
- Crossing Legacy context into B1 is explicit, attributable, one-way, and current-time through named B1 commands or non-authoritative Claim recording.
- B1 actions do not rewrite bounded Legacy data.

## Migration

- Code schema: `v20`
- Migration: `Migration020ManualContinuitySpine`
- Migrations 001-019: unchanged
- Landing behavior: additive only
- Existing Projects receive mechanical Legacy-origin eligibility rows only.
- Migration creates no B1 authority, identity, Claim, Handoff, Assignment, Revision, or accepted-state history.
- Disposable synthetic historical chain `v11 -> ... -> v19 -> v20`: PASS
- `PRAGMA user_version`: `20`
- `quick_check`: `ok`
- `foreign_key_check`: zero rows
- Bounded Legacy snapshot: preserved
- Live production database: not opened, copied, initialized, migrated, or written during R5-B1 implementation or certification

## Master Verification

The following smoke ran after merge from `master` with the repository-default `dotnet test` entry point:

| Test project | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Workbench.Core.Tests | 128 | 0 | 0 |
| Workbench.Storage.Tests | 299 | 0 | 0 |
| Workbench.Project.Tests | 28 | 0 | 0 |
| Workbench.Runtime.Tests | 58 | 0 | 0 |
| Workbench.App.Tests | 441 | 0 | 0 |

- Total: `954 passed / 0 failed / 0 skipped`
- Integration build before merge: PASS, `0 warnings / 0 errors`
- `git diff --check`: PASS
- Task 14 temporary-directory seal: `BEFORE = 62657`, `AFTER = 62657`, `NEW = 0`

Storage tests use a Storage-test-assembly-local serialized collection policy. App tests retain bounded four-way collection concurrency. Core, Project, and Runtime test parallelism is unchanged.

## Known Warning

The merge smoke emitted existing compiler warning `CS9113` for unread parameter `authoritySettings` in `LeaderReviewAskUserGate`. R5-B1 did not introduce or modify this warning; it does not affect the certification results.

## Explicit Non-Ownership

R5-B1 adds no Agent Gateway, provider integration, runtime execution engine, worktree/Git orchestration, sandbox or permission policy, review execution, automated Claim verification, general Truth engine, generic ACL system, UI, or Legacy retirement.

## Final Judgment

`R5-B1 Manual Continuity Spine — SEALED`

R5-B1 is the stable baseline for later architecture documentation and any separately designed R6 work. No R6 implementation is authorized by this seal.
