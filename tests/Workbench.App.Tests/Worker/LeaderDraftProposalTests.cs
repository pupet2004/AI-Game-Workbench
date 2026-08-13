using Workbench.App.Leader;
using Workbench.Core.Tasks;
using Workbench.Storage.Database;
using Workbench.Storage.Tasks;
using Workbench.Core.Projects;
using Workbench.Storage.Projects;
using CoreProject = Workbench.Core.Projects.Project;
using Workbench.App.Tests.Support;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Registry;

namespace Workbench.App.Tests.Worker;

public sealed class LeaderDraftProposalTests
{
    [Fact]
    public void Structured_envelope_parses_without_provider_specific_fields()
    {
        var projectId = Guid.NewGuid();
        var json = "{\"response\":\"I drafted this task.\",\"draft_proposal\":{\"title\":\"Title\",\"goal\":\"goal\",\"scope\":\"scope\",\"outOfScope\":\"out\",\"acceptance\":[\"accept\"],\"riskLevel\":\"Low\",\"recommendedExecutionProfile\":{\"providerId\":\"p\",\"providerAccountId\":\"a\",\"modelProfileId\":\"m\",\"agentRuntimeId\":\"r\"}}}";
        Assert.True(LeaderStructuredResponse.TryParse(json, projectId, out var parsed));
        Assert.Equal("I drafted this task.", parsed.Response);
        Assert.Equal(projectId, parsed.Proposal!.ProjectId);
    }
    [Fact]
    public async Task Valid_provider_neutral_proposal_persists_one_project_scoped_draft_and_revision()
    {
        await using var fixture = await Fixture.CreateAsync();
        var proposal = new LeaderDraftProposal(
            fixture.ProjectId,
            "Ship onboarding",
            "Create a guided onboarding flow",
            "Onboarding screens and validation",
            "Billing and account recovery",
            ["A new user can complete onboarding"],
            TaskRiskLevel.Medium,
            ExecutionProfile.Create("provider", "account", "model", "runtime"));

        var result = await fixture.Builder.CreateDraftAsync(proposal);

        Assert.True(result.Succeeded);
        Assert.NotEqual(Guid.Empty, result.TaskId);
        var stored = await fixture.Tasks.GetAsync(fixture.ProjectId, result.TaskId!.Value);
        Assert.NotNull(stored);
        Assert.Equal("Ship onboarding", stored!.Title);
        Assert.Single(await fixture.Revisions.ListAsync(fixture.ProjectId, result.TaskId.Value));
    }

    [Fact]
    public async Task Invalid_proposal_creates_no_draft_or_revision()
    {
        await using var fixture = await Fixture.CreateAsync();
        var proposal = new LeaderDraftProposal(
            fixture.ProjectId, "", "goal", "scope", "out", ["accept"], TaskRiskLevel.Low,
            ExecutionProfile.Create("provider", "account", "model", "runtime"));

        var result = await fixture.Builder.CreateDraftAsync(proposal);

        Assert.False(result.Succeeded);
        Assert.Empty(await fixture.Tasks.ListAsync(fixture.ProjectId));
    }

    [Fact]
    public async Task Proposal_for_another_project_is_rejected_without_side_effect()
    {
        await using var fixture = await Fixture.CreateAsync();
        var proposal = new LeaderDraftProposal(
            Guid.NewGuid(), "Title", "goal", "scope", "out", ["accept"], TaskRiskLevel.Low,
            ExecutionProfile.Create("provider", "account", "model", "runtime"));

        var result = await fixture.Builder.CreateDraftAsync(proposal);

        Assert.False(result.Succeeded);
        Assert.Empty(await fixture.Tasks.ListAsync(fixture.ProjectId));
    }

    [Fact]
    public async Task Leader_turn_persists_proposal_but_keeps_structured_envelope_out_of_transcript()
    {
        var runtime = new FakeAgentRuntime();
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        await using var context = await AppTestContext.CreateAsync(runtimeRegistry: registry);
        var workspace = await context.CreateWorkspaceForNewProjectAsync();
        runtime.QueueTurn(new AgentTurnCompleted(
            new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed,
                "{\"response\":\"Draft ready.\",\"draft_proposal\":{\"title\":\"Title\",\"goal\":\"goal\",\"scope\":\"scope\",\"outOfScope\":\"out\",\"acceptance\":[\"accept\"],\"riskLevel\":\"Low\",\"recommendedExecutionProfile\":{\"providerId\":\"p\",\"providerAccountId\":\"a\",\"modelProfileId\":\"m\",\"agentRuntimeId\":\"r\"}}}", null),
            DateTimeOffset.UtcNow));
        await workspace.LeaderPane.InitializeAsync();
        workspace.LeaderPane.DraftMessage = "Plan onboarding";

        await workspace.LeaderPane.SendAsync();

        Assert.Equal("Draft ready.", workspace.LeaderPane.Messages.Last().Text);
        Assert.Single(await context.Services.TaskRepository.ListAsync(workspace.Result.Project.Id));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _path;
        private Fixture(string path, WorkbenchDatabase database, Guid projectId)
        {
            _path = path;
            ProjectId = projectId;
            Tasks = new TaskRepository(database);
            Revisions = new TaskRevisionRepository(database);
            Builder = new LeaderDraftProposalBuilder(projectId, Tasks);
        }

        public Guid ProjectId { get; }
        public TaskRepository Tasks { get; }
        public TaskRevisionRepository Revisions { get; }
        public LeaderDraftProposalBuilder Builder { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var path = Path.Combine(Path.GetTempPath(), $"leader-proposal-{Guid.NewGuid():N}.db");
            var database = new WorkbenchDatabase(path);
            await database.InitializeAsync();
            var projectId = Guid.NewGuid();
            await new ProjectRepository(database).UpsertAsync(new CoreProject(projectId, "Test", Path.GetTempPath(), ProjectType.Generic, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
            return new Fixture(path, database, projectId);
        }

        public ValueTask DisposeAsync()
        {
            try { File.Delete(_path); } catch { }
            return ValueTask.CompletedTask;
        }
    }
}
