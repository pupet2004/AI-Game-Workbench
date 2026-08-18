# R5-A Summary Delta Minimal Slice Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Persist sparse Summary Delta emitted during normal Leader cognition, with no second model pass, and expose deterministic bounded project-local queries.

**Architecture:** The existing `LeaderPaneViewModel.SendCoreAsync` remains the operation owner. It sends one `AgentRequest` through `IAgentRuntime`, parses the existing structured `FinalText` envelope into the visible reply plus an optional Summary Delta sidecar, and then calls a narrowly scoped `ProjectSummaryRepository`. `AppServices` owns that repository and passes it through `MainWindowViewModel` and `WorkspaceViewModel`; the runtime contract and provider registry remain unchanged.

**Tech Stack:** C# / .NET 10 / Avalonia / SQLite / existing Agent Runtime abstraction

**Spec:** `docs/superpowers/specs/2026-08-15-truth-governance-v1-design.md`

## Global Constraints

- R5-A implements only optional `SummaryDelta` emission from one normal Leader cognition, append-only persistence, and an explicit bounded project-local query.
- `SummaryDelta` contains `occurred_at`, `kind`, `text`, and zero or more natural `source_refs`; `kind` is one of `Decision`, `Change`, `Constraint`, `RejectedPath`, or `Unresolved`.
- Workbench metadata is `entry_id`, `result_id`, `delta_ordinal`, and `created_at`; `UNIQUE(result_id, delta_ordinal)` is the replay identity.
- `project_summary_entries` and `project_summary_source_refs` are the complete R5-A conceptual persistence shape. No body/evidence copy, text/similarity deduplication, update API, delete API, or automatic migration of legacy Memory is allowed.
- `SummaryQuery` requires `project_id` and `limit`; `limit` is 1..200; optional filters are `kinds`, `occurred_from`, `occurred_to`, `source_kind`, and `source_locator`; ordering is `occurred_at DESC, created_at DESC, entry_id`.
- The optional sidecar is carried in the same structured response as the visible Leader response. A fresh operation performs exactly one runtime `SendAsync`; no second model pass, session-end summarizer, background extractor, synthesis job, retrieval, search, ranking, pagination framework, boot injection, or automatic context selection is introduced.
- `project_memory_items`, `project_memory_sources`, `project_memory_synthesis_jobs`, and Daily Summary state remain frozen read-only. Reopen, boot, rollover, and provider-resume paths must not query or inject Summary rows.
- No Truth Heads, Intent/Implemented Heads, Alignment, DRIFT, Truth Proposal, Evolution, Recall, BM25, embeddings, semantic search, global Source Platform, plugin/SPI layer, or UI overhaul is part of this slice.
- Concrete SQL types, DDL, FK/index implementation, migration number, repository/class/API names below are implementation-plan decisions only; the approved conceptual two-table shape is not weakened.
- Do not open, initialize, migrate, or write the live production database while executing this plan. Use temporary databases for tests and a disposable production copy for the final seal rehearsal.

## File / Responsibility Map

### Create

- `src/Workbench.Storage/Memory/ProjectSummaryModels.cs` — immutable Summary kinds, source references, deltas, stored entries, and query value objects with validation boundaries.
- `src/Workbench.Storage/Memory/ProjectSummaryRepository.cs` — append-only parent/child persistence, replay validation, and deterministic bounded query.
- `src/Workbench.Storage/Migrations/Migration019ProjectSummary.cs` — SQLite creation of the two Summary tables, constraints, and indexes inside the existing migration transaction.
- `tests/Workbench.Storage.Tests/Memory/ProjectSummaryRepositoryTests.cs` — storage RED/GREEN coverage for validation, atomic append, replay, ownership, and query ordering/filtering.
- `tests/Workbench.Storage.Tests/Database/ProjectSummaryMigrationTests.cs` — migration v19 schema, foreign-key, index, and legacy-preservation certification.
- `tests/Workbench.App.Tests/TruthGovernanceR5ACertificationTests.cs` — same-cognition Leader integration and negative regression certification.

### Modify

- `src/Workbench.Storage/Database/MigrationRunner.cs` — register `Migration019ProjectSummary` after v18.
- `src/Workbench.App/Leader/LeaderDraftProposalBuilder.cs` — extend the existing structured response record/parser/schema with an optional Summary Delta sidecar while preserving visible response and existing proposal/memory commands.
- `src/Workbench.App/ViewModels/Panes/LeaderPaneViewModel.cs` — create the Workbench-owned operation `result_id`, pass the parsed sidecar to the repository after a completed normal turn, and keep normal reply behavior independent of Summary persistence errors.
- `src/Workbench.App/Services/AppServices.cs` — construct and expose one `ProjectSummaryRepository` and pass it through composition.
- `src/Workbench.App/ViewModels/MainWindowViewModel.cs` — pass `ProjectSummaryRepository` into each workspace.
- `src/Workbench.App/ViewModels/WorkspaceViewModel.cs` — accept the repository dependency and pass it to `LeaderPaneViewModel`.
- `tests/Workbench.App.Tests/Worker/LeaderDraftProposalTests.cs` — structured envelope/schema tests for optional, valid, invalid, and absent Summary sidecars.
- `tests/Workbench.App.Tests/LeaderPaneViewModelTests.cs` — one-send, visible-reply, result identity, and Summary persistence failure behavior.
- `tests/Workbench.App.Tests/LeaderPersistenceTests.cs` — restart/replay assertions using the same operation identity where the existing persistence harness can exercise it.
- `tests/Workbench.App.Tests/LeaderBootIntegrationTests.cs` — prove Summary is not injected into Leader boot context.
- `tests/Workbench.App.Tests/LeaderSessionRolloverServiceTests.cs` — prove rollover does not read or write Summary rows.
- `tests/Workbench.App.Tests/ProviderIndependentProjectRecoveryCertificationTests.cs` — prove provider-independent reopen/resume recovery remains independent of Summary query.
- `tests/Workbench.App.Tests/LeaderMemoryPolicyCoordinatorTests.cs` — prove frozen legacy Memory policy remains no-op and Summary wiring does not reactivate it.
- `tests/Workbench.App.Tests/ProjectMemoryLearningUiTests.cs` — prove Daily Summary and legacy Memory UI state remains read-only.

## Task 1: Define And Validate Summary Contracts

- [ ] **Files:** Create `src/Workbench.Storage/Memory/ProjectSummaryModels.cs`; create `tests/Workbench.Storage.Tests/Memory/ProjectSummaryRepositoryTests.cs` with contract-focused tests.

  **Interfaces: Consumes** existing `DateTimeOffset`, `Guid`, `IReadOnlyList<T>`, and the approved R5-A field/kind vocabulary. **Produces** `SummaryDeltaKind`, `SummarySourceRef`, `SummaryDelta`, `StoredSummaryEntry`, and `SummaryQuery` with signatures used unchanged by Tasks 2–4:

  ```csharp
  public enum SummaryDeltaKind { Decision, Change, Constraint, RejectedPath, Unresolved }
  public sealed record SummarySourceRef(string SourceKind, string SourceLocator);
  public sealed record SummaryDelta(
      DateTimeOffset OccurredAt,
      SummaryDeltaKind Kind,
      string Text,
      IReadOnlyList<SummarySourceRef> SourceRefs);
  public sealed record StoredSummaryEntry(
      Guid EntryId,
      Guid ProjectId,
      DateTimeOffset OccurredAt,
      DateTimeOffset CreatedAt,
      SummaryDeltaKind Kind,
      string Text,
      Guid ResultId,
      int DeltaOrdinal,
      IReadOnlyList<SummarySourceRef> SourceRefs);
  public sealed record SummaryQuery(
      Guid ProjectId,
      int Limit,
      IReadOnlyList<SummaryDeltaKind>? Kinds = null,
      DateTimeOffset? OccurredFrom = null,
      DateTimeOffset? OccurredTo = null,
      string? SourceKind = null,
      string? SourceLocator = null);
  ```

  **RED:** Add `SummaryQuery_rejects_empty_project_or_limit_outside_1_to_200`, `SummaryDelta_rejects_blank_text`, `SummaryDelta_rejects_unknown_kind`, `SummarySourceRef_rejects_blank_kind_or_locator`, and `SummaryQuery_accepts_optional_filters`; tests fail because these types and validation do not exist.

  **Minimal implementation:** Put constructor validation in the value objects: reject `Guid.Empty`, blank text/kind/locator, null source collections, negative source ordinals when represented by repository input, limits below 1 or above 200, `OccurredFrom > OccurredTo`, and a null/empty kind filter only as “no kind filter.” Keep source references as natural locators only; do not add evidence/body fields or text normalization that changes user content.

  **GREEN:** The focused storage test class passes; every subsequent task can construct the exact records above without casts or stringly-typed kind checks; no model contains Truth Head, Recall, search, or legacy-memory fields.

  **Focused command:** `dotnet test tests/Workbench.Storage.Tests/Workbench.Storage.Tests.csproj --filter FullyQualifiedName~ProjectSummaryRepositoryTests`

  **Stop condition:** Stop if the approved five-kind enum, four-field delta payload, or five optional query filters cannot be represented without adding a new product concept; do not expand the model in this task.

  **Commit:** `feat(summary): define r5-a delta contracts`

## Task 2: Add Migration 019 And Append-Only Repository

- [ ] **Files:** Create `src/Workbench.Storage/Migrations/Migration019ProjectSummary.cs`; modify `src/Workbench.Storage/Database/MigrationRunner.cs`; create `src/Workbench.Storage/Memory/ProjectSummaryRepository.cs`; extend `tests/Workbench.Storage.Tests/Memory/ProjectSummaryRepositoryTests.cs`; create `tests/Workbench.Storage.Tests/Database/ProjectSummaryMigrationTests.cs`.

  **Interfaces: Consumes** Task 1 models, `WorkbenchDatabase`, `ProjectRepository`, the v18 `MigrationRunner` registration pattern, SQLite transactions, and existing `TemporaryDatabase`. **Produces** this concrete repository surface:

  ```csharp
  public sealed class ProjectSummaryRepository(WorkbenchDatabase database)
  {
      public Task<IReadOnlyList<StoredSummaryEntry>> AppendAsync(
          Guid projectId,
          Guid resultId,
          IReadOnlyList<SummaryDelta> deltas,
          DateTimeOffset createdAt,
          CancellationToken cancellationToken = default);

      public Task<IReadOnlyList<StoredSummaryEntry>> QueryAsync(
          SummaryQuery query,
          CancellationToken cancellationToken = default);
  }
  ```

  **RED:** Add `Migration019_creates_summary_tables_and_sets_user_version_19`, `AppendAsync_persists_parent_and_source_refs_atomically`, `AppendAsync_replays_same_result_and_ordinal_without_duplicate_rows`, `AppendAsync_rejects_same_identity_with_changed_payload`, `AppendAsync_rejects_unknown_project`, `QueryAsync_orders_by_occurred_created_entry`, `QueryAsync_applies_kind_and_date_filters`, `QueryAsync_applies_source_filters_without_duplicate_parents`, `QueryAsync_enforces_limit_1_to_200`, `Summary_tables_enforce_entry_and_source_foreign_keys`, and `Summary_migration_preserves_legacy_counts_and_synthesis_states`; tests fail because v19 and the repository do not exist.

  **Minimal implementation:**

  - `Migration019ProjectSummary.Version` is `19`; register it immediately after v18 in `MigrationRunner` using the existing transaction helper.
  - Create `project_summary_entries` with `entry_id TEXT PRIMARY KEY`, `project_id TEXT NOT NULL REFERENCES projects(id) ON DELETE CASCADE`, `occurred_at TEXT NOT NULL`, `created_at TEXT NOT NULL`, `kind TEXT NOT NULL CHECK(kind IN ('Decision','Change','Constraint','RejectedPath','Unresolved'))`, `text TEXT NOT NULL CHECK(length(trim(text)) > 0)`, `result_id TEXT NOT NULL`, `delta_ordinal INTEGER NOT NULL CHECK(delta_ordinal >= 0)`, and `UNIQUE(result_id, delta_ordinal)`.
  - Create `project_summary_source_refs` with `entry_id TEXT NOT NULL REFERENCES project_summary_entries(entry_id) ON DELETE CASCADE`, `ordinal INTEGER NOT NULL CHECK(ordinal >= 0)`, `source_kind TEXT NOT NULL CHECK(length(trim(source_kind)) > 0)`, `source_locator TEXT NOT NULL CHECK(length(trim(source_locator)) > 0)`, and `PRIMARY KEY(entry_id, ordinal)`.
  - Add a project/order index on `(project_id, occurred_at DESC, created_at DESC, entry_id)` and a source-filter index on `(source_kind, source_locator, entry_id)`. Do not add future-head columns.
  - `AppendAsync` validates project/result identities and nonempty deltas, assigns zero-based `delta_ordinal` in input order, generates a new `entry_id` per delta, and inserts parent plus all source refs in one transaction. Use `INSERT OR IGNORE` for replay, reread the existing `(result_id, delta_ordinal)` row, and throw if project, kind, timestamps, text, or source refs differ. Replaying equal content returns the existing rows and never updates or deletes them.
  - `QueryAsync` validates `SummaryQuery`, uses `EXISTS` for source filters so one parent appears once, applies project/kind/date predicates, and orders exactly `occurred_at DESC, created_at DESC, entry_id`. Load child refs ordered by `(entry_id, ordinal)` after the parent query within the same connection.

  **GREEN:** v19 initializes only temporary databases; parent/source writes are atomic; duplicate replay is idempotent by `result_id + delta_ordinal`; changed replay is rejected; source filters never duplicate parents; project ownership and all foreign keys are enforced; legacy table row counts and synthesis job states are unchanged.

  **Focused command:** `dotnet test tests/Workbench.Storage.Tests/Workbench.Storage.Tests.csproj --filter FullyQualifiedName~ProjectSummary`

  **Stop condition:** Stop if repository behavior requires update/delete APIs, text similarity, a third table, a global query scope, or a migration that reads legacy Memory. Keep the implementation inside these two tables and v19.

  **Commit:** `feat(storage): persist r5-a summary deltas`

## Task 3: Integrate Optional Sidecar In The Existing Same-Cognition Envelope

- [ ] **Files:** Modify `src/Workbench.App/Leader/LeaderDraftProposalBuilder.cs`; extend `tests/Workbench.App.Tests/Worker/LeaderDraftProposalTests.cs`.

  **Interfaces: Consumes** the existing `LeaderStructuredResponse.TryParse`, `LeaderResponseSchema.Json`, `AgentRequest.OutputSchema`, and `AgentResult.FinalText`. **Produces** an additive `SummaryDeltas` collection and nonfatal `SummaryDeltaError` on `LeaderStructuredResponse`; no change to `IAgentRuntime`, `AgentResult`, `AgentEvent`, `CodexAgentRuntime`, or provider registration.

  **RED:** Add `Schema_declares_optional_summary_deltas_without_changing_existing_required_fields`, `Valid_summary_sidecar_parses_without_polluting_visible_response`, `Absent_summary_sidecar_is_normal_no_summary`, `Invalid_kind_or_blank_text_discards_only_summary_sidecar`, `Malformed_summary_item_keeps_core_response_and_proposal`, and `Runtime_receives_one_request_with_summary_schema`; tests fail because the schema/parser has no Summary field and invalid sidecars currently cannot be classified independently.

  **Minimal implementation:** Extend the existing record with `IReadOnlyList<SummaryDelta> SummaryDeltas` defaulting to `[]` and nullable `string? SummaryDeltaError`. Add optional `summary_deltas` to the existing JSON schema as `null` or an array of objects with `occurred_at`, `kind`, `text`, and `source_refs`; do not make it required so old structured responses remain valid. Parse the core `response`, `draft_proposal`, and `memory_commands` exactly as today, then parse the sidecar in a separate guarded block. Require ISO round-trip timestamps, one of the five enum values, nonblank text, and natural nonblank source kind/locator; if any sidecar item is invalid, return the visible core response/proposal with `SummaryDeltas=[]` and a nonfatal `SummaryDeltaError`. Missing or null sidecar is `[]` with no error. Keep existing malformed-core behavior and existing library/draft command handling unchanged.

  **GREEN:** The focused Worker tests pass; visible `response` never contains sidecar JSON; absent/invalid optional sidecars do not create an error-only UI result; the schema still accepts current proposal/memory fixtures; `FakeAgentRuntime.SentRequests.Count` is one for the operation.

  **Focused command:** `dotnet test tests/Workbench.App.Tests/Workbench.App.Tests.csproj --filter FullyQualifiedName~LeaderDraftProposalTests`

  **Stop condition:** Stop if the design requires changing `IAgentRuntime`, adding provider-specific metadata, parsing a second response, or making Summary mandatory for existing structured responses.

  **Commit:** `feat(leader): parse same-response summary sidecar`

## Task 4: Persist Summary From The Normal Leader Operation

- [ ] **Files:** Modify `src/Workbench.App/ViewModels/Panes/LeaderPaneViewModel.cs`, `src/Workbench.App/Services/AppServices.cs`, `src/Workbench.App/ViewModels/MainWindowViewModel.cs`, and `src/Workbench.App/ViewModels/WorkspaceViewModel.cs`; extend `tests/Workbench.App.Tests/LeaderPaneViewModelTests.cs` and `tests/Workbench.App.Tests/LeaderPersistenceTests.cs`; extend `tests/Workbench.App.Tests/TruthGovernanceR5ACertificationTests.cs`.

  **Interfaces: Consumes** Task 2 `ProjectSummaryRepository.AppendAsync`, Task 3 `LeaderStructuredResponse.SummaryDeltas`, existing `ProjectLeaderSessionManager.PersistUserMessageAsync`/`PersistCompletedAssistantAsync`, and `_timeProvider`. **Produces** one Workbench-owned `Guid result_id` per `SendCoreAsync` operation and the existing normal visible assistant reply plus optional persisted Summary entries.

  **Stable result_id owner:** Add a private `LeaderOperationContext` record in `LeaderPaneViewModel.cs` (or a private nested record) with `Guid ResultId` and the operation's input/turn lifetime. Instantiate it with `Guid.NewGuid()` at the beginning of each fresh `SendCoreAsync` invocation, before user-message persistence and before `runtime.SendAsync`. Pass that context through completion and Summary append retry code. The current product has no durable Leader replay command; a fresh user send, `ContinuePreviousAsync`, or `StartFreshAsync` creates a new context, while reprocessing the same completed turn or retrying its Summary append within that operation reuses the same `ResultId`. Never derive it from response text, model output, or a hash. Do not add a staging table or distributed transaction.

  **RED:** Add `Completed_turn_persists_summary_with_operation_result_id_and_ordinals`, `No_summary_keeps_existing_assistant_persistence`, `One_leader_operation_sends_once_and_preserves_visible_reply`, `Repeated_append_with_same_operation_context_is_idempotent`, `Summary_persistence_failure_keeps_visible_reply_and_reports_retryable_error`, `Fresh_send_gets_new_result_id`, `Boot_gate_path_does_not_emit_summary`, and `AppServices_composes_one_project_summary_repository`; tests fail because the ViewModel has no Summary repository dependency, no operation identity, and no append call.

  **Minimal implementation:**

  - Add nullable `ProjectSummaryRepository? summaryRepository = null` to the end of the existing `LeaderPaneViewModel` constructor to preserve current test fixture call sites; wire the real instance from `AppServices` through `MainWindowViewModel` and `WorkspaceViewModel`.
  - Generate the operation context before `_conversation.IsBusy = true`; retain it through user persistence, the single runtime enumeration, structured parsing, and completed-turn handling. The response binder early-return path has no runtime completion and therefore writes no Summary.
  - On `AgentTurnCompleted` with `FinalStatus == Completed`, keep the current `finalText` visible/persisted behavior, then call `AppendAsync(_project.Id, operation.ResultId, structured.SummaryDeltas, _timeProvider.GetUtcNow(), cancellationToken)` only when the parsed sidecar is nonempty. Use the same operation context for any in-process append retry. A sidecar parse error is surfaced as nonfatal `MemoryCommandStatus`-style status; it does not replace the visible response.
  - Catch Summary append failures separately after normal assistant persistence, add the existing user-facing retryable error message, and leave the normal assistant message intact. Do not roll back leader messages, issue a second model call, or invoke legacy Memory services.
  - Preserve current proposal/library command side effects and current failed/interrupted/stopped turn semantics. Summary is written only for completed normal cognition.

  **GREEN:** The focused App tests pass; one normal send produces exactly one `FakeAgentRuntime.SendAsync`, visible assistant text remains the core response, one result ID is attached to all delta rows, repeated append with that ID produces no duplicates, fresh sends use different IDs, and Summary failure never erases an already persisted normal reply.

  **Focused command:** `dotnet test tests/Workbench.App.Tests/Workbench.App.Tests.csproj --filter FullyQualifiedName~LeaderPaneViewModelTests|FullyQualifiedName~LeaderPersistenceTests|FullyQualifiedName~TruthGovernanceR5ACertificationTests`

  **Stop condition:** Stop if wiring requires a second runtime request, a provider-specific API, automatic boot/reopen query, durable operation table, or a change to legacy Memory policy. Escalate an architecture decision instead of expanding this task.

  **Commit:** `feat(app): persist leader summary deltas`

## Task 5: R5-A Negative Regression And Certification

- [ ] **Files:** Create/extend `tests/Workbench.App.Tests/TruthGovernanceR5ACertificationTests.cs`; modify `tests/Workbench.App.Tests/LeaderBootIntegrationTests.cs`, `tests/Workbench.App.Tests/LeaderSessionRolloverServiceTests.cs`, `tests/Workbench.App.Tests/ProviderIndependentProjectRecoveryCertificationTests.cs`, `tests/Workbench.App.Tests/LeaderMemoryPolicyCoordinatorTests.cs`, and `tests/Workbench.App.Tests/ProjectMemoryLearningUiTests.cs`; extend `tests/Workbench.Storage.Tests/Database/ProjectSummaryMigrationTests.cs`.

  **Interfaces: Consumes** the completed Task 1–4 contracts, existing boot/reopen/rollover/provider-resume certification fixtures, frozen `LeaderMemoryPolicyCoordinator`, `LeaderBootContextBuilder`, `ProjectMemorySynthesisRepository.RecoverRunningAsync`, and temporary database helpers. **Produces** a reviewer-visible R5-A certification set proving the new path is isolated from frozen continuity behavior.

  **RED:** Add `Project_reopen_does_not_query_or_inject_summary`, `Leader_boot_context_contains_no_summary_rows`, `Session_rollover_does_not_read_or_write_summary`, `Provider_resume_recovery_succeeds_without_summary_query`, `Legacy_memory_policy_remains_noop_after_summary_wiring`, `Daily_summary_remains_read_only`, `Completed_turn_does_not_create_synthesis_job_or_second_send`, `Summary_query_is_project_local`, and `Migration_preserves_legacy_rows_and_job_states`; tests fail until the complete wiring and explicit negative assertions exist.

  **Minimal implementation:** Instrument the existing fake repository/runtime seams with call counters or a test double only in test code. Assert reopen, boot, rollover, and provider resume call the existing Continuity Kernel paths without `ProjectSummaryRepository.QueryAsync`; assert no Summary text appears in boot context. Seed legacy Memory, Daily Summary, and pending synthesis-job rows in a temporary database, run app initialization and a completed Leader turn, and assert counts/states are unchanged, no synthesis recovery occurs, and the runtime send count remains one. Assert a Summary query with another project ID returns no rows.

  **GREEN:** All negative tests pass while Summary append/query tests remain green; no existing R4 freeze certification regresses; no automatic Summary injection or retrieval exists; provider-independent recovery remains valid when the runtime is unavailable.

  **Focused command:** `dotnet test tests/Workbench.App.Tests/Workbench.App.Tests.csproj --filter FullyQualifiedName~TruthGovernanceR5ACertificationTests|FullyQualifiedName~LeaderBootIntegrationTests|FullyQualifiedName~LeaderSessionRolloverServiceTests|FullyQualifiedName~ProviderIndependentProjectRecoveryCertificationTests|FullyQualifiedName~LeaderMemoryPolicyCoordinatorTests|FullyQualifiedName~ProjectMemoryLearningUiTests`

  **Stop condition:** Stop if any test needs Summary in boot context, a legacy table migration, a synthesis job, a second runtime request, Recall/search, a provider SPI, or UI search. Those are outside R5-A.

  **Commit:** `test(cert): seal r5-a negative boundaries`

## Final Verification And Disposable Production-Copy Seal

- [ ] Run the focused test command for each task before accepting its commit. Do not run implementation tasks during this planning-only change.
- [ ] After all implementation tasks are complete, run the full suites in this order:

  ```powershell
  dotnet test tests/Workbench.Core.Tests/Workbench.Core.Tests.csproj
  dotnet test tests/Workbench.Storage.Tests/Workbench.Storage.Tests.csproj
  dotnet test tests/Workbench.Project.Tests/Workbench.Project.Tests.csproj
  dotnet test tests/Workbench.Runtime.Tests/Workbench.Runtime.Tests.csproj
  dotnet test tests/Workbench.App.Tests/Workbench.App.Tests.csproj
  dotnet build
  git diff --check
  ```

- [ ] Perform the final seal against a disposable copy only. Read a hash of `%LOCALAPPDATA%\AI Game Workbench\workbench.db` before touching the copy; create a consistent temporary copy without modifying the live file; construct `WorkbenchDatabase` for the copy and call `InitializeAsync`; assert `PRAGMA integrity_check` is `ok`, `PRAGMA foreign_key_check` returns zero rows, v19 tables and indexes exist, duplicate `(result_id, delta_ordinal)` replay is idempotent, and legacy table counts plus synthesis-job states match the pre-copy snapshot. Hash the live database again and require the before/after hashes to match. Delete only the disposable copy after the rehearsal.
- [ ] Confirm no live database handle was opened by the implementation test run, no production code is changed by the plan-writing task, and the final implementation tree contains only the intended R5-A files.

## Plan Self-Review Checklist

- [ ] Every approved R5-A field, kind, metadata constraint, source-ref rule, query filter, limit, ordering rule, and append/replay rule is covered by a named task and test.
- [ ] `result_id` is Workbench-generated at fresh `SendCoreAsync` start, reused for in-process replay, never model-generated, and never derived from text.
- [ ] The same normal Leader request yields visible output plus optional Summary deltas; `no-summary` is a normal result; invalid optional sidecars degrade to visible output only.
- [ ] No task introduces a second model pass, Provider SPI, automatic boot injection, legacy migration, Recall, search, ranking, pagination, Truth Heads, Proposal, or Evolution.
- [ ] No task uses vague implementation placeholders; all files, interfaces, method signatures, test names, commands, stop conditions, and commit messages are explicit.
