using Workbench.App.ViewModels.Panes;
using Workbench.App.Tests.Support;
using Workbench.Storage.Memory;

namespace Workbench.App.Tests;

public sealed class LibrarySearchCoverageTests
{
    [Fact]
    public async Task Timeline_node_content_is_searchable_and_returns_owning_library_object_once()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var folder = new TemporaryDirectory("library-search-node");
        var opened = await context.Services.ProjectOpenService.OpenAsync(folder.Path);
        var library = context.Services.ProjectLibraryEvolutionRepository;
        var libraryObject = await library.CreateObjectAsync(opened.Project.Id, "Chapter Delivery", "Chapter 1 交付与未接受生活化设定", context.Time.GetUtcNow());
        var node = await library.AddNodeAsync(
            opened.Project.Id,
            libraryObject.Id,
            new DateOnly(2026, 9, 5),
            "时间礼仪课与时间层通勤。",
            [],
            context.Time.GetUtcNow());

        var pane = new LibraryPaneViewModel(opened, () => Task.CompletedTask, evolutionLibrary: library);
        await pane.InitializeAsync();
        pane.LibraryTextFilter = "时间礼仪课";

        await pane.SearchLibraryCommand.ExecuteAsync(null);

        var result = Assert.Single(pane.SearchResults);
        Assert.Equal(libraryObject.Topic, result.Topic);
        Assert.Contains("时间礼仪课", result.Text, StringComparison.Ordinal);
        Assert.Contains(node.Id.ToString(), result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Material_reference_and_label_are_searchable_without_duplicate_library_results()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var folder = new TemporaryDirectory("library-search-material");
        var opened = await context.Services.ProjectOpenService.OpenAsync(folder.Path);
        var library = context.Services.ProjectLibraryEvolutionRepository;
        var libraryObject = await library.CreateObjectAsync(opened.Project.Id, "Chapter Delivery", "Chapter 1 交付与未接受生活化设定", context.Time.GetUtcNow());
        await library.AddNodeAsync(
            opened.Project.Id,
            libraryObject.Id,
            new DateOnly(2026, 9, 5),
            "交付正文。",
            [new("Artifact", "chapter-01.md", "Chapter 1 manuscript")],
            context.Time.GetUtcNow());

        var pane = new LibraryPaneViewModel(opened, () => Task.CompletedTask, evolutionLibrary: library);
        await pane.InitializeAsync();

        pane.LibraryTextFilter = "chapter-01.md";
        await pane.SearchLibraryCommand.ExecuteAsync(null);
        var referenceResult = Assert.Single(pane.SearchResults);
        Assert.Equal(libraryObject.Topic, referenceResult.Topic);
        Assert.Contains("chapter-01.md", referenceResult.Text, StringComparison.Ordinal);

        pane.LibraryTextFilter = "Chapter 1 manuscript";
        await pane.SearchLibraryCommand.ExecuteAsync(null);
        var labelResult = Assert.Single(pane.SearchResults);
        Assert.Equal(libraryObject.Topic, labelResult.Topic);
        Assert.Contains("Chapter 1 manuscript", labelResult.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Topic_and_current_overview_search_remain_working_and_search_is_read_only()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var folder = new TemporaryDirectory("library-search-existing-fields");
        var opened = await context.Services.ProjectOpenService.OpenAsync(folder.Path);
        var library = context.Services.ProjectLibraryEvolutionRepository;
        var libraryObject = await library.CreateObjectAsync(opened.Project.Id, "Design", "Topic marker", context.Time.GetUtcNow());
        await library.UpdateOverviewAsync(opened.Project.Id, libraryObject.Id, "Overview marker", 0, context.Time.GetUtcNow());
        var node = await library.AddNodeAsync(opened.Project.Id, libraryObject.Id, new DateOnly(2026, 9, 5), "Stable content", [], context.Time.GetUtcNow());
        var objectBefore = await library.GetObjectAsync(opened.Project.Id, libraryObject.Id);
        var nodeBefore = await library.GetNodeAsync(opened.Project.Id, node.Id);
        var materialsBefore = await library.GetMaterialReferencesAsync(opened.Project.Id, node.Id);

        var pane = new LibraryPaneViewModel(opened, () => Task.CompletedTask, evolutionLibrary: library);
        await pane.InitializeAsync();

        pane.LibraryTextFilter = "Topic marker";
        await pane.SearchLibraryCommand.ExecuteAsync(null);
        Assert.Single(pane.SearchResults);

        pane.LibraryTextFilter = "Overview marker";
        await pane.SearchLibraryCommand.ExecuteAsync(null);
        Assert.Single(pane.SearchResults);

        var objectAfter = await library.GetObjectAsync(opened.Project.Id, libraryObject.Id);
        var nodeAfter = await library.GetNodeAsync(opened.Project.Id, node.Id);
        var materialsAfter = await library.GetMaterialReferencesAsync(opened.Project.Id, node.Id);
        Assert.Equal(objectBefore, objectAfter);
        Assert.Equal(nodeBefore, nodeAfter);
        Assert.Equal(materialsBefore, materialsAfter);
    }
}
