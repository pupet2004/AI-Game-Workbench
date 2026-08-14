using Workbench.App.Tests.Support;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Registry;

namespace Workbench.App.Tests;

public sealed class WorkbenchSmokeLibraryProposalTests
{
    [Fact]
    public async Task Smoke_leader_preview_reject_and_accept_close_the_confirmed_library_loop()
    {
        var runtime = new FakeAgentRuntime();
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        await using var context = await AppTestContext.CreateAsync(runtimeRegistry: registry);
        var workspace = await context.CreateWorkspaceForNewProjectAsync();
        using var otherFolder = new TemporaryDirectory("smoke-library-other");
        var other = await context.Services.ProjectOpenService.OpenAsync(otherFolder.Path);
        await workspace.LeaderPane.InitializeAsync();
        await workspace.LibraryPane.InitializeAsync();

        runtime.QueueTurn(Completed(Proposal("First proposal", "Preview-only state")));
        workspace.LeaderPane.DraftMessage = "Assess the closed stage.";
        await workspace.LeaderPane.SendAsync();

        var first = Assert.Single(workspace.LibraryPane.PendingLibraryProposals);
        Assert.Equal("Preview-only state", first.Draft.NodeContent);
        Assert.Empty(workspace.LibraryPane.CategoryGroups);
        Assert.Empty(workspace.LibraryPane.TimeGroups);

        workspace.LibraryPane.SelectedLibraryProposal = first;
        await workspace.LibraryPane.RejectLibraryProposalAsync();
        Assert.Empty(workspace.LibraryPane.PendingLibraryProposals);
        Assert.Empty(workspace.LibraryPane.CategoryGroups);
        Assert.Empty(workspace.LibraryPane.TimeGroups);

        runtime.QueueTurn(Completed(Proposal("Second proposal", "Confirmed relic mechanism")));
        workspace.LeaderPane.DraftMessage = "Propose the final factual state.";
        await workspace.LeaderPane.SendAsync();
        workspace.LibraryPane.SelectedLibraryProposal = Assert.Single(workspace.LibraryPane.PendingLibraryProposals);
        await workspace.LibraryPane.AcceptLibraryProposalAsync();

        var category = Assert.Single(workspace.LibraryPane.CategoryGroups);
        Assert.Equal("Design", category.Category);
        var libraryObject = Assert.Single(category.Objects);
        Assert.Equal("Relics", libraryObject.Topic);
        Assert.Equal("Confirmed current relic overview", libraryObject.CurrentOverview);
        var time = Assert.Single(workspace.LibraryPane.TimeGroups);
        Assert.Equal(new DateOnly(2026, 8, 14), time.LocalDate);
        var node = Assert.Single(time.Nodes);
        Assert.Equal("Confirmed relic mechanism", node.Content);
        Assert.Equal("commit-728f779", Assert.Single(node.Materials).Reference);
        Assert.Empty(await context.Services.ProjectLibraryEvolutionRepository.ListObjectsAsync(other.Project.Id));
    }

    private static string Proposal(string response, string content) => """
        {"response":"RESPONSE_MARKER","draft_proposal":null,"memory_commands":{"daily_summary":null,"library_proposal":{"action":"CreateNode","target_object_id":null,"target_node_id":null,"expected_node_revision":null,"expected_overview_revision":0,"category":"Design","topic":"Relics","local_date":"2026-08-14","node_content":"CONTENT_MARKER","current_overview":"Confirmed current relic overview","materials":[{"kind":"GitCommit","reference":"commit-728f779","label":"Implementation baseline"}]}}}
        """
        .Replace("RESPONSE_MARKER", response, StringComparison.Ordinal)
        .Replace("CONTENT_MARKER", content, StringComparison.Ordinal);

    private static AgentTurnCompleted Completed(string text) =>
        new(new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed, text, null), DateTimeOffset.UtcNow);
}
