# Memory Continuity And Evolution Library Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Skill-directed Daily Summary, optional Brain Handoff, explicit New Brain continuity selection, and a Category/Time Project Library evolution archive without turning Workbench into a knowledge-judgment engine.

**Architecture:** Keep raw conversation and epoch Handoff in existing Leader storage, add one Project+LocalDate Daily Summary document API, and introduce a minimal Library Object→Timeline Node→Material Reference model. A provider-neutral application API and structured Leader policy envelope carry explicit commands and continuity selections; Workbench validates, persists, indexes, and renders them but never decides when or what to remember.

**Tech Stack:** .NET 10, Avalonia 12, CommunityToolkit.Mvvm, Microsoft.Data.Sqlite, System.Text.Json, xUnit, Windows PowerShell.

## Global Constraints

- Workbench owns Memory API, persistence, deterministic indexes, source references, preferences, and UI; Leader plus Leader Skill owns intelligent judgment.
- Recent Raw Conversation, Brain Handoff, Daily Summary, and Project Library have distinct canonical stores and lifecycles.
- New Brain does not imply Save Memory, Daily Summary, Handoff generation, Library mutation, or End Day.
- Do not add fixed intervals, fixed event-driven knowledge governance, background summarization, autonomous cleanup, or automatic truth certification.
- Do not add Knowledge Graph, embeddings, vector database, semantic search, RAG orchestration, fuzzy topic merging, or copied artifact/transcript bodies.
- Do not delete or roll back `project_activity_events`, M1.5 memory/synthesis tables, or `project_library_entries`.
- Store Project Library artifacts as typed references only; Git remains the history for repository content.
- Category and Time are projections over one Object/Timeline record set.
- Continuity limits are explicit caller/Skill selections and preferences; this plan introduces no fixed token, message-count, or hour thresholds.
- Every storage operation is Project-scoped; mutable documents and nodes use optimistic concurrency.
- Each task below is an independently testable stopping point and receives its own commit.

---

## File And Type Map

### Existing Types To Reuse

- `StoredLeaderMessage`, `LeaderMessageRepository`: canonical raw transcript.
- `StoredLeaderSessionEpoch`, `LeaderSessionEpochRepository`: optional Handoff and delivery state.
- `ProjectLeaderRepository`: atomic archive/successor/current-pointer transition.
- `LeaderPaneViewModel`, `ProjectLeaderSessionManager`: one visible send path and original-message persistence.
- `LeaderStructuredResponse`, `LeaderResponseSchema`: provider-neutral structured Leader output pattern.
- `ProjectMemorySource`: provenance convention only; new aggregates use aggregate-specific reference records.
- `ProjectLibraryRepository`: legacy v8 reader/import source, not the target write model.

### New Focused Types

- Core: `LibraryGranularityMode`, `ContinuityMode`, `ProjectMemoryPreferences`.
- Storage: `DailySummaryDocument`, `DailySummaryWrite`, `DailySummarySourceReference`, `DailySummaryRepository`, `ProjectMemoryPreferencesRepository`.
- Storage: `RecentConversationStats`, `RecentConversationSlice`.
- Storage: `ContinuityMaterialKind`, `ContinuityMaterialSelection`, `LeaderEpochContinuityPlan`, `LeaderEpochContinuityRepository`.
- App: `IProjectMemoryApi`, `ProjectMemoryApi`, `ProjectContinuityMaterialService`.
- App: `LeaderMemoryPolicyDecision`, `LeaderMemoryPolicyPromptBuilder`, `LeaderMemoryPolicyPayloadParser`, `LeaderMemoryPolicyCoordinator`.
- Storage: `ProjectLibraryObject`, `ProjectLibraryTimelineNode`, `LibraryMaterialReference`, `ProjectLibraryProposal`, `ProjectLibraryEvolutionRepository`.
- App/UI: Category and Time projections plus proposal review view models.

---

### Task 1: Daily Summary And Project Memory Preferences

**Slice:** A — Daily Summary And Preferences

**Files:**
- Create: `src/Workbench.Core/Memory/ProjectMemoryPreferences.cs`
- Create: `src/Workbench.Storage/Migrations/Migration009MemoryContinuity.cs`
- Modify: `src/Workbench.Storage/Database/MigrationRunner.cs`
- Create: `src/Workbench.Storage/Memory/DailySummaryModels.cs`
- Create: `src/Workbench.Storage/Memory/MemoryRevisionConflictException.cs`
- Create: `src/Workbench.Storage/Memory/DailySummaryRepository.cs`
- Create: `src/Workbench.Storage/Settings/ProjectMemoryPreferencesRepository.cs`
- Modify: `tests/Workbench.Storage.Tests/Database/WorkbenchDatabaseTests.cs`
- Create: `tests/Workbench.Storage.Tests/Memory/DailySummaryRepositoryTests.cs`
- Create: `tests/Workbench.Storage.Tests/Settings/ProjectMemoryPreferencesRepositoryTests.cs`

**Interfaces:**

```csharp
public enum LibraryGranularityMode { Balanced, Detailed, Compact, Custom }
public enum ContinuityMode { Balanced, HighContinuity, LowToken, Custom }

public sealed record ProjectMemoryPreferences(
    Guid ProjectId,
    LibraryGranularityMode LibraryGranularity,
    ContinuityMode Continuity,
    string TimeZoneId,
    string? CustomInstructions,
    DateTimeOffset UpdatedAt);

public sealed record DailySummarySourceReference(string SourceType, string SourceRef);

public sealed record DailySummaryDocument(
    Guid ProjectId,
    DateOnly LocalDate,
    string Content,
    int Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<DailySummarySourceReference> Sources);

public sealed record DailySummaryWrite(
    Guid ProjectId,
    DateOnly LocalDate,
    string Content,
    int? ExpectedRevision,
    IReadOnlyList<DailySummarySourceReference> Sources);

public Task<DailySummaryDocument?> GetAsync(Guid projectId, DateOnly localDate, CancellationToken cancellationToken = default);
public Task<IReadOnlyList<DailySummaryDocument>> ListAsync(Guid projectId, DateOnly? from = null, DateOnly? through = null, CancellationToken cancellationToken = default);
public Task<DailySummaryDocument> SaveAsync(DailySummaryWrite write, DateTimeOffset savedAt, CancellationToken cancellationToken = default);
```

- [ ] **Step 1: Write failing migration, isolation, revision, and preference tests**

```csharp
[Fact]
public async Task One_project_local_date_is_one_revisable_document()
{
    var created = await summaries.SaveAsync(new(projectA.Id, new DateOnly(2026, 8, 14), "First", null, []), t0);
    var revised = await summaries.SaveAsync(new(projectA.Id, created.LocalDate, "Compressed", 1, [new("LeaderEpoch", epochId.ToString())]), t1);

    Assert.Equal(2, revised.Revision);
    Assert.Equal("Compressed", revised.Content);
    Assert.Empty(await summaries.ListAsync(projectB.Id));
}

[Fact]
public async Task Stale_revision_writes_nothing()
{
    await summaries.SaveAsync(new(project.Id, day, "One", null, []), t0);
    await Assert.ThrowsAsync<MemoryRevisionConflictException>(() =>
        summaries.SaveAsync(new(project.Id, day, "Stale", 0, []), t1));
    Assert.Equal("One", (await summaries.GetAsync(project.Id, day))!.Content);
}
```

- [ ] **Step 2: Run focused tests and verify RED**

Run:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test 'tests\Workbench.Storage.Tests\Workbench.Storage.Tests.csproj' --filter 'FullyQualifiedName~DailySummaryRepositoryTests|FullyQualifiedName~ProjectMemoryPreferencesRepositoryTests|FullyQualifiedName~WorkbenchDatabaseTests'
```

Expected: FAIL because Migration009, Daily Summary types/repository, and preference repository do not exist.

- [ ] **Step 3: Implement Migration009 and minimal repositories**

Migration009 creates exactly:

```sql
CREATE TABLE project_daily_summaries (
    project_id TEXT NOT NULL,
    local_date TEXT NOT NULL,
    content TEXT NOT NULL,
    revision INTEGER NOT NULL CHECK(revision >= 1),
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    PRIMARY KEY(project_id, local_date),
    FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE);

CREATE TABLE project_daily_summary_sources (
    project_id TEXT NOT NULL,
    local_date TEXT NOT NULL,
    source_type TEXT NOT NULL,
    source_ref TEXT NOT NULL,
    PRIMARY KEY(project_id, local_date, source_type, source_ref),
    FOREIGN KEY(project_id, local_date)
        REFERENCES project_daily_summaries(project_id, local_date) ON DELETE CASCADE);

CREATE TABLE project_memory_preferences (
    project_id TEXT PRIMARY KEY,
    library_granularity TEXT NOT NULL,
    continuity_mode TEXT NOT NULL,
    time_zone_id TEXT NOT NULL,
    custom_instructions TEXT NULL,
    updated_at TEXT NOT NULL,
    FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE);
```

`DailySummaryRepository.SaveAsync` must insert on `ExpectedRevision == null`, update with `WHERE revision = $expectedRevision` otherwise, replace source references in the same transaction, and throw `MemoryRevisionConflictException` when the compare-and-swap affects zero rows.

- [ ] **Step 4: Run focused and full Storage tests to GREEN**

Run:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test 'tests\Workbench.Storage.Tests\Workbench.Storage.Tests.csproj'
```

Expected: PASS with zero failed tests.

- [ ] **Step 5: Commit Slice A storage**

```powershell
git add src/Workbench.Core/Memory src/Workbench.Storage/Migrations/Migration009MemoryContinuity.cs src/Workbench.Storage/Database/MigrationRunner.cs src/Workbench.Storage/Memory/DailySummaryModels.cs src/Workbench.Storage/Memory/DailySummaryRepository.cs src/Workbench.Storage/Settings/ProjectMemoryPreferencesRepository.cs tests/Workbench.Storage.Tests
git commit -m 'feat(memory): add daily summary storage api'
```

**Stop condition:** Daily Summary and preferences are durable, isolated, revision-safe APIs with no App trigger, scheduler, or Leader integration.

---

### Task 2: Optional Brain Handoff And Bounded Recent Raw APIs

**Slice:** B — Continuity Material Access, part 1

**Files:**
- Create: `src/Workbench.Storage/Leaders/ContinuitySourceModels.cs`
- Modify: `src/Workbench.Storage/Leaders/LeaderSessionEpochRepository.cs`
- Modify: `src/Workbench.Storage/Leaders/LeaderMessageRepository.cs`
- Modify: `tests/Workbench.Storage.Tests/Leaders/LeaderPersistenceRepositoryTests.cs`
- Create: `tests/Workbench.Storage.Tests/Leaders/RecentConversationRepositoryTests.cs`

**Interfaces:**

```csharp
public sealed record RecentConversationStats(
    Guid ProjectId, Guid EpochId, int MessageCount, int Utf8Bytes, long? LastSequence);

public sealed record RecentConversationSlice(
    Guid ProjectId,
    Guid EpochId,
    IReadOnlyList<StoredLeaderMessage> Messages,
    int Utf8Bytes,
    int OmittedMessageCount);

public Task<StoredLeaderSessionEpoch> SaveActiveHandoffAsync(
    Guid projectId, Guid epochId, string? content, CancellationToken cancellationToken = default);

public Task<RecentConversationStats> GetStatsAsync(
    Guid projectId, Guid epochId, CancellationToken cancellationToken = default);

public Task<RecentConversationSlice> GetRecentAsync(
    Guid projectId, Guid epochId, long? beforeSequence,
    int maxMessages, int maxUtf8Bytes, CancellationToken cancellationToken = default);
```

- [ ] **Step 1: Write failing tests for optional/frozen Handoff and exact raw paging**

```csharp
[Fact]
public async Task Active_handoff_can_be_written_or_cleared_but_archived_handoff_is_frozen()
{
    Assert.Equal("focus", (await epochs.SaveActiveHandoffAsync(project.Id, active.Id, "focus")).HandoffSummary);
    Assert.Null((await epochs.SaveActiveHandoffAsync(project.Id, active.Id, null)).HandoffSummary);
    await Assert.ThrowsAsync<InvalidOperationException>(() =>
        epochs.SaveActiveHandoffAsync(project.Id, archived.Id, "late"));
}

[Fact]
public async Task Recent_slice_returns_complete_messages_without_copying_or_crossing_projects()
{
    var slice = await messages.GetRecentAsync(projectA.Id, epochA.Id, null, 2, 1000);
    Assert.Equal([2L, 3L], slice.Messages.Select(message => message.Sequence));
    Assert.All(slice.Messages, message => Assert.Equal(epochA.Id, message.EpochId));
    await Assert.ThrowsAsync<InvalidOperationException>(() =>
        messages.GetRecentAsync(projectB.Id, epochA.Id, null, 2, 1000));
}
```

- [ ] **Step 2: Run focused tests and verify RED**

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test 'tests\Workbench.Storage.Tests\Workbench.Storage.Tests.csproj' --filter 'FullyQualifiedName~LeaderPersistenceRepositoryTests|FullyQualifiedName~RecentConversationRepositoryTests'
```

Expected: FAIL because the focused Handoff and recent-tail methods do not exist.

- [ ] **Step 3: Implement project-scoped Handoff and raw-tail queries**

`SaveActiveHandoffAsync` performs one guarded update:

```sql
UPDATE leader_session_epochs
SET handoff_summary = $content
WHERE id = $epochId AND project_id = $projectId AND ended_at IS NULL;
```

`GetRecentAsync` reads newest rows in reverse, selects only complete messages that fit both caller limits, then returns them in ascending sequence. It never truncates or stores message text elsewhere.

- [ ] **Step 4: Run Storage tests to GREEN**

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test 'tests\Workbench.Storage.Tests\Workbench.Storage.Tests.csproj'
```

- [ ] **Step 5: Commit bounded source access**

```powershell
git add src/Workbench.Storage/Leaders tests/Workbench.Storage.Tests/Leaders
git commit -m 'feat(memory): expose handoff and recent conversation api'
```

**Stop condition:** callers can explicitly read/write Handoff and select exact raw ranges; rollover behavior is still unchanged.

---

### Task 3: Durable Continuity Plans And Material Resolution

**Slice:** B — Continuity Material Access, part 2

**Files:**
- Create: `src/Workbench.Storage/Migrations/Migration010LeaderContinuityPlans.cs`
- Modify: `src/Workbench.Storage/Database/MigrationRunner.cs`
- Create: `src/Workbench.Storage/Memory/ContinuityMaterialModels.cs`
- Create: `src/Workbench.Storage/Memory/LeaderEpochContinuityRepository.cs`
- Create: `src/Workbench.App/Memory/IProjectMemoryApi.cs`
- Create: `src/Workbench.App/Memory/ProjectMemoryApi.cs`
- Create: `src/Workbench.App/Memory/ProjectContinuityMaterialService.cs`
- Modify: `tests/Workbench.Storage.Tests/Database/WorkbenchDatabaseTests.cs`
- Create: `tests/Workbench.Storage.Tests/Memory/LeaderEpochContinuityRepositoryTests.cs`
- Create: `tests/Workbench.App.Tests/ProjectContinuityMaterialServiceTests.cs`

**Interfaces:**

```csharp
public enum ContinuityMaterialKind
{
    DailySummary,
    BrainHandoff,
    RecentConversation,
    LegacyFormal,
    LegacyLearned,
    LibraryOverview,
    LibraryTimelineNode
}

public sealed record ContinuityMaterialDescriptor(
    string Reference,
    ContinuityMaterialKind Kind,
    Guid ProjectId,
    string Label,
    DateTimeOffset? OccurredAt,
    int Utf8Bytes);

public sealed record ContinuityMaterialSelection(
    int Ordinal,
    ContinuityMaterialKind Kind,
    string Reference,
    int MaxUtf8Bytes,
    string? SelectorJson = null);

public sealed record LeaderEpochContinuityPlan(
    Guid EpochId,
    int TotalMaxUtf8Bytes,
    IReadOnlyList<ContinuityMaterialSelection> Selections,
    DateTimeOffset CreatedAt);

public sealed record ResolvedContinuityBundle(
    Guid ProjectId,
    IReadOnlyList<ResolvedContinuityMaterial> Materials,
    int Utf8Bytes,
    IReadOnlyList<string> OmittedReferences);

public sealed record ResolvedContinuityMaterial(
    ContinuityMaterialKind Kind,
    string Reference,
    string Label,
    string Content,
    int Utf8Bytes);

public sealed record ContinuityMaterialCatalog(
    Guid ProjectId,
    Guid SourceEpochId,
    IReadOnlyList<ContinuityMaterialDescriptor> Materials);

public interface IProjectMemoryApi
{
    Task<DailySummaryDocument?> GetDailySummaryAsync(Guid projectId, DateOnly localDate, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DailySummaryDocument>> ListDailySummariesAsync(Guid projectId, DateOnly? from = null, DateOnly? through = null, CancellationToken cancellationToken = default);
    Task<DailySummaryDocument> SaveDailySummaryAsync(DailySummaryWrite write, CancellationToken cancellationToken = default);
    Task<string?> GetBrainHandoffAsync(Guid projectId, Guid epochId, CancellationToken cancellationToken = default);
    Task<StoredLeaderSessionEpoch> SaveBrainHandoffAsync(Guid projectId, Guid epochId, string? content, CancellationToken cancellationToken = default);
    Task<RecentConversationStats> GetRecentConversationStatsAsync(Guid projectId, Guid epochId, CancellationToken cancellationToken = default);
    Task<RecentConversationSlice> ReadRecentConversationAsync(Guid projectId, Guid epochId, long? beforeSequence, int maxMessages, int maxUtf8Bytes, CancellationToken cancellationToken = default);
    Task<ProjectMemoryPreferences> GetPreferencesAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<ContinuityMaterialCatalog> ListContinuityMaterialsAsync(Guid projectId, Guid sourceEpochId, CancellationToken cancellationToken = default);
    Task<ResolvedContinuityBundle> ResolveContinuityAsync(Guid projectId, LeaderEpochContinuityPlan plan, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 1: Write failing persistence and resolution tests**

```csharp
[Fact]
public async Task Plan_round_trips_in_order_and_is_owned_by_successor_epoch()
{
    var plan = new LeaderEpochContinuityPlan(successor.Id, 9000,
        [new(0, ContinuityMaterialKind.DailySummary, "daily:2026-08-14", 4000),
         new(1, ContinuityMaterialKind.RecentConversation, $"raw:{source.Id}", 5000, "{\"maxMessages\":8}")], t0);
    await repository.SaveAsync(project.Id, plan);
    Assert.Equal(plan, await repository.GetAsync(project.Id, successor.Id));
}

[Fact]
public async Task Resolver_uses_only_selected_project_material_and_reports_overflow()
{
    var bundle = await service.ResolveAsync(projectA.Id, plan);
    Assert.DoesNotContain(bundle.Materials, item => item.Content.Contains("PROJECT_B", StringComparison.Ordinal));
    Assert.True(bundle.Utf8Bytes <= plan.TotalMaxUtf8Bytes);
    Assert.Contains("daily:2026-08-13", bundle.OmittedReferences);
}
```

- [ ] **Step 2: Run focused tests and verify RED**

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test 'tests\Workbench.Storage.Tests\Workbench.Storage.Tests.csproj' --filter 'FullyQualifiedName~LeaderEpochContinuityRepositoryTests|FullyQualifiedName~WorkbenchDatabaseTests'
& 'C:\Program Files\dotnet\dotnet.exe' test 'tests\Workbench.App.Tests\Workbench.App.Tests.csproj' --filter 'FullyQualifiedName~ProjectContinuityMaterialServiceTests'
```

- [ ] **Step 3: Implement Migration010 and deterministic resolution**

Migration010 creates a plan header and ordered reference rows:

```sql
CREATE TABLE leader_epoch_continuity_plans (
    epoch_id TEXT PRIMARY KEY,
    total_max_utf8_bytes INTEGER NOT NULL CHECK(total_max_utf8_bytes > 0),
    created_at TEXT NOT NULL,
    FOREIGN KEY(epoch_id) REFERENCES leader_session_epochs(id) ON DELETE CASCADE);

CREATE TABLE leader_epoch_continuity_selections (
    epoch_id TEXT NOT NULL,
    ordinal INTEGER NOT NULL,
    material_kind TEXT NOT NULL,
    material_ref TEXT NOT NULL,
    max_utf8_bytes INTEGER NOT NULL CHECK(max_utf8_bytes > 0),
    selector_json TEXT NULL,
    PRIMARY KEY(epoch_id, ordinal),
    FOREIGN KEY(epoch_id) REFERENCES leader_epoch_continuity_plans(epoch_id) ON DELETE CASCADE);
```

`ProjectContinuityMaterialService.ResolveAsync` processes selections by ordinal, validates each referenced aggregate belongs to `projectId`, adds complete material bodies only, and records every invalid/overflow reference in `OmittedReferences`.

- [ ] **Step 4: Run focused App/Storage tests and then both projects to GREEN**

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test 'tests\Workbench.Storage.Tests\Workbench.Storage.Tests.csproj'
& 'C:\Program Files\dotnet\dotnet.exe' test 'tests\Workbench.App.Tests\Workbench.App.Tests.csproj'
```

- [ ] **Step 5: Commit durable explicit selection**

```powershell
git add src/Workbench.Storage/Migrations/Migration010LeaderContinuityPlans.cs src/Workbench.Storage/Database/MigrationRunner.cs src/Workbench.Storage/Memory/ContinuityMaterialModels.cs src/Workbench.Storage/Memory/LeaderEpochContinuityRepository.cs src/Workbench.App/Memory tests/Workbench.Storage.Tests tests/Workbench.App.Tests/ProjectContinuityMaterialServiceTests.cs
git commit -m 'feat(memory): persist explicit leader continuity plans'
```

**Stop condition:** a fake caller can catalog/select/resolve Daily, Handoff, and Raw material by reference; no runtime or New Brain flow is changed.

---

### Task 4: Leader Skill Policy Protocol And New Brain Integration

**Slice:** C — Leader Skill Continuity Integration

**Files:**
- Create: `src/Workbench.App/Memory/LeaderMemoryPolicyModels.cs`
- Create: `src/Workbench.App/Memory/LeaderMemoryPolicyPromptBuilder.cs`
- Create: `src/Workbench.App/Memory/LeaderMemoryPolicyPayloadParser.cs`
- Create: `src/Workbench.App/Memory/LeaderMemoryPolicyCoordinator.cs`
- Modify: `src/Workbench.App/Leader/LeaderSessionRolloverService.cs`
- Modify: `src/Workbench.App/Leader/LeaderBootContextBuilder.cs`
- Modify: `src/Workbench.Storage/Leaders/ProjectLeaderRepository.cs`
- Modify: `src/Workbench.App/Services/AppServices.cs`
- Modify: `src/Workbench.App/ViewModels/MainWindowViewModel.cs`
- Modify: `src/Workbench.App/ViewModels/WorkspaceViewModel.cs`
- Modify: `src/Workbench.App/ViewModels/Panes/LeaderPaneViewModel.cs`
- Modify: `tests/Workbench.App.Tests/AppTestContext.cs`
- Create: `tests/Workbench.App.Tests/LeaderMemoryPolicyPayloadParserTests.cs`
- Create: `tests/Workbench.App.Tests/LeaderMemoryPolicyCoordinatorTests.cs`
- Modify: `tests/Workbench.App.Tests/LeaderBootContextBuilderTests.cs`
- Modify: `tests/Workbench.App.Tests/LeaderBootIntegrationTests.cs`
- Modify: `tests/Workbench.App.Tests/LeaderRolloverPolicyTests.cs`
- Modify: `tests/Workbench.App.Tests/LeaderSessionRolloverServiceTests.cs`
- Modify: `tests/Workbench.Storage.Tests/Leaders/LeaderPersistenceRepositoryTests.cs`

**Interfaces:**

```csharp
public sealed record LeaderMemoryPolicyDecision(
    string? BrainHandoff,
    DailySummaryWrite? DailySummary,
    int? TotalContinuityBudgetUtf8Bytes,
    IReadOnlyList<ContinuityMaterialSelection> ContinuitySelection);

public sealed record LeaderMemoryPolicyPreparation(
    bool Available,
    LeaderMemoryPolicyDecision? Decision,
    string? Error);

public Task<LeaderMemoryPolicyPreparation> PrepareForNewBrainAsync(
    Project project,
    AgentSession sourceSession,
    StoredLeaderSessionEpoch sourceEpoch,
    bool resumeSourceSession,
    CancellationToken cancellationToken = default);
```

Policy JSON is strict and makes every write optional:

```json
{
  "brain_handoff": null,
  "daily_summary": null,
  "total_continuity_budget_utf8_bytes": 12000,
  "continuity_selection": [
    {"ordinal":0,"kind":"DailySummary","reference":"daily:2026-08-13","max_utf8_bytes":5000,"selector":null}
  ]
}
```

- [ ] **Step 1: Write failing protocol, no-write, rollover, restart, and boot tests**

```csharp
[Fact]
public async Task New_brain_policy_may_select_material_without_writing_memory()
{
    runtime.QueueTurn(Completed(DecisionJson(handoff: null, dailySummary: null, selections: [dailySelection])));
    await pane.StartNewBrainAsync();

    Assert.Null(await summaries.GetAsync(project.Id, day));
    Assert.NotNull(await continuityPlans.GetAsync(project.Id, pane.SessionEpochId!.Value));
}

[Fact]
public async Task Restart_before_first_user_send_resolves_the_persisted_selection_once()
{
    await pane.StartNewBrainAsync();
    var restarted = CreatePane(newManager: true);
    restarted.DraftMessage = "continue";
    await restarted.SendAsync();

    Assert.Contains("DAILY SUMMARY 2026-08-13", runtime.SentRequests.Single().Text, StringComparison.Ordinal);
    Assert.Equal("continue", (await messages.GetAllAsync(restarted.SessionEpochId!.Value)).Single(m => m.Role == "user").Text);
}

[Fact]
public async Task Invalid_policy_creates_no_memory_and_still_allows_empty_continuity_rollover()
{
    runtime.QueueTurn(Completed("not-json"));
    await pane.StartNewBrainAsync();
    Assert.Null(await continuityPlans.GetAsync(project.Id, pane.SessionEpochId!.Value));
    Assert.Empty(await summaries.ListAsync(project.Id));
}
```

- [ ] **Step 2: Run focused tests and verify RED**

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test 'tests\Workbench.App.Tests\Workbench.App.Tests.csproj' --filter 'FullyQualifiedName~LeaderMemoryPolicy|FullyQualifiedName~LeaderBoot|FullyQualifiedName~LeaderRollover'
& 'C:\Program Files\dotnet\dotnet.exe' test 'tests\Workbench.Storage.Tests\Workbench.Storage.Tests.csproj' --filter 'FullyQualifiedName~LeaderPersistenceRepositoryTests'
```

- [ ] **Step 3: Implement strict policy preparation without automatic writes**

`LeaderMemoryPolicyPromptBuilder` must state:

```text
New Brain is not Save Memory, End Day, Daily Summary, Handoff, or Library archival.
Every write field is optional. Return null when no write is useful.
Select continuity material by descriptor reference only. Do not invent references.
```

The coordinator sends one policy turn to the source session, rejects approval/tool activity, validates references against the supplied catalog, and applies a Daily Summary only when the parsed command is non-null.

- [ ] **Step 4: Make rollover Handoff optional and persist successor plan atomically**

Change the Storage contract to:

```csharp
public Task RolloverAsync(
    Guid projectId,
    Guid oldEpochId,
    StoredLeaderSessionEpoch newEpoch,
    DateTimeOffset endedAt,
    string rolloverReason,
    string? handoffSummary,
    LeaderEpochContinuityPlan? continuityPlan,
    CancellationToken cancellationToken = default);
```

The rollover transaction archives the old epoch with the optional Handoff, inserts the successor, switches the current pointer, retains the legacy Pending synthesis opportunity for compatibility, and inserts the plan header/selections only when a validated plan exists.

- [ ] **Step 5: Replace automatic Formal/Learned/Handoff boot selection with resolved plan delivery**

`LeaderBootContextBuilder` emits one envelope in this order:

```text
WORKBENCH PROJECT CONTEXT
PROJECT
SELECTED CONTINUITY MATERIALS
CURRENT USER MESSAGE
```

Each selected material is labeled by kind/date/Object. The builder reads the current epoch's stored plan, resolves only that plan, preserves the user text unchanged, and keeps existing `boot_context_delivered_at` first-event semantics.

- [ ] **Step 6: Run App/Storage tests to GREEN**

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test 'tests\Workbench.Storage.Tests\Workbench.Storage.Tests.csproj'
& 'C:\Program Files\dotnet\dotnet.exe' test 'tests\Workbench.App.Tests\Workbench.App.Tests.csproj'
```

- [ ] **Step 7: Commit Skill-directed New Brain continuity**

```powershell
git add src/Workbench.App/Memory src/Workbench.App/Leader src/Workbench.App/Services/AppServices.cs src/Workbench.App/ViewModels src/Workbench.Storage/Leaders tests/Workbench.App.Tests tests/Workbench.Storage.Tests/Leaders
git commit -m 'feat(memory): let leader skill select new brain continuity'
```

**Stop condition:** New Brain supports null writes, explicit selection, first-send delivery, and restart recovery; policy failure does not invent continuity.

---

### Task 5: Remove Default Automatic Governance And Add Daily/Preference UI

**Slices:** A UI completion and C legacy-policy cleanup

**Files:**
- Modify: `src/Workbench.App/Services/AppServices.cs`
- Modify: `src/Workbench.App/ViewModels/MainWindowViewModel.cs`
- Modify: `src/Workbench.App/ViewModels/WorkspaceViewModel.cs`
- Modify: `src/Workbench.App/ViewModels/Panes/LeaderPaneViewModel.cs`
- Modify: `src/Workbench.App/ViewModels/Panes/LibraryPaneViewModel.cs`
- Modify: `src/Workbench.App/Views/Panes/LibraryPaneView.axaml`
- Modify: `src/Workbench.App/ViewModels/SettingsViewModel.cs`
- Modify: `src/Workbench.App/Views/SettingsView.axaml`
- Modify: `tests/Workbench.App.Tests/ProjectMemoryLearningUiTests.cs`
- Modify: `tests/Workbench.App.Tests/WorkspaceViewModelTests.cs`
- Create: `tests/Workbench.App.Tests/MemoryPreferencesUiTests.cs`

**Interfaces:**

- `AppServices` exposes `DailySummaryRepository`, `ProjectMemoryPreferencesRepository`, and `IProjectMemoryApi`.
- Library Daily view lists `DailySummaryDocument` by `LocalDate DESC` and displays content, revision, update time, and source references.
- Settings and project override UI expose exact policy values without translating them into schedules.
- No project-open, completed-turn, rollover, or Library-open callback calls `ScheduleMemorySynthesis`.

- [ ] **Step 1: Write failing UI/composition tests**

```csharp
[Fact]
public async Task Opening_project_and_completing_turn_do_not_schedule_legacy_synthesis()
{
    var scheduled = 0;
    var main = new MainWindowViewModel(services, picker, leaderSessions, _ => scheduled++);
    await OpenProjectAsync(main);
    await CompleteLeaderTurnAsync(main);
    Assert.Equal(0, scheduled);
}

[Fact]
public async Task Daily_view_and_preferences_survive_reconstruction()
{
    await summaries.SaveAsync(new(project.Id, day, "Today", null, []), at);
    await preferences.SaveAsync(new(project.Id, LibraryGranularityMode.Detailed, ContinuityMode.LowToken, timeZone.Id, null, at));
    var restored = CreateLibraryAndSettings();
    await restored.Library.InitializeAsync();
    Assert.Equal("Today", Assert.Single(restored.Library.DailySummaries).Content);
    Assert.Equal(ContinuityMode.LowToken, restored.Settings.ContinuityMode);
}
```

- [ ] **Step 2: Run focused App tests and verify RED**

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test 'tests\Workbench.App.Tests\Workbench.App.Tests.csproj' --filter 'FullyQualifiedName~ProjectMemoryLearningUiTests|FullyQualifiedName~MemoryPreferencesUiTests|FullyQualifiedName~WorkspaceViewModelTests'
```

- [ ] **Step 3: Remove only the four default scheduling triggers**

Keep legacy synthesis repositories, schema, status, startup recovery, and tests that exercise the coordinator directly. Remove automatic calls from project open, completed Leader turn, successful rollover, and Library open. Do not create a replacement timer or background loop.

- [ ] **Step 4: Add Daily and preference projections**

Use these observable properties without embedding policy decisions:

```csharp
public ObservableCollection<DailySummaryDocument> DailySummaries { get; } = [];
public LibraryGranularityMode LibraryGranularity { get; set; } = LibraryGranularityMode.Balanced;
public ContinuityMode ContinuityMode { get; set; } = ContinuityMode.Balanced;
public string ProjectTimeZoneId { get; set; } = TimeZoneInfo.Local.Id;
public string? MemoryCustomInstructions { get; set; }
```

- [ ] **Step 5: Run App tests to GREEN**

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test 'tests\Workbench.App.Tests\Workbench.App.Tests.csproj'
```

- [ ] **Step 6: Commit the policy boundary and UI**

```powershell
git add src/Workbench.App tests/Workbench.App.Tests
git commit -m 'feat(memory): expose daily continuity preferences'
```

**Stop condition:** users can inspect Daily Summary and set preferences; Workbench performs no event-triggered knowledge synthesis.

---

### Task 6: Library Object, Timeline, References, And Legacy Import

**Slice:** D — Library Object And Timeline

**Files:**
- Create: `src/Workbench.Storage/Migrations/Migration011ProjectLibraryEvolution.cs`
- Modify: `src/Workbench.Storage/Database/MigrationRunner.cs`
- Create: `src/Workbench.Storage/Memory/ProjectLibraryEvolutionModels.cs`
- Create: `src/Workbench.Storage/Memory/ProjectLibraryEvolutionRepository.cs`
- Create: `src/Workbench.Storage/Memory/LibraryRevisionConflictException.cs`
- Modify: `src/Workbench.Storage/Memory/ProjectLibraryRepository.cs`
- Modify: `tests/Workbench.Storage.Tests/Database/WorkbenchDatabaseTests.cs`
- Create: `tests/Workbench.Storage.Tests/Memory/ProjectLibraryEvolutionRepositoryTests.cs`
- Create: `tests/Workbench.Storage.Tests/Memory/ProjectLibraryLegacyImportTests.cs`

**Interfaces:**

```csharp
public sealed record ProjectLibraryObject(
    Guid Id, Guid ProjectId, string Category, string Topic,
    string CategoryKey, string TopicKey, string? CurrentOverview,
    int OverviewRevision, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record ProjectLibraryTimelineNode(
    Guid Id, Guid ObjectId, DateOnly LocalDate, string Content,
    int Revision, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record LibraryMaterialReference(
    Guid NodeId, string MaterialKind, string Reference, string? Label, DateTimeOffset CreatedAt);

public sealed record LibraryMaterialReferenceDraft(
    string MaterialKind, string Reference, string? Label);

public Task<ProjectLibraryObject> CreateObjectAsync(
    Guid projectId, string category, string topic, DateTimeOffset createdAt, CancellationToken cancellationToken = default);
public Task<ProjectLibraryObject> UpdateOverviewAsync(
    Guid projectId, Guid objectId, string? overview, int expectedRevision, DateTimeOffset updatedAt, CancellationToken cancellationToken = default);
public Task<ProjectLibraryTimelineNode> AddNodeAsync(
    Guid projectId, Guid objectId, DateOnly localDate, string content,
    IReadOnlyList<LibraryMaterialReferenceDraft> references, DateTimeOffset createdAt,
    CancellationToken cancellationToken = default);
public Task<ProjectLibraryTimelineNode> UpdateNodeAsync(
    Guid projectId, Guid nodeId, string content, int expectedRevision,
    IReadOnlyList<LibraryMaterialReferenceDraft> references, DateTimeOffset updatedAt,
    CancellationToken cancellationToken = default);
```

- [ ] **Step 1: Write failing evolution and v8 import tests**

```csharp
[Fact]
public async Task Object_supports_overview_multiple_same_day_nodes_and_reference_only_materials()
{
    var obj = await library.CreateObjectAsync(project.Id, "Design", "Relics", t0);
    await library.UpdateOverviewAsync(project.Id, obj.Id, "Current direction", 0, t1);
    var first = await library.AddNodeAsync(project.Id, obj.Id, day, "First change", [new("GitCommit", "abc123", "Implementation")], t1);
    var second = await library.AddNodeAsync(project.Id, obj.Id, day, "Substantive change", [], t2);

    Assert.Equal([second.Id, first.Id], (await library.GetTimelineAsync(project.Id, obj.Id)).Select(node => node.Id));
    Assert.Equal("abc123", Assert.Single(await library.GetMaterialReferencesAsync(project.Id, first.Id)).Reference);
}

[Fact]
public async Task Migration011_imports_every_v8_entry_and_preserves_the_legacy_table()
{
    await CreateVersion8DatabaseWithTwoRelicEntriesAsync();
    await database.InitializeAsync();
    Assert.Equal(2, (await evolution.BrowseNodesByDateAsync(project.Id, null, null)).Count);
    Assert.Equal(2L, await ScalarAsync("SELECT COUNT(*) FROM project_library_entries"));
}
```

- [ ] **Step 2: Run focused tests and verify RED**

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test 'tests\Workbench.Storage.Tests\Workbench.Storage.Tests.csproj' --filter 'FullyQualifiedName~ProjectLibraryEvolution|FullyQualifiedName~ProjectLibraryLegacyImport|FullyQualifiedName~WorkbenchDatabaseTests'
```

- [ ] **Step 3: Implement Migration011 schema and C# legacy import inside the transaction**

Create `project_library_objects`, `project_library_timeline_nodes`, `project_library_material_refs`, and `project_library_proposals`. Required keys/indexes:

```sql
UNIQUE(project_id, category_key, topic_key)
INDEX ix_library_nodes_object_time ON project_library_timeline_nodes(object_id, local_date DESC, created_at DESC, id)
INDEX ix_library_objects_project_category ON project_library_objects(project_id, category_key, topic_key)
```

The proposal table is a small confirmation queue, not an event engine:

```sql
CREATE TABLE project_library_proposals (
    id TEXT PRIMARY KEY,
    project_id TEXT NOT NULL,
    source_session_id TEXT NOT NULL,
    status TEXT NOT NULL CHECK(status IN ('Pending','Accepted','Rejected')),
    payload_json TEXT NOT NULL,
    created_at TEXT NOT NULL,
    decided_at TEXT NULL,
    FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE);
CREATE INDEX ix_library_proposals_project_status
    ON project_library_proposals(project_id, status, created_at DESC, id);
```

Import rules are exact:

1. Normalize legacy Category/Topic in C# with trim, whitespace collapse, and `ToUpperInvariant()`.
2. Group by Project + normalized keys.
3. Use the earliest `(CreatedAt, Id)` entry ID as Object ID.
4. Use every entry ID as its Node ID.
5. Use the UTC calendar portion of legacy `CreatedAt` for imported `LocalDate`.
6. Insert typed `AgentSession`, `Task`, and `Reference` locators without copying source bodies.
7. Leave `project_library_entries` unchanged.

- [ ] **Step 4: Implement optimistic Object/Node APIs**

`UpdateOverviewAsync` and `UpdateNodeAsync` must guard `revision = $expectedRevision`, update references and content in one transaction, and throw `LibraryRevisionConflictException` without partial writes.

- [ ] **Step 5: Run Storage tests to GREEN**

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test 'tests\Workbench.Storage.Tests\Workbench.Storage.Tests.csproj'
```

- [ ] **Step 6: Commit the evolution substrate**

```powershell
git add src/Workbench.Storage tests/Workbench.Storage.Tests
git commit -m 'feat(library): add object evolution timeline'
```

**Stop condition:** legacy entries and new Objects/Nodes/references are durable and queryable; no Leader or UI proposal integration is required yet.

---

### Task 7: Symmetric Category And Time Library UI

**Slice:** E — Category/Time Library UI

**Files:**
- Modify: `src/Workbench.Storage/Memory/ProjectLibraryEvolutionModels.cs`
- Modify: `src/Workbench.Storage/Memory/ProjectLibraryEvolutionRepository.cs`
- Modify: `src/Workbench.App/Services/AppServices.cs`
- Modify: `src/Workbench.App/ViewModels/MainWindowViewModel.cs`
- Modify: `src/Workbench.App/ViewModels/WorkspaceViewModel.cs`
- Modify: `src/Workbench.App/ViewModels/Panes/LibraryPaneViewModel.cs`
- Modify: `src/Workbench.App/Views/Panes/LibraryPaneView.axaml`
- Modify: `tests/Workbench.Storage.Tests/Memory/ProjectLibraryEvolutionRepositoryTests.cs`
- Create: `tests/Workbench.App.Tests/ProjectLibraryEvolutionUiTests.cs`

**Interfaces:**

```csharp
public sealed record LibraryCategoryProjection(
    string Category, IReadOnlyList<ProjectLibraryObjectSummary> Objects);

public sealed record ProjectLibraryObjectSummary(
    Guid Id,
    string Category,
    string Topic,
    string? CurrentOverview,
    IReadOnlyList<ProjectLibraryTimelineNode> Nodes);

public sealed record LibraryDateCategoryProjection(
    string Category,
    IReadOnlyList<ProjectLibraryObjectSummary> Objects);

public sealed record LibraryDateProjection(
    DateOnly LocalDate, IReadOnlyList<LibraryDateCategoryProjection> Categories);

public Task<IReadOnlyList<LibraryCategoryProjection>> BrowseByCategoryAsync(
    Guid projectId, CancellationToken cancellationToken = default);

public Task<IReadOnlyList<LibraryDateProjection>> BrowseByTimeAsync(
    Guid projectId, DateOnly? from = null, DateOnly? through = null,
    CancellationToken cancellationToken = default);
```

- [ ] **Step 1: Write failing projection identity and UI tests**

```csharp
[Fact]
public async Task Category_and_time_views_return_the_same_node_identity()
{
    var categoryNodeIds = (await repository.BrowseByCategoryAsync(project.Id))
        .SelectMany(category => category.Objects).SelectMany(obj => obj.Nodes).Select(node => node.Id).Order().ToArray();
    var timeNodeIds = (await repository.BrowseByTimeAsync(project.Id))
        .SelectMany(day => day.Categories).SelectMany(category => category.Objects).SelectMany(obj => obj.Nodes).Select(node => node.Id).Order().ToArray();
    Assert.Equal(categoryNodeIds, timeNodeIds);
}

[Fact]
public async Task Object_detail_shows_overview_then_newest_to_oldest_timeline()
{
    await viewModel.OpenObjectAsync(objectId);
    Assert.Equal("Current", viewModel.SelectedObject!.CurrentOverview);
    Assert.Equal([newerNodeId, olderNodeId], viewModel.SelectedTimeline.Select(node => node.Id));
}
```

- [ ] **Step 2: Run focused tests and verify RED**

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test 'tests\Workbench.Storage.Tests\Workbench.Storage.Tests.csproj' --filter 'FullyQualifiedName~ProjectLibraryEvolutionRepositoryTests'
& 'C:\Program Files\dotnet\dotnet.exe' test 'tests\Workbench.App.Tests\Workbench.App.Tests.csproj' --filter 'FullyQualifiedName~ProjectLibraryEvolutionUiTests'
```

- [ ] **Step 3: Implement both projections as queries over the same rows**

Category ordering is Category key, Object topic key, then each Object timeline newest first. Time ordering is LocalDate descending, then Category key and Object topic key. Do not persist projection rows.

- [ ] **Step 4: Replace flat Browse UI with Category/Time navigation and Object detail**

The Object detail renders:

```text
Current Overview
latest node
↑
older node
↑
earliest node
```

Do not render graph-edge labels or `SupersededBy` language. Keep legacy Project Memory in its existing Project section.

- [ ] **Step 5: Run App/Storage tests to GREEN**

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test 'tests\Workbench.Storage.Tests\Workbench.Storage.Tests.csproj'
& 'C:\Program Files\dotnet\dotnet.exe' test 'tests\Workbench.App.Tests\Workbench.App.Tests.csproj'
```

- [ ] **Step 6: Commit symmetric Library navigation**

```powershell
git add src/Workbench.Storage/Memory src/Workbench.App tests/Workbench.Storage.Tests/Memory/ProjectLibraryEvolutionRepositoryTests.cs tests/Workbench.App.Tests/ProjectLibraryEvolutionUiTests.cs
git commit -m 'feat(library): browse evolution by category and time'
```

**Stop condition:** Category-first and Time-first navigation show the same persisted data and survive reconstruction.

---

### Task 8: Stage-End Library Proposal And User Confirmation

**Slice:** F — Stage-End Summary To Library Proposal

**Files:**
- Create: `src/Workbench.Storage/Memory/ProjectLibraryProposalService.cs`
- Modify: `src/Workbench.Storage/Memory/ProjectLibraryEvolutionModels.cs`
- Modify: `src/Workbench.Storage/Memory/ProjectLibraryEvolutionRepository.cs`
- Modify: `src/Workbench.App/Memory/IProjectMemoryApi.cs`
- Modify: `src/Workbench.App/Memory/ProjectMemoryApi.cs`
- Modify: `src/Workbench.App/Leader/LeaderDraftProposalBuilder.cs`
- Modify: `src/Workbench.App/ViewModels/Panes/LeaderPaneViewModel.cs`
- Modify: `src/Workbench.App/ViewModels/Panes/LibraryPaneViewModel.cs`
- Modify: `src/Workbench.App/Views/Panes/LibraryPaneView.axaml`
- Create: `tests/Workbench.Storage.Tests/Memory/ProjectLibraryProposalTests.cs`
- Modify: `tests/Workbench.App.Tests/Worker/LeaderDraftProposalTests.cs`
- Create: `tests/Workbench.App.Tests/ProjectLibraryProposalUiTests.cs`

**Interfaces:**

```csharp
public enum LibraryProposalAction { CreateNode, UpdateNode }
public enum LibraryProposalStatus { Pending, Accepted, Rejected }

public sealed record ProjectLibraryProposal(
    Guid Id,
    Guid ProjectId,
    Guid SourceSessionId,
    LibraryProposalStatus Status,
    ProjectLibraryProposalDraft Draft,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt);

public sealed record ProjectLibraryProposalDraft(
    Guid ProposalId,
    Guid ProjectId,
    Guid SourceSessionId,
    LibraryProposalAction Action,
    Guid? TargetObjectId,
    Guid? TargetNodeId,
    int? ExpectedNodeRevision,
    int? ExpectedOverviewRevision,
    string Category,
    string Topic,
    DateOnly LocalDate,
    string NodeContent,
    string? CurrentOverview,
    IReadOnlyList<LibraryMaterialReferenceDraft> Materials,
    DateTimeOffset CreatedAt);

public sealed record LibraryProposalEdit(
    string NodeContent,
    string? CurrentOverview,
    IReadOnlyList<LibraryMaterialReferenceDraft> Materials);

public Task<ProjectLibraryProposal> CreateProposalAsync(ProjectLibraryProposalDraft draft, CancellationToken cancellationToken = default);
public Task AcceptAsync(Guid projectId, Guid proposalId, CancellationToken cancellationToken = default);
public Task EditAndAcceptAsync(Guid projectId, Guid proposalId, LibraryProposalEdit edit, CancellationToken cancellationToken = default);
public Task RejectAsync(Guid projectId, Guid proposalId, CancellationToken cancellationToken = default);
```

Extend the ordinary Leader response schema with one optional command collection while preserving visible response and Worker draft behavior:

```json
{
  "response": "Today’s relic direction is closed.",
  "draft_proposal": null,
  "memory_commands": {
    "daily_summary": null,
    "library_proposal": {
      "action": "UpdateNode",
      "target_object_id": "10000000-0000-4000-8000-000000000001",
      "target_node_id": "20000000-0000-4000-8000-000000000001",
      "expected_node_revision": 2,
      "expected_overview_revision": 4,
      "category": "Design",
      "topic": "Relics / 清一色罗盘",
      "local_date": "2026-08-14",
      "node_content": "Revised mechanism and visual direction.",
      "current_overview": "Current relic direction.",
      "materials": [{"kind":"Document","reference":"docs/relics.md","label":"Design notes"}]
    }
  }
}
```

- [ ] **Step 1: Write failing atomic proposal and UI tests**

```csharp
[Fact]
public async Task Accept_applies_node_overview_and_references_atomically()
{
    var proposal = await service.CreateProposalAsync(UpdateNodeDraft(expectedNodeRevision: 1, expectedOverviewRevision: 0));
    await service.AcceptAsync(project.Id, proposal.Id);
    Assert.Equal(LibraryProposalStatus.Accepted, (await service.GetAsync(project.Id, proposal.Id))!.Status);
    Assert.Equal("Revised", (await repository.GetNodeAsync(project.Id, node.Id))!.Content);
    Assert.Equal("Current", (await repository.GetObjectAsync(project.Id, objectId))!.CurrentOverview);
}

[Fact]
public async Task Stale_target_leaves_proposal_pending_and_writes_nothing()
{
    var proposal = await service.CreateProposalAsync(UpdateNodeDraft(expectedNodeRevision: 0));
    await Assert.ThrowsAsync<LibraryRevisionConflictException>(() => service.AcceptAsync(project.Id, proposal.Id));
    Assert.Equal(LibraryProposalStatus.Pending, (await service.GetAsync(project.Id, proposal.Id))!.Status);
}

[Fact]
public async Task Ordinary_leader_turn_may_emit_no_memory_command()
{
    runtime.QueueTurn(Completed("{\"response\":\"ok\",\"draft_proposal\":null,\"memory_commands\":null}"));
    await pane.SendAsync();
    Assert.Empty(await proposals.GetPendingAsync(project.Id));
}

private ProjectLibraryProposalDraft UpdateNodeDraft(
    int expectedNodeRevision,
    int expectedOverviewRevision = 0) =>
    new(
        Guid.Parse("30000000-0000-4000-8000-000000000001"),
        project.Id,
        sourceSessionId,
        LibraryProposalAction.UpdateNode,
        objectId,
        node.Id,
        expectedNodeRevision,
        expectedOverviewRevision,
        "Design",
        "Relics",
        day,
        "Revised",
        "Current",
        [new("Document", "docs/relics.md", "Design notes")],
        t0);
```

- [ ] **Step 2: Run focused tests and verify RED**

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test 'tests\Workbench.Storage.Tests\Workbench.Storage.Tests.csproj' --filter 'FullyQualifiedName~ProjectLibraryProposalTests'
& 'C:\Program Files\dotnet\dotnet.exe' test 'tests\Workbench.App.Tests\Workbench.App.Tests.csproj' --filter 'FullyQualifiedName~LeaderDraftProposalTests|FullyQualifiedName~ProjectLibraryProposalUiTests'
```

- [ ] **Step 3: Implement proposal persistence and atomic acceptance**

Store a validated bounded JSON payload in `project_library_proposals`, but deserialize and validate it before every apply. In one transaction, acceptance:

1. compares target revisions;
2. creates or resolves the exact normalized Object;
3. creates or updates the selected Node;
4. replaces the proposal-owned material references;
5. optionally updates Current Overview;
6. marks the proposal Accepted.

Any failure rolls back all six operations.

- [ ] **Step 4: Extend Leader structured output without hiding visible failures**

`LeaderStructuredResponse.TryParse` returns visible `response`, optional Worker `draft_proposal`, and optional validated `memory_commands`. Invalid memory commands create no proposal and surface a non-transcript UI status; they must not replace the visible Leader response with raw JSON.

- [ ] **Step 5: Add proposal review UI**

Show action, target, LocalDate, node content, proposed Overview, and reference labels. Provide Accept, Edit+Accept, and Reject. Do not label a pending proposal as Library history until acceptance succeeds.

- [ ] **Step 6: Run App/Storage tests to GREEN**

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test 'tests\Workbench.Storage.Tests\Workbench.Storage.Tests.csproj'
& 'C:\Program Files\dotnet\dotnet.exe' test 'tests\Workbench.App.Tests\Workbench.App.Tests.csproj'
```

- [ ] **Step 7: Commit explicit Library confirmation**

```powershell
git add src/Workbench.Storage/Memory src/Workbench.App/Memory src/Workbench.App/Leader/LeaderDraftProposalBuilder.cs src/Workbench.App/ViewModels/Panes src/Workbench.App/Views/Panes tests/Workbench.Storage.Tests/Memory/ProjectLibraryProposalTests.cs tests/Workbench.App.Tests
git commit -m 'feat(library): confirm leader evolution proposals'
```

**Stop condition:** the Skill may propose at any normal or catch-up turn, but only user confirmation changes the evolution archive.

---

### Task 9: Full Verification, Additive Migration Evidence, And Documentation

**Files:**
- Modify: `README.md`
- Modify: `docs/superpowers/specs/2026-08-14-memory-continuity-library-design.md` only if implementation reveals a proven contract correction
- Modify: this plan to mark only completed tasks

- [ ] **Step 1: Run restore and build**

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' restore 'AI.Game.Workbench.sln'
& 'C:\Program Files\dotnet\dotnet.exe' build 'AI.Game.Workbench.sln' --no-restore
```

Expected: 0 warnings, 0 errors.

- [ ] **Step 2: Run every test project and the solution entry point**

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test 'tests\Workbench.Core.Tests\Workbench.Core.Tests.csproj' --no-build --no-restore
& 'C:\Program Files\dotnet\dotnet.exe' test 'tests\Workbench.Runtime.Tests\Workbench.Runtime.Tests.csproj' --no-build --no-restore
& 'C:\Program Files\dotnet\dotnet.exe' test 'tests\Workbench.Storage.Tests\Workbench.Storage.Tests.csproj' --no-build --no-restore
& 'C:\Program Files\dotnet\dotnet.exe' test 'tests\Workbench.Project.Tests\Workbench.Project.Tests.csproj' --no-build --no-restore
& 'C:\Program Files\dotnet\dotnet.exe' test 'tests\Workbench.App.Tests\Workbench.App.Tests.csproj' --no-build --no-restore
& 'C:\Program Files\dotnet\dotnet.exe' test 'AI.Game.Workbench.sln' --no-build --no-restore
```

Expected: every project and the full solution pass with zero failures.

- [ ] **Step 3: Run additive v8→v11 migration and legacy-preservation tests**

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test 'tests\Workbench.Storage.Tests\Workbench.Storage.Tests.csproj' --no-build --no-restore --filter 'FullyQualifiedName~WorkbenchDatabaseTests|FullyQualifiedName~ProjectLibraryLegacyImportTests'
```

Expected: PASS. The v8 fixture reaches version 11 with integrity `ok`, no foreign-key violations, an unchanged `project_library_entries` count, and one imported timeline node per legacy entry.

- [ ] **Step 4: Run deterministic fake-runtime behavior smokes**

Verify all four cases:

1. New Brain with null writes and empty selection.
2. New Brain with Daily + Handoff + Raw selection and restart before first send.
3. Same-day Library `UpdateNode` proposal accepted after edit.
4. Substantive change `CreateNode` proposal rejected without archive mutation.

No real Provider call is required unless a separate release audit explicitly requests it.

- [ ] **Step 5: Update README milestone state and inspect scope**

Document completed slices only. Then run:

```powershell
git diff --stat
git diff
git diff --check
git status --short
```

Expected: only intended source/tests/docs changes, no database, build output, or unrelated files.

- [ ] **Step 6: Commit release verification**

```powershell
git add README.md docs/superpowers
git commit -m 'docs(memory): record continuity and library rollout'
```

**Stop condition:** additive migrations preserve legacy data, all tests are green, documented completed slices match code, and the working tree is clean.

---

## Plan Self-Review

- Spec coverage: Tasks 1–5 cover Daily Summary, preferences, Handoff, raw transcript reuse, explicit continuity selection, New Brain delivery, and removal of default automatic governance. Tasks 6–8 cover Object/Overview/Timeline/reference storage, legacy import, symmetric projections, and confirmation. Task 9 covers additive migration and full verification.
- Type consistency: `ProjectMemoryPreferences`, `DailySummaryDocument`, `ContinuityMaterialSelection`, `LeaderEpochContinuityPlan`, `ProjectLibraryObject`, `ProjectLibraryTimelineNode`, and proposal revision fields retain the same names and meanings across producer and consumer tasks.
- Scope: every task ends at an independently testable boundary and can be deferred without invalidating earlier storage. No task requires graph, vector, semantic, artifact-ingestion, or background-agent infrastructure.
- Placeholder scan: the plan contains no unspecified implementation placeholders; exact contracts, tables, tests, commands, commits, and stopping conditions are present.
