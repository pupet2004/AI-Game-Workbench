using Workbench.App.Memory;
using Workbench.Storage.Leaders;
using Workbench.Storage.Memory;

namespace Workbench.App.Tests;

public sealed class ProjectContinuityMaterialServiceTests
{
    [Fact]
    public async Task Invalid_explicit_selection_is_reported_without_substituting_material()
    {
        await using var context = await AppTestContext.CreateAsync();
        var reference = $"raw:{Guid.NewGuid()}";
        var plan = new LeaderEpochContinuityPlan(Guid.NewGuid(), 1000,
            [new(0, ContinuityMaterialKind.RecentConversation, reference, 1000, "{")], context.Time.GetUtcNow());

        var resolved = await context.Services.ProjectMemoryApi.ResolveContinuityAsync(Guid.NewGuid(), plan);

        Assert.Empty(resolved.Materials);
        Assert.Equal([reference], resolved.OmittedReferences);
    }

    [Fact]
    public async Task Catalog_is_metadata_only_and_explicit_selection_reads_daily_handoff_and_raw_without_side_effects()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var directory = new Support.TemporaryDirectory();
        var project = (await context.Services.ProjectOpenService.OpenAsync(directory.Path)).Project;
        var now = context.Time.GetUtcNow();
        var epoch = new StoredLeaderSessionEpoch(Guid.NewGuid(), project.Id, "codex", Guid.NewGuid(), "model", Guid.NewGuid(), "thread", project.RootPath, now, now, null, null, null, null);
        await context.Services.ProjectLeaderRepository.CreateCurrentEpochAsync(new(project.Id, null, now, now), epoch);
        await context.Services.ProjectMemoryApi.UpsertDailySummaryAsync(new(project.Id, new DateOnly(2026, 8, 14), "DAILY BODY", null, []), now);
        await context.Services.ProjectMemoryApi.SaveBrainHandoffAsync(project.Id, epoch.Id, "HANDOFF BODY");
        await context.Services.LeaderMessageRepository.AppendAsync(epoch.Id, "user", "RAW EARLY", now);
        await context.Services.LeaderMessageRepository.AppendAsync(epoch.Id, "assistant", "RAW LATE", now);

        var catalog = await context.Services.ProjectMemoryApi.ListContinuityMaterialsAsync(project.Id, epoch.Id);
        var plan = new LeaderEpochContinuityPlan(epoch.Id, 1000, [
            new(0, ContinuityMaterialKind.DailySummary, "daily:2026-08-14", 1000),
            new(1, ContinuityMaterialKind.BrainHandoff, $"handoff:{epoch.Id}", 1000),
            new(2, ContinuityMaterialKind.RecentConversation, $"raw:{epoch.Id}", 1000, "{\"beforeSequence\":2,\"maxMessages\":1,\"maxUtf8Bytes\":1000}")], now);
        var resolved = await context.Services.ProjectMemoryApi.ResolveContinuityAsync(project.Id, plan);

        Assert.Equal(["daily:2026-08-14", $"handoff:{epoch.Id}", $"raw:{epoch.Id}"], catalog.Materials.Select(item => item.Reference));
        Assert.All(catalog.Materials, item => Assert.DoesNotContain("BODY", item.Label, StringComparison.Ordinal));
        Assert.Equal(10, catalog.Materials.Single(item => item.Reference == "daily:2026-08-14").Utf8Bytes);
        Assert.Equal(["DAILY BODY", "HANDOFF BODY", "RAW EARLY"], resolved.Materials.Select(item => item.Content));
        var invalid = await context.Services.ProjectMemoryApi.ResolveContinuityAsync(Guid.NewGuid(), plan);
        Assert.Empty(invalid.Materials);
        Assert.Equal(["daily:2026-08-14", $"handoff:{epoch.Id}", $"raw:{epoch.Id}"], invalid.OmittedReferences);
    }
}
