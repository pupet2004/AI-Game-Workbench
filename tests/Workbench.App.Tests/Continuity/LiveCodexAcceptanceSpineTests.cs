using System.Diagnostics;
using System.Text.Json;
using Workbench.App.Continuity;
using Workbench.App.ProjectWorld;
using Workbench.App.Services;
using Workbench.App.Tests.Support;
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
    public async Task Real_codex_worker_completes_three_accepted_counter_rounds()
    {
        var runtime = await CodexRuntimeComposition.ConnectAsync(CancellationToken.None);
        TemporaryDirectory? projectDirectory = null;
        try
        {
            var model = (await runtime.GetModelsAsync()).First();
            var registry = new AgentRuntimeRegistry();
            registry.Register(runtime);
            projectDirectory = new TemporaryDirectory("live-codex-counter");
            var projectPath = projectDirectory.Path;
            await InitializeGitWorkspaceAsync(projectPath);

            var databasePath = Path.Combine(projectDirectory.Path, "workbench.db");
            var now = DateTimeOffset.UtcNow;
            var project = new CoreProject(
                Guid.NewGuid(),
                "Live Codex Counter",
                projectPath,
                ProjectType.Generic,
                null,
                now,
                now);
            var principal = new UserPrincipalRef("user:live-codex");
            var expectedStatements = new[]
            {
                "The counter now increments by 1.",
                "The counter now increments by 2.",
                "The counter now increments by 3."
            };

            for (var round = 0; round < expectedStatements.Length; round++)
            {
                var services = AppServices.CreateForDatabasePath(databasePath, TimeProvider.System, registry);
                try
                {
                    await services.InitializeAsync();
                    if (round == 0)
                    {
                        await services.B1ProjectGovernance.CreateGovernedProjectAsync(project, principal);
                        await services.ProjectWorldInitialization.CommitAsync(new ProjectWorldInitializationRequest(
                            new ProjectRef(project.Id),
                            principal,
                            RoleKind.Worker,
                            "Implement one bounded counter change.",
                            "A concise result and verification report.",
                            "Only modify the declared counter file and report the proposed accepted-state statement."));
                    }
                    else
                    {
                        var boot = await services.LeaderBootContextBuilder.BuildAsync(
                            project,
                            "What is the current counter state?");
                        for (var priorRound = 0; priorRound < round; priorRound++)
                            Assert.Contains(expectedStatements[priorRound], boot.Text, StringComparison.Ordinal);
                        Assert.Contains("CURRENT AUTHORITY STATE (USER-ACCEPTED)", boot.Text, StringComparison.Ordinal);
                    }

                    var projection = await services.B1Projections.GetProjectProjectionAsync(new ProjectRef(project.Id));
                    var assignment = Assert.Single(projection.AcceptedProjectState.CurrentDelegationAssignments
                        .Select(value => projection.AcceptedProjectState.Assignments[value]));
                    var profile = ExecutionProfile.Create(
                        runtime.Provider.Id.Value,
                        runtime.Account.Id.Value.ToString(),
                        model.ModelId,
                        runtime.RuntimeKind);
                    var target = round + 1;
                    var taskId = Guid.NewGuid();
                    var revision = new TaskRevision(
                        taskId,
                        1,
                        $"Change the counter increment from {round} to {target}.",
                        "`src/counter.js`",
                        "Do not change unrelated files.",
                        [expectedStatements[round]],
                        TaskRiskLevel.Low,
                        profile,
                        "live-codex",
                        TaskRevisionApprover.User,
                        DateTimeOffset.UtcNow,
                        null);
                    await services.TaskRepository.CreateAsync(project.Id, new TaskDraft(
                        taskId,
                        revision.Goal,
                        revision.Goal,
                        revision.Scope,
                        revision.OutOfScope,
                        revision.Acceptance,
                        revision.RiskLevel,
                        profile,
                        DateTimeOffset.UtcNow,
                        revision));

                    var executionId = Guid.NewGuid();
                    var identity = WorkerExecutionIdentity.Start(
                        revision.CreateReference(),
                        await GitAsync(projectPath, "rev-parse", "HEAD"),
                        "main",
                        ProviderAccountBinding.Create(profile.ProviderId, profile.ProviderAccountId),
                        profile,
                        $"worker/live-codex-counter-{target}",
                        projectPath);
                    var finalReport = JsonSerializer.Serialize(new
                    {
                        Kind = "FinalReport",
                        Message = $"Implemented counter increment {target}.",
                        ValidationSummary = "counter.js verified",
                        ProposedChanges = new[] { expectedStatements[round] }
                    });
                    StoredCanonicalWorkerCompletion? pending = null;
                    var start = await services.WorkerSessionRouter.StartAsync(new WorkerStartRequest(
                        project,
                        taskId,
                        revision.Id,
                        revision.Goal,
                        profile,
                        $"""
                        Change the counter implementation in `src/counter.js` from +{round} to +{target}.
                        After editing the file, verify it contains `return value + {target}`.
                        Your final response must be exactly one JSON object, with no Markdown fences:
                        {finalReport}
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
                    Assert.Equal(expectedStatements[round], Assert.Single(completion.Facts.ProposedChanges));
                    Assert.Contains(
                        $"return value + {target}",
                        await File.ReadAllTextAsync(Path.Combine(projectPath, "src", "counter.js")),
                        StringComparison.Ordinal);

                    var execution = await services.WorkerExecutionRepository.GetAsync(project.Id, executionId);
                    Assert.NotNull(execution);
                    Assert.Equal(WorkerExecutionState.CompletedPendingReview, execution!.State);
                    Assert.NotNull(await new B1WorkerBridgeRepository(services.Database)
                        .GetEvidenceAsync(new ProjectRef(project.Id), executionId));
                    var claimState = await services.B1AuthorityRepository.LoadProjectStateAsync(new ProjectRef(project.Id));
                    Assert.Contains(claimState.Claims, claim => claim.ClaimRef == completion.Facts.ResultClaimRef);
                    Assert.Contains(claimState.Handoffs, handoff => handoff.HandoffRef == completion.Facts.HandoffRef);

                    var before = await services.B1Projections.GetAcceptedProjectStateAsync(new ProjectRef(project.Id));
                    Assert.Equal(round, before.CurrentContributions.Count);
                    var decision = await services.GuidedDecision.CommitAsync(new GuidedDecisionRequest(
                        new ProjectRef(project.Id),
                        principal,
                        completion.Facts.HandoffRef,
                        AssignmentDisposition.Accepted,
                        ContributionDecisionMode.AdoptVerbatim,
                        null,
                        null));
                    var accepted = await services.B1Projections.GetAcceptedProjectStateAsync(new ProjectRef(project.Id));
                    Assert.Contains(accepted.CurrentContributions, contribution =>
                        contribution.AuthorityDecisionRef == decision.DecisionRef &&
                        contribution.Statement == expectedStatements[round]);
                    Assert.Contains(
                        await services.ProjectSummaryRepository.QueryAsync(new SummaryQuery(project.Id, 200)),
                        summary => summary.Text.Contains(expectedStatements[round], StringComparison.Ordinal));

                    if (round + 1 < expectedStatements.Length)
                    {
                        var successor = await services.B1AuthorityCommands.DelegateAssignmentAsync(
                            new DelegateAssignmentCommand(
                                new ProjectRef(project.Id),
                                principal,
                                new DecidingAuthorityRef.UserPrincipal(principal),
                                new AssignmentDelegationInstruction(
                                    new ResponsibilityTarget.Existing(assignment.ResponsibilityRef),
                                    new AssignmentAssigneeTarget.Existing(assignment.AssigneeActorRef),
                                    new AssignmentRevisionContract($"Counter round {round + 2}"),
                                    assignment.AssignmentRef),
                                null,
                                [],
                                []));
                        Assert.NotNull(successor.AssignmentDelegationEffect);
                    }
                }
                finally
                {
                    await services.DisposeAsync();
                }
            }

            var restarted = AppServices.CreateForDatabasePath(databasePath, TimeProvider.System, new AgentRuntimeRegistry());
            try
            {
                await restarted.InitializeAsync();
                var accepted = await restarted.B1Projections.GetAcceptedProjectStateAsync(new ProjectRef(project.Id));
                Assert.Equal(expectedStatements.Length, accepted.CurrentContributions.Count);
                foreach (var statement in expectedStatements)
                    Assert.Contains(accepted.CurrentContributions, value => value.Statement == statement);

                var boot = await restarted.LeaderBootContextBuilder.BuildAsync(
                    project,
                    "What is the current counter state?");
                foreach (var statement in expectedStatements)
                    Assert.Contains(statement, boot.Text, StringComparison.Ordinal);
                Assert.Contains("CURRENT AUTHORITY STATE (USER-ACCEPTED)", boot.Text, StringComparison.Ordinal);
            }
            finally
            {
                await restarted.DisposeAsync();
            }

            var finalCounter = await File.ReadAllTextAsync(Path.Combine(projectPath, "src", "counter.js"));
            Assert.Equal(
                "export function increment(value) { return value + 3; }\n",
                finalCounter.Replace("\r\n", "\n", StringComparison.Ordinal));
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
