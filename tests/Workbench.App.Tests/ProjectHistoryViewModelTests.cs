using Workbench.App.ProjectWorld;
using Workbench.App.Tests.Support;

namespace Workbench.App.Tests;

public sealed class ProjectHistoryViewModelTests
{
    [Fact]
    public async Task History_projects_the_existing_evolution_index_as_read_only_entries()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var folder = new TemporaryDirectory();
        var opened = await context.Services.ProjectOpenService.OpenAsync(folder.Path);
        await context.Services.ProjectLibraryEvolutionRepository.CreateObjectAsync(
            opened.Project.Id,
            "Chapter",
            "Chapter 1",
            context.Time.GetUtcNow());

        var history = new ProjectHistoryViewModel(context.Services, opened, () => Task.CompletedTask);
        await history.InitializeAsync();

        var item = Assert.Single(history.Entries);
        Assert.Equal("Library", item.CategoryText);
        Assert.Contains("Chapter 1", item.Summary, StringComparison.Ordinal);
        Assert.Equal("Records: 1", history.EntryCountText);
    }
}
