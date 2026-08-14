using Workbench.App.Tests.Support;
using Workbench.Storage.Memory;

namespace Workbench.App.Tests;

public sealed class WorkbenchSmokeLibraryDataTests
{
    [Fact]
    public async Task Smoke_library_shape_exposes_relic_history_and_same_day_cross_category_time_groups()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var folder = new TemporaryDirectory("library-smoke");
        var opened = await context.Services.ProjectOpenService.OpenAsync(folder.Path);
        var library = context.Services.ProjectLibraryEvolutionRepository;
        var compass = await library.CreateObjectAsync(opened.Project.Id, "Design", "清一色罗盘", context.Time.GetUtcNow());
        await library.UpdateOverviewAsync(opened.Project.Id, compass.Id, "清一色罗盘已确定为遗物导航核心；当前只保留读图、定位与视觉语言决策。", 0, context.Time.GetUtcNow());
        await library.AddNodeAsync(opened.Project.Id, compass.Id, new DateOnly(2026, 8, 12), "确认罗盘承担探索方向提示。", [], context.Time.GetUtcNow());
        await library.AddNodeAsync(opened.Project.Id, compass.Id, new DateOnly(2026, 8, 13), "确定清一色配色与刻度表现。", [new("Document", "docs/relics.md", "罗盘设计说明")], context.Time.GetUtcNow().AddMinutes(1));
        await library.AddNodeAsync(opened.Project.Id, compass.Id, new DateOnly(2026, 8, 14), "整合罗盘图标与场景提示。", [], context.Time.GetUtcNow().AddMinutes(2));
        await library.AddNodeAsync(opened.Project.Id, compass.Id, new DateOnly(2026, 8, 14), "补充同日的可读性取舍。", [], context.Time.GetUtcNow().AddMinutes(3));
        var runtime = await library.CreateObjectAsync(opened.Project.Id, "Engineering", "Runtime", context.Time.GetUtcNow());
        await library.AddNodeAsync(opened.Project.Id, runtime.Id, new DateOnly(2026, 8, 14), "Runtime 保持启动路径的最小依赖。", [new("GitCommit", "18f9390", "Library UI baseline")], context.Time.GetUtcNow().AddMinutes(4));

        var category = await library.GetTimelineAsync(opened.Project.Id, compass.Id);
        var time = await library.BrowseNodesByDateAsync(opened.Project.Id);

        Assert.Equal(4, category.Count);
        Assert.Equal(["补充同日的可读性取舍。", "整合罗盘图标与场景提示。", "确定清一色配色与刻度表现。", "确认罗盘承担探索方向提示。"], category.Select(node => node.Content));
        Assert.Equal(3, time.Select(node => node.LocalDate).Distinct().Count());
        Assert.Equal(3, time.Count(node => node.LocalDate == new DateOnly(2026, 8, 14)));
        Assert.Contains(await library.GetMaterialReferencesAsync(opened.Project.Id, category.Single(node => node.LocalDate == new DateOnly(2026, 8, 13)).Id), reference => reference.Label == "罗盘设计说明");
    }
}
