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
- `src/Workbench.App/Leader/LeaderSummaryRecoveryService.cs` — restart recovery that reads pending durable LeaderResult payloads from existing Leader message persistence and mechanically replays Summary append.
- `tests/Workbench.Storage.Tests/Memory/ProjectSummaryRepositoryTests.cs` — storage RED/GREEN coverage for validation, atomic append, replay, ownership, and query ordering/filtering.
- `tests/Workbench.Storage.Tests/Database/ProjectSummaryMigrationTests.cs` — migration v19 schema, foreign-key, index, and legacy-preservation certification.
- `tests/Workbench.Storage.Tests/Leaders/LeaderSummaryMetadataTests.cs` — durable result metadata, pending-row discovery, completion marking, and restart persistence coverage.
- `tests/Workbench.App.Tests/TruthGovernanceR5ACertificationTests.cs` — same-cognition Leader integration and negative regression certification.

### Modify

- `src/Workbench.Storage/Database/MigrationRunner.cs` — register `Migration019ProjectSummary` after v18.
- `src/Workbench.Storage/Leaders/LeaderMessageRepository.cs` — persist and enumerate LeaderResult metadata in the existing `leader_messages` table and mark Summary append completion.
- `src/Workbench.Storage/Leaders/StoredLeaderMessage.cs` — expose nullable durable result payload, result ID, and Summary completion timestamp.
- `src/Workbench.App/Leader/LeaderDraftProposalBuilder.cs` — extend the existing structured response record/parser/schema with an optional Summary Delta sidecar while preserving visible response and existing proposal/memory commands.
- `src/Workbench.App/Leader/LeaderSummaryRecoveryService.cs` — compose storage metadata and Summary repository for no-model-call restart recovery.
- `src/Workbench.App/ViewModels/Panes/LeaderPaneViewModel.cs` — create the Workbench-owned operation `result_id`, pass the parsed sidecar to the repository after a completed normal turn, and keep normal reply behavior independent of Summary persistence errors.
- `src/Workbench.App/Services/AppServices.cs` — construct and expose one `ProjectSummaryRepository` and pass it through composition.
- `src/Workbench.App/ViewModels/MainWindowViewModel.cs` — pass `ProjectSummaryRepository` into each workspace.
- `src/Workbench.App/ViewModels/WorkspaceViewModel.cs` — accept the repository dependency and pass it to `LeaderPaneViewModel`.
- `tests/Workbench.App.Tests/Worker/LeaderDraftProposalTests.cs` — structured envelope/schema tests for optional, valid, invalid, and absent Summary sidecars.
- `tests/Workbench.App.Tests/LeaderSummaryRecoveryTests.cs` — crash-window replay, post-append replay, and zero-runtime-send recovery tests using a temporary database.
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

## Task 2: Add Migration 019, Summary Storage, And Durable LeaderResult Metadata

- [ ] **Files:** Create `src/Workbench.Storage/Migrations/Migration019ProjectSummary.cs` and `src/Workbench.Storage/Memory/ProjectSummaryRepository.cs`; modify `src/Workbench.Storage/Database/MigrationRunner.cs`, `src/Workbench.Storage/Leaders/LeaderMessageRepository.cs`, and `src/Workbench.Storage/Leaders/StoredLeaderMessage.cs`; extend `tests/Workbench.Storage.Tests/Memory/ProjectSummaryRepositoryTests.cs` and `tests/Workbench.Storage.Tests/Database/ProjectSummaryMigrationTests.cs`; create `tests/Workbench.Storage.Tests/Leaders/LeaderSummaryMetadataTests.cs`.

  **Interfaces: Consumes** Task 1 models, `WorkbenchDatabase`, `ProjectRepository`, the v18 `MigrationRunner` registration pattern, SQLite transactions, and existing `TemporaryDatabase`. **Produces** this concrete Summary repository surface and durable Leader message metadata surface:

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

  `LeaderMessageRepository.AppendAsync` gains an optional `LeaderResultMetadata? metadata` argument for assistant rows. `LeaderResultMetadata` contains `Guid ResultId` and the raw structured `ResultPayloadJson`; `StoredLeaderMessage` exposes `Guid? ResultId`, `string? ResultPayloadJson`, and `DateTimeOffset? SummaryPersistedAt`. Add `GetPendingSummaryResultsAsync` returning assistant rows whose metadata is present and `SummaryPersistedAt IS NULL`, plus `MarkSummaryPersistedAsync(long messageId, Guid resultId, DateTimeOffset persistedAt)`. These are extensions of the existing `leader_messages` repository, not a generic result framework.

  **RED:** Add `Migration019_creates_summary_tables_and_sets_user_version_19`, `AppendAsync_persists_parent_and_source_refs_atomically`, `AppendAsync_replays_same_result_and_ordinal_without_duplicate_rows`, `AppendAsync_rejects_same_identity_with_changed_payload`, `AppendAsync_rejects_unknown_project`, `QueryAsync_orders_by_occurred_created_entry`, `QueryAsync_applies_kind_and_date_filters`, `QueryAsync_applies_source_filters_without_duplicate_parents`, `QueryAsync_enforces_limit_1_to_200`, `Summary_tables_enforce_entry_and_source_foreign_keys`, `Leader_message_metadata_round_trips_result_id_and_raw_payload`, `Pending_summary_query_returns_only_unmarked_structured_assistant_rows`, and `MarkSummaryPersisted_is_idempotent_for_same_message_and_result`; tests fail because v19, Summary storage, and durable LeaderResult metadata do not exist.

  **Minimal implementation:**

  - `Migration019ProjectSummary.Version` is `19`; register it immediately after v18 in `MigrationRunner` using the existing transaction helper.
  - In the same v19 migration, add nullable `result_id TEXT`, `result_payload_json TEXT`, and `summary_persisted_at TEXT` columns to the existing `leader_messages` table. Enforce that `result_payload_json` and `result_id` are present together and only on assistant rows with a table check; add an index on `(summary_persisted_at, result_id)` for pending recovery. Existing rows remain null and unchanged.
  - Create `project_summary_entries` with `entry_id TEXT PRIMARY KEY`, `project_id TEXT NOT NULL REFERENCES projects(id) ON DELETE CASCADE`, `occurred_at TEXT NOT NULL`, `created_at TEXT NOT NULL`, `kind TEXT NOT NULL CHECK(kind IN ('Decision','Change','Constraint','RejectedPath','Unresolved'))`, `text TEXT NOT NULL CHECK(length(trim(text)) > 0)`, `result_id TEXT NOT NULL`, `delta_ordinal INTEGER NOT NULL CHECK(delta_ordinal >= 0)`, and `UNIQUE(result_id, delta_ordinal)`.
  - Create `project_summary_source_refs` with `entry_id TEXT NOT NULL REFERENCES project_summary_entries(entry_id) ON DELETE CASCADE`, `ordinal INTEGER NOT NULL CHECK(ordinal >= 0)`, `source_kind TEXT NOT NULL CHECK(length(trim(source_kind)) > 0)`, `source_locator TEXT NOT NULL CHECK(length(trim(source_locator)) > 0)`, and `PRIMARY KEY(entry_id, ordinal)`.
  - Add a project/order index on `(project_id, occurred_at DESC, created_at DESC, entry_id)` and a source-filter index on `(source_kind, source_locator, entry_id)`. Do not add future-head columns.
  - `AppendAsync` validates project/result identities and nonempty deltas, assigns zero-based `delta_ordinal` in input order, generates a new `entry_id` per delta, and inserts parent plus all source refs in one transaction. Use `INSERT OR IGNORE` for replay, reread the existing `(result_id, delta_ordinal)` row, and throw if project, kind, timestamps, text, or source refs differ. Replaying equal content returns the existing rows and never updates or deletes them.
  - `QueryAsync` validates `SummaryQuery`, uses `EXISTS` for source filters so one parent appears once, applies project/kind/date predicates, and orders exactly `occurred_at DESC, created_at DESC, entry_id`. Load child refs ordered by `(entry_id, ordinal)` after the parent query within the same connection.
  - `LeaderMessageRepository` writes the visible assistant text and the raw structured `ResultPayloadJson` plus `ResultId` in the same existing message transaction. `GetPendingSummaryResultsAsync` joins `leader_messages` to `leader_session_epochs` to return project ownership, message ID, result ID, payload, and creation time; `MarkSummaryPersistedAsync` updates only a matching pending row and treats an already-marked identical row as success.

  **GREEN:** v19 initializes only temporary databases; parent/source writes are atomic; duplicate replay is idempotent by `result_id + delta_ordinal`; changed replay is rejected; source filters never duplicate parents; project ownership and all foreign keys are enforced; structured LeaderResult metadata survives database restart; pending discovery excludes marked rows; legacy table row counts and synthesis job states are unchanged.

  **Focused command:** `dotnet test tests/Workbench.Storage.Tests/Workbench.Storage.Tests.csproj --filter FullyQualifiedName~ProjectSummary`

  **Stop condition:** Stop if repository behavior requires update/delete APIs, text similarity, a third table, a global query scope, or a migration that reads legacy Memory. Keep the implementation inside these two tables and v19.

  **Commit:** `feat(storage): persist r5-a summary deltas`

## Task 3: Admit Sparse Summary In The Existing Same-Cognition Envelope

- [ ] **Files:** Modify `src/Workbench.App/Leader/LeaderDraftProposalBuilder.cs` and `src/Workbench.App/ViewModels/Panes/LeaderPaneViewModel.cs`; extend `tests/Workbench.App.Tests/Worker/LeaderDraftProposalTests.cs` and `tests/Workbench.App.Tests/LeaderPaneViewModelTests.cs`.

  **Reconnaissance:** Normal Leader turns currently have no separate prompt builder: `SendCoreAsync` sends the user/boot text with `LeaderResponseSchema.Json`, while `LeaderDraftProposalBuilder.cs` owns the structured response record and schema. The schema uses `additionalProperties: false` and a `required` list with nullable `draft_proposal` and `memory_commands`; therefore R5-A must add `summary_deltas` to `required` as an `anyOf` array-or-null field. No arbitrary optional property is assumed.

  **Interfaces: Consumes** the existing `LeaderStructuredResponse.TryParse`, `LeaderResponseSchema.Json`, `AgentRequest.OutputSchema`, and `AgentResult.FinalText`. **Produces** an additive `SummaryDeltas` collection, nonfatal `SummaryDeltaError`, and a `LeaderSummaryAdmissionInstruction.Text` constant owned beside the existing structured response schema. `SendCoreAsync` composes that instruction into the same `AgentRequest.Text` used for the one runtime call; no change to `IAgentRuntime`, `AgentResult`, `AgentEvent`, `CodexAgentRuntime`, or provider registration.

  **RED:** Add `Schema_requires_nullable_summary_deltas_under_existing_strict_convention`, `Admission_instruction_is_present_in_the_same_sent_request_as_output_schema`, `Valid_summary_sidecar_parses_without_polluting_visible_response`, `Null_summary_sidecar_is_normal_no_summary`, `Invalid_kind_or_blank_text_discards_only_summary_sidecar`, `Malformed_summary_item_keeps_core_response_and_proposal`, and `Admission_instruction_forbids_routine_facts_and_fabricated_sources`; tests fail because the schema/parser and same-request admission instruction do not exist.

  **Minimal implementation:** Extend the existing record with `IReadOnlyList<SummaryDelta> SummaryDeltas` defaulting to `[]` and nullable `string? SummaryDeltaError`. Add `summary_deltas` to the strict schema `required` list as `anyOf` null or an array of objects with `occurred_at`, `kind`, `text`, and `source_refs`; update all existing fixtures to send explicit `"summary_deltas": null`. Define `LeaderSummaryAdmissionInstruction.Text` with these exact rules: Summary is sparse durable rationale; emit only a decision/change/constraint/rejected path/unresolved item whose removal would make a future Leader more likely to make a wrong decision, repeat an important dead end, or misunderstand the reason for a current constraint or decision; use only the five allowed kinds; omit tests passed, build passed, files changed, Worker PASS, routine implementation steps, routine tool output, session chatter, and generic Fact/Note/Progress/Result/Memory; use only natural source locators, and use an empty `source_refs` array when no natural locator exists; never fabricate provenance. Compose the instruction with the existing boot-generated or direct user text before constructing `AgentRequest(text, LeaderResponseSchema.Json)`.

  Parse the core `response`, `draft_proposal`, and `memory_commands` exactly as today, then parse the required nullable sidecar in a separate guarded block. Require ISO round-trip timestamps, one of the five enum values, nonblank text, and natural nonblank source kind/locator; if any sidecar item is invalid, return the visible core response/proposal with `SummaryDeltas=[]` and a nonfatal `SummaryDeltaError`. Null sidecar is `[]` with no error. Keep existing malformed-core behavior and existing library/draft command handling unchanged.

  **GREEN:** The focused App tests pass; visible `response` never contains sidecar JSON; explicit null/invalid sidecars do not create an error-only UI result; the schema follows the existing strict required-plus-nullable convention; the admission instruction and output schema are present on the same `FakeAgentRuntime.SentRequests.Single()` request; runtime send count is one.

  **Focused command:** `dotnet test tests/Workbench.App.Tests/Workbench.App.Tests.csproj --filter FullyQualifiedName~LeaderDraftProposalTests`

  **Stop condition:** Stop if the design requires changing `IAgentRuntime`, adding provider-specific metadata, parsing a second response, or making Summary mandatory for existing structured responses.

  **Commit:** `feat(leader): parse same-response summary sidecar`

## Task 4: Persist, Recover, And Replay The Durable LeaderResult

- [ ] **Files:** Create `src/Workbench.App/Leader/LeaderSummaryRecoveryService.cs` and `tests/Workbench.App.Tests/LeaderSummaryRecoveryTests.cs`; modify `src/Workbench.App/ViewModels/Panes/LeaderPaneViewModel.cs`, `src/Workbench.App/Services/AppServices.cs`, `src/Workbench.App/ViewModels/MainWindowViewModel.cs`, and `src/Workbench.App/ViewModels/WorkspaceViewModel.cs`; extend `tests/Workbench.App.Tests/LeaderPaneViewModelTests.cs` and `tests/Workbench.App.Tests/LeaderPersistenceTests.cs`.

  **Interfaces: Consumes** Task 2 `ProjectSummaryRepository.AppendAsync`, `LeaderMessageRepository.GetPendingSummaryResultsAsync`/`MarkSummaryPersistedAsync`, Task 3 `LeaderStructuredResponse.SummaryDeltas` and admission envelope, existing `ProjectLeaderSessionManager.PersistUserMessageAsync`/`PersistCompletedAssistantAsync`, and `_timeProvider`. **Produces** a crash-durable Workbench-owned `result_id`, durable raw SummaryDelta source, mechanical restart replay, and the existing normal visible assistant reply.

  **Durable result owner:** `LeaderPaneViewModel.SendCoreAsync` creates `Guid resultId = Guid.NewGuid()` before user-message persistence and before the single `runtime.SendAsync`. On a completed structured turn with one or more valid Summary deltas, `ProjectLeaderSessionManager.PersistCompletedAssistantAsync` writes the visible assistant text plus `LeaderResultMetadata(resultId, completed.Result.FinalText)` to one `leader_messages` row before Summary append; it must write that row even when the visible response is empty so the durable result is never lost. The durable `leader_messages.result_id` is the stable identity; `leader_messages.result_payload_json` is the durable source for the exact SummaryDelta[] after restart. A completed null-summary turn has no pending metadata. A fresh user send creates a new ID. No model output, response text hash, session ID, or transient operation context is used as identity.

  **Crash window and recovery:** The sequence is (1) one runtime completion, (2) durable LeaderResult message metadata, (3) `ProjectSummaryRepository.AppendAsync` with the same result ID and delta ordinals, (4) `LeaderMessageRepository.MarkSummaryPersistedAsync`. If the process exits between steps 2 and 3, `LeaderSummaryRecoveryService.RecoverAsync` reads pending rows from `GetPendingSummaryResultsAsync`, parses the stored payload with `LeaderStructuredResponse.TryParse`, calls `AppendAsync` without any runtime, and marks the message. If it exits after step 3 and before step 4, the same replay is accepted by `UNIQUE(result_id, delta_ordinal)` and then marked; no duplicate rows result. Recovery runs from `AppServices.InitializeAsync` after database initialization and before UI boot, so reopen and provider-unavailable paths do not require a second Leader request.

  **RED:** Add `Completed_turn_writes_durable_result_id_and_raw_payload_before_summary_append`, `One_fresh_operation_creates_one_result_id_with_ordinals_zero_through_n_minus_one`, `Crash_after_durable_result_before_append_reopens_and_appends_without_runtime_send`, `Crash_after_append_before_mark_reopens_without_duplicate`, `Changed_payload_under_same_result_id_and_ordinal_is_rejected`, `Fresh_user_operation_gets_new_result_id`, `No_summary_keeps_existing_assistant_persistence`, and `Summary_append_failure_keeps_visible_reply`; tests fail because no durable metadata, recovery service, or composition exists.

  **Minimal implementation:**

  - Add nullable `ProjectSummaryRepository? summaryRepository = null` and `LeaderSummaryRecoveryService? summaryRecovery = null` dependencies at the ends of existing constructors to preserve current fixture call sites; wire concrete instances from `AppServices` through `MainWindowViewModel` and `WorkspaceViewModel`.
  - Extend `ProjectLeaderSessionManager.PersistCompletedAssistantAsync` with `LeaderResultMetadata? metadata`; write metadata in the same assistant message transaction, while worker handoffs continue passing null metadata. Persist the visible response, never raw JSON, to the normal message text column.
  - On `AgentTurnCompleted` with `FinalStatus == Completed`, parse the same `completed.Result.FinalText`, persist the durable LeaderResult before append, then call `AppendAsync(_project.Id, resultId, structured.SummaryDeltas, _timeProvider.GetUtcNow(), cancellationToken)` only when deltas are nonempty. Catch Summary append failures after durable message persistence, keep the visible reply, and leave the row pending for restart recovery. Do not issue a second model call or invoke legacy Memory services.
  - `LeaderSummaryRecoveryService` uses only `GetPendingSummaryResultsAsync`, `LeaderStructuredResponse.TryParse`, `ProjectSummaryRepository.AppendAsync`, and `MarkSummaryPersistedAsync`; it never calls `IAgentRuntime`, `LeaderBootContextBuilder`, `SummaryQuery`, or any legacy Memory API.
  - Preserve current proposal/library command side effects and failed/interrupted/stopped turn semantics. Null Summary is a completed LeaderResult with no pending recovery work.

  **GREEN:** The focused App tests pass; one normal operation sends once, produces one durable result ID, stores N deltas with ordinals `0..N-1`, and keeps visible text unchanged; both crash windows recover after restart with zero `SendAsync` calls; replay is idempotent; changed payload is rejected; fresh operations receive distinct IDs; Summary failure does not erase the normal reply.

  **Focused command:** `dotnet test tests/Workbench.App.Tests/Workbench.App.Tests.csproj --filter FullyQualifiedName~LeaderSummaryRecoveryTests|FullyQualifiedName~LeaderPaneViewModelTests|FullyQualifiedName~LeaderPersistenceTests`

  **Stop condition:** Stop if recovery cannot obtain both the same `result_id` and raw SummaryDelta payload from existing `leader_messages` metadata, or if it requires a third Summary/staging/outbox table, a second runtime request, a provider-specific API, automatic boot injection, or a legacy Memory change. Report `PLAN_BLOCKED_BY_ARCHITECTURE_DECISION` instead of weakening crash tests.

  **Commit:** `feat(app): make r5-a leader results crash durable`

## Task 5: R5-A Negative Regression And Certification

- [ ] **Files:** Create/extend `tests/Workbench.App.Tests/TruthGovernanceR5ACertificationTests.cs`; modify `tests/Workbench.App.Tests/LeaderBootIntegrationTests.cs`, `tests/Workbench.App.Tests/LeaderSessionRolloverServiceTests.cs`, `tests/Workbench.App.Tests/ProviderIndependentProjectRecoveryCertificationTests.cs`, `tests/Workbench.App.Tests/LeaderMemoryPolicyCoordinatorTests.cs`, and `tests/Workbench.App.Tests/ProjectMemoryLearningUiTests.cs`; extend `tests/Workbench.Storage.Tests/Database/ProjectSummaryMigrationTests.cs`.

  **Interfaces: Consumes** the completed Task 1–4 contracts, existing boot/reopen/rollover/provider-resume certification fixtures, frozen `LeaderMemoryPolicyCoordinator`, `LeaderBootContextBuilder`, `ProjectMemorySynthesisRepository.RecoverRunningAsync`, `FakeAgentRuntime.SentRequests`, and temporary database helpers. **Produces** a reviewer-visible R5-A certification set proving the new path is isolated from frozen continuity behavior without introducing an unowned repository test seam.

  **BASELINE CHARACTERIZATION (run before R5-A implementation):** Run the existing `LeaderBootIntegrationTests`, `LeaderSessionRolloverServiceTests`, `ProviderIndependentProjectRecoveryCertificationTests`, `LeaderMemoryPolicyCoordinatorTests`, and `ProjectMemoryLearningUiTests` unchanged and record that these already-green invariants remain green: no boot Summary injection, no rollover Summary read/write, provider-independent recovery, frozen legacy Memory/Daily behavior, and no second runtime send. These are not fabricated RED tests and do not require a Summary repository double.

  **RED for new behavior only:** Add `Summary_query_is_project_local_after_wiring`, `Migration_preserves_legacy_rows_and_job_states_while_adding_only_v19_summary_objects`, `Durable_pending_result_recovery_does_not_change_boot_context`, and `Completed_turn_does_not_create_synthesis_job_or_second_send`; these fail only because the new Summary query, v19 objects, durable recovery wiring, and same-cognition append are absent.

  **Minimal implementation:** Use a real temporary `WorkbenchDatabase`, concrete `ProjectSummaryRepository`, existing `FakeAgentRuntime.SentRequests`, and existing persistence repositories. Do not add a call counter or fake for the sealed repository. Assert Summary queries with another project ID return no rows; assert migration v19 adds exactly the two Summary tables plus the documented nullable metadata columns/index and leaves legacy counts/job states unchanged; seed a pending durable LeaderResult and run `AppServices.InitializeAsync`, then assert recovery does not alter boot context and `SentRequests.Count` remains zero; run a completed turn and assert no synthesis job is created and send count remains one. Re-run the baseline suites after wiring and require their original assertions to remain green.

  **POST-WIRING CERTIFICATION:** All baseline characterization suites and the four new tests pass; reopen, boot, rollover, provider resume, frozen Memory/Daily behavior, and no-second-send invariants remain green; pending Summary recovery is mechanical and invisible to boot; no automatic Summary injection or retrieval exists; provider-independent recovery remains valid when the runtime is unavailable.

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
- [ ] `result_id` is crash-durable in `leader_messages.result_id`; `leader_messages.result_payload_json` is the durable SummaryDelta source after restart; `summary_persisted_at` gates mechanical recovery.
- [ ] The same normal Leader request yields visible output plus optional Summary deltas; `no-summary` is a normal result; invalid optional sidecars degrade to visible output only.
- [ ] `CRASH_RECOVERY_WITHOUT_SECOND_LLM = YES`; `DURABLE_RESULT_ID_OWNER = leader_messages.result_id written by ProjectLeaderSessionManager`; `DURABLE_SUMMARY_DELTA_SOURCE_AFTER_RESTART = leader_messages.result_payload_json`; `SAME_COGNITION_ADMISSION_INSTRUCTION = YES`; `FAKE_RED_REMOVED = YES`.
- [ ] No task introduces a second model pass, Provider SPI, automatic boot injection, legacy migration, Recall, search, ranking, pagination, Truth Heads, Proposal, or Evolution.
- [ ] No task uses vague implementation placeholders; all files, interfaces, method signatures, test names, commands, stop conditions, and commit messages are explicit.
