using System.Diagnostics;
using System.Text.Json;
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
using Workbench.Runtime.Registry;
using Workbench.Storage.Memory;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.Tests.Continuity;

public sealed class TinyCounterAcceptanceSpineCertificationTests
{
    [Fact]
    public async Task Three_accepted_counter_changes_survive_restart_and_leader_recovery()
    {
        using var directory = new TemporaryDirectory("tiny-counter-spine");
        var databasePath = Path.Combine(directory.Path, "workbench.db");
        var projectPath = Path.Combine(directory.Path, "tiny-counter");
        Directory.CreateDirectory(projectPath);
        Directory.CreateDirectory(Path.Combine(projectPath, "src"));
        await InitializeGitWorkspaceAsync(projectPath);
        var project = new CoreProject(Guid.NewGuid(), "Tiny Counter Game", projectPath, ProjectType.Generic, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var principal = new UserPrincipalRef("user:certification");
        var expectedStatements = new[] { "The counter now increments by 1.", "The counter now increments by 2.", "The counter now increments by 3." };

        for (var round = 0; round < expectedStatements.Length; round++)
        {
            var runtime = new FakeAgentRuntime();
            var registry = new AgentRuntimeRegistry();
            registry.Register(runtime);
            await using var services = AppServices.CreateForDatabasePath(databasePath, TimeProvider.System, registry);
            await services.InitializeAsync();
            if (round == 0)
            {
                await services.B1ProjectGovernance.CreateGovernedProjectAsync(project, principal);
                await services.ProjectWorldInitialization.CommitAsync(new ProjectWorldInitializationRequest(
                    new ProjectRef(project.Id), principal, RoleKind.Worker,
                    "Build the Tiny Counter Game", "A button increments a counter.", "Keep the project small."));
            }
            else
            {
                var boot = await services.LeaderBootContextBuilder.BuildAsync(project, "What is the current counter state?");
                for (var priorRound = 0; priorRound < round; priorRound++)
                    Assert.Contains(expectedStatements[priorRound], boot.Text, StringComparison.Ordinal);
                Assert.Contains("CURRENT AUTHORITY STATE (USER-ACCEPTED)", boot.Text, StringComparison.Ordinal);
            }

            var initialProjection = await services.B1Projections.GetProjectProjectionAsync(new ProjectRef(project.Id));
            var currentAssignment = Assert.Single(initialProjection.AcceptedProjectState.CurrentDelegationAssignments
                .Select(assignmentRef => initialProjection.AcceptedProjectState.Assignments[assignmentRef]));
            var currentResponsibility = currentAssignment.ResponsibilityRef;
            var currentWorker = currentAssignment.AssigneeActorRef;
            var before = await services.B1Projections.GetAcceptedProjectStateAsync(new ProjectRef(project.Id));

            var increment = round + 1;
            var profile = ExecutionProfile.Create(runtime.Provider.Id.Value, runtime.Account.Id.Value.ToString(), "model-a", "fake-runtime");
            var taskRevision = new TaskRevision(Guid.NewGuid(), 1, $"Change counter to {round + 1}",
                "`src/counter.js`", "Do not change unrelated files",
                [$"The counter increments by {round + 1}"], TaskRiskLevel.Low, profile, "certification",
                TaskRevisionApprover.User, DateTimeOffset.UtcNow, null);
            await services.TaskRepository.CreateAsync(project.Id, new TaskDraft(
                taskRevision.TaskId, taskRevision.Goal, taskRevision.Goal, taskRevision.Scope,
                taskRevision.OutOfScope, taskRevision.Acceptance, taskRevision.RiskLevel, profile,
                DateTimeOffset.UtcNow, taskRevision));

            runtime.BeforeSendAsync = (_, _) => File.WriteAllTextAsync(
                Path.Combine(projectPath, "src", "counter.js"),
                $"export function increment(value) {{ return value + {increment}; }}{Environment.NewLine}");
            runtime.QueueTurn(new AgentTurnCompleted(
                new AgentResult(
                    AgentSessionId.New(),
                    AgentSessionStatus.Completed,
                    System.Text.Json.JsonSerializer.Serialize(new
                    {
                        Kind = "FinalReport",
                        Message = $"Implemented counter increment {increment}.",
                        ValidationSummary = "counter.js verified",
                        ProposedChanges = new[] { expectedStatements[round] }
                    }),
                    null),
                DateTimeOffset.UtcNow));
            var executionId = Guid.NewGuid();
            StoredCanonicalWorkerCompletion? pending = null;
            var identity = WorkerExecutionIdentity.Start(
                taskRevision.CreateReference(),
                $"counter-{round}",
                "main",
                ProviderAccountBinding.Create(profile.ProviderId, profile.ProviderAccountId),
                profile,
                $"worker/counter-{round + 1}",
                projectPath);
            var start = await services.WorkerSessionRouter.StartAsync(new WorkerStartRequest(
                project, taskRevision.TaskId, taskRevision.Id, taskRevision.Goal, profile,
                $"Change the counter to {increment}.", null, "Tiny Counter Worker",
                ExecutionId: executionId,
                ExecutionIdentity: identity,
                OnCanonicalCompletion: (completion, _) =>
                {
                    pending = completion;
                    return Task.CompletedTask;
                }));
            Assert.True(start.Succeeded, start.Error);
            Assert.NotNull(pending);
            Assert.Equal(CanonicalWorkerCompletionStatus.GovernanceReady, pending!.Status);
            var execution = await services.WorkerExecutionRepository.GetAsync(project.Id, executionId);
            Assert.NotNull(execution);
            Assert.Equal(WorkerExecutionState.CompletedPendingReview, execution!.State);
            var verificationEvidence = await new Workbench.Storage.Continuity.B1WorkerBridgeRepository(services.Database)
                .GetEvidenceAsync(new ProjectRef(project.Id), executionId);
            Assert.NotNull(verificationEvidence);
            Assert.Contains("verification", verificationEvidence!.EvidenceRef.Value, StringComparison.Ordinal);
            Assert.Equal("NotVerifiable", verificationEvidence.VerificationResult);
            var verification = JsonSerializer.Deserialize<WorkerCompletionVerification>(
                verificationEvidence.VerificationJson);
            Assert.NotNull(verification);
            Assert.Equal(WorkerCompletionVerificationResult.Passed,
                Assert.Single(verification!.Checks, value => value.Name == "declared-deliverable").Result);
            Assert.Equal(WorkerCompletionVerificationResult.Passed,
                Assert.Single(verification.Checks, value => value.Name == "scope").Result);
            Assert.Equal(before.CurrentContributions.Count,
                (await services.B1Projections.GetAcceptedProjectStateAsync(new ProjectRef(project.Id))).CurrentContributions.Count);

            var decision = await services.GuidedDecision.CommitAsync(new GuidedDecisionRequest(
                new ProjectRef(project.Id), principal, pending.Facts.HandoffRef, AssignmentDisposition.Accepted,
                ContributionDecisionMode.AdoptVerbatim, null, null));
            var accepted = await services.B1Projections.GetAcceptedProjectStateAsync(new ProjectRef(project.Id));
            var contribution = Assert.Single(accepted.CurrentContributions,
                value => value.AuthorityDecisionRef == decision.DecisionRef);
            Assert.Equal(expectedStatements[round], contribution.Statement);
            var summaries = await services.ProjectSummaryRepository.QueryAsync(new SummaryQuery(project.Id, 200));
            Assert.Contains(summaries, value => value.Text.Contains(expectedStatements[round], StringComparison.Ordinal));

            if (round + 1 < expectedStatements.Length)
            {
                var nextDelegation = await services.B1AuthorityCommands.DelegateAssignmentAsync(
                    new DelegateAssignmentCommand(
                        new ProjectRef(project.Id), principal,
                        new DecidingAuthorityRef.UserPrincipal(principal),
                        new AssignmentDelegationInstruction(
                            new ResponsibilityTarget.Existing(currentResponsibility),
                            new AssignmentAssigneeTarget.Existing(currentWorker),
                            new AssignmentRevisionContract($"Counter round {round + 2}"),
                            currentAssignment.AssignmentRef),
                        null, [], []));
                Assert.NotNull(nextDelegation.AssignmentDelegationEffect);
            }
        }

        await using (var restarted = AppServices.CreateForDatabasePath(databasePath, TimeProvider.System, new AgentRuntimeRegistry()))
        {
            await restarted.InitializeAsync();
            var accepted = await restarted.B1Projections.GetAcceptedProjectStateAsync(new ProjectRef(project.Id));
            Assert.Equal(expectedStatements.Length, accepted.CurrentContributions.Count);
            foreach (var statement in expectedStatements)
                Assert.Contains(accepted.CurrentContributions, value => value.Statement == statement);

            var boot = await restarted.LeaderBootContextBuilder.BuildAsync(project, "What is the current counter state?");
            foreach (var statement in expectedStatements)
                Assert.Contains(statement, boot.Text, StringComparison.Ordinal);
            Assert.Contains("CURRENT AUTHORITY STATE (USER-ACCEPTED)", boot.Text, StringComparison.Ordinal);
        }

        Assert.Equal("export function increment(value) { return value + 3; }" + Environment.NewLine,
            await File.ReadAllTextAsync(Path.Combine(projectPath, "src", "counter.js")));
    }

    private static async Task InitializeGitWorkspaceAsync(string path)
    {
        await File.WriteAllTextAsync(Path.Combine(path, "src", "counter.js"),
            $"export function increment(value) {{ return value + 0; }}{Environment.NewLine}");
        await RunGitAsync(path, "init", "-b", "main");
        await RunGitAsync(path, "config", "user.name", "Workbench Tests");
        await RunGitAsync(path, "config", "user.email", "workbench-tests@local.invalid");
        await RunGitAsync(path, "add", ".");
        await RunGitAsync(path, "commit", "-m", "initial counter");
    }

    private static async Task RunGitAsync(string workingDirectory, params string[] arguments)
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
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(error);
    }
}
