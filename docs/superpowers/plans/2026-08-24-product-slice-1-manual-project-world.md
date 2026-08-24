# Product Slice 1 — Manual Project World Alpha Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a self-contained Manual-first Project World path from Project Home through governed initialization, Manual Handoff, attributable Decision, and restart recovery without requiring an Agent runtime.

**Architecture:** Add bounded Application read/composer services above the sealed R5-B1 named commands and repositories, then route B1-ready Projects into a new state-first Avalonia surface. Keep the Legacy workspace readable and separate. No new Accepted State writer, B1 capability, domain command shape, or automatic Legacy conversion is introduced.

**Tech Stack:** .NET 10, C# 14, Avalonia UI, CommunityToolkit.Mvvm, Microsoft.Data.Sqlite, xUnit.

**Spec:** `docs/superpowers/specs/2026-08-24-product-slice-1-manual-project-world-design.md`

## Global Constraints

- Windows 11 x64 single-user Alpha; Git is optional and Agent runtime is absent from the acceptance path.
- Preserve every invariant in `docs/architecture/r5-b1-baseline.md`.
- `UserPrincipalRef != LogicalActorRef`; RoleKind grants no authority.
- Accepted Project State changes only through persisted existing named B1 authority commands.
- Manual Attempt/Handoff operations create zero SessionBindings and zero authority effects.
- Legacy remains readable and is never automatically mapped into B1.
- Do not modify migrations 001–020 or the live database.
- Use only disposable test databases.
- No Codex/ACP/provider adapter, Summary work, attachment ingestion, retention policy, generic builder, execution framework, or Legacy cleanup.
- Every production behavior follows genuine RED → minimal GREEN → focused regression → commit.
- Each task is an independent review gate. Do not enter the next task before the current task is committed and accepted.

---

## File Structure

New Application product units:

- `ProjectWorld/ProjectWorldEntryStatus.cs` — closed entry states and display-safe facts.
- `ProjectWorld/ProjectWorldEntryStatusService.cs` — read-only mechanical classification.
- `ProjectWorld/ILocalUserPrincipalProvider.cs` — external local principal boundary.
- `ProjectWorld/WindowsLocalUserPrincipalProvider.cs` — Windows SID adapter.
- `ProjectWorld/ProjectWorldEntryService.cs` — new Project creation and explicit Legacy adoption.
- `ProjectWorld/ProjectWorldInitializationService.cs` — fixed preview/confirm initialization use case.
- `ProjectWorld/ProjectWorldQueryService.cs` — snapshot/read composition for Overview, Work, Decisions, and Legacy marker.
- `ProjectWorld/ManualWorkService.cs` — explicit Attempt creation/selection.
- `ProjectWorld/ManualHandoffComposerService.cs` — fixed typed Handoff transaction.
- `ProjectWorld/ManualDecisionComposerService.cs` — preview and existing named authority-command submission.

New presentation units:

- `ViewModels/ProjectWorldSetupViewModel.cs` and `Views/ProjectWorldSetupView.axaml`.
- `ViewModels/ProjectWorldViewModel.cs` and `Views/ProjectWorldView.axaml`.
- `ViewModels/ManualWorkViewModel.cs` and `Views/ManualWorkView.axaml`.
- `ViewModels/ManualHandoffViewModel.cs` and `Views/ManualHandoffView.axaml`.
- `ViewModels/ManualDecisionViewModel.cs` and `Views/ManualDecisionView.axaml`.

Storage additions remain mechanical:

- read-only governance entry facts in `B1ProjectGovernanceRepository`;
- one atomic typed Claim/Handoff/selection transaction in `B1ClaimHandoffRepository`.

Existing Legacy `WorkspaceViewModel` and its panes are not renamed or reinterpreted.

---

### Task 1: Mechanical Project World Entry Classification

**Files:**

- Create: `src/Workbench.App/ProjectWorld/ProjectWorldEntryStatus.cs`
- Create: `src/Workbench.App/ProjectWorld/ProjectWorldEntryStatusService.cs`
- Modify: `src/Workbench.Storage/Continuity/B1ProjectGovernanceRepository.cs`
- Modify: `src/Workbench.App/ViewModels/RecentProjectItemViewModel.cs`
- Modify: `src/Workbench.App/ViewModels/HomeViewModel.cs`
- Modify: `src/Workbench.App/Services/AppServices.cs`
- Modify: `src/Workbench.App/Views/HomeView.axaml`
- Test: `tests/Workbench.Storage.Tests/Continuity/B1ProjectGovernanceRepositoryTests.cs`
- Test: `tests/Workbench.App.Tests/ProjectWorldEntryStatusServiceTests.cs`
- Test: `tests/Workbench.App.Tests/HomeViewModelTests.cs`
- Test support: `tests/Workbench.App.Tests/AppTestContext.cs`

**Interfaces:**

- Produces:

```csharp
public enum ProjectWorldEntryKind
{
    ProjectWorldReady,
    LegacySetupRequired,
    UnmanagedProjectUnavailable,
    CorruptProjectUnavailable,
    PathUnavailable
}

public sealed record B1GovernanceEntryFacts(
    bool ProjectExists,
    bool GovernanceExists,
    bool LegacyOriginExists,
    bool B1HistoryExists);

public sealed record ProjectWorldEntryStatus(
    ProjectRef ProjectRef,
    ProjectWorldEntryKind Kind,
    bool HasLegacyContext,
    string DisplayLabel);

public Task<B1GovernanceEntryFacts> GetEntryFactsAsync(
    ProjectRef projectRef,
    CancellationToken cancellationToken = default);

public Task<ProjectWorldEntryStatus> GetStatusAsync(
    CoreProject project,
    CancellationToken cancellationToken = default);
```

- Classification is literal:

```text
missing path                                  -> PathUnavailable
governance exists + state readable            -> ProjectWorldReady
legacy origin + no governance + no B1 history -> LegacySetupRequired
no governance + no legacy origin + no history -> UnmanagedProjectUnavailable
all other combinations                        -> CorruptProjectUnavailable
```

- `RecentProjectItemViewModel` receives a computed `ProjectWorldEntryStatus`; it does not query storage.

- [ ] **Step 1: Write failing Storage fact-query tests**

Add tests proving four independent facts: governed Project, eligible migrated Legacy Project, persisted post-B1 unmanaged Project, and governance-history-without-root corruption fixture. Assert the returned booleans, not SQL text.

- [ ] **Step 2: Run the focused Storage RED**

Run:

```powershell
dotnet test tests\Workbench.Storage.Tests\Workbench.Storage.Tests.csproj --filter FullyQualifiedName~B1ProjectGovernanceRepositoryTests --nologo
```

Expected: build failure because `GetEntryFactsAsync` and `B1GovernanceEntryFacts` do not exist.

- [ ] **Step 3: Implement the minimal read-only facts query**

Use one SQLite query with `EXISTS` expressions over `projects`, `b1_project_governance`, `b1_legacy_project_origins`, and the four governance-history tables already used by adoption validation. Do not infer eligibility from `bootstrap_user_principal IS NULL`.

- [ ] **Step 4: Verify Storage GREEN**

Run the Step 2 command. Expected: focused tests pass.

- [ ] **Step 5: Write failing Application classification tests**

Add literal cases for all five entry kinds. The mutation each case catches is a wrong route caused by treating any missing governance root as Legacy-adoptable. Add a Home test proving recent items display `Project World ready` or `Setup required · Legacy data available` from the service.

- [ ] **Step 6: Run the Application RED**

Run:

```powershell
dotnet test tests\Workbench.App.Tests\Workbench.App.Tests.csproj --filter "FullyQualifiedName~ProjectWorldEntryStatusServiceTests|FullyQualifiedName~HomeViewModelTests" --nologo
```

Expected: build failure because the status service and Home integration do not exist.

- [ ] **Step 7: Implement classification and Home status presentation**

Keep `ProjectWorldEntryStatusService` read-only. Inject it into `HomeViewModel`; load statuses while preserving recent-project ordering. Add one visible status label to each Home card. Do not change navigation yet.

- [ ] **Step 8: Verify focused and broader GREEN**

Run:

```powershell
dotnet test tests\Workbench.App.Tests\Workbench.App.Tests.csproj --filter "FullyQualifiedName~ProjectWorldEntryStatusServiceTests|FullyQualifiedName~HomeViewModelTests|FullyQualifiedName~NavigationTests" --nologo
dotnet test tests\Workbench.Storage.Tests\Workbench.Storage.Tests.csproj --filter "FullyQualifiedName~B1ProjectGovernanceRepositoryTests|FullyQualifiedName~ProjectRepositoryTests" --nologo
dotnet build AI.Game.Workbench.sln --nologo
```

- [ ] **Step 9: Commit Task 1**

```powershell
git add src/Workbench.App/ProjectWorld src/Workbench.App/ViewModels/HomeViewModel.cs src/Workbench.App/ViewModels/RecentProjectItemViewModel.cs src/Workbench.App/Views/HomeView.axaml src/Workbench.App/Services/AppServices.cs src/Workbench.Storage/Continuity/B1ProjectGovernanceRepository.cs tests/Workbench.App.Tests tests/Workbench.Storage.Tests/Continuity/B1ProjectGovernanceRepositoryTests.cs
git commit -m "feat(app): classify project world entry state"
```

Stop at the Task 1 review gate.

---

### Task 2: Local Principal and Governed Project Entry

**Files:**

- Create: `src/Workbench.App/ProjectWorld/ILocalUserPrincipalProvider.cs`
- Create: `src/Workbench.App/ProjectWorld/WindowsLocalUserPrincipalProvider.cs`
- Create: `src/Workbench.App/ProjectWorld/ProjectWorldEntryService.cs`
- Modify: `src/Workbench.Project/Opening/ProjectOpenService.cs`
- Modify: `src/Workbench.Project/Opening/ProjectOpenResult.cs`
- Modify: `src/Workbench.App/Services/AppServices.cs`
- Test: `tests/Workbench.Project.Tests/Opening/ProjectOpenServiceTests.cs`
- Test: `tests/Workbench.App.Tests/ProjectWorldEntryServiceTests.cs`
- Test: `tests/Workbench.App.Tests/WindowsLocalUserPrincipalProviderTests.cs`

**Interfaces:**

```csharp
public sealed record LocalUserPrincipal(
    UserPrincipalRef UserPrincipalRef,
    string DisplayName);

public interface ILocalUserPrincipalProvider
{
    LocalUserPrincipal GetCurrent();
}

public sealed record ProjectFolderInspection(
    string RootPath,
    string SuggestedName,
    ProjectType ProjectType,
    GitSnapshot Git,
    CoreProject? ExistingProject);

public Task<ProjectFolderInspection> InspectAsync(string folderPath, CancellationToken ct = default);
public Task<ProjectOpenResult> CreateGovernedProjectAsync(ProjectFolderInspection inspection, CancellationToken ct = default);
public Task<ProjectGovernance> AdoptEligibleLegacyProjectAsync(ProjectRef projectRef, CancellationToken ct = default);
```

- Unknown-folder inspection performs no write.
- Governed creation uses `B1ProjectGovernanceRepository.CreateGovernedProjectAsync` and creates layout only after governance succeeds.
- Existing Legacy opening stays read-only until explicit adoption.
- Windows production identity is `windows:<SID>`; tests inject `UserPrincipalRef("test:U1")`.

- [ ] Write and verify RED tests for inspection-with-zero-writes, governed creation, duplicate-folder rejection, explicit adoption, ineligible adoption, and stable SID mapping.
- [ ] Split the current discovery portion of `ProjectOpenService.OpenAsync` into `InspectAsync` without changing Legacy `OpenAsync` behavior used by existing tests.
- [ ] Implement the bounded entry service and principal adapter.
- [ ] Verify Project, Storage governance, App entry, and existing Navigation regressions.
- [ ] Commit:

```powershell
git commit -m "feat(app): establish governed project entry"
```

Stop at the Task 2 review gate.

---

### Task 3: Guided Atomic Initialization Service

**Files:**

- Create: `src/Workbench.App/ProjectWorld/ProjectWorldInitializationService.cs`
- Create: `src/Workbench.App/ProjectWorld/ProjectWorldInitializationModels.cs`
- Modify: `src/Workbench.App/Services/AppServices.cs`
- Test: `tests/Workbench.App.Tests/ProjectWorldInitializationServiceTests.cs`

**Interfaces:**

```csharp
public sealed record ProjectWorldInitializationInput(
    ProjectRef ProjectRef,
    RoleKind RoleKind,
    string Obligation,
    string ExpectedOutcome,
    string InitialWorkContract);

public sealed record ProjectWorldInitializationPreview(
    ProjectWorldInitializationInput Input,
    UserPrincipalRef DecidingPrincipal,
    IReadOnlyList<string> EffectDescriptions);

public ProjectWorldInitializationPreview Preview(ProjectWorldInitializationInput input);
public Task<AuthorityDecision> ConfirmAsync(ProjectWorldInitializationPreview preview, CancellationToken ct = default);
```

Implementation maps exactly to `EstablishResponsibilityCommand` with one established Actor, Responsibility, Assignment, and initial Revision. `DelegatedAuthorityBoundary` is empty. The UI identifies the resulting Actor by RoleKind plus a shortened immutable Actor reference; it does not add an alias store.

- [ ] Write RED tests proving preview creates zero rows and confirmation creates exactly one Decision/Actor/Responsibility/Assignment/initial Revision.
- [ ] Add failure tests for blank input, wrong Project principal, repeated initialization, and authority failure with zero partial effects.
- [ ] Implement minimal preview/confirm mapping using `B1AuthorityCommandService` only.
- [ ] Verify focused tests plus B1 evaluator/projection/App certification regressions.
- [ ] Commit:

```powershell
git commit -m "feat(app): guide atomic project world initialization"
```

Stop at the Task 3 review gate.

---

### Task 4: Project Home Routing and Setup UI

**Files:**

- Create: `src/Workbench.App/ViewModels/ProjectWorldSetupViewModel.cs`
- Create: `src/Workbench.App/Views/ProjectWorldSetupView.axaml`
- Create: `src/Workbench.App/Views/ProjectWorldSetupView.axaml.cs`
- Modify: `src/Workbench.App/App.axaml`
- Modify: `src/Workbench.App/ViewModels/HomeViewModel.cs`
- Modify: `src/Workbench.App/ViewModels/MainWindowViewModel.cs`
- Modify: `src/Workbench.App/Views/HomeView.axaml`
- Test: `tests/Workbench.App.Tests/ProjectWorldSetupViewModelTests.cs`
- Test: `tests/Workbench.App.Tests/NavigationTests.cs`
- Test: `tests/Workbench.App.Tests/HomeViewLayoutTests.cs`

**Behavior:**

- Home shows distinct Create Project and Open Local Project actions.
- Known governed Project routes to the Project World surface introduced in Task 5 through a temporary `ShowProjectWorldAsync` callback contract; until Task 5, the navigation test captures the requested route without constructing an intermediate page.
- Eligible Legacy Project routes to explicit adoption confirmation, then initialization.
- Unknown folder routes to governed creation confirmation, then initialization.
- Unmanaged/corrupt/path-unavailable items never invoke mutation.
- Setup preview shows signed-in principal, acting Actor description, Responsibility, Assignment, and the exact four authority effects.

- [ ] Write ViewModel RED tests for all routes and cancel/failure zero-effects.
- [ ] Write a layout regression at 1280×720 logical pixels proving Settings and both primary actions remain visible; use real Avalonia layout where available, not XAML text assertions.
- [ ] Implement minimal navigation and setup views using existing Fluent controls.
- [ ] Verify keyboard command reachability and accessible names.
- [ ] Run focused UI/navigation tests and the full App suite.
- [ ] Commit:

```powershell
git commit -m "feat(ui): add project world setup journey"
```

Stop at the Task 4 review gate.

---

### Task 5: State-first Project World Explorer

**Files:**

- Create: `src/Workbench.App/ProjectWorld/ProjectWorldQueryService.cs`
- Create: `src/Workbench.App/ProjectWorld/ProjectWorldSnapshot.cs`
- Create: `src/Workbench.App/ViewModels/ProjectWorldViewModel.cs`
- Create: `src/Workbench.App/Views/ProjectWorldView.axaml`
- Create: `src/Workbench.App/Views/ProjectWorldView.axaml.cs`
- Modify: `src/Workbench.App/App.axaml`
- Modify: `src/Workbench.App/ViewModels/MainWindowViewModel.cs`
- Modify: `src/Workbench.App/Services/AppServices.cs`
- Test: `tests/Workbench.App.Tests/ProjectWorldQueryServiceTests.cs`
- Test: `tests/Workbench.App.Tests/ProjectWorldViewModelTests.cs`
- Test: `tests/Workbench.App.Tests/NavigationTests.cs`

**Read model:**

```csharp
public sealed record ProjectWorldSnapshot(
    CoreProject Project,
    ProjectGovernance Governance,
    AcceptedProjectState AcceptedState,
    IReadOnlyList<ProjectWorldAttentionItem> NeedsAttention,
    IReadOnlyList<ProjectWorldWorkItem> ActiveWork,
    IReadOnlyList<AuthorityDecision> RecentDecisions,
    bool HasLegacyContext);
```

`NeedsAttention` is derived from current projection plus unresolved Claim/Handoff input; it is never persisted. `RecentDecisions` is ordered by `ProjectCommitSequence`, not wall-clock time. Empty accepted state stays empty.

- [ ] RED-test restart-stable snapshot composition, no fabricated contribution, dispositioned Assignment exclusion, explicit continuation, Decision order, and Legacy marker separation.
- [ ] Implement the query service from B1 repository/projection reads only.
- [ ] RED-test and implement the Overview, Governance, Work, Decisions, and Legacy Context navigation sections.
- [ ] Route `ProjectWorldReady` Home entries here; keep Legacy workspace accessible only as explicitly labeled Legacy Context, not as the default B1 page.
- [ ] Verify focused tests, Navigation regressions, full App suite, and build.
- [ ] Commit:

```powershell
git commit -m "feat(ui): present accepted project world state"
```

Stop at the Task 5 review gate.

---

### Task 6: Explicit Manual Attempt Continuation

**Files:**

- Create: `src/Workbench.App/ProjectWorld/ManualWorkService.cs`
- Create: `src/Workbench.App/ViewModels/ManualWorkViewModel.cs`
- Create: `src/Workbench.App/Views/ManualWorkView.axaml`
- Create: `src/Workbench.App/Views/ManualWorkView.axaml.cs`
- Modify: `src/Workbench.App/App.axaml`
- Modify: `src/Workbench.App/ViewModels/ProjectWorldViewModel.cs`
- Test: `tests/Workbench.App.Tests/ManualWorkServiceTests.cs`
- Test: `tests/Workbench.App.Tests/ManualWorkViewModelTests.cs`

**Interfaces:**

```csharp
public Task<Attempt> BeginAsync(
    ProjectRef projectRef,
    AssignmentRef assignmentRef,
    AttemptRef? expectedStoredAttemptRef,
    CancellationToken ct = default);

public Task<ManualWorkContext> ContinueAsync(
    ProjectRef projectRef,
    AssignmentRef assignmentRef,
    CancellationToken ct = default);
```

`BeginAsync` loads the current effective Revision and calls `CreateAttemptAndSelectAsync`. `ContinueAsync` uses `EffectiveCurrentAttemptRefs`; it never sorts Attempts by time. Both require the current local UserPrincipal as operator and create zero SessionBindings.

- [ ] RED-test Begin, Continue, stale stored selection, stale Revision, disposition invalidation, and zero SessionBinding/Claim/Handoff/Decision side effects.
- [ ] Implement the minimal service and Manual Work surface.
- [ ] Verify routing Storage tests and B1 Manual continuity certification.
- [ ] Commit:

```powershell
git commit -m "feat(app): begin explicit manual work attempts"
```

Stop at the Task 6 review gate.

---

### Task 7: Atomic Guided Handoff Composer

**Files:**

- Create: `src/Workbench.App/ProjectWorld/ManualHandoffComposerService.cs`
- Create: `src/Workbench.App/ProjectWorld/ManualHandoffModels.cs`
- Create: `src/Workbench.App/ViewModels/ManualHandoffViewModel.cs`
- Create: `src/Workbench.App/Views/ManualHandoffView.axaml`
- Create: `src/Workbench.App/Views/ManualHandoffView.axaml.cs`
- Modify: `src/Workbench.Storage/Continuity/B1ClaimHandoffRepository.cs`
- Modify: `src/Workbench.App/Services/AppServices.cs`
- Modify: `src/Workbench.App/App.axaml`
- Test: `tests/Workbench.Storage.Tests/Continuity/B1ClaimHandoffRepositoryTests.cs`
- Test: `tests/Workbench.App.Tests/ManualHandoffComposerServiceTests.cs`
- Test: `tests/Workbench.App.Tests/ManualHandoffViewModelTests.cs`

**Application input:**

```csharp
public sealed record ManualHandoffInput(
    ProjectRef ProjectRef,
    AttemptRef AttemptRef,
    string PrimaryResult,
    IReadOnlyList<string> Validations,
    IReadOnlyList<string> UnresolvedIssues,
    IReadOnlyList<ManualContributionProposal> ProposedContributions,
    ManualRevisionProposal? ProposedRevision,
    IReadOnlyList<EvidenceRef> EvidenceRefs,
    HandoffRef? ExpectedStoredHandoffRef);

public sealed record ManualHandoffPreview(
    ManualHandoffInput Input,
    LogicalActorRef ClaimantRef,
    IReadOnlyList<ClaimPayload> ClaimPayloads);
```

Add one repository transaction accepting fully constructed validated Claims, Handoff, and expected stored selection. It must use existing table constraints and expected-old CAS. It is not a new Core command or public generic transaction API.

- [ ] RED-test assignee attribution, typed payload mapping, same Project/Attempt/Revision validation, atomic rollback, stale selection, and accepted-state non-mutation.
- [ ] Implement the repository transaction and fixed composer service.
- [ ] Implement the guided UI without a generic Claim type selector.
- [ ] Verify Storage/App focused tests, Claim/Handoff regressions, and zero-session certification.
- [ ] Commit:

```powershell
git commit -m "feat(app): record atomic manual handoffs"
```

Stop at the Task 7 review gate.

---

### Task 8: Guided Decision Composer and Post-Decision Loop

**Files:**

- Create: `src/Workbench.App/ProjectWorld/ManualDecisionComposerService.cs`
- Create: `src/Workbench.App/ProjectWorld/ManualDecisionModels.cs`
- Create: `src/Workbench.App/ViewModels/ManualDecisionViewModel.cs`
- Create: `src/Workbench.App/Views/ManualDecisionView.axaml`
- Create: `src/Workbench.App/Views/ManualDecisionView.axaml.cs`
- Modify: `src/Workbench.App/ViewModels/ManualWorkViewModel.cs`
- Modify: `src/Workbench.App/ViewModels/ProjectWorldViewModel.cs`
- Modify: `src/Workbench.App/App.axaml`
- Test: `tests/Workbench.App.Tests/ManualDecisionComposerServiceTests.cs`
- Test: `tests/Workbench.App.Tests/ManualDecisionViewModelTests.cs`
- Test: `tests/Workbench.App.Tests/ProjectWorldViewModelTests.cs`

**Interfaces:**

```csharp
public sealed record ManualDecisionInput(
    ProjectRef ProjectRef,
    AssignmentRef AssignmentRef,
    RevisionRef EffectiveRevisionRef,
    HandoffRef HandoffRef,
    AssignmentDisposition Disposition,
    IReadOnlyList<ManualContributionSelection> Contributions,
    ManualRevisionActivation? RevisionActivation);

public sealed record ManualDecisionPreview(
    ManualDecisionInput Input,
    UserPrincipalRef DecidingPrincipal,
    IReadOnlyList<string> EffectDescriptions);

public Task<AuthorityDecision> ConfirmAsync(ManualDecisionPreview preview, CancellationToken ct = default);
```

The service reloads current state and constructs the existing `DecideAssignmentCommand`. Edited contribution text creates an authority-owned accepted contribution while preserving the original proposed Claim as `SourceClaimRef`. It never mutates the Claim.

- [ ] RED-test Accepted/Rejected/RevisionRequired, selected/ignored/edited proposals, stale Revision, stale source, unauthorized scope, and full atomic rollback.
- [ ] Implement preview and confirmation using `B1AuthorityCommandService.DecideAssignmentAsync` only.
- [ ] Implement distinct Submitted-as and Deciding-as UI labels; avoid “Accept Handoff.”
- [ ] After commit, reload `ProjectWorldSnapshot`; assert stored routing history remains and effective continuation follows the projector.
- [ ] Verify focused tests, evaluator/repository/projection regressions, and full App suite.
- [ ] Commit:

```powershell
git commit -m "feat(app): guide attributable manual decisions"
```

Stop at the Task 8 review gate.

---

### Task 9: Product Slice 1 Recovery and Coexistence Certification

**Files:**

- Create: `tests/Workbench.App.Tests/ProductSlice1ManualProjectWorldCertificationTests.cs`
- Create: `tests/Workbench.App.Tests/ProductSlice1LegacyAdoptionCertificationTests.cs`
- Modify: `tests/Workbench.App.Tests/NavigationTests.cs`

This task is certification-only. It adds no production API. The first run after adding certification is expected to pass; if it fails, return to the responsible implementation task and reproduce the failure there with a genuine RED.

Certify:

- new governed Project → atomic initialization → Attempt → Handoff → Decision → restart → same Accepted State;
- eligible Legacy adoption creates only governance root before initialization;
- Legacy rows remain byte-for-byte semantically unchanged by the B1 flow;
- no automatic Legacy-to-B1 identities/history;
- Runtime registry empty;
- zero SessionBindings, Leader epochs/messages, Worker execution/events, Summary rows;
- Git unavailable does not block Manual Project World;
- Home reopens the Project into the B1 Explorer after restart.

- [ ] Add the two end-to-end certification tests.
- [ ] Run them once; expected PASS. A failure is assigned to Tasks 2–8 rather than patched here.
- [ ] Run Core, Storage, Project, Runtime, and App default suites.
- [ ] Commit:

```powershell
git commit -m "test(cert): seal manual project world alpha"
```

Stop at the Task 9 review gate.

---

### Task 10: Self-contained Windows Alpha Package

**Files:**

- Create: `src/Workbench.App/Properties/PublishProfiles/WindowsAlpha.pubxml`
- Create: `scripts/verify-windows-alpha-package.ps1`
- Modify: `README.md`
- Test: `tests/Workbench.App.Tests/WindowsAlphaPackagingTests.cs`

**Publish contract:**

```xml
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<SelfContained>true</SelfContained>
<PublishSingleFile>false</PublishSingleFile>
<PublishTrimmed>false</PublishTrimmed>
<DebugType>embedded</DebugType>
```

Multi-file self-contained output is intentional for Avalonia/native dependency reliability. The package must not contain Node, Codex, Git, or a copied database.

- [ ] RED-test the package verification script against a controlled invalid fixture: missing executable, accidental `.db`, and copied Node/Codex binary each fail with a distinct message.
- [ ] Implement the publish profile and verification script.
- [ ] Publish to a disposable output directory and run the script.
- [ ] Launch the published executable with a disposable application-data override and certify Project Home starts without a separately installed runtime or Agent CLI.
- [ ] Update README with the Manual Alpha start path and explicit non-goals.
- [ ] Run full solution build/tests and `git diff --check`.
- [ ] Commit:

```powershell
git commit -m "build: package manual project world alpha"
```

Stop at the Task 10 final implementation review gate. Do not merge automatically.

---

## Final Verification Matrix

Run from the feature worktree:

```powershell
dotnet build AI.Game.Workbench.sln --nologo
dotnet test tests\Workbench.Core.Tests\Workbench.Core.Tests.csproj --no-build --nologo
dotnet test tests\Workbench.Storage.Tests\Workbench.Storage.Tests.csproj --no-build --nologo
dotnet test tests\Workbench.Project.Tests\Workbench.Project.Tests.csproj --no-build --nologo
dotnet test tests\Workbench.Runtime.Tests\Workbench.Runtime.Tests.csproj --no-build --nologo
dotnet test tests\Workbench.App.Tests\Workbench.App.Tests.csproj --no-build --nologo
$publishDir = Join-Path ([System.IO.Path]::GetTempPath()) 'workbench-windows-alpha-verification'
pwsh -NoLogo -NoProfile -File scripts\verify-windows-alpha-package.ps1 -PublishDirectory $publishDir
git diff --check master...HEAD
git status --short
```

Required evidence:

- all suites pass with zero failures;
- no migration 001–020 diff;
- no live database access;
- new Project and eligible Legacy journeys both pass;
- restart produces the same Accepted Project State;
- zero SessionBindings and zero Agent/runtime dependencies in certification;
- Legacy data remains readable and unchanged;
- package contains no database, Node, Codex, or Git payload;
- feature worktree is clean after independent commits.

## Plan Self-review

- Spec coverage: all Product Slice 1 requirements map to Tasks 1–10.
- Scope: one coherent Manual-first vertical slice; Agent, Summary, attachment, retention, and multi-Provider work are excluded.
- Type consistency: entry state, principal, inspection, initialization, snapshot, Manual work, Handoff, and Decision interfaces are defined before consumers.
- Authority: every accepted-state change still uses existing named B1 authority commands.
- Atomicity: new atomicity exists only for the fixed non-authoritative Handoff composer transaction; no generic transaction API is introduced.
- Legacy: adoption remains root-only and explicit.
- Completeness: no open implementation choice, provisional command, or deferred code hook is authorized by this plan.
