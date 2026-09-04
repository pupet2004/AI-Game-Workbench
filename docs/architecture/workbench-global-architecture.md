# Workbench Global Architecture Reconciliation

Status: **Post-R5-B1 architecture reconciliation record**

Recorded: **2026-08-23**

Baseline: **R5-B1 Manual Continuity Spine — sealed**

## 1. Purpose and Authority

This document re-expresses the whole Workbench architecture after R5-B1 moved the product center from Agent execution to durable Project continuity. It places the current solution, including its Legacy surfaces, around that center.

This is an architectural record, not a new specification, implementation plan, migration decision, retirement authorization, or R6 proposal. It does not change the [R5-B1 Baseline](./r5-b1-baseline.md). When this document describes current code, it is descriptive. When it states an invariant, the sealed R5 Boundary and R5-B1 documents remain authoritative.

The 2026-08-23 sections below describe the sealed R5-B1 baseline. Since that baseline, the current working tree has added a provider-neutral B1 Agent participation adapter, an OpenCode runtime path, and B1 Project Library projections. Those additions are recorded as current working-tree deltas until committed and versioned.

The governing product statement is:

> **Workbench is not an agent system with memory. It is a project world system where agents participate temporarily.**

> **Workbench 不是一个带记忆的 Agent 系统，而是一个允许 Agent 临时参与其中的项目世界系统。**

## 2. Reconciled Product Center

The durable center is the Project World:

- it owns what the Project currently acknowledges;
- it identifies durable responsibility-bearing principals;
- it records who is responsible for bounded work;
- it preserves attributable Claims and Decisions;
- it rebuilds current accepted state after restart.

Agent, Session, Provider, model, runtime, transcript, and Summary are not the Project's durable center. They may supply connectivity, context, evidence, or work, but the Project must remain intelligible when all of them disappear.

The architecture is therefore organized around three different questions:

| Boundary | Question | Durable Project authority? |
|---|---|---|
| Project World | What does the Project currently acknowledge, and why? | Yes, only through persisted `AuthorityDecision` effects. |
| Participation and Application | Who or what is interacting with the Project, and through which bounded use case? | No independent authority; it submits Claims, Handoffs, routing updates, or named authority commands. |
| Execution Environment | How is delegated work performed? | No. Execution output enters the Project only as non-authoritative input. |

## 3. Current Overall Architecture

```text
┌──────────────────────────────────────────────────────────────────────┐
│                         PROJECT WORLD                                │
│                                                                      │
│  Governance and identity                                             │
│    Project governance root                                           │
│    LogicalActor -> Responsibility -> Assignment -> Revision          │
│                                                                      │
│  Work continuity                                                     │
│    Attempt -> optional SessionBinding                                 │
│    immutable Claim + bounded Handoff                                 │
│                                                                      │
│  Authority and accepted state                                        │
│    named command -> validation -> persisted AuthorityDecision        │
│                                      -> AcceptedProjectState          │
│                                                                      │
│  Recovery                                                            │
│    durable history -> pure projection -> same current state          │
└──────────────────────────────────────────────────────────────────────┘
                               ▲
                               │ explicit bounded input
                               │ participation is not authority
                               │
┌──────────────────────────────────────────────────────────────────────┐
│                PARTICIPATION / APPLICATION BOUNDARY                  │
│                                                                      │
│  Implemented B1 application surface                                  │
│    Manual-capable named authority commands                           │
│    non-authoritative Attempt / Binding / Claim / Handoff commands    │
│    projection queries                                                 │
│                                                                      │
│  Current product participation                                       │
│    User interface                                                     │
│    Legacy Leader / Worker orchestration                              │
│    provider and project adapters                                     │
│                                                                      │
│  Current working-tree B1 participation                             │
│    provider-neutral Agent adapter (non-authoritative)                │
└──────────────────────────────────────────────────────────────────────┘
                   ▲                              │
                   │ result / event / locator     │ delegated work
                   │                              ▼
┌──────────────────────────────────────────────────────────────────────┐
│                     EXECUTION ENVIRONMENT                            │
│                                                                      │
│  Runtime | external Session | Provider | model | tools               │
│  Codex protocol/process adapter | runtime registry                   │
│                                                                      │
│  Owns execution and connectivity, not Project identity,              │
│  responsibility, accepted facts, or authoritative history.          │
└──────────────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────────────┐
│                         LEGACY WORLD                                 │
│                                                                      │
│  Leader epochs/messages | Tasks/reviews/AutoProceed                  │
│  Worker execution/events/completion | Memory/Synthesis               │
│  Daily Summary | Library compatibility and evolution                 │
│                                                                      │
│  Independently readable under original semantics.                    │
└──────────────────────────────────────────────────────────────────────┘
                               │
                               │ explicit, attributable, one-way,
                               │ current-time Claim/command crossing
                               ▼
                         PROJECT WORLD

Supporting continuity view:
  R5-A Summary = non-authoritative recovery/navigation aid.
  It is neither AcceptedProjectState nor equivalent to Legacy Daily Summary.
```

Two qualifications are load-bearing:

1. The Manual path is implemented through B1 Application services and certified with zero SessionBindings. R5-B1 did not add a separate `ManualGateway` product API or UI.
2. The existing Codex runtime is a real execution adapter, but the old Leader/Worker workflows are not automatically a B1 Agent participation adapter. No current adapter may synthesize B1 authority from Agent output.

## 4. Ownership Boundaries

### 4.1 Project World

The Project World owns:

- durable Project governance;
- `LogicalActor`, `Responsibility`, `Assignment`, immutable `Revision`, and `Attempt` identity;
- attributable `Claim` and immutable `Handoff` history;
- validated and persisted `AuthorityDecision` history;
- `AcceptedProjectState` and its deterministic projection;
- stored and effective routing semantics needed for continuity;
- the one-time governance root for new or explicitly adopted pre-B1 Projects.

It does not own Agent cognition, Provider availability, execution strategy, Git/worktree operations, runtime permission policy, or automatic truth verification.

### 4.2 Participation and Application

This boundary owns use cases that let a UserPrincipal, LogicalActor, UI, or adapter interact with the Project:

- named authority command submission;
- non-authoritative Claim, Handoff, Attempt, SessionBinding, and routing operations;
- command-shape coordination and projection queries;
- transient interaction forwarding and presentation;
- selection of an external execution adapter;
- explicit, current-time admission of Legacy context.

Participation never implies authority. A participant can act only through the command and attribution rules of the Project World. UI control of a Manual LogicalActor does not turn the UI or Workbench into the Claimant.

### 4.3 Execution Environment

The Execution Environment owns:

- creating, resuming, sending to, observing, and stopping external sessions;
- Provider protocol and process mechanics;
- model and account discovery;
- Provider-specific interaction transport;
- execution strategy, tools, sandboxing, worktree/Git execution, tests, and runtime permission enforcement.

It may return output, evidence locators, interaction requests, and opaque session locators. None of those changes Project identity or accepted state without an explicit Project-side operation.

### 4.4 Legacy World

Legacy is an independently meaningful historical world, not an earlier version of the B1 domain waiting to be renamed. Its records preserve the semantics under which they were written.

Legacy may be read, inspected, or referenced as bounded context. Crossing into B1 requires an explicit, attributable, one-way action. A Legacy Task completion, review approval, AutoProceed result, Leader epoch, Memory item, Library row, transcript, or Summary never becomes a B1 Decision or accepted contribution merely because the names appear similar.

## 5. Current Solution Mapping

The solution contains five production projects. Their names are physical packaging, not architectural classifications. Responsibility must be classified below the assembly level.

### 5.1 Physical dependency graph

```text
Workbench.Core
    ▲          ▲              ▲
    │          │              │
Storage     Runtime       Workbench.Project
    ▲          ▲              ▲
    └──────────┴──── App ──────┘

Additional current dependency:
Workbench.Project -> Workbench.Storage
```

`Workbench.App` is the composition root and references every production project. This physical graph does not itself prove that every responsibility is in its final conceptual layer.

### 5.2 Responsibility map

| Current code area | Reconciled position | Current status and boundary note |
|---|---|---|
| `Workbench.Core/Continuity` | Project World domain | **SEALED B1 CORE.** Contains durable identities, contracts, Claims, command shapes, authority evaluation, state, and pure projection. |
| `Workbench.Storage/Continuity` + `Migration020ManualContinuitySpine` | Project World persistence adapter | **SEALED B1 PERSISTENCE.** Persists governance, routing, Claims/Handoffs, complete validated Decisions, and reloadable history. Migration020 is additive and does not reinterpret Legacy. |
| `Workbench.App/Continuity` | Participation/Application boundary | **SEALED B1 APPLICATION SURFACE.** Provides named authority commands, non-authoritative commands, and projection queries. It has no direct AcceptedProjectState writer. |
| `Workbench.Core/Projects` and `Workbench.Storage/Projects` | Durable Project identity and supporting persistence | **ACTIVE FOUNDATION.** Project identity predates B1 and remains durable. B1 governance is established separately rather than inferred by `ProjectRepository.UpsertAsync`. |
| `Workbench.Project/Opening` and `Detection` | Application/project-locator adapter | **ACTIVE.** Opens and classifies a filesystem project; despite the assembly name, this is not the Project World Kernel. |
| `Workbench.Project/Git` | Read-only external locator/provenance adapter | **ACTIVE ADAPTER.** Repository root, HEAD, branch, and dirty-state inspection do not make Workbench the owner of Git execution. |
| `Workbench.Runtime` | Execution Environment and Provider adapter | **ACTIVE, REPLACEABLE, MIXED CONTRACT.** Codex remains the primary runtime path; the current working tree also contains an OpenCode runtime used through the B1 participation adapter. Broader provider coverage remains unvalidated. `IAgentRuntime` includes useful connection operations plus optional discovery, status, capability, and transcript surfaces; it is broader than a minimal participation gateway. |
| `Workbench.App/Services/AppServices` | Composition root | **ACTIVE COEXISTENCE ROOT.** Composes B1 services beside Legacy Leader, Worker, Memory, Library, review, and Runtime services. B1 and Worker remain semantically distinct and now have a durable typed bridge; composition does not make the worlds equivalent. |
| `Workbench.App/ViewModels` and `Views` | Presentation/Application | **MIXED CURRENT UI.** The current working tree consumes B1 accepted-state and Library projections, while the full designed B1 Manual/Agent experience remains transitional. Legacy Leader/Worker surfaces continue to coexist. |
| `Workbench.Core/Leaders`, `Tasks`, `Workers`, `Memory` | Legacy and mixed domain/application-era models | **TRANSITIONAL.** Assembly membership does not promote these types into B1 authority semantics. Useful identity, contract, and provenance fragments coexist with execution-era assumptions. |
| `Workbench.App/Leader`, `Worker`, `Memory` | Legacy participation, orchestration, compatibility, and delegated leakage | **TRANSITIONAL / MIXED.** Contains valid routing and UI use cases, plus Agent-centric review, rollover, execution, transcript, and compatibility behavior. |
| `Workbench.Storage/Leaders`, `Tasks`, `Workers`, `Reviews`, most `Memory`, and `Migrations001-019` | Legacy persistence and compatibility history | **READABLE / ACTIVE COMPATIBILITY.** These surfaces are not B1 persistence merely because they share the database or Project IDs. Applied migrations remain history. |
| `ProjectSummaryRepository` and Migration019 | Non-authoritative R5-A continuity storage | **SEALED SUPPORTING VIEW.** Summary is append-only and recoverable, but never an authority source or required projection of AcceptedProjectState. |
| Legacy Memory, Synthesis, Daily Summary, Library and review structures | Legacy World and supporting product views | **FROZEN OR TRANSITIONAL BY PRIOR AUDIT.** They retain data and current consumers; they are not automatically B1 Claims, Decisions, or accepted state. No deletion is authorized here. |

## 6. Authoritative and Non-Authoritative Flows

### 6.1 Project-state change

```text
Human or Actor input
  -> named Application command
  -> command-shape and authority evaluation
  -> fully validated bounded decision
  -> Project sequence CAS + atomic repository commit
  -> persisted AuthorityDecision
  -> pure projection
  -> AcceptedProjectState
```

There is no legal shortcut from UI, Runtime, Agent event, Handoff, Summary, or Legacy state to AcceptedProjectState.

### 6.2 Manual participation

```text
bootstrap UserPrincipal
  -> establish Actor / Responsibility / Assignment / Revision
  -> create Manual Attempt, with SessionBindings = 0
  -> record assignee-attributed ResultClaim and Handoff
  -> make attributable AuthorityDecision
  -> restart and rebuild the same Project projection
```

This certified flow proves that continuity belongs to the Project rather than to execution infrastructure.

### 6.3 Agent participation

The safe architectural shape is:

```text
Assignment + Attempt + LogicalActor
  -> optional SessionBinding / execution adapter
  -> external output
  -> attributable Claim + bounded Handoff
  -> separate authority command
  -> AuthorityDecision, if valid
```

The sealed R5-B1 baseline contained the first and last parts plus Codex execution infrastructure, but not the joining adapter. The current working tree now implements `B1AgentParticipationAdapter` and an OpenCode runtime path. The adapter records Attempt, SessionBinding, Claim/Handoff output, and still has no direct AuthorityDecision write path.

### 6.4 Legacy crossing

```text
Legacy record or locator
  -> explicit human/Application selection
  -> EvidenceRef, considered reference, or newly recorded Claim
  -> named current-time B1 command, when authority effects are intended
```

The bridge is explicit and one-way. It does not backdate B1 identity or authority, and B1 writes do not silently rewrite Legacy records.

## 7. Current Boundary Mismatches

These are reconciliation findings, not immediate refactoring instructions.

### 7.1 Agent-centric naming still dominates the active UI

`Leader`, `Worker`, `AgentSession`, and epoch-oriented names make external participants appear to be the Project's identity. Under B1, Leader/Worker/Reviewer are RoleKinds on durable LogicalActors, while Session is optional connectivity. Existing names retain Legacy meaning until explicitly bridged or migrated.

### 7.2 B1 exists beside, not underneath, the old workflows

`AppServices` composes the new B1 spine and the old Leader/Worker/review/Memory surfaces in parallel. The B1 services are real and recovery-certified, and the Worker lane now has an explicit durable typed attribution bridge. The current UI and existing Agent workflows do not universally pass through B1 Claims and authority commands; treating composition as full semantic cutover would be false.

### 7.3 Mixed orchestration obscures ownership

`WorkerSessionRouter`, the Leader review pipeline, and Leader rollover combine legitimate Application routing with Provider transport, Legacy persistence, delegated cognition, and execution assumptions. Their future unit of classification remains responsibility, not file.

### 7.4 Runtime contracts are broader than the durable boundary

`IAgentRuntime` contains create/resume/send/respond/stop behavior that resembles a connection adapter, but also model discovery, capability/status, and transcript access. Those optional execution and navigation surfaces must not become Project World prerequisites. Current Codex sandbox/approval choices are Provider/runtime concerns, not new domain policy.

### 7.5 “Summary” names cover different semantics

Legacy Daily Summary, transcript-derived context, and R5-A Summary are not interchangeable. R5-A Summary is a non-authoritative continuity aid; Legacy Daily Summary preserves its old mutable-document semantics; neither is AcceptedProjectState or an automatic source for it.

### 7.6 Shared storage does not imply shared truth

The database stores B1 authority history, Project identity, UI settings, transcripts, Legacy events, Memory, Library, and compatibility rows. Only the B1 authority history projects AcceptedProjectState. A table's presence in `Workbench.Storage` or its use of a Project ID does not give it Project World authority.

### 7.7 Legacy approval is not B1 authority

Typed review rows, user gates, AutoProceed, Task completion, and Library proposal acceptance retain their original product behavior. They must not be relabeled as `AuthorityDecision` or accepted contributions without an explicit, attributable B1 action. In particular, Agent review output remains a Claim-like input even when the Legacy flow marks a Task completed.

### 7.8 `Workbench.Project` is not synonymous with Project World

The assembly primarily detects, opens, and inspects filesystem/Git projects. It belongs to the Application/Adapter edge. The durable Project World is currently implemented mainly by the B1 vertical slice across Core, Storage, and App.

## 8. Stable, Transitional, and Delegated Areas

### Stable baseline

- R5-B1 domain identities, closed authority model, Decisions, AcceptedProjectState, and projector;
- B1 additive persistence and Project-sequence atomic commit;
- B1 Manual zero-Session command flow and recovery certification;
- explicit one-time pre-B1 adoption and one-way Legacy crossing;
- Project identity and read-only project/Git discovery;
- R5-A Summary's non-authoritative status;
- Legacy independent readability.

### Transitional coexistence

- Leader/epoch identity and rollover;
- Task/revision versus B1 Assignment/Revision semantics;
- WorkerExecution and WorkerSessionRouter;
- review, AutoProceed, and user-gate authority semantics;
- Legacy Memory, Synthesis, Daily Summary, and Library product surfaces;
- current UI composition: B1 Library/accepted-state projections exist, while the full designed B1 Manual/Agent experience remains transitional;
- the broad `IAgentRuntime` contract and the current Codex-primary/OpenCode-participation execution paths.

“Transitional” does not mean unused, deprecated, or authorized for removal. Prior Call/Data Audit evidence showed that several old structures still carry live recovery, compatibility, and user-data responsibilities.

### Delegated execution

- worktree, branch, commit, merge, build, test, sandbox, network, and execution strategy;
- Provider runtime permission policy and enforcement;
- review method, depth, and evidence-gathering cognition;
- semantic handoff generation strategy.

Workbench may connect, route bounded inputs, and record attributable outputs around these capabilities. It does not need to reimplement them to complete the Project World.

## 9. Future Admission Gate

Any later proposal must answer these questions before feature selection or implementation planning.

1. **Which world changes?** Is the proposal changing the Project World, the Participation/Application boundary, the Execution Environment, a supporting view, or Legacy compatibility? “Everywhere” or an unclear answer requires decomposition.
2. **Does it create a durable Project fact?** If yes, identify its owner, deciding authority, attribution, persistence, concurrency, and restart/recovery behavior.
3. **Can it bypass authority?** If accepted state can change without a persisted attributable AuthorityDecision produced through a named command, reject or redesign it.
4. **Does it make Agent infrastructure central again?** If Project identity, responsibility, accepted state, or recovery becomes unintelligible when an Agent, Session, Provider, model, transcript, or runtime disappears, reject or redesign it.
5. **Does participation masquerade as authority?** Agent output, successful execution, UI interaction, selected routing, and Provider events remain inputs until separately authorized.
6. **Does it infer new semantics from Legacy?** Automatic interpretation, backdated identity, or synthetic historical B1 authority requires rejection or a separate explicit architecture decision.
7. **Is an execution gap being promoted into Kernel ownership?** First prove the durable Project requirement and why an Application or Adapter boundary cannot own it.
8. **Does it change the sealed B1 vocabulary or command surface?** A new durable identity, Claimant kind, contribution scope, capability, command shape, authority writer, or automatic bridge requires a new architecture decision and cannot enter as implementation convenience.

Passing this gate only admits a proposal for further design. It does not choose R6 or authorize implementation.

The non-normative research note [From Continuity to Accountability](./from-continuity-to-accountability.md) records a possible longer-term provenance and accountability horizon. It is research context only and does not expand this architecture, the R5-B1 baseline, or the implementation roadmap.

## 10. Reconciliation Judgment

R5-B1 successfully moved the durable center of Workbench into a recoverable Project World. The current repository, however, remains a deliberate coexistence system: the sealed B1 spine operates beside active Agent-centric and Legacy product paths. This is not a contradiction, provided their records and behaviors are not mistaken for B1 identity, authority, or accepted state.

The current canonical Leader-to-Worker path is B1-linked when a unique current delegation and typed Git-backed execution identity are available; legacy entry remains available for ambiguous or non-governed starts.

The global architecture should therefore be read in this order:

```text
Project World defines continuity and accepted state.
Participation/Application submits bounded interactions.
Execution performs delegated work.
Legacy remains readable and crosses only explicitly.
Supporting views aid cognition but never manufacture authority.
```

Future work should classify responsibility before moving or retiring code. Assembly names, table names, Agent capability gaps, and apparent feature similarity are not sufficient architectural evidence.

## 11. Evidence and Precedence

This reconciliation was checked against the sealed master and these records:

- [R5-B1 Baseline](./r5-b1-baseline.md)
- [R5-B1 Boundary Reconciliation Design](../superpowers/specs/2026-08-22-r5-boundary-reconciliation-design.md)
- [R5-B1 Manual Continuity Spine Design](../superpowers/specs/2026-08-22-r5-b1-manual-continuity-spine-design.md)
- [R5 Repository Ownership Audit](../superpowers/reports/2026-08-22-r5-repository-ownership-audit.md)
- [R5 Call Graph and Persistence Data Audit](../superpowers/reports/2026-08-22-r5-call-data-audit.md)
- [R5-B1 Architecture Retrospective](../superpowers/reports/2026-08-23-r5-b1-architecture-retrospective.md)
- [R5-B1 Final Seal](../superpowers/reports/2026-08-23-r5-b1-final-seal.md)

If this descriptive mapping conflicts with the sealed R5-B1 Baseline or its governing written specifications, the sealed baseline and specifications take precedence.
