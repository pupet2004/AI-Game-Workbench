# M2-01 Leader Can Hire Implementation Plan

**Goal:** Deliver the thinnest M2-01 loop: a Main Leader can propose a Project-scoped Draft, the user can confirm it, Workbench can create or reuse the explicitly selected Worker Session, route prompts and reports, persist labels, and restore the relationship after restart.

**Architecture:** Workbench is session routing, labels, persistence, and Project Library. Leader Skill owns orchestration judgment. Agent/CLI owns implementation, files, shell, tests, builds, and any Git workflow described in the Prompt. Existing WorkerExecution/TaskRevision/Permission/TaskGrant/TaskEvent/CompletionPackage/Evidence storage remains available as an Optional Reliability Layer, not a mandatory execution engine.

**Constraints:** Documentation revision only in this file. Tasks 1–3 remain completed history. Task 4A containment exploration is stopped; do not schedule Windows sandbox, `writableRoots`, ACL, `CreateProcessAsUserW`, Git containment, worktree creation, BaseCommit freeze, deterministic evidence, merge, or M2-02.

## Completed History

- **Task 1:** Core task/revision/permission contracts, completed.
- **Task 2:** Complex durable Storage substrate, completed. Reinterpreted here as optional reliability storage; do not delete or roll back it.
- **Task 3:** Provider-neutral Leader Draft proposal and Draft Card, completed.
- **Task 4A:** Containment spike, architectural exploration stopped after product simplification. It is not a gate for Session creation.

## Shared Rules

- Leader recommendation is provider-neutral and user-editable before Start.
- `Start Worker` is explicit. It creates a new Agent Session or reuses the exact Worker Session selected by the Leader Skill.
- Workbench persists Project, LeaderSession, WorkerSession, Task, status, timestamps, and routing edges. It does not choose a model or Worker itself.
- Runtime create followed by persistence failure receives best-effort stop. Restart ambiguity is surfaced as Interrupted/Unknown for user/Leader choice; no session-discovery infrastructure is added here.
- V1 trusts the Worker report. Existing Evidence/Completion records may be attached later and do not block handoff.

## Task 4: Worker Session Handoff & Routing

**Outcome:** Confirmed Drafts create or reuse a Worker Session, route the Leader Prompt, show the transcript, persist the Task-to-Session relation, and route the final report back to the Leader.

**Implementation boundary:** Resolve the Leader's recommended Provider, ProviderAccount, ModelProfile, Reasoning, and AgentRuntime; allow user edits; create a new session or reuse the explicitly named session; send the Prompt; retain `AgentSessionId`, `ExternalSessionId`, ProviderAccount, WorkingDirectory, TaskId, and routing metadata. Do not create worktrees, freeze BaseCommit, enforce containment, stage/commit Git, collect deterministic evidence, merge, or auto-select a Worker.

**RED tests:**

- valid Draft confirmation creates exactly one new session with the selected profile/account and sends the Leader Prompt;
- explicit reuse sends the follow-up to the named existing Worker Session and never creates a replacement session;
- Task and Worker Session remain distinct and the Project relation is persisted;
- every routed message records sender, receiver, Task, Worker Session, and timestamp;
- Worker report routes back to the Main Leader without appearing as an unstructured Leader transcript injection;
- account/session mismatch is rejected; resume uses the persisted same identity;
- runtime creation followed by persistence failure calls best-effort stop and does not expose a false completed state;
- restart restores Leader, Worker, Task, status, and last-active relationships;
- Leader may recommend termination but only explicit user choice closes/abandons the Task.

**GREEN/review:** Use existing provider-neutral `IAgentRuntime` and repositories. Keep the adapter free of Codex-specific Core types. Run focused App/Runtime tests and commit independently:
`feat(m2): route leader and worker sessions`.

## Task 5: Minimal Work UI

**Outcome:** Preserve the three-column `LEADER | WORK | PROJECT LIBRARY` layout while making Task-to-Session relationships immediately legible.

**RED tests:**

- Leader shows the Main Leader conversation and a bounded set of Worker statuses needing attention;
- Work cards show Task, Provider/Model/Profile, status, and LastActiveAt;
- open, peek, attach, follow-up, resume, and close controls target the selected Worker Session;
- Draft Card retains Edit, Cancel, and explicit Start Worker actions;
- restart and interrupted states remain visible without a complex workflow dashboard;
- no Agent graph, org chart, Kanban, merge button, or every-event Leader chat rendering.

**GREEN/review:** Bind the thinnest view models and commands over Task 4 services. Do not add orchestration judgment. Commit independently:
`feat(m2): expose minimal worker session UI`.

## Task 6: Library Submission / Browsing Polish

**Outcome:** Add only the missing lightweight entry points for Skills to submit concise Project Library notes and for Leader/Worker to browse them.

**RED tests:**

- a note stores category, topic, timestamp, short summary, source reference, and ProjectId;
- Leader/Worker can browse and search by category, topic, and time;
- invalid or cross-Project source references do not create partial notes;
- Library submission does not auto-audit the Project, build a knowledge graph, or inject all history into a session;
- existing M1.5 Library/Memory behavior remains intact.

**GREEN/review:** Reuse existing Library/Memory persistence and UI where sufficient. Keep synthesis in Leader/Worker Skills. Commit independently:
`feat(m2): polish project library handoffs`.

## Task 7: End-to-End Real Smoke And M2 Seal

**Outcome:** Validate the complete thin handoff loop with fakes and one opt-in real Agent session in a disposable safe Project.

**RED tests:**

- Leader proposal -> validated Draft -> explicit user Start -> new/reused Worker Session -> routed report -> Leader receipt;
- Project/Task/Session labels and LastActiveAt survive restart;
- interrupted/unknown runtime state is surfaced for user/Leader choice;
- no Workbench worktree, BaseCommit, containment, Git commit/evidence, merge, or model-selection assertion blocks the smoke;
- existing Storage reliability records remain readable and optional.

**GREEN/review:** Run the existing Core, Storage, Project, Runtime, and App suites plus an opt-in disposable real smoke. Do not touch real game projects. Commit the seal independently:
`test(m2): seal agent handoff workflow`.

## M2-02 Boundary

Do not implement Auditor, verification verdicts, merge readiness, automatic merge, semantic retrieval, embeddings, RAG, Agent graphs, RBAC, cloud sandboxing, billing, or enterprise workflow controls.

## Self-Review

- **Spec coverage:** Product core, Leader/Worker distinction, routing, persistence, Library, UI, reliability, and M2-02 boundary are represented.
- **Start crash windows:** Create-before-persist receives best-effort stop; restart ambiguity requires user/Leader decision; no duplicate-session guarantee is claimed.
- **Runtime containment:** Task 4A is explicitly stopped exploration and is not a creation gate.
- **Task dependencies:** Task 4 handoff -> Task 5 UI -> Task 6 Library polish -> Task 7 smoke/seal.
- **Type/state consistency:** Existing contracts are reused; target labels are documented without adding enums in this revision.
- **M2-02 leakage:** none.
- **Placeholders:** none; no `TBD`/`TODO`.
