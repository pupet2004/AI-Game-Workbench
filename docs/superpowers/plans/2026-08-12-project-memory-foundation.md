# Project Memory Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Establish Workbench-owned, project-scoped activity and memory certification infrastructure with a minimal Library review surface.

**Architecture:** Migration004 creates independent activity, memory, and provenance tables. `ProjectMemoryService` is the sole certification boundary: agent-facing creation accepts Candidate only; Accept creates a new Formal record while preserving the Candidate. The Library Project section only reviews persisted Candidate/Formal data.

**Tech Stack:** .NET 10, Avalonia 12, CommunityToolkit.Mvvm, Microsoft.Data.Sqlite, xUnit.

## Global Constraints

- No Runtime/provider dependency, LLM synthesis, automatic extraction, RAG, embeddings, boot injection, or transcript copies.
- Activity summaries are bounded to 1000 UTF-8 bytes; memory topics to 200 bytes and content to 8000 bytes.
- Formal memory is created only through user certification.

### Task 1: Migration and repositories

**Files:** `Migration004ProjectMemoryFoundation.cs`, `ProjectActivityRepository.cs`, `ProjectMemoryRepository.cs`, Storage tests.

- [x] Write failing Storage tests for candidate persistence, provenance, isolation, bounds, and certification.
- [x] Add transactional 3→4 schema migration with project cascade foreign keys.
- [x] Add project-scoped activity/memory/provenance persistence.

### Task 2: Certification service

**Files:** `ProjectMemoryService.cs`, Storage tests.

- [x] Create Candidate-only agent entry point and Manual Activity entry point.
- [x] Accept/Edit+Accept create Formal while preserving Candidate; Reject marks Candidate rejected.
- [x] Reject direct Formal agent creation and direct modification APIs by omission.

### Task 3: Library review UI

**Files:** App services, workspace composition, `LibraryPaneViewModel`, `LibraryPaneView.axaml`.

- [x] Surface pending count, candidate selection, Accept/Edit+Accept/Reject, and read-only Formal list in Library Project.
- [x] Keep review entirely deterministic and provider-free.

### Task 4: Verification and release

- [x] Run full restore/build/test, real v3→v4 database verification, UI smoke, diff check, and commit.

M1.5A manual certification smoke was completed on 2026-08-13 in `AI Game Workbench M105B Smoke`: Accept, Edit + Accept, and Reject all passed without modifying protected projects.
