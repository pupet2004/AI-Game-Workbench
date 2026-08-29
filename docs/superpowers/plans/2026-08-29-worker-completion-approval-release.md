# Worker Completion, Approval, and Release Stabilization Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make successful Worker completion advance its Assignment into review, verify the real approval route, then preserve the stabilized Alpha work in Git.

**Architecture:** `WorkerSessionRouter` remains the single owner of Worker turn results. A successful unstructured result is normalized into a canonical final report before persistence; explicit typed `NeedsLeaderDecision` payloads retain their existing state transition. Approval remains owned by `IAgentHost` and projected into the Leader surface.

**Tech Stack:** .NET 10, C#, Avalonia, xUnit, SQLite, Codex app-server.

**Spec:** `docs/architecture/workbench-alpha-architecture.md`

## Global Constraints

- Preserve `Claim != Decision != Accepted State`.
- A Worker completion may enter review but may not update Accepted Project State.
- Do not redesign the Relay or Agent Surface.
- Work in the current user-owned checkout and preserve unrelated changes.

---

### Task 1: Normalize Successful Worker Completion

**Files:**
- Modify: `src/Workbench.App/Worker/WorkerSessionRouter.cs`
- Modify: `tests/Workbench.App.Tests/Worker/WorkerSessionRoutingTests.cs`

- [ ] Change the unstructured-completion test to require `Working -> Reviewing`, one canonical `WorkerFinalReportReceived`, and one linked Handoff.
- [ ] Run the focused test and confirm it fails because the Assignment remains `Working`.
- [ ] Normalize a successful unstructured result to `WorkerHandoffKind.FinalReport` while preserving explicit typed decision requests.
- [ ] Run Worker routing tests and confirm they pass.

### Task 2: Verify Runtime Approval Routing

**Files:**
- Verify: `src/Workbench.App/AgentHost/AgentHost.cs`
- Verify: `src/Workbench.App/ViewModels/Panes/LeaderPaneViewModel.cs`
- Verify: `src/Workbench.App/AgentHost/HostedAgentSurfaceViewModel.cs`
- Test: `tests/Workbench.App.Tests/Worker/WorkerSessionRoutingTests.cs`

- [ ] Run focused approval tests for Worker request projection and Leader response routing.
- [ ] Start a real Codex Worker whose bounded command requires approval.
- [ ] Confirm the request reaches Leader, can be approved or denied, and the Worker continues without opening another writer.

### Task 3: Full Verification

**Files:**
- Verify all solution projects and tests.

- [ ] Run the complete solution test suite.
- [ ] Build the development application with zero errors.
- [ ] Recheck the live database for consistent Task, Execution, Handoff, and review states.

### Task 4: Preserve the Alpha Work

**Files:**
- Stage the product, tests, skills, tools, and release documentation that belong to the current Workbench milestone.
- Exclude unrelated generated media and upstream spike working trees.

- [ ] Review `git diff` and classify generated artifacts.
- [ ] Commit the stabilized Workbench milestone with an accurate message.
- [ ] Report the commit hash and remaining documented Alpha limitations.
