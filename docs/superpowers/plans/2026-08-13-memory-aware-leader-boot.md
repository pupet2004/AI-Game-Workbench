# Memory-Aware Leader Boot Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Inject one bounded, authority-aware Project Memory envelope into the first real user turn of each fresh Leader epoch without exposing or persisting that internal context.

**Architecture:** Add one SQLite delivery timestamp to each Leader epoch so boot eligibility survives process restarts and pre-acceptance failures. A focused `LeaderBootContextBuilder` loads deterministic Active Formal/Learned memory plus the immediate handoff at send time, builds one bounded request, and the Leader pane marks delivery only after the runtime produces its first event. Existing handoff generation remains unchanged; its old boot-envelope builder is replaced by the unified builder.

**Tech Stack:** .NET 10, Avalonia 12, CommunityToolkit.Mvvm, Microsoft.Data.Sqlite, xUnit.

## Global Constraints

- Current explicit user message outranks Active Formal memory, which outranks Active Learned memory, which outranks the immediately previous handoff.
- Formal is labeled `USER-CERTIFIED PROJECT MEMORY`; Learned is labeled `AI-SYNTHESIZED PROJECT MEMORY` and explicitly provisional.
- Pending Candidate, Rejected, and Superseded items are excluded.
- Budgets are 12000 Formal UTF-8 bytes, 8000 Learned UTF-8 bytes, 6000 handoff UTF-8 bytes, and 32000 UTF-8 bytes for Workbench-generated context excluding the unmodified user message.
- Selection is deterministic and never truncates a memory item: Formal by `CertifiedAt DESC, UpdatedAt DESC, Id`; Learned by `UpdatedAt DESC, Id`.
- A normalized Formal topic suppresses every Learned item with that topic; normalization trims, collapses whitespace, and compares case-insensitively.
- Only the original user text appears in the UI and `leader_messages`; no boot envelope is persisted or shown.
- No RAG, embeddings, semantic search, vector database, knowledge graph, transcript replay, or additional Agent turn.

---

### Task 1: Migration006 and durable boot-delivery state

**Files:**
- Create: `src/Workbench.Storage/Migrations/Migration006LeaderBootDelivery.cs`
- Modify: `src/Workbench.Storage/Database/MigrationRunner.cs`
- Modify: `src/Workbench.Storage/Leaders/StoredLeaderSessionEpoch.cs`
- Modify: `src/Workbench.Storage/Leaders/LeaderSessionEpochRepository.cs`
- Modify: `src/Workbench.Storage/Leaders/ProjectLeaderRepository.cs`
- Modify: `tests/Workbench.Storage.Tests/Database/WorkbenchDatabaseTests.cs`
- Modify: `tests/Workbench.Storage.Tests/Leaders/LeaderPersistenceRepositoryTests.cs`

**Interfaces:**
- `StoredLeaderSessionEpoch.BootContextDeliveredAt` is null only while a new epoch still needs boot context.
- `MarkBootContextDeliveredAsync(Guid epochId, DateTimeOffset deliveredAt)` performs a one-way, idempotent transition for the active epoch.
- Migration006 backfills existing epochs that already contain user messages so upgrading cannot inject context into an established conversation.

- [x] Add failing migration and repository tests for v6, backfill, new zero-message epochs, one-way marking, and project isolation.
- [x] Run focused Storage tests and verify failures are caused by missing v6 behavior.
- [x] Implement Migration006 and delivery-state persistence.
- [x] Run focused Storage tests to green.

### Task 2: Unified bounded boot builder

**Files:**
- Create: `src/Workbench.App/Leader/LeaderBootContextBuilder.cs`
- Modify: `src/Workbench.App/Leader/LeaderHandoffBuilder.cs`
- Modify: `src/Workbench.App/Leader/LeaderSessionRolloverService.cs`
- Create: `tests/Workbench.App.Tests/LeaderBootContextBuilderTests.cs`
- Modify: `tests/Workbench.App.Tests/LeaderSessionRolloverServiceTests.cs`

**Interfaces:**
- `BuildAsync(Project project, string originalUserText, CancellationToken)` returns one `AgentRequest` containing project identity, non-empty Formal/Learned sections, optional immediate handoff, and the original user message.
- The builder queries only Active Formal/Learned items, applies authority suppression and deterministic complete-item budgets, and emits `WORKBENCH PROJECT CONTEXT` exactly once.
- `LeaderHandoffBuilder` continues semantic/fallback handoff creation only; it no longer creates a separate boot envelope.

- [x] Add failing builder tests for labels, authority order, status exclusion, deterministic ordering, normalized-topic suppression, Unicode byte limits, whole-item selection, empty sections, project isolation, and one unified envelope.
- [x] Run focused App tests and verify expected red failures.
- [x] Implement the builder and replace old handoff-only boot composition.
- [x] Run focused App tests to green.

### Task 3: First-turn send integration and failure semantics

**Files:**
- Modify: `src/Workbench.App/Services/AppServices.cs`
- Modify: `src/Workbench.App/ViewModels/MainWindowViewModel.cs`
- Modify: `src/Workbench.App/ViewModels/WorkspaceViewModel.cs`
- Modify: `src/Workbench.App/ViewModels/Panes/LeaderPaneViewModel.cs`
- Modify: `src/Workbench.App/ViewModels/Leader/ProjectLeaderSessionManager.cs`
- Modify: `tests/Workbench.App.Tests/AppTestContext.cs`
- Modify: `tests/Workbench.App.Tests/Support/FakeAgentRuntime.cs`
- Modify: `tests/Workbench.App.Tests/LeaderPaneViewModelTests.cs`
- Modify: `tests/Workbench.App.Tests/LeaderPersistenceTests.cs`
- Modify: `tests/Workbench.App.Tests/LeaderRolloverPolicyTests.cs`

**Interfaces:**
- Boot context is built at send time before clearing the draft, adding the visible user bubble, persisting a user row, or calling the provider.
- A null delivery timestamp selects boot; the first runtime event marks delivery; no later turn in that epoch injects again.
- Memory-load failure shows `Project memory could not be loaded.`, preserves the draft, and performs no user-message persistence, provider send, or replacement-epoch creation.
- A send failure before any runtime event leaves boot pending; retry and restart retry inject it again. A failure after the first event cannot duplicate boot on a later turn.

- [x] Add failing integration tests for first-ever, auto rollover, Ask→Start Fresh, Manual New Brain, zero-message restart, resumed conversation, later turns, send-time memory freshness, memory-load failure, pre-acceptance retry/restart, post-acceptance failure, visible/persisted-text privacy, pending-synthesis non-blocking, and cross-project isolation.
- [x] Run focused App tests and verify expected red failures.
- [x] Wire the builder and delivery transition through the single Leader send path without changing `IAgentRuntime`.
- [x] Run focused App and Storage tests to green.

### Task 4: Verification, real smoke, documentation, and release

**Files:**
- Modify: `README.md`
- Modify: this plan

- [x] Update README to show M1.5A and M1.5B Completed and M1.5C Current.
- [x] Run restore, solution build, every test project, and full solution tests with zero warnings/errors.
- [x] In `AI Game Workbench M105B Smoke`, create/certify `Memory Boot Smoke` with marker `MEMORY_BOOT_FORMAL_731`, start a Manual New Brain, verify distinct external session IDs, and send the exact no-files/no-commands smoke prompt.
- [x] Verify the real Codex reply is exactly `MEMORY_BOOT_FORMAL_731` and the persisted user row contains neither marker nor envelope.
- [x] Run fake-runtime Learned marker and Formal/Learned same-topic conflict smokes, verify provisional labeling and Formal suppression.
- [x] Verify database v6, foreign keys, integrity, protected-project isolation, and no unexpected current-epoch changes.
- [x] Inspect the final diff, commit as `feat(memory): inject project memory into leader boot`, and confirm `master` is clean.
