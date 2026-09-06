using Workbench.App.Tests.Support;
using Workbench.App.ViewModels.Panes;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Registry;
using Workbench.Storage.Memory;

namespace Workbench.App.Tests;

public sealed class ProjectLibraryProposalUiTests
{
    private static readonly DateOnly Day = new(2026, 8, 14);

    [Fact]
    public async Task Explicit_leader_proposal_is_visible_but_preview_writes_no_library()
    {
        var runtime = new FakeAgentRuntime();
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        await using var context = await AppTestContext.CreateAsync(runtimeRegistry: registry);
        var workspace = await context.CreateWorkspaceForNewProjectAsync();
        runtime.QueueTurn(Completed("""
            {"response":"The relic stage is closed; review this Library proposal.","draft_proposal":null,"memory_commands":{"daily_summary":null,"library_proposal":{"action":"CreateNode","target_object_id":null,"target_node_id":null,"expected_node_revision":null,"expected_overview_revision":0,"category":"Design","topic":"Relics","local_date":"2026-08-14","node_content":"The relic mechanism is implemented.","current_overview":"Current relic state.","materials":[{"kind":"Document","reference":"docs/relics.md","label":"Design notes"}]}}}
            """));
        await workspace.LeaderPane.InitializeAsync();
        await workspace.LibraryPane.InitializeAsync();
        workspace.LeaderPane.DraftMessage = "Close this stage.";

        await workspace.LeaderPane.SendAsync();

        var proposal = Assert.Single(await context.Services.ProjectMemoryApi.GetPendingLibraryProposalsAsync(workspace.Result.Project.Id));
        Assert.Equal(workspace.LeaderPane.Session!.Id.Value, proposal.SourceSessionId);
        Assert.Equal(proposal.Id, Assert.Single(workspace.LibraryPane.PendingLibraryProposals).Id);
        Assert.Empty(await context.Services.ProjectLibraryEvolutionRepository.ListObjectsAsync(workspace.Result.Project.Id));
        Assert.Empty(workspace.LibraryPane.CategoryGroups);
        Assert.Empty(workspace.LibraryPane.TimeGroups);
        Assert.Equal("The relic stage is closed; review this Library proposal.", workspace.LeaderPane.Messages.Last().Text);
        Assert.Equal(2, (await context.Services.LeaderMessageRepository.GetAllAsync(workspace.LeaderPane.SessionEpochId!.Value)).Count);
    }

    [Fact]
    public async Task Reject_removes_pending_review_without_archive_or_memory_side_effects()
    {
        await using var context = await AppTestContext.CreateAsync();
        var workspace = await context.CreateWorkspaceForNewProjectAsync();
        var created = await context.Services.ProjectMemoryApi.CreateLibraryProposalAsync(Draft(workspace.Result.Project.Id));
        await workspace.LibraryPane.InitializeAsync();
        workspace.LibraryPane.SelectedLibraryProposal = created;

        await workspace.LibraryPane.RejectLibraryProposalAsync();

        Assert.Empty(workspace.LibraryPane.PendingLibraryProposals);
        Assert.Empty(await context.Services.ProjectLibraryEvolutionRepository.ListObjectsAsync(workspace.Result.Project.Id));
        Assert.Equal(LibraryProposalStatus.Rejected,
            (await context.Services.ProjectMemoryApi.GetLibraryProposalAsync(workspace.Result.Project.Id, created.Id))!.Status);
        Assert.Empty(await context.Services.DailySummaryRepository.ListAsync(workspace.Result.Project.Id, Day, Day));
        var synthesis = await context.Services.ProjectMemorySynthesisRepository.GetStatusAsync(workspace.Result.Project.Id);
        Assert.Equal(0, synthesis.PendingCount + synthesis.RunningCount);
        Assert.Empty(await context.Services.LeaderSessionEpochRepository.GetAllForProjectAsync(workspace.Result.Project.Id));
    }

    [Fact]
    public async Task Accept_with_one_pending_proposal_auto_selects_and_does_not_crash()
    {
        await using var context = await AppTestContext.CreateAsync();
        var workspace = await context.CreateWorkspaceForNewProjectAsync();
        var created = await context.Services.ProjectMemoryApi.CreateLibraryProposalAsync(Draft(workspace.Result.Project.Id));
        await workspace.LibraryPane.InitializeAsync();

        workspace.LibraryPane.SelectedLibraryProposal = null;
        await workspace.LibraryPane.AcceptLibraryProposalAsync();

        Assert.Equal(LibraryProposalStatus.Accepted,
            (await context.Services.ProjectMemoryApi.GetLibraryProposalAsync(workspace.Result.Project.Id, created.Id))!.Status);
        Assert.Empty(workspace.LibraryPane.PendingLibraryProposals);
    }

    [Fact]
    public async Task Edit_and_accept_immediately_refreshes_category_time_overview_timeline_and_materials()
    {
        await using var context = await AppTestContext.CreateAsync();
        var workspace = await context.CreateWorkspaceForNewProjectAsync();
        using var otherFolder = new TemporaryDirectory("proposal-other");
        var other = await context.Services.ProjectOpenService.OpenAsync(otherFolder.Path);
        var created = await context.Services.ProjectMemoryApi.CreateLibraryProposalAsync(Draft(workspace.Result.Project.Id));
        await workspace.LibraryPane.InitializeAsync();
        workspace.LibraryPane.SelectedLibraryProposal = created;
        workspace.LibraryPane.ProposalEditContent = "User-confirmed factual state.";
        workspace.LibraryPane.ProposalEditOverview = "User-confirmed current overview.";

        await workspace.LibraryPane.EditAndAcceptLibraryProposalAsync();

        Assert.Empty(workspace.LibraryPane.PendingLibraryProposals);
        var libraryObject = Assert.Single(workspace.LibraryPane.LibraryObjects);
        Assert.Equal("Design", Assert.Single(workspace.LibraryPane.CategoryGroups).Category);
        var time = Assert.Single(workspace.LibraryPane.TimeGroups);
        Assert.Equal(Day, time.LocalDate);
        Assert.Equal("User-confirmed factual state.", Assert.Single(time.Nodes).Content);
        Assert.Equal("docs/relics.md", Assert.Single(time.Nodes.Single().Materials).Reference);
        await workspace.LibraryPane.SelectLibraryObjectAsync(libraryObject.Id);
        Assert.Equal("User-confirmed current overview.", workspace.LibraryPane.CurrentOverviewText);
        Assert.Equal("User-confirmed factual state.", Assert.Single(workspace.LibraryPane.ObjectTimeline).Content);
        Assert.Empty(await context.Services.ProjectLibraryEvolutionRepository.ListObjectsAsync(other.Project.Id));
    }

    [Fact]
    public async Task Daily_summary_write_does_not_create_a_library_proposal()
    {
        await using var context = await AppTestContext.CreateAsync();
        var workspace = await context.CreateWorkspaceForNewProjectAsync();

        await context.Services.ProjectMemoryApi.UpsertDailySummaryAsync(
            new(workspace.Result.Project.Id, Day, "Why and tradeoffs stay here.", null, []),
            context.Time.GetUtcNow());

        Assert.Empty(await context.Services.ProjectMemoryApi.GetPendingLibraryProposalsAsync(workspace.Result.Project.Id));
        Assert.Empty(await context.Services.ProjectLibraryEvolutionRepository.ListObjectsAsync(workspace.Result.Project.Id));
    }

    [Fact]
    public void Library_markup_exposes_pending_review_and_explicit_confirmation_actions()
    {
        var markup = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Workbench.App", "Views", "Panes", "LibraryPaneView.axaml"));

        Assert.Contains("[Library.PendingProposal]", markup, StringComparison.Ordinal);
        Assert.Contains("[Library.Accept]", markup, StringComparison.Ordinal);
        Assert.Contains("[Library.EditAccept]", markup, StringComparison.Ordinal);
        Assert.Contains("[Library.Reject]", markup, StringComparison.Ordinal);
        Assert.Contains("ProposalEditContent", markup, StringComparison.Ordinal);
        Assert.Contains("Materials", markup, StringComparison.Ordinal);
        Assert.Contains("LibraryProposalStatusMessage", markup, StringComparison.Ordinal);
    }

    private static ProjectLibraryProposalDraft Draft(Guid projectId) => new(
        Guid.NewGuid(),
        projectId,
        Guid.NewGuid(),
        LibraryProposalAction.CreateNode,
        null,
        null,
        null,
        0,
        "Design",
        "Relics",
        Day,
        "Proposed factual state.",
        "Proposed current overview.",
        [new("Document", "docs/relics.md", "Design notes")],
        DateTimeOffset.Parse("2026-08-14T08:00:00+00:00"));

    private static AgentTurnCompleted Completed(string text) =>
        new(new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed, text, null), DateTimeOffset.UtcNow);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AI.Game.Workbench.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
