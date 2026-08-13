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
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Workbench.App.Tests.Worker;

public sealed class LeaderDraftProposalTests
{
    [Fact]
    public void Leader_schema_closes_every_object_branch_for_codex_nullable_validation()
    {
        using var document = JsonDocument.Parse(LeaderResponseSchema.Json);
        var draft = document.RootElement.GetProperty("properties").GetProperty("draft_proposal");
        var objectBranch = draft.GetProperty("anyOf").EnumerateArray().Single(item => item.GetProperty("type").GetString() == "object");
        Assert.False(objectBranch.GetProperty("additionalProperties").GetBoolean());
        var profile = objectBranch.GetProperty("properties").GetProperty("recommendedExecutionProfile");
        Assert.False(profile.GetProperty("additionalProperties").GetBoolean());
    }
    [Fact]
    public async Task Leader_turn_request_carries_output_schema_but_worker_request_does_not()
    {
        var runtime = new FakeAgentRuntime();
        var registry = new AgentRuntimeRegistry(); registry.Register(runtime);
        await using var context = await AppTestContext.CreateAsync(runtimeRegistry: registry);
        var workspace = await context.CreateWorkspaceForNewProjectAsync();
        runtime.QueueTurn(new AgentTurnCompleted(new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed, "{\"response\":\"ok\",\"draft_proposal\":null}", null), DateTimeOffset.UtcNow));
        await workspace.LeaderPane.InitializeAsync();
        workspace.LeaderPane.DraftMessage = "ordinary";
        await workspace.LeaderPane.SendAsync();
        Assert.NotNull(runtime.SentRequests.Single().OutputSchema);
        Assert.Contains("draft_proposal", runtime.SentRequests.Single().OutputSchema!, StringComparison.Ordinal);

        Assert.DoesNotContain("outputSchema", new AgentRequest("worker").Text, StringComparison.Ordinal);
    }
    [Fact]
    public async Task Ordinary_structured_response_creates_no_draft_or_execution_side_effect()
    {
        var runtime = new FakeAgentRuntime();
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        await using var context = await AppTestContext.CreateAsync(runtimeRegistry: registry);
        var workspace = await context.CreateWorkspaceForNewProjectAsync();
        runtime.QueueTurn(new AgentTurnCompleted(
            new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed,
                "{\"response\":\"The project currently has no active worker.\"}", null), DateTimeOffset.UtcNow));
        await workspace.LeaderPane.InitializeAsync();
        workspace.LeaderPane.DraftMessage = "Explain the current project state.";

        await workspace.LeaderPane.SendAsync();

        Assert.Equal("The project currently has no active worker.", workspace.LeaderPane.Messages.Last().Text);
        Assert.Empty(await context.Services.TaskRepository.ListAsync(workspace.Result.Project.Id));
        await using var connection = context.Services.Database.CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM worker_executions WHERE project_id = $projectId";
        command.Parameters.AddWithValue("$projectId", workspace.Result.Project.Id.ToString());
        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
        Assert.Single(runtime.CreatedSessions);
    }
    [Fact]
    public void Structured_envelope_parses_without_provider_specific_fields()
    {
        var projectId = Guid.NewGuid();
        var json = "{\"response\":\"I drafted this task.\",\"draft_proposal\":{\"title\":\"Title\",\"goal\":\"goal\",\"scope\":\"scope\",\"outOfScope\":\"out\",\"acceptance\":[\"accept\"],\"riskLevel\":\"Low\",\"recommendedExecutionProfile\":{\"providerHint\":\"Codex\",\"modelHint\":\"GPT-5.6-Sol\",\"runtimeHint\":\"codex-app-server\"}}}";
        Assert.True(LeaderStructuredResponse.TryParse(json, projectId, out var parsed));
        Assert.Equal("I drafted this task.", parsed.Response);
        Assert.Equal(projectId, parsed.Proposal!.ProjectId);
        Assert.Equal("Codex", parsed.Proposal.Recommendation.ProviderHint);
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
            new LeaderExecutionRecommendation("Provider", "model", "runtime"));

        var profile = ExecutionProfile.Create("provider", "account", "model", "runtime");
        var result = await fixture.Builder.CreateDraftAsync(proposal, profile);

        Assert.True(result.Succeeded);
        Assert.NotEqual(Guid.Empty, result.TaskId);
        var stored = await fixture.Tasks.GetAsync(fixture.ProjectId, result.TaskId!.Value);
        Assert.NotNull(stored);
        Assert.Equal("Ship onboarding", stored!.Title);
        Assert.Equal(TaskLifecycleStatus.Draft, stored.Status);
        var revision = Assert.Single(await fixture.Revisions.ListAsync(fixture.ProjectId, result.TaskId.Value));
        Assert.Equal(proposal.Goal, revision.Goal);
        Assert.Equal(proposal.Scope, revision.Scope);
        Assert.Equal(proposal.OutOfScope, revision.OutOfScope);
        Assert.Equal(proposal.Acceptance, revision.Acceptance);
        Assert.Equal(proposal.RiskLevel, revision.RiskLevel);
        Assert.Equal(profile, revision.RecommendedExecutionProfile);
    }

    [Fact]
    public async Task Invalid_proposal_creates_no_draft_or_revision()
    {
        await using var fixture = await Fixture.CreateAsync();
        var proposal = new LeaderDraftProposal(
            fixture.ProjectId, "", "goal", "scope", "out", ["accept"], TaskRiskLevel.Low,
            new LeaderExecutionRecommendation(null, null, null));

        var result = await fixture.Builder.CreateDraftAsync(proposal, ExecutionProfile.Create("provider", "account", "model", "runtime"));

        Assert.False(result.Succeeded);
        Assert.Empty(await fixture.Tasks.ListAsync(fixture.ProjectId));
    }

    [Fact]
    public async Task Proposal_for_another_project_is_rejected_without_side_effect()
    {
        await using var fixture = await Fixture.CreateAsync();
        var proposal = new LeaderDraftProposal(
            Guid.NewGuid(), "Title", "goal", "scope", "out", ["accept"], TaskRiskLevel.Low,
            new LeaderExecutionRecommendation(null, null, null));

        var result = await fixture.Builder.CreateDraftAsync(proposal, ExecutionProfile.Create("provider", "account", "model", "runtime"));

        Assert.False(result.Succeeded);
        Assert.Empty(await fixture.Tasks.ListAsync(fixture.ProjectId));
    }

    [Fact]
    public async Task Revision_insert_failure_rolls_back_the_task_insert()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.RejectRevisionInsertsAsync();
        var proposal = new LeaderDraftProposal(
            fixture.ProjectId, "Title", "goal", "scope", "out", ["accept"], TaskRiskLevel.Low,
            new LeaderExecutionRecommendation(null, null, null));

        var result = await fixture.Builder.CreateDraftAsync(proposal, ExecutionProfile.Create("provider", "account", "model", "runtime"));

        Assert.False(result.Succeeded);
        Assert.Empty(await fixture.Tasks.ListAsync(fixture.ProjectId));
        Assert.Equal(0, await fixture.CountRevisionsAsync());
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
                "{\"response\":\"Draft ready.\",\"draft_proposal\":{\"title\":\"Title\",\"goal\":\"goal\",\"scope\":\"scope\",\"outOfScope\":\"out\",\"acceptance\":[\"accept\"],\"riskLevel\":\"Low\",\"recommendedExecutionProfile\":{\"providerHint\":\"fake-provider\",\"modelHint\":\"model-a\",\"runtimeHint\":\"fake-runtime\"}}}", null),
            DateTimeOffset.UtcNow));
        await workspace.LeaderPane.InitializeAsync();
        workspace.LeaderPane.DraftMessage = "Plan onboarding";

        await workspace.LeaderPane.SendAsync();

        Assert.Equal("Draft ready.", workspace.LeaderPane.Messages.Last().Text);
        Assert.Single(await context.Services.TaskRepository.ListAsync(workspace.Result.Project.Id));
        var transcript = await context.Services.LeaderMessageRepository.GetAllAsync(workspace.LeaderPane.SessionEpochId!.Value);
        Assert.Equal("Draft ready.", transcript.Last().Text);
        Assert.DoesNotContain("draft_proposal", transcript.Last().Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Draft_confirmation_binds_candidate_creates_immutable_override_and_starts_one_worker()
    {
        var provider = new Workbench.Runtime.Providers.ProviderId("fake-provider");
        var runtime = new FakeAgentRuntime("Fake Provider", "Fake Account", null,
            new Workbench.Runtime.Providers.ModelProfile(provider, "model-a", "Model A", Workbench.Runtime.Agents.AgentCapability.StructuredEvents),
            new Workbench.Runtime.Providers.ModelProfile(provider, "model-b", "Model B", Workbench.Runtime.Agents.AgentCapability.StructuredEvents));
        var registry = new AgentRuntimeRegistry(); registry.Register(runtime);
        await using var context = await AppTestContext.CreateAsync(runtimeRegistry: registry);
        var workspace = await context.CreateWorkspaceForNewProjectAsync();
        runtime.QueueTurn(new AgentTurnCompleted(new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed,
            "{\"response\":\"Draft ready.\",\"draft_proposal\":{\"title\":\"Title\",\"goal\":\"goal\",\"scope\":\"scope\",\"outOfScope\":\"out\",\"acceptance\":[\"accept\"],\"riskLevel\":\"Low\",\"recommendedExecutionProfile\":{\"providerHint\":\"fake-provider\",\"modelHint\":\"model-a\",\"runtimeHint\":\"fake-runtime\"}}}", null), DateTimeOffset.UtcNow));
        runtime.QueueTurn(new AgentTurnCompleted(new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed, "Worker complete", null), DateTimeOffset.UtcNow));
        await workspace.LeaderPane.InitializeAsync();
        workspace.LeaderPane.SelectedModel = workspace.LeaderPane.AvailableModels[0];
        workspace.LeaderPane.DraftMessage = "delegate";

        await workspace.LeaderPane.SendAsync();

        Assert.Equal("Draft ready.", workspace.LeaderPane.Messages.Last().Text);
        Assert.True(workspace.LeaderPane.HasDraftConfirmation);
        Assert.Single(await context.Services.TaskRepository.ListAsync(workspace.Result.Project.Id));
        Assert.Single(runtime.CreateRequests);
        var p2 = workspace.LeaderPane.WorkerResources.Single(item => item.ModelProfileId == "model-b");
        await workspace.LeaderPane.ChangeWorkerResourceAsync(p2);
        var task = (await context.Services.TaskRepository.ListAsync(workspace.Result.Project.Id)).Single();
        var revisions = await context.Services.TaskRevisionRepository.ListAsync(workspace.Result.Project.Id, task.TaskId);
        Assert.Equal(2, revisions.Count);
        Assert.Equal("model-a", revisions[0].RecommendedExecutionProfile.ModelProfileId);
        Assert.Equal("model-b", revisions[1].RecommendedExecutionProfile.ModelProfileId);

        await workspace.LeaderPane.ConfirmDraftAsync();

        Assert.Equal(2, runtime.CreateRequests.Count);
        Assert.Equal("model-b", runtime.CreateRequests.Last().ModelId);
    }

    [Fact]
    public async Task Invalid_structured_response_is_not_shown_as_raw_json()
    {
        var runtime = new FakeAgentRuntime(); var registry = new AgentRuntimeRegistry(); registry.Register(runtime);
        await using var context = await AppTestContext.CreateAsync(runtimeRegistry: registry);
        var workspace = await context.CreateWorkspaceForNewProjectAsync();
        runtime.QueueTurn(new AgentTurnCompleted(new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed, "{\"draft_proposal\":true}", null), DateTimeOffset.UtcNow));
        await workspace.LeaderPane.InitializeAsync(); workspace.LeaderPane.DraftMessage = "delegate";

        await workspace.LeaderPane.SendAsync();

        Assert.DoesNotContain(workspace.LeaderPane.Messages, message => message.Text.Contains("draft_proposal", StringComparison.Ordinal));
        Assert.Contains(workspace.LeaderPane.Messages, message => message.Text == "Leader response could not be processed.");
        Assert.False(workspace.LeaderPane.HasDraftConfirmation);
        Assert.Empty(await context.Services.TaskRepository.ListAsync(workspace.Result.Project.Id));
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

        public async Task RejectRevisionInsertsAsync()
        {
            await using var connection = new SqliteConnection($"Data Source={_path};Foreign Keys=True;Pooling=False");
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = "CREATE TRIGGER reject_task_revision BEFORE INSERT ON task_revisions BEGIN SELECT RAISE(ABORT, 'revision rejected'); END;";
            await command.ExecuteNonQueryAsync();
        }

        public async Task<int> CountRevisionsAsync()
        {
            await using var connection = new SqliteConnection($"Data Source={_path};Foreign Keys=True;Pooling=False");
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM task_revisions";
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        public ValueTask DisposeAsync()
        {
            try { File.Delete(_path); } catch { }
            return ValueTask.CompletedTask;
        }
    }
}
