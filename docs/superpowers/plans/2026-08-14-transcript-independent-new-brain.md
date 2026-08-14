# Transcript-Independent New Brain Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose Project Library Current Overviews and Timeline Nodes as explicitly selectable continuity materials so a New Brain can boot from Daily Summary plus Library without an old Agent transcript or Handoff.

**Architecture:** Add metadata-only Library read projections to `ProjectLibraryEvolutionRepository`, then inject that repository into the existing `ProjectContinuityMaterialService`. Use stable `library-overview:{objectId}` and `library-timeline:{nodeId}` references; catalog calls read only structural fields and byte counts, while resolver calls fetch bodies and typed material references only for selected items. Keep the existing continuity plan, ordering, omission, and `LeaderBootContextBuilder` path unchanged.

**Tech Stack:** .NET 10, C#, Microsoft.Data.Sqlite, xUnit, Windows PowerShell.

## Global Constraints

- Reuse `ContinuityMaterialKind.LibraryOverview` and `ContinuityMaterialKind.LibraryTimelineNode`; add no material kind, table, or migration.
- Library is explicit-selection-only: no automatic inclusion, ranking, search, embedding, RAG, recent-N loading, or keyword lookup.
- Catalog returns metadata only and must not load Overview or Timeline bodies.
- Resolution is strictly Project-scoped; stale, invalid, wrong-kind, or cross-Project references enter `OmittedReferences` without substitution.
- Timeline source/material handling returns typed reference metadata only; it never reads or copies referenced file, image, commit, or transcript bodies.
- Preserve selection ordinal semantics and the existing first-send continuity-plan lifecycle.
- Reads and boot resolution must produce zero Library, Daily Summary, Handoff, Leader message, or synthesis writes.
- Do not perform retention cleanup or change transcript, Handoff, Assignment, Final Report, or synthesis lifecycles.
- Use one final commit: `feat(memory): read library continuity materials`.

---

### Task 1: Metadata-Only Library Catalog And Explicit Reads

**Files:**
- Modify: `src/Workbench.Storage/Memory/ProjectLibraryEvolutionModels.cs`
- Modify: `src/Workbench.Storage/Memory/ProjectLibraryEvolutionRepository.cs`
- Modify: `src/Workbench.App/Memory/ProjectContinuityMaterialService.cs`
- Modify: `src/Workbench.App/Memory/ProjectMemoryApi.cs`
- Modify: `src/Workbench.App/Services/AppServices.cs`
- Modify: `tests/Workbench.App.Tests/ProjectContinuityMaterialServiceTests.cs`

**Interfaces:**

```csharp
public sealed record ProjectLibraryOverviewMetadata(
    Guid ObjectId, Guid ProjectId, string Category, string Topic,
    int Revision, DateTimeOffset UpdatedAt, int Utf8Bytes);

public sealed record ProjectLibraryTimelineNodeMetadata(
    Guid NodeId, Guid ObjectId, Guid ProjectId, string Category, string Topic,
    DateOnly LocalDate, int Revision, DateTimeOffset CreatedAt, int Utf8Bytes);

public Task<IReadOnlyList<ProjectLibraryOverviewMetadata>> ListOverviewMetadataAsync(
    Guid projectId, CancellationToken cancellationToken = default);

public Task<IReadOnlyList<ProjectLibraryTimelineNodeMetadata>> ListTimelineMetadataAsync(
    Guid projectId, CancellationToken cancellationToken = default);
```

- [ ] **Step 1: Write failing catalog tests**

Add tests proving: non-empty Overview produces one `LibraryOverview` descriptor; empty Overview produces none; each Timeline Node produces one `LibraryTimelineNode` descriptor; labels contain Category/Topic/Object and Timeline date; byte sizes are exact; catalog labels contain no body marker.

- [ ] **Step 2: Run RED**

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test 'tests\Workbench.App.Tests\Workbench.App.Tests.csproj' --filter 'FullyQualifiedName~ProjectContinuityMaterialServiceTests'
```

Expected: FAIL because Library metadata is absent from the catalog.

- [ ] **Step 3: Implement metadata-only SQL projections and catalog descriptors**

Use SQL `length(CAST(current_overview AS BLOB))` and `length(CAST(node.content AS BLOB))`; do not select either body in catalog queries. Produce stable references exactly as `library-overview:{objectId}` and `library-timeline:{nodeId}`.

- [ ] **Step 4: Write failing explicit-read/isolation tests**

Add tests proving selected Overview returns exact content; selected Timeline returns exact factual content plus `MaterialKind`, reference, and optional label metadata; referenced artifact bodies are absent; invalid/stale and Project-B references are omitted without replacement.

- [ ] **Step 5: Run RED, implement minimal explicit readers, then run GREEN**

Overview resolution must call project-scoped `GetObjectAsync`; Timeline resolution must call project-scoped `GetNodeAsync` and `GetMaterialReferencesAsync`. Validate the selection kind and reference prefix before reading. Format Timeline reference metadata deterministically and include its UTF-8 bytes in budget accounting.

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test 'tests\Workbench.App.Tests\Workbench.App.Tests.csproj' --filter 'FullyQualifiedName~ProjectContinuityMaterialServiceTests'
```

---

### Task 2: Durable First-Send And Transcript-Independent Boot

**Files:**
- Modify: `tests/Workbench.App.Tests/ProjectContinuityMaterialServiceTests.cs`
- Modify: `tests/Workbench.App.Tests/LeaderBootContextBuilderTests.cs`
- Modify only if required by RED: `src/Workbench.App/Leader/LeaderBootContextBuilder.cs`

**Interfaces:** Reuse `LeaderEpochContinuityPlan`, `LeaderEpochContinuityRepository`, `ResolvedContinuityBundle`, and `LeaderBootContextBuilder.BuildAsync` unchanged.

- [ ] **Step 1: Write failing selection/boot tests**

Cover all of the following in focused tests:

```csharp
var plan = new LeaderEpochContinuityPlan(successor.Id, 32_000,
[
    new(0, ContinuityMaterialKind.DailySummary, "daily:2026-08-14", 8_000),
    new(1, ContinuityMaterialKind.LibraryOverview, $"library-overview:{libraryObject.Id}", 8_000),
    new(2, ContinuityMaterialKind.LibraryTimelineNode, $"library-timeline:{node.Id}", 8_000)
], now);
```

Assert persisted selection restart round-trip; selected Daily+Overview+Timeline appear in ordinal order on first send; unselected Library body never appears; stale Library reference appears only in `OmittedReferences`; other valid selections remain.

- [ ] **Step 2: Run RED**

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test 'tests\Workbench.App.Tests\Workbench.App.Tests.csproj' --filter 'FullyQualifiedName~ProjectContinuityMaterialServiceTests|FullyQualifiedName~LeaderBootContextBuilderTests'
```

- [ ] **Step 3: Implement only code required by RED and run GREEN**

Do not introduce fallback reads. The transcript-independent fixture contains no `leader_messages`, no Handoff, and no accessible old external Agent Session; `BuildAsync` must still resolve the persisted Daily+Library plan without runtime access.

- [ ] **Step 4: Verify zero write side effects**

Snapshot row counts after fixture setup and assert catalog/read/boot do not change Library tables, Daily tables, `leader_messages`, Handoff values, or `project_memory_synthesis_jobs`.

---

### Task 3: Minimal Leader Guidance And Full Verification

**Files:**
- Modify: `src/Workbench.App/Memory/LeaderMemoryPolicyPromptBuilder.cs`
- Test: `tests/Workbench.App.Tests/LeaderMemoryPolicyCoordinatorTests.cs`
- Include: `docs/superpowers/plans/2026-08-14-transcript-independent-new-brain.md`

- [ ] **Step 1: Write a failing instruction test**

Assert the policy prompt says Current Overview represents an object's current factual state, Timeline Nodes represent historical evolution, older projects may prefer explicit Library+Daily selections over Raw Conversation, and selection remains descriptor/reference-driven rather than automatic.

- [ ] **Step 2: Run RED, add the minimal instruction text, run focused GREEN**

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test 'tests\Workbench.App.Tests\Workbench.App.Tests.csproj' --filter 'FullyQualifiedName~LeaderMemoryPolicyCoordinatorTests|FullyQualifiedName~ProjectContinuityMaterialServiceTests|FullyQualifiedName~LeaderBootContextBuilderTests'
```

- [ ] **Step 3: Run required regression matrix**

Run focused tests, Slice A/B/C/D and New Brain persistence/rotation tests by filter, then full `Workbench.Storage.Tests`, `Workbench.Project.Tests`, `Workbench.App.Tests`, `Workbench.Core.Tests`, `Workbench.Runtime.Tests`, solution tests, and solution build. Every command must exit 0 with zero warnings and zero errors.

- [ ] **Step 4: Review requirements and diff**

```powershell
git diff --check
git status --short
git diff --stat
```

Confirm all 13 RED requirements are represented, no migration/schema/retention files changed, and no placeholder text remains in this plan.

- [ ] **Step 5: Commit once and verify repository state**

```powershell
git add docs/superpowers/plans/2026-08-14-transcript-independent-new-brain.md src tests
git commit -m 'feat(memory): read library continuity materials'
git rev-parse HEAD
git status --short
git diff --check
```

Expected: commit succeeds on `master`; final tree is clean.
