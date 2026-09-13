using System.Diagnostics;
using Workbench.App.Services;
using Workbench.App.Tests.Support;
using Workbench.App.Continuity;
using Workbench.App.ProjectWorld;
using Workbench.App.Worker;
using Workbench.Core.Continuity;
using Workbench.Core.Projects;
using Workbench.Core.Tasks;
using Workbench.Core.Workers;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Providers;
using Workbench.Runtime.Registry;
using Workbench.Storage.Continuity;
using Workbench.Storage.Memory;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.Tests.Continuity;

public sealed class LiveCodexAcceptanceSpineTests
{
    [LiveFact("WORKBENCH_RUN_CODEX_WORKER_LIVE")]
    public async Task Real_codex_worker_change_requires_acceptance_and_survives_restart()
    {
        var runtime = await CodexRuntimeComposition.ConnectAsync(CancellationToken.None);
        TemporaryDirectory? projectDirectory = null;
        try
        {
            var models = await runtime.GetModelsAsync();
            var model = models.First();
            var registry = new AgentRuntimeRegistry();
            registry.Register(runtime);
            await using var context = await AppTestContext.CreateAsync(runtimeRegistry: registry);
            projectDirectory = new TemporaryDirectory("live-codex-counter");
            var projectPath = projectDirectory.Path;
            await InitializeGitWorkspaceAsync(projectPath);

            var now = context.Time.GetUtcNow();
            var project = new CoreProject(
                Guid.NewGuid(),
                "Live Codex Counter",
                projectPath,
                ProjectType.Generic,
                null,
                now,
                now);
            var principal = context.Services.UserPrincipalProvider.GetCurrent();
            await context.Services.B1ProjectGovernance.CreateGovernedProjectAsync(project, principal);
            await context.Services.ProjectWorldInitialization.CommitAsync(new ProjectWorldInitializationRequest(
            new ProjectRef(project.Id),
            principal,
            RoleKind.Worker,
            "Implement one bounded counter change.",
            "A concise result and verification report.",
            "Only modify the declared counter file and report the proposed accepted-state statement."));

            var profile = ExecutionProfile.Create(
            runtime.Provider.Id.Value,
            runtime.Account.Id.Value.ToString(),
            model.ModelId,
            runtime.RuntimeKind);
            var taskId = Guid.NewGuid();
            var revision = new TaskRevision(
            taskId,
            1,
            "Change the counter increment from 1 to 2.",
            "`src/counter.js`",
            "Do not change unrelated files.",
            ["The counter now increments by 2."],
            TaskRiskLevel.Low,
            profile,
            "live-codex",
            TaskRevisionApprover.User,
            context.Time.GetUtcNow(),
            null);
            await context.Services.TaskRepository.CreateAsync(project.Id, new TaskDraft(
            taskId,
            revision.Goal,
            revision.Goal,
            revision.Scope,
            revision.OutOfScope,
            revision.Acceptance,
            revision.RiskLevel,
            profile,
            context.Time.GetUtcNow(),
            revision));

            var executionId = Guid.NewGuid();
            var identity = WorkerExecutionIdentity.Start(
            revision.CreateReference(),
            await GitAsync(projectPath, "rev-parse", "HEAD"),
            "main",
            ProviderAccountBinding.Create(profile.ProviderId, profile.ProviderAccountId),
            profile,
            "worker/live-codex-counter",
            projectPath);
            StoredCanonicalWorkerCompletion? pending = null;
            var start = await context.Services.WorkerSessionRouter.StartAsync(new WorkerStartRequest(
            project,
            taskId,
            revision.Id,
            revision.Goal,
            profile,
            """
            Change the counter implementation in `src/counter.js` from +1 to +2.
            After editing the file, verify it contains `return value + 2`.
            Your final response must be exactly one JSON object, with no Markdown fences:
            {"Kind":"FinalReport","Message":"Implemented counter increment 2.","ValidationSummary":"counter.js verified","ProposedChanges":["The counter now increments by 2."]}
            Do not omit the ProposedChanges array.
            """,
            null,
            "Live Codex Worker",
            ExecutionId: executionId,
            ExecutionIdentity: identity,
            AccessMode: AgentAccessMode.Full,
            OnCanonicalCompletion: (completion, _) =>
            {
                pending = completion;
                return Task.CompletedTask;
            }));

            Assert.True(start.Succeeded, start.Error);
            Assert.NotNull(pending);
            var completion = pending!;
            Assert.Equal(CanonicalWorkerCompletionStatus.GovernanceReady, completion.Status);
            Assert.NotEmpty(completion.Facts.ProposedChanges);
            Assert.Contains("return value + 2", await File.ReadAllTextAsync(
            Path.Combine(projectPath, "src", "counter.js")), StringComparison.Ordinal);

            var execution = await context.Services.WorkerExecutionRepository.GetAsync(project.Id, executionId);
            Assert.NotNull(execution);
            Assert.Equal(WorkerExecutionState.CompletedPendingReview, execution!.State);
            var evidence = await new B1WorkerBridgeRepository(context.Services.Database)
            .GetEvidenceAsync(new ProjectRef(project.Id), executionId);
            Assert.NotNull(evidence);
            var claimState = await context.Services.B1AuthorityRepository.LoadProjectStateAsync(new ProjectRef(project.Id));
            Assert.Contains(claimState.Claims, claim => claim.ClaimRef == completion.Facts.ResultClaimRef);
            Assert.Contains(claimState.Handoffs, handoff => handoff.HandoffRef == completion.Facts.HandoffRef);

            var before = await context.Services.B1Projections.GetAcceptedProjectStateAsync(new ProjectRef(project.Id));
            Assert.Empty(before.CurrentContributions);
            var decision = await context.Services.GuidedDecision.CommitAsync(new GuidedDecisionRequest(
            new ProjectRef(project.Id),
            principal,
            completion.Facts.HandoffRef,
            AssignmentDisposition.Accepted,
            ContributionDecisionMode.AdoptVerbatim,
            null,
            null));
            var accepted = await context.Services.B1Projections.GetAcceptedProjectStateAsync(new ProjectRef(project.Id));
            Assert.Contains(accepted.CurrentContributions, contribution =>
            contribution.AuthorityDecisionRef == decision.DecisionRef &&
            contribution.Statement == "The counter now increments by 2.");
            Assert.Contains(
            await context.Services.ProjectSummaryRepository.QueryAsync(new SummaryQuery(project.Id, 200)),
            summary => summary.Text.Contains("The counter now increments by 2.", StringComparison.Ordinal));

            var databasePath = context.DatabasePath;
            await context.Services.DisposeAsync();
            await using var restarted = AppServices.CreateForDatabasePath(databasePath, TimeProvider.System, new AgentRuntimeRegistry());
            await restarted.InitializeAsync();
            var recovered = await restarted.B1Projections.GetAcceptedProjectStateAsync(new ProjectRef(project.Id));
            Assert.Contains(recovered.CurrentContributions, contribution =>
            contribution.Statement == "The counter now increments by 2.");
            var boot = await restarted.LeaderBootContextBuilder.BuildAsync(project, "What is the current counter state?");
            Assert.Contains("The counter now increments by 2.", boot.Text, StringComparison.Ordinal);
            Assert.Contains("CURRENT AUTHORITY STATE (USER-ACCEPTED)", boot.Text, StringComparison.Ordinal);
        }
        finally
        {
            if (runtime is IAsyncDisposable disposable)
                await disposable.DisposeAsync();
            projectDirectory?.Dispose();
        }
    }

    private static async Task InitializeGitWorkspaceAsync(string path)
    {
        Directory.CreateDirectory(Path.Combine(path, "src"));
        await File.WriteAllTextAsync(
            Path.Combine(path, "src", "counter.js"),
            $"export function increment(value) {{ return value + 1; }}{Environment.NewLine}");
        await GitAsync(path, "init", "-b", "main");
        await GitAsync(path, "config", "user.name", "Workbench Live Tests");
        await GitAsync(path, "config", "user.email", "workbench-live-tests@local.invalid");
        await GitAsync(path, "add", ".");
        await GitAsync(path, "commit", "-m", "initial counter");
    }

    private static async Task<string> GitAsync(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start git.");
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(error);
        return output.Trim();
    }
}
