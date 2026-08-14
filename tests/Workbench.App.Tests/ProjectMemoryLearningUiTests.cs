using Workbench.App.Leader;
using Workbench.App.ViewModels;
using Workbench.App.ViewModels.Panes;
using Workbench.App.Tests.Support;
using Workbench.Core.Layout;
using Workbench.Project.Git;
using Workbench.Project.Opening;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Registry;
using Workbench.Storage.Memory;

namespace Workbench.App.Tests;

public sealed class ProjectMemoryLearningUiTests
{
    [Fact]
    public async Task Current_workspace_does_not_expose_legacy_memory_items()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var folder = new TemporaryDirectory("legacy-memory-freeze");
        var opened = await context.Services.ProjectOpenService.OpenAsync(folder.Path);
        await context.Services.ProjectMemoryService.CreateCandidateAsync(
            opened.Project.Id, "Legacy", "must remain compatibility-only", [new("Manual", "test")]);
        var main = new MainWindowViewModel(
            context.Services, new TestFolderPickerService(folder.Path), context.LeaderSessions);
        await main.InitializeAsync();
        await ((HomeViewModel)main.CurrentPage).OpenProjectFolderAsync();
        var workspace = Assert.IsType<WorkspaceViewModel>(main.CurrentPage);

        await workspace.LibraryPane.LoadMemoryAsync();

        Assert.Empty(workspace.LibraryPane.PendingCandidates);
        Assert.Single(await context.Services.ProjectMemoryService.GetPendingCandidatesAsync(opened.Project.Id));
    }

    [Fact]
    public async Task Library_browse_restores_submissions_and_filters_by_category_after_reconstruction()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var folder = new TemporaryDirectory("library");
        var opened = await context.Services.ProjectOpenService.OpenAsync(folder.Path);
        var at = DateTimeOffset.Parse("2026-08-13T09:00:00.0000000+00:00");
        await context.Services.ProjectLibraryRepository.SubmitAsync(new LibrarySubmission(Guid.NewGuid(), opened.Project.Id, Guid.NewGuid(), null, "Design", "Relics", "Tea cup rule.", "docs/relics.md", at));
        await context.Services.ProjectLibraryRepository.SubmitAsync(new LibrarySubmission(Guid.NewGuid(), opened.Project.Id, Guid.NewGuid(), null, "Implementation", "Relics", "Code note.", "commit abc123", at.AddMinutes(1)));

        var first = new LibraryPaneViewModel(opened, () => Task.CompletedTask, library: context.Services.ProjectLibraryRepository);
        await first.InitializeAsync();
        Assert.Equal(2, first.LibraryEntries.Count);
        var restored = new LibraryPaneViewModel(opened, () => Task.CompletedTask, library: context.Services.ProjectLibraryRepository) { LibraryCategoryFilter = "Design" };
        await restored.LoadLibraryAsync();

        var entry = Assert.Single(restored.LibraryEntries);
        Assert.Equal("Design", entry.Category);
        Assert.Equal("Tea cup rule.", entry.Summary);
        Assert.Equal("docs/relics.md", entry.SourceReference);
    }

    [Fact]
    public async Task Library_shows_pending_learning_then_separate_ai_learned_and_session_candidate_source()
    {
        await using var context = await CoordinatorContext.CreateAsync();
        var result = Result(context);
        var library = new LibraryPaneViewModel(
            result, () => Task.CompletedTask,
            memory: context.Services.ProjectMemoryService,
            synthesisJobs: context.Jobs,
            epochRepository: context.Epochs);
        await library.InitializeAsync();
        await library.LoadMemoryAsync();
        Assert.Equal("Memory learning: 1 session pending", library.MemoryLearningStatus);

        context.Runtime.QueueTurn(context.Completed("""
            {"learned":[{"topic":"Phase","content":"M1.5B","source_message_sequences":[1]}],"candidates":[{"topic":"Rule","content":"Workbench owns memory.","source_message_sequences":[1]}]}
            """));
        await context.Coordinator.TryProcessNextAsync(context.Project.Id);
        await library.LoadMemoryAsync();
        library.SelectCandidateCommand.Execute(Assert.Single(library.PendingCandidates));

        Assert.Equal("Memory learning: Up to date", library.MemoryLearningStatus);
        Assert.Equal("M1.5B", Assert.Single(library.LearnedMemories).Content);
        Assert.Contains("AI-generated", library.LearnedMemoryLabel, StringComparison.Ordinal);
        Assert.Equal("From Leader session · Aug 13", library.SelectedCandidateSourceLabel);
        Assert.Empty(library.FormalMemories);
        library.ShowProjectCommand.Execute(null);
    }

    [Fact]
    public async Task Running_job_is_presented_as_learning()
    {
        await using var context = await CoordinatorContext.CreateAsync();
        await context.Jobs.ClaimNextPendingAsync(context.Project.Id);
        var library = new LibraryPaneViewModel(
            Result(context), () => Task.CompletedTask,
            memory: context.Services.ProjectMemoryService,
            synthesisJobs: context.Jobs,
            epochRepository: context.Epochs);

        await library.InitializeAsync();
        await library.LoadMemoryAsync();

        Assert.Equal("Learning...", library.MemoryLearningStatus);
    }

    [Fact]
    public async Task Project_open_completed_turn_and_rollover_do_not_schedule_legacy_synthesis()
    {
        await using var appContext = await AppTestContext.CreateAsync();
        using var projectFolder = new TemporaryDirectory("open-trigger");
        var main = new MainWindowViewModel(
            appContext.Services,
            new TestFolderPickerService(projectFolder.Path),
            appContext.LeaderSessions);
        await main.InitializeAsync();
        await ((HomeViewModel)main.CurrentPage).OpenProjectFolderAsync();

        await using var leaderContext = await PersistentLeaderContext.CreateAsync();
        var runtime = leaderContext.CreateRuntime();
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        var pane = new LeaderPaneViewModel(
            leaderContext.ProjectA, registry, leaderContext.CreateManager(), () => Task.CompletedTask,
            rolloverService: leaderContext.CreateRolloverService(registry));
        runtime.QueueTurn(leaderContext.Completed("first answer"));
        await pane.InitializeAsync();
        pane.DraftMessage = "first question";
        await pane.SendAsync();
        await pane.StartNewBrainAsync();
    }

    [Fact]
    public async Task App_scheduler_returns_while_synthesis_is_still_running()
    {
        await using var context = await CoordinatorContext.CreateAsync();
        context.Runtime.PauseBeforeEvents = true;
        context.Runtime.QueueTurn(context.Completed("""
            {"learned":[],"candidates":[]}
            """));

        context.Services.ScheduleMemorySynthesis(context.Project.Id);
        await context.Runtime.WaitForSendAsync();

        Assert.Equal(ProjectMemorySynthesisJobStatus.Running, (await context.Jobs.GetAsync(context.ArchivedEpoch.Id))!.Status);
        context.Runtime.ReleaseSend();
    }

    [Fact]
    public void Library_markup_does_not_expose_legacy_memory_as_a_primary_surface()
    {
        var root = FindRepositoryRoot();
        var markup = File.ReadAllText(Path.Combine(root, "src", "Workbench.App", "Views", "Panes", "LibraryPaneView.axaml"));

        Assert.DoesNotContain("Project Memory", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Learned Memory", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Formal Memory", markup, StringComparison.Ordinal);
    }

    private static ProjectOpenResult Result(CoordinatorContext context) =>
        new(
            context.Project,
            ProjectLayout.CreateDefault(context.Project.Id),
            new GitSnapshot(true, false, null, null, null, false, false, null));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AI.Game.Workbench.sln")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
