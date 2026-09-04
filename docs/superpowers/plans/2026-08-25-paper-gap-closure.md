# Paper Gap Closure Plan

Status: **Historical plan completed; current validation superseded by 2026-09-04 record**
Date: **2026-08-25**

## Scope

This plan closes the first three implementation gaps found by the paper-to-repository audit:

1. seal and document the current Manual Project World product slice;
2. connect external agent execution through a provider-neutral B1 participation adapter;
3. upgrade evidence from opaque locators to verifiable evidence records while preserving existing data.

Explicitly deferred:

- multi-user authority delegation;
- policy-based or automatic governance;
- user studies, longitudinal deployment, token-savings measurement, and formal verification.

The paper is an evaluation reference, not an implementation instruction. No paper statement is treated as a command unless it is implemented and tested in the repository.

## Baseline and success criteria

The audit baseline is the current working tree, including uncommitted Product Slice 1 changes. The sealed `HEAD` baseline and the working tree must be reported separately.

Success means:

- the manual journey is committed/documented as an implemented product slice;
- a provider-neutral adapter can execute an Assignment through an external runtime and record only non-authoritative Claim/Handoff output;
- a separate named authority command is still required before AcceptedProjectState changes;
- evidence records can identify kind, locator, and content digest, with old locator-only rows remaining readable;
- full build and test suites pass with zero warnings and zero failures.

## Phase 1 — Seal Manual Project World

### Work

- Review current uncommitted Project World, Library projection, Guided Decision, and recovery changes.
- Keep the existing B1 commands and projection boundary; do not add a second manual domain API.
- Add or update certification coverage for create/open, setup, manual work, handoff, decision, Library projection, close, reopen, and no-runtime operation.
- Update the paper and architecture/status records to reflect the actual working-tree implementation and current test count only after tests pass.

### Acceptance

- Manual flow works with an empty runtime registry and zero SessionBindings.
- Handoff remains non-authoritative until a named Decision is committed.
- Reopen rebuilds the same AcceptedProjectState and Library provenance.
- Documentation distinguishes sealed `HEAD` from the current implementation.

### Implementation record

- Existing Product Slice 1 implementation was reviewed rather than duplicated.
- Manual create/setup/work/handoff/decision/Library/reopen certification remains green.
- The intermediate paper-era `967` and `996` test counts are historical. The current canonical working tree reports `1,068` passing tests; see [Current Working-Tree Validation](../../validation/current-working-tree-20260904.md).

## Phase 2 — Provider-neutral B1 Agent Adapter

### Work

- Define a small B1 participation contract around `Assignment`, `Attempt`, optional `SessionBinding`, external output, `Claim`, and bounded `Handoff`.
- Implement an application adapter that can use any `IAgentRuntime`; keep Codex-specific translation inside the existing Runtime provider boundary.
- Ensure adapter failures and provider events cannot write AcceptedProjectState.
- Add a fake-runtime end-to-end certification covering assignment dispatch, output attribution, Handoff persistence, restart recovery, and explicit Decision commit.
- Do not reclassify existing Legacy Leader/Worker review flows as B1 authority.

### Acceptance

- The same B1 adapter test passes with a fake provider/runtime.
- Agent output produces Claims/Handoff only.
- Accepted state changes only through `B1AuthorityCommandService`.
- Provider/session replacement does not change Project identity or accepted state.

### Implementation record

- Added `B1AgentParticipationAdapter` and exposed it from `AppServices`.
- The adapter creates/selects an Attempt, creates/selects a SessionBinding, dispatches through any `IAgentRuntime`, and records a bounded Handoff.
- Agent output, provider approvals, and runtime failures have no AcceptedProjectState write path.
- Added fake-runtime end-to-end certifications for successful output and rejected approval requests.

## Phase 3 — Verifiable Evidence

### Work

- Introduce a structured evidence record with evidence kind, stable locator, optional media/content metadata, and digest algorithm/value.
- Preserve existing `EvidenceRef` locator values through an additive migration or compatibility projection.
- Add repository read/write paths and validation for digest format and evidence ownership/provenance.
- Expose evidence in Claim/Handoff/Decision provenance without making evidence itself authoritative.
- Add migration, round-trip, invalid-digest, legacy-row, and decision-provenance tests.

### Acceptance

- Existing locator-only data remains readable and is not silently reinterpreted.
- New evidence can be verified against its digest when content is available.
- Evidence never bypasses Claim → Decision → AcceptedState.
- Rich media ingestion remains out of scope; this phase establishes the verifiable record boundary.

### Implementation record

- Added `EvidenceRecord`, `EvidenceKind`, and SHA-256 digest helpers in Core.
- Added additive `Migration022EvidenceRecords` and `B1EvidenceRepository` with immutable same-reference semantics.
- Existing locator-only `EvidenceRef` values remain valid; new records can be verified when content is available.
- Added round-trip, tamper detection, duplicate provenance, and invalid digest tests.

## Verification and reporting

After each phase:

1. run focused tests for the changed boundary;
2. run `dotnet build AI.Game.Workbench.sln --no-restore`;
3. run `dotnet test AI.Game.Workbench.sln --no-restore --verbosity minimal`;
4. record the result and update this plan's status.

## Final verification

- `dotnet build AI.Game.Workbench.sln --no-restore`: PASS, 0 warnings, 0 errors.
- Historical final verification: `996 passed, 0 failed, 0 skipped`.
- Current verification: see [Current Working-Tree Validation](../../validation/current-working-tree-20260904.md).
- Deferred by request: multi-user governance, automatic governance, empirical research, and formal verification.

The final audit report should still distinguish the sealed `HEAD` baseline from the current uncommitted working tree and should not claim the deferred items are implemented.
