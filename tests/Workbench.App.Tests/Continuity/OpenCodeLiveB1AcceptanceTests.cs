using Workbench.App.Continuity;
using Workbench.App.Services;
using Workbench.App.Tests.Support;
using Workbench.App.ProjectWorld;
using Workbench.Core.Continuity;
using Workbench.Runtime.Providers.OpenCode;
using Workbench.App.Tests;

namespace Workbench.App.Tests.Continuity;

public sealed class OpenCodeLiveB1AcceptanceTests
{
    [LiveFact("WORKBENCH_RUN_OPENCODE_LIVE")]
    public async Task OpenCode_and_DeepSeek_complete_the_B1_participation_and_decision_path()
    {
        await using var context = await AppTestContext.CreateAsync();
        await using var runtime = await OpenCodeAcpAgentRuntime.ConnectAsync(
            OpenCodeRuntimeComposition.CreateOptions(
                Environment.GetEnvironmentVariable,
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppContext.BaseDirectory),
            OpenCodeRuntimeComposition.CreateLocalAccountSummary().Id);
        var configuredProjectPath = Environment.GetEnvironmentVariable("WORKBENCH_LIVE_PROJECT_PATH");
        TemporaryDirectory? temporaryFolder = null;
        var projectPath = configuredProjectPath;
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            temporaryFolder = new TemporaryDirectory("opencode-live-b1");
            projectPath = temporaryFolder.Path;
        }

        using (temporaryFolder)
        {
            var opened = await context.Services.ProjectOpenService.OpenAsync(projectPath);
        var projectRef = new ProjectRef(opened.Project.Id);
        var principal = context.Services.UserPrincipalProvider.GetCurrent();
        await context.Services.B1ProjectGovernance.CreateGovernedProjectForExistingProjectAsync(projectRef, principal);
        await context.Services.ProjectWorldInitialization.CommitAsync(
            new ProjectWorldInitializationRequest(
                projectRef,
                principal,
                RoleKind.Worker,
                "Own one bounded verification task",
                "A concise verification report",
                "Return a concise verification report. Do not use tools or modify files."));
        var assignment = Assert.Single(B1Projector.Build(
            await context.Services.B1AuthorityRepository.LoadProjectStateAsync(projectRef)).AcceptedProjectState.Assignments.Values);

        var result = await context.Services.B1AgentParticipation.ExecuteAsync(
            runtime,
            new B1AgentExecutionRequest(
                projectRef,
                principal,
                assignment.AssignmentRef,
                "deepseek/deepseek-v4-flash",
                projectPath,
                "Reply with one concise verification result. Do not use tools or modify files.",
                []));

        var handoff = result.Handoff;
        var expectedBindings = 1;
        if (string.Equals(Environment.GetEnvironmentVariable("WORKBENCH_RUN_CODEX_LIVE"), "1", StringComparison.Ordinal))
        {
            var codex = await CodexRuntimeComposition.ConnectAsync(CancellationToken.None);
            try
            {
                var models = await codex.GetModelsAsync();
                Assert.NotEmpty(models);
                var model = models.First();
                var continued = await context.Services.B1AgentParticipation.ExecuteAsync(
                    codex,
                    new B1AgentExecutionRequest(
                        projectRef,
                        principal,
                        assignment.AssignmentRef,
                        model.ModelId,
                        projectPath,
                        "Continue from the bounded Handoff. Reply with one concise verification result. Do not use tools or modify files.",
                        [],
                        result.Attempt.AttemptRef));
                handoff = continued.Handoff;
                expectedBindings = 2;
            }
            finally
            {
                if (codex is IAsyncDisposable disposable)
                {
                    await disposable.DisposeAsync();
                }
            }
        }

        var decision = await context.Services.GuidedDecision.CommitAsync(new GuidedDecisionRequest(
            projectRef,
            principal,
            handoff.HandoffRef,
            AssignmentDisposition.Accepted,
            ContributionDecisionMode.Ignore,
            null,
            null));

        var after = await context.Services.B1AuthorityRepository.LoadProjectStateAsync(projectRef);
        Assert.Single(after.Attempts);
        Assert.Equal(expectedBindings, after.SessionBindings.Count);
        Assert.Equal(expectedBindings, after.Handoffs.Count);
        Assert.Contains(after.AuthorityDecisions, value => value.DecisionRef == decision.DecisionRef);
        Assert.Equal(AssignmentDisposition.Accepted, decision.AssignmentDispositionEffect!.Disposition);

        await context.Services.DisposeAsync();
        await using var restarted = AppServices.CreateForDatabasePath(context.DatabasePath, context.Time);
        await restarted.InitializeAsync();
        var recovered = await restarted.B1AuthorityRepository.LoadProjectStateAsync(projectRef);
        Assert.Contains(recovered.AuthorityDecisions, value => value.DecisionRef == decision.DecisionRef);
        Assert.Equal(AssignmentDisposition.Accepted, B1Projector.Build(recovered).AcceptedProjectState.RevisionDispositions.Values.Single().Disposition);
        }
    }
}
