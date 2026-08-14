# Memory Continuity And Evolution Library Design Spec

## 1. Product Boundary

AI Game Workbench owns durable memory APIs, persistence, deterministic indexes, source references, user preferences, and UI. It does not decide what is important, when a summary is warranted, whether a design change is substantial, or which historical material is relevant.

The responsibility split is:

- **Workbench:** expose bounded read/write operations, preserve project ownership, enforce optimistic concurrency and referential integrity, report material age/size, and render persisted data.
- **Leader plus Leader Skill:** decide when to write, what to write, whether to revise or skip, what to carry into a fresh Brain, and whether a Daily Summary deserves a Library proposal.
- **User:** choose policy profiles, approve or reject Library mutations when confirmation is required, and override every prior memory source through the current instruction.

Workbench must not add fixed-interval summarization, mandatory end-of-day behavior, mandatory Handoff generation, automatic truth certification, autonomous cleanup, or a background knowledge-governance scheduler. New Brain is a continuity boundary, not an implicit Save Memory or End Day command.

## 2. Existing Architecture Assessment

### Reuse

- `leader_messages` remains the canonical Recent Raw Conversation. It already stores project-owned visible user/assistant messages through `LeaderSessionEpoch`; no second transcript is needed.
- `leader_session_epochs.handoff_summary` remains the storage location for an optional Brain Handoff. Its lifecycle is one source epoch to its immediate successor.
- `LeaderSessionEpochRepository`, `LeaderMessageRepository`, `ProjectLeaderRepository`, `ProjectLeaderSessionManager`, and the durable `boot_context_delivered_at` mechanism remain the continuity substrate.
- `ProjectMemorySource` establishes the useful convention that provenance is a typed reference, not copied source content. New Daily Summary and Library APIs use focused source-reference records with the same rule.
- `ProjectLibraryRepository` demonstrates project-scoped persistence, deterministic reverse-time ordering, bounded text validation, and idempotent submission IDs. Those implementation patterns are reusable.
- Existing Workbench and project settings repositories demonstrate global defaults plus project overrides. New memory preferences use a focused repository so clearing rotation settings cannot delete unrelated policy fields.

### Preserve But Downgrade To Legacy Substrate

- `project_activity_events` remains available for explicit activity/audit records. It is not a Daily Summary because it is an event stream rather than one revisable document per local date.
- `project_memory_items`, `project_memory_sources`, and the Activity/Learned/Candidate/Formal UI remain readable and migration-safe. Formal certification remains valid historical data.
- `project_memory_synthesis_jobs` and the synthesis coordinator remain compatible with existing data, but the new design does not create new automatic synthesis triggers or feed their output into Daily Summary or the Evolution Library.
- Existing Active Formal/Learned records may be exposed as labeled legacy material to a Leader Skill during transition. Workbench no longer treats them as the primary project-continuity model.
- `project_library_entries` remains untouched as a legacy source table. A later additive migration imports every row into the evolution model before the old write API is frozen.

### Do Not Continue Expanding

- Do not add Daily Summary or Library semantics as new `ProjectMemoryItem.Layer` values.
- Do not extend archived-epoch synthesis into a general memory engine.
- Do not keep project-open, completed-turn, rollover, or Library-open automatic synthesis as the default policy path.
- Do not keep mandatory semantic/fallback Handoff generation inside `LeaderSessionRolloverService`.
- Do not expand the flat `LibrarySubmission` contract with enough nullable fields to impersonate an Object, Overview, Timeline Node, proposal, and material collection.

### Schema Reality

The current v8 source and default database contain `project_memory_items`, `project_memory_sources`, `project_memory_synthesis_jobs`, and `project_library_entries`. They do **not** contain `project_memory_links`, `project_memory_topics`, or `project_memory_topics_join`. The new design does not assume those absent tables and does not introduce them under those names.

## 3. Approaches Considered

### A. Put Everything In `project_memory_items`

This minimizes tables but conflates certification records, mutable daily documents, epoch-local Handoffs, Library Objects, and timeline nodes. Layer/status strings would become an implicit state machine, while references and same-day updates would remain awkward. Rejected.

### B. Keep Extending `project_library_entries`

This preserves the current repository but leaves no stable Object identity, no independently revised Current Overview, one source reference only, a required source session, and no clean distinction between updating today's node and adding a historical node. Suitable only as legacy import. Rejected as the target model.

### C. Separate Small Documents From Evolution Records

Recommended. Daily Summary gets one focused document table; Handoff and raw conversation reuse existing storage; the Library gets Object, Timeline Node, Material Reference, and Proposal records. Category and Time are queries over the same nodes. This adds only relations required by the product model and keeps intelligent policy outside Workbench.

## 4. Final Memory Concepts

| Concept | Canonical storage | Lifecycle | Who decides content/use |
| --- | --- | --- | --- |
| Recent Raw Conversation | `leader_messages` | Append-only visible transcript inside an epoch | Leader Skill chooses an exact bounded range when needed |
| Brain Handoff | `leader_session_epochs.handoff_summary` | Optional, short, source-epoch scoped, frozen after archive | Leader Skill chooses whether and when to write/update |
| Daily Summary | New `project_daily_summaries` | One revisable document per Project + persisted local date | Leader Skill chooses skip/create/revise/compress |
| Project Library | New Object/Timeline/Material tables | Long-lived project evolution archive | Leader Skill proposes organization; user confirms configured mutations |

These concepts never alias one another. A Handoff does not become a Daily Summary automatically. A Daily Summary does not become a Library node automatically. Raw conversation is referenced, not copied, by the other concepts.

## 5. Daily Summary Model

### Tables

`project_daily_summaries`:

- `project_id TEXT NOT NULL`
- `local_date TEXT NOT NULL` in exact `yyyy-MM-dd` form
- `content TEXT NOT NULL`
- `revision INTEGER NOT NULL CHECK(revision >= 1)`
- `created_at TEXT NOT NULL`
- `updated_at TEXT NOT NULL`
- primary key `(project_id, local_date)`
- project cascade foreign key

`project_daily_summary_sources`:

- `project_id TEXT NOT NULL`
- `local_date TEXT NOT NULL`
- `source_type TEXT NOT NULL`
- `source_ref TEXT NOT NULL`
- primary key `(project_id, local_date, source_type, source_ref)`
- composite cascade foreign key to the Daily Summary

The API accepts a complete replacement document plus `ExpectedRevision`. Create requires no existing document; revise/compress/append are all Leader Skill decisions expressed as a new complete document. Workbench performs compare-and-swap and never merges prose.

The Project local date is calculated using the persisted project memory time-zone setting. The first use records the current system time-zone identifier. A later time-zone change affects future writes only; existing `local_date` keys do not move.

## 6. Brain Handoff And Recent Raw

Brain Handoff reuses the nullable `handoff_summary` column. A focused repository method may write or clear the Handoff only while the source epoch is active. Rollover freezes the value and no longer requires non-empty content. Workbench does not generate a semantic or fallback Handoff on its own.

Recent Raw remains in `leader_messages`. Add project-scoped reverse paging and statistics APIs rather than a new table:

- validate that the requested epoch belongs to the project;
- return exact user/assistant text ordered by sequence;
- accept caller-supplied `beforeSequence`, message count, and UTF-8 budget;
- report available count and UTF-8 size;
- never persist a selected tail separately.

The caller-supplied limits come from the Leader Skill decision and user preference. This design intentionally defines no fixed message count, hour threshold, or token constant.

## 7. User Memory Preferences

`project_memory_preferences` stores only user policy input:

- `project_id` primary key;
- `library_granularity`: `Balanced`, `Detailed`, `Compact`, or `Custom`;
- `continuity_mode`: `Balanced`, `HighContinuity`, `LowToken`, or `Custom`;
- `time_zone_id`;
- optional bounded `custom_instructions`;
- `updated_at`.

Absence of a row means `Balanced`/`Balanced` and the current system time zone. Workbench returns these values to the Leader Skill. It does not translate a profile into automatic summary frequency, node creation, or material selection.

## 8. Continuity Material API

The App layer exposes a provider-neutral `IProjectMemoryApi`. Repositories remain internal implementation details. The contract includes:

- list/read/save Daily Summary;
- read/save/clear active Brain Handoff;
- inspect and read a bounded Recent Raw range;
- browse/read Library Objects, Overviews, Timeline Nodes, and references;
- create Library mutation proposals and accept/edit/reject them;
- read Project memory preferences;
- list material descriptors and resolve an explicit continuity selection.

A `ContinuityMaterialDescriptor` contains a stable reference, material kind, project ID, date/time, age, UTF-8 size, and a short structural label such as date or Category/Topic. It does not duplicate the material body.

A `ContinuitySelection` is an ordered list of descriptor references plus explicit per-item and total budgets. Workbench resolves only those references, validates project ownership, preserves exact raw text, omits nothing silently, and reports anything that could not fit. The Leader Skill chooses the references and budgets.

For transport, the first implementation extends the existing provider-neutral structured Leader response pattern. A dedicated policy envelope carries explicit memory commands and continuity selections; it does not expose SQLite or a Codex-specific tool. The `IProjectMemoryApi` boundary allows a future native function/MCP adapter without changing storage or policy semantics.

## 9. New Brain Continuity Flow

1. A user or existing rotation policy requests a new Leader Brain.
2. Workbench builds a metadata-only catalog of available Daily Summary, current source-epoch Handoff, raw-tail statistics, and Library descriptors, together with user preferences.
3. The Leader Skill receives a continuity-policy request and returns an explicit selection. Daily Summary, Handoff, and Library commands in that response are optional; null means no write.
4. Workbench validates and applies only explicit commands. New Brain itself never synthesizes or writes knowledge.
5. Rollover atomically archives the source epoch, creates the successor, and stores the ordered continuity selection as references in `leader_epoch_continuity_selections`. Material bodies remain in their canonical tables.
6. The first real user send resolves the stored selection, builds one continuity envelope, appends the original user message unchanged, and uses the existing delivery timestamp to avoid duplicate injection after acceptance.
7. The current user instruction outranks every selected material. Handoff is labeled immediate continuity, Daily Summary is dated, Recent Raw is verbatim, and Library material is labeled by Object and node date.

If the policy turn is unavailable or invalid, Workbench creates no Summary, Handoff, or Library write. The successor can still start with Project identity and an empty optional selection; the UI reports that continuity selection was unavailable. Persistence is not lost, and no Workbench heuristic guesses which historical content to inject.

The metadata catalog and ordered selection are bounded and paged. A long-lived project never loads every transcript, every Daily Summary, or every Library node into a fresh context.

## 10. Library Evolution Model

### Object

`project_library_objects` is the stable identity for one project-specific topic/object:

- `id`, `project_id`;
- display `category` and `topic`;
- deterministic `category_key` and `topic_key` using trim, whitespace collapse, and ordinal case normalization;
- nullable `current_overview`;
- `overview_revision`, `created_at`, and `updated_at`;
- unique `(project_id, category_key, topic_key)`.

Workbench performs only exact deterministic key normalization. It does not infer aliases or fuzzy equivalence.

### Current Overview

Current Overview is a persisted mutable projection on the Object record, not an independent timeline node and not an automatic computation. The Leader Skill may propose a concise replacement; acceptance uses `ExpectedOverviewRevision`. The complete timeline remains authoritative history even when the Overview changes.

### Timeline Node

`project_library_timeline_nodes` contains:

- `id`, `object_id`;
- `local_date`;
- bounded `content`;
- `revision`, `created_at`, and `updated_at`.

Multiple nodes on the same date are allowed. For a small same-day continuation, the Leader Skill targets an existing node and supplies its expected revision. For a substantive change, it proposes a new node. Workbench does not classify the difference.

The canonical display order is `local_date DESC, created_at DESC, id`. The UI places newest at the top and uses upward arrows to communicate historical movement from older to newer. It does not render `SupersededBy`, `DerivedFrom`, or graph terminology.

### Material Reference

`project_library_material_refs` contains `node_id`, `material_kind`, `reference`, optional `label`, and `created_at`. It stores references only:

- Image, Screenshot, Document, and File use a path or URI;
- GitCommit uses a repository-relative commit reference;
- WorkerReport uses Task/session/event identity;
- LeaderDecision and Conversation use epoch/message identity;
- short explanatory text and design rules live in the node content itself.

No image bytes, document body, complete transcript, Worker report body, or Git diff is copied into this table.

### Proposal And Confirmation

`project_library_proposals` persists one validated Leader Skill proposal with status `Pending`, `Accepted`, or `Rejected`. Its payload identifies Create Node or Update Node, optional Overview replacement, expected revisions, local date, and material references. Accept/Edit+Accept applies Object, Overview, Node, and references in one transaction. Reject changes only proposal state.

This is not automatic truth certification. It is an explicit UI confirmation boundary for durable organization. The Leader Skill decides when to propose, including stage-end or next-day catch-up; Workbench never schedules proposals.

## 11. Category And Time Projections

Category and Time query the same Object and Timeline Node records.

- Category-first: Category -> Object/Topic -> reverse chronological nodes.
- Time-first: Local Date -> Category -> Object/Topic -> nodes on that date.

No duplicate Category tree or Time archive is stored. Changing view or navigation does not write data. Both projections return the same Object IDs, Node IDs, content, and material references.

## 12. Legacy Library Import

An additive migration creates the evolution tables and then imports every `project_library_entries` row inside the migration transaction:

- group exact normalized Project + Category + Topic into one Object;
- choose the earliest `(created_at, id)` entry ID as the stable imported Object ID;
- use each entry ID as its Timeline Node ID;
- use the UTC calendar portion of legacy `created_at` as `local_date`, because v8 did not persist the original project-local date;
- map `source_session_id`, optional `task_id`, and optional `source_reference` to typed material references;
- leave Current Overview empty;
- preserve the old table unchanged.

After import, new production writes use the evolution repository. `ProjectLibraryRepository.SubmitAsync` is retained only for legacy compatibility and is not expanded.

## 13. Failure And Concurrency Semantics

- All reads and writes validate Project ownership.
- Daily Summary, Handoff, Overview, and Timeline updates use optimistic revision/state checks where applicable. A conflict writes nothing and asks the caller to reload.
- Library proposal acceptance is one transaction; a missing target, stale revision, invalid reference, or duplicate Object conflict leaves the proposal Pending and writes no partial archive state.
- Structured policy parse failure creates no memory writes.
- Failure to resolve a selected material is surfaced in the continuity result; Workbench does not substitute a different source.
- Missing external files or commits remain visible as unavailable references and do not delete their node.
- Application close does not start or wait for summarization. There is no background memory loop.

## 14. UI

The Library pane evolves without adding a graph UI:

- Daily view: date list, one readable Daily Summary document per date, revision/update time, and source links.
- Category view: Category -> Object -> Current Overview -> newest-to-oldest timeline.
- Time view: Date -> Categories -> Objects and nodes changed that day.
- Proposal review: proposed action, target Object/node, text edit, references, Accept, Edit+Accept, Reject.
- Project settings: Library granularity, Continuity mode, project time zone, and Custom instructions.

Existing Formal/Learned/Candidate UI remains under a clearly labeled Legacy Project Memory section during transition.

## 15. Scope Slices

### Slice A — Daily Summary And Preferences

Add project-local-date Daily Summary persistence/API, typed reference storage, optimistic revisions, preference storage, and a read-only Daily UI. Stop when one project can create/revise one day without affecting another project or creating background work.

### Slice B — Continuity Material Access

Add optional Handoff write/clear, project-scoped raw-tail paging/stats, material descriptors, explicit selection resolution, and durable successor selection references. Stop when a fake caller can select exact Daily/Handoff/Raw material without duplicating transcript data.

### Slice C — Leader Skill Continuity Integration

Add the structured policy envelope, replace mandatory Handoff generation, apply optional commands, store selection during rollover, and inject only the selected bundle on first send. Remove legacy automatic synthesis triggers from the default path. Stop when New Brain works with zero memory writes, optional writes, restart-before-first-send, and policy failure.

### Slice D — Library Object And Timeline

Add Object, Overview, Timeline Node, Material Reference, Proposal storage, and transactional v8 import while retaining legacy tables. Stop when old entries and new multi-node objects are queryable with stable IDs and exact references.

### Slice E — Category/Time Library UI

Add Category-first and Time-first projections plus Current Overview/timeline detail. Stop when both views show the same node IDs and newest-first history after restart.

### Slice F — Stage-End Library Proposal Flow

Allow Leader Skill output to create, but never auto-accept, a Library proposal; add Accept/Edit+Accept/Reject UI and atomic apply. Stop when same-day update versus new-node behavior is entirely determined by the proposal and user confirmation.

Each slice is independently testable and committable. Implementation may stop after any slice without requiring the remaining system.

## 16. Required Decision Answers

1. **Daily Summary storage:** use new focused storage. Reusing `project_memory_items` would mix document revision with Learned/Candidate/Formal certification.
2. **Handoff versus Daily Summary:** remain distinct. Handoff is optional epoch-local immediate continuity; Daily Summary is one Project + local-date revisable document.
3. **Recent Raw Tail:** reuse `leader_messages` through bounded project-scoped queries; never copy it.
4. **Library without Knowledge Graph:** one Object owns ordered Timeline Nodes; nodes own typed references. No arbitrary edges or semantic graph.
5. **Current Overview:** a persisted mutable projection on the Object record, maintained explicitly by Leader Skill proposals, not a timeline event.
6. **Existing `project_library_entries`:** preserve and deterministically import; do not keep stretching its flat submission shape.
7. **Category versus Time:** two query projections over the same Objects/Nodes, not two persistence systems.
8. **Images/files/commits/sources:** store typed locators and optional labels only; never copy source bodies or binary data.
9. **New Brain bounds:** present metadata, let Leader Skill select explicit references/budgets, persist the selection, and resolve only those references for first-send delivery.
10. **Leader Skill API:** use provider-neutral `IProjectMemoryApi` plus a validated structured command/selection bridge. The Skill never accesses SQLite; Workbench never authors the memory policy.

## 17. Explicit Non-Goals

- Knowledge Graph, graph traversal, embeddings, vector database, semantic search, or RAG orchestration.
- Fixed summary intervals, automatic end-of-day detection, mandatory New Brain writes, background memory agents, or autonomous cleanup.
- Full artifact ingestion, copied documents/images/transcripts/diffs, content extraction, or file indexing.
- Fuzzy topic merging, automatic truth certification, or a complex supersession state machine.
- Deleting M1.5 data, rolling back migrations, or replacing Git history.

## 18. Testable Invariants

- Exactly one Daily Summary key exists per Project + persisted local date, with compare-and-swap revision.
- A New Brain can complete with no Summary, Handoff, or Library write.
- Handoff is optional and frozen with its archived source epoch.
- Recent Raw bytes exist only in `leader_messages`.
- Continuity delivery contains only explicitly selected project-owned material and the unmodified current user message.
- Category and Time projections return the same persisted Node identities.
- Same-day node update and new-node creation are both supported; Workbench never chooses between them.
- Current Overview changes do not remove or rewrite timeline nodes.
- Every artifact-like material is a reference, not copied content.
- Legacy M1.5 and v8 Library rows survive every additive migration.
- No project-open, turn-complete, rollover, or Library-open event automatically performs knowledge governance.
- Storage and application APIs remain provider-neutral.

## 19. Self-Review

The four memory concepts have distinct canonical stores and lifecycles. New Brain selection does not imply a write. Category and Time use one record set. Current Overview is explicitly persisted but Skill-maintained. Legacy tables are preserved and imported without rollback. No requirement depends on an absent memory topic/link table, provider-specific API, copied source body, semantic retrieval system, or unspecified fixed context threshold.
