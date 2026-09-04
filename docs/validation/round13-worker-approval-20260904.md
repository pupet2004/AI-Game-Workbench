# Round 13 — Real Codex Worker Approval

Recorded: **2026-09-04**  
Status: **PASS**

## Scope

This record certifies the real Codex provider approval path used by a Workbench-managed Worker. It is not a FakeAgentRuntime test and it does not treat the Worker final report alone as proof.

## Execution identity

- ProjectId: `6f7c056c-8da4-4e9c-ad56-bef97cbb1f5c`
- TaskId: `8a97d59a-6784-4f6b-8dfb-ba81a39f322c`
- TaskRevisionId: `6632a11a-6182-4328-99f3-76b0c9898aab`
- WorkerExecutionId: `4e62bd9a-c78b-4425-a986-a7e46f73b4ed`
- AgentSessionId: `b696e2a0-8d92-4d26-b4b8-5d4f7614f29a`
- Codex provider session: `01a06c0b-e36c-77e3-88ec-1a622d627db0`
- Provider/model: `codex` / `gpt-5.6-sol`
- Working directory: `C:\Users\pupet\Documents\ChatGPT\AI Game Workbench\acceptance-lab-20260830`
- Execution state after completion: `CompletedPendingReview`
- Task state after completion: `Reviewing`

## Evidence

### Approve-once

The Codex rollout contains a real `exec_command` request for `Get-Date` with `sandbox_permissions: require_escalated`, followed by a completed `CommandExecution` item in the acceptance workspace. The command exited with code `0` and returned `2026-09-04 18:55:39`.

### Decline

The same provider session then issued a real `exec_command` request for `Get-Location` with `sandbox_permissions: require_escalated`. The request was rejected with `Rejected("rejected by user")`; no completed command execution was produced for that request.

### Structured interaction

The live Worker transcript explicitly records both operations as structured approval interactions. The Codex rollout independently records the provider-side command requests, waiting state, completion, and rejection result. No assistant-text question was used as a substitute for approval.

### Continuity and boundaries

- Both approval decisions occurred in one WorkerExecution, one AgentSession, and one Codex provider session.
- Workbench task events contain `WorkerSessionStarted`, `AssignmentReadyToStart`, `WorkerAssignmentStarted`, completion verification, final report, and handoff for the same execution.
- The persisted workspace baseline contains the three pre-existing files; the post-run hashes match the baseline.
- No new `AuthorityDecision` or `WorkerCompletionPackage` was created after execution start.
- The assignment has zero `AcceptedContribution` rows; `AcceptedProjectState` was not changed.
- No merge or push occurred.

## Evidence files

- Live Worker surface: `artifacts/worker-approval-live-verification.png`
- Completed Worker surface: `artifacts/worker-approval-live-verification-final.png`
- Codex rollout: `C:\Users\pupet\.codex\sessions\2026\09\04\rollout-2026-09-04T18-51-53-01a06c0b-e36c-77e3-88ec-1a622d627db0.jsonl`
- Persisted database: `C:\Users\pupet\AppData\Local\AI Game Workbench\workbench.db`

## Boundary note

Transient provider approval requests are not copied into the durable `task_events` table as separate approval rows. The approval path is nevertheless independently evidenced by the provider rollout's structured command lifecycle, the live Worker transcript, and the resulting Workbench execution state. This is an observability limitation, not an approval-routing failure.

## Result

**REAL CODEX WORKER APPROVAL PATH = PASS**

Both `approve-once` and `decline` were exercised against real Codex command-execution approval requests, routed through the same Workbench Worker execution chain, with no authority or workspace mutation.
