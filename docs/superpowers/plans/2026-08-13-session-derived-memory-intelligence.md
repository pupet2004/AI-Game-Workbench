# Session-Derived Memory Intelligence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn each newly archived Leader epoch into one durable, retryable, provider-neutral synthesis job that can update Learned memory and propose certification Candidates without touching Formal memory.

**Architecture:** Extend the existing SQLite data model and rollover transaction with a per-epoch job. A thin App-layer coordinator resumes the persisted archived `AgentSession`, sends one bounded maintenance prompt, validates one strict JSON batch, and delegates one atomic memory/job transaction to Storage. App services schedule at most one best-effort job per project without awaiting it from UI paths.

**Tech Stack:** .NET 10, Avalonia 12, CommunityToolkit.Mvvm, Microsoft.Data.Sqlite, System.Text.Json, xUnit.

## Global Constraints

- Synthesis unit is one archived epoch; current epochs are never queued or synthesized.
- One archived epoch costs one additional Agent turn; no per-item turns.
- No transcript replay, embeddings, RAG, semantic search, vector database, knowledge graph, boot injection, Worker, file/tool use, approval, or replacement session.
- Output caps: 5 Learned items, 5 Candidates, 200 UTF-8 bytes per topic, 8000 UTF-8 bytes per content.
- Context caps: 12000 UTF-8 bytes of newest Active Formal memory and 8000 UTF-8 bytes of newest Active Learned memory.
- Only user certification can create Formal memory.

---

### Task 1: Migration005 and durable job lifecycle

**Files:**
- Create: `src/Workbench.Storage/Migrations/Migration005MemorySynthesisJobs.cs`
- Create: `src/Workbench.Storage/Memory/ProjectMemorySynthesisModels.cs`
- Create: `src/Workbench.Storage/Memory/ProjectMemorySynthesisRepository.cs`
- Modify: `src/Workbench.Storage/Database/MigrationRunner.cs`
- Modify: `tests/Workbench.Storage.Tests/Database/WorkbenchDatabaseTests.cs`
- Create: `tests/Workbench.Storage.Tests/Memory/ProjectMemorySynthesisJobTests.cs`

**Interfaces:**
- `QueueSynthesisForEpochAsync(Guid epochId)` inserts one Pending job only when the epoch is archived.
- `ClaimNextPendingAsync(Guid projectId)` atomically changes the oldest Pending job to Running and increments `AttemptCount`.
- `ReturnToPendingAsync(Guid epochId, string error)` preserves retry opportunity with bounded diagnostics.
- `RecoverRunningAsync()` returns interrupted Running jobs to Pending on application startup.

- [x] Add v4 fixtures and failing migration/job tests for schema v5, v4 preservation, archived-only queueing, round-trip, project isolation, duplicate queueing, and restart recovery.
- [x] Run the focused Storage tests and verify failures are caused by missing v5 behavior.
- [x] Implement the transactional 4→5 migration and job repository.
- [x] Run focused Storage tests to green.

### Task 2: Atomic rollover enqueue and atomic synthesis apply

**Files:**
- Modify: `src/Workbench.Storage/Leaders/ProjectLeaderRepository.cs`
- Modify: `src/Workbench.Storage/Memory/ProjectMemoryRepository.cs`
- Modify: `src/Workbench.Storage/Memory/ProjectMemoryService.cs`
- Modify: `tests/Workbench.Storage.Tests/Leaders/LeaderPersistenceRepositoryTests.cs`
- Create: `tests/Workbench.Storage.Tests/Memory/ProjectMemorySynthesisApplyTests.cs`

**Interfaces:**
- Successful `ProjectLeaderRepository.RolloverAsync` archives old epoch, inserts successor, switches current, and inserts old epoch's Pending job in one SQLite transaction.
- `ApplySynthesisAsync(ProjectMemorySynthesisApplication application)` supersedes Learned topics, inserts deduplicated Learned/Candidates and sources, and marks the job Completed in one transaction.

- [x] Add failing tests for rollover enqueue, no current-epoch job, Learned versioning, Candidate/Formal dedupe, retry idempotency, provenance, project isolation, and write rollback.
- [x] Run focused Storage tests and record the expected red result.
- [x] Implement atomic rollover enqueue and atomic synthesis apply with normalized topic/content comparisons.
- [x] Run focused Storage tests to green.

### Task 3: Strict batch parser and bounded maintenance prompt

**Files:**
- Create: `src/Workbench.App/Memory/ProjectMemorySynthesisPayloadParser.cs`
- Create: `src/Workbench.App/Memory/ProjectMemorySynthesisPromptBuilder.cs`
- Create: `tests/Workbench.App.Tests/ProjectMemorySynthesisPayloadParserTests.cs`
- Create: `tests/Workbench.App.Tests/ProjectMemorySynthesisPromptBuilderTests.cs`

**Interfaces:**
- `Parse(string json, IReadOnlyDictionary<long, StoredLeaderMessage> messages)` returns a complete validated batch or throws before any write.
- `Build(formal, learned)` emits no transcript text and truncates complete newest items to the exact Formal/Learned UTF-8 byte caps.

- [x] Add failing parser tests for valid JSON, malformed JSON, item caps, UTF-8 bounds, unknown sequences, and all-or-nothing behavior.
- [x] Add failing prompt tests for the durable-memory rubric, explicit-user-decision priority, Learned-vs-Candidate distinction, strict JSON-only/tool-free instructions, byte caps, and absence of transcript replay.
- [x] Run focused App tests and verify expected red failures.
- [x] Implement parser and prompt builder; run focused tests to green.

### Task 4: Provider-neutral coordinator and failure semantics

**Files:**
- Create: `src/Workbench.App/Memory/ProjectMemorySynthesisCoordinator.cs`
- Modify: `src/Workbench.App/Services/AppServices.cs`
- Create: `tests/Workbench.App.Tests/ProjectMemorySynthesisCoordinatorTests.cs`
- Modify: `tests/Workbench.App.Tests/Support/FakeAgentRuntime.cs`

**Interfaces:**
- `TryProcessNextAsync(Guid projectId, CancellationToken)` claims at most one job and enforces per-project single flight.
- The coordinator rehydrates the persisted provider/account/model/session/external-session/working-directory identity, calls only `ResumeSessionAsync` and one `SendAsync`, and never calls `CreateSessionAsync`.
- Invalid output, unavailable runtime/account, resume/send failure, tool event, approval request, cancellation, or apply failure returns the job to Pending with a short error.

- [x] Add failing coordinator tests covering archived identity, no replacement session, current-session isolation, invisible maintenance, no message/activity mutation, approval/tool failure, retry, single flight, failure non-blocking, abrupt restart, and cross-project isolation.
- [x] Run focused App tests and verify expected red failures.
- [x] Implement coordinator composition and startup recovery.
- [x] Run focused App tests to green.

### Task 5: Non-blocking triggers and minimal Library UI

**Files:**
- Modify: `src/Workbench.App/ViewModels/MainWindowViewModel.cs`
- Modify: `src/Workbench.App/ViewModels/WorkspaceViewModel.cs`
- Modify: `src/Workbench.App/ViewModels/Panes/LeaderPaneViewModel.cs`
- Modify: `src/Workbench.App/ViewModels/Panes/LibraryPaneViewModel.cs`
- Modify: `src/Workbench.App/Views/Panes/LibraryPaneView.axaml`
- Modify: `tests/Workbench.App.Tests/WorkspaceViewModelTests.cs`
- Create: `tests/Workbench.App.Tests/ProjectMemoryLearningUiTests.cs`

**Interfaces:**
- `AppServices.ScheduleMemorySynthesis(Guid projectId)` starts best-effort background work and returns immediately.
- Project open, completed Leader turn, successful rollover, and opening Project Memory call the scheduler without awaiting synthesis.
- Library exposes `Memory learning: Up to date`, pending count, or `Learning...`; Learned memory is visibly AI-generated and separate from Formal; synthesized Candidate source is `From Leader session · MMM d`.

- [x] Add failing VM tests proving all four triggers return without waiting and failed synthesis does not block current Leader use.
- [x] Add failing UI-state tests for learning status, Learned/Formal separation, Candidate source label, and preserved Manual certification commands.
- [x] Implement scheduler callbacks, projections, and minimal AXAML.
- [x] Run focused App tests to green.

### Task 6: Verification, real smoke, documentation, and release

**Files:**
- Modify: `README.md`
- Modify: `docs/superpowers/plans/2026-08-12-project-memory-foundation.md`
- Modify: this plan

- [x] Run restore, solution build, every test project, full solution tests, and confirm zero warnings/errors.
- [x] Re-run real M1.5A Accept/Edit+Accept/Reject UI smoke in `AI Game Workbench M105B Smoke`.
- [x] Create one archived real Codex session containing the exact Memory Smoke Rule, queue it, restart once while Pending, then run one synthesis turn.
- [x] Verify generated Learned/Candidate meaning, no Formal before approval, real UI Accept creates Formal, no protected-project writes, and no visible transcript/activity contamination.
- [x] Update README to M1.5A Completed / M1.5B Current / M1.5C Planned and mark both plans complete where applicable.
- [x] Inspect final diff, commit as `feat(memory): synthesize project memory from leader sessions`, and confirm `master` is clean.

## Verification Notes

- The first real synthesis attempt correctly returned to Pending after the model cited a sequence outside the Workbench epoch. The prompt now supplies only valid `sequence:role` metadata (never message text), and the retry completed.
- The Pending job survived an application close/restart. The safe project produced one Candidate expressing the Memory Smoke Rule, zero Formal items before approval, and one Formal item only after real UI Accept.
