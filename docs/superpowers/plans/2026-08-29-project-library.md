# Project Library Axis Browsing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Present Project Library as overview, category, time, and project projections using existing durable data.

**Architecture:** Keep storage and authority contracts unchanged. Extend the Library pane view model with hierarchical category/time selection state and dense event projections, then replace the flat Avalonia templates with explicit index/detail regions.

**Tech Stack:** C#/.NET 10, Avalonia, CommunityToolkit.Mvvm, existing Workbench.Storage repositories, xUnit.

**Spec:** `docs/superpowers/specs/2026-08-29-project-library-design.md`

## Global Constraints

- Do not add database tables or change authority semantics.
- Browsing must be read-only.
- Keep horizontal scrolling disabled.
- Preserve English and Simplified Chinese localization patterns.

---

### Task 1: Add Library projection state and daily activity models

**Files:**
- Modify: `src/Workbench.App/ViewModels/Panes/LibraryPaneViewModel.cs`
- Modify: `src/Workbench.App/Memory/IProjectMemoryApi.cs`
- Modify: `src/Workbench.App/Memory/ProjectMemoryApi.cs`
- Test: `tests/Workbench.App.Tests/LibraryCategoryTimeViewTests.cs`

- [x] Add selected category/object/year/month/day state and read-only view records.
- [x] Add a summary-entry query projection for a local date.
- [x] Load daily summaries and summary entries without writing state.
- [x] Make repeated object/date selection clear the detail projection.

### Task 2: Replace flat Library templates with axis navigation

**Files:**
- Modify: `src/Workbench.App/Views/Panes/LibraryPaneView.axaml`
- Modify: `src/Workbench.App/Services/LocalizationService.cs`
- Test: `tests/Workbench.App.Tests/LibraryVisualClosureTests.cs`

- [x] Render category index and selected category detail separately.
- [x] Render time year → month → day indexes and selected-day event detail separately.
- [x] Put timestamped events and source references in the selected-day detail.
- [x] Keep project and overview projections readable and vertically scrollable.

### Task 3: Verify complete solution and desktop development instance

**Files:**
- No production files beyond Tasks 1-2.

- [x] Run focused Library tests.
- [x] Run the full solution tests with one test worker.
- [x] Build the solution with zero warnings/errors.
- [ ] Restart the local Native Surface instance from `tools/run-native-surface.ps1`.
