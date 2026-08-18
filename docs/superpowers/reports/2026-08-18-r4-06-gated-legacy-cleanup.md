# Workbench R4-06 Gated Legacy Cleanup

## Result

`PARTIAL_SAFE_CLEANUP`

Cleanup was limited to production code with no current caller and no migration or historical-data responsibility. Legacy data, compatibility readers, core worker state, and history remain intact.

## Baseline

- Start HEAD: `01afc5068591d8da4a7cae341c88bc1f40e32579`
- Branch: `master`
- Code schema: `v18`
- Production DB: `v11` (not opened or modified)

## Cleanup Gate

| Candidate | Current writer | Current reader | Migration | History | Replacement | Safe-delete condition | Decision |
|---|---|---|---|---|---|---|---|
| Memory synthesis coordinator, prompt, payload parser | None after R4-04 freeze | No production caller | None | No persisted code dependency | Frozen read-only gate plus manual repositories | No caller, no migration reader, no history contract | `DELETE_NOW` |
| Memory synthesis repository/models/table | Manual/test compatibility only | Restart recovery, library status, manual reads | Migration005 | 20/28/16 legacy rows retained | R5 destination not defined | Data destination and R5 truth not available | `BLOCKED_BY_R5` / `KEEP_COMPAT` |
| Daily-summary policy prompt/parser | None | No production caller | None | None | Frozen policy gate | No caller and no persisted contract | `DELETE_NOW` |
| `LeaderMemoryPolicyCoordinator` | Frozen no-op gate | Leader rollover path | None | None | Explicit frozen read-only behavior | Gate still prevents second automatic pass | `KEEP_CORE` |
| Daily summary repository/table | Manual API | Continuity reads and compatibility UI | Daily-summary migrations | Historical summaries | R5 destination not defined | Destination and reconciliation unavailable | `BLOCKED_BY_R5` |
| Worker execution state | Active router/repository | Recovery, routing, certification | Worker migrations | Current execution history | None | Canonical authority is required | `KEEP_CORE` |
| `task_events` | Active audit/routing writes | Review, handoff, migration readers | Migrations017/018 | Historical event payloads | None | Historical payload preservation required | `KEEP_HISTORY` |
| `worker_completion_packages` | No observed app writer | Compatibility repository/tests | Migration007 | Destination/audit dependency not disproven | None | Migration and production-copy reconciliation unavailable | `KEEP_COMPAT` |
| FinalReport/Handoff compatibility | Thin handoff writer; canonical report writer | Review input and legacy fallback reader | Review migrations | Three historical handoffs lack `SourceEventId` | Typed review authority | Legacy fallback remains required | `KEEP_COMPAT` |
| Legacy Library entries/API | No current canonical writer; manual `SubmitAsync` remains | Browse, Migration011 import, coverage API | Migration008/011 | Legacy rows may exist in older DBs | Objects/Timeline/MaterialRefs | Old DB/manual compatibility must remain | `KEEP_COMPAT` |
| Legacy Review JSON helpers | Typed reader and migration backfill | History and migration readers | Migration017/018 | `task_events.payload_json` is historical | Typed review authority | Payload rewrite/drop is prohibited | `KEEP_HISTORY` |

## Deleted Now

- Classes: frozen `ProjectMemorySynthesisCoordinator`, synthesis prompt/payload helpers, and unused daily-summary policy prompt/payload helpers.
- Production wiring: `AppServices.ScheduleMemorySynthesis` and its removed automatic execution composition.
- Obsolete tests: tests that only exercised removed automatic synthesis or policy serialization paths.
- Repositories: none.
- Tables: none.

## Retained Compatibility / R5 Blocks

- `project_memory_items`, `project_memory_sources`, `project_memory_synthesis_jobs`: retained with existing data and readers; `BLOCKED_BY_R5_DESTINATION`.
- `project_daily_summaries` and source data/schema: retained; `BLOCKED_BY_R5_DESTINATION`.
- Project Library legacy repository, browse/import, and deterministic coverage API: retained for old DB/manual compatibility.
- Worker handoff fallback for historical payloads without `SourceEventId`: retained.
- `worker_executions`: `KEEP_CORE`.
- `task_events`: `KEEP_HISTORY`.

## Schema

`Migration019`: not created. No table satisfied the full drop gate, so `NO_SCHEMA_DROP_THIS_TASK`.

## Certifications

The App and Storage suites cover Memory freeze/restart, Library reconciliation and compatibility, Review compatibility, Worker execution, FinalReport/Handoff, and provider-independent recovery (R1 Task5). All passed after cleanup.

## Safety

- Live DB: not accessed or mutated.
- Historical data: no rows deleted or rewritten.
- Migration: no new migration; existing migrations remain unchanged.
- Rollback: code-only deletion is revertible; retained data/schema has no destructive change.

## Scope

No R4-07 production-copy seal, R5 Truth, Summary Delta, replacement platform, or live mutation was performed.

## Simplification Gain

`MEDIUM`: removed the dead automatic synthesis runtime and policy serialization surface while preserving frozen data repositories, manual reads, compatibility migrations, core execution state, and event history.
