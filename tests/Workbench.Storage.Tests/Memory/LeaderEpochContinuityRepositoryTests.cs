using Workbench.Storage.Memory;
using Workbench.Storage.Tests.Leaders;

namespace Workbench.Storage.Tests.Memory;

public sealed class LeaderEpochContinuityRepositoryTests
{
    [Fact]
    public async Task Plan_round_trips_in_order_and_is_owned_by_the_project_epoch()
    {
        await using var context = await LeaderStorageContext.CreateAsync();
        var epoch = context.CreateEpoch(context.ProjectA.Id); await context.CreateCurrentEpochAsync(epoch);
        var plan = new LeaderEpochContinuityPlan(epoch.Id, 9000, [new(0, ContinuityMaterialKind.DailySummary, "daily:2026-08-14", 4000), new(1, ContinuityMaterialKind.RecentConversation, $"raw:{epoch.Id}", 5000, "{\"maxMessages\":8}")], context.T0);
        var repository = new LeaderEpochContinuityRepository(context.Database);

        await repository.SaveAsync(context.ProjectA.Id, plan);

        var restored = await repository.GetAsync(context.ProjectA.Id, epoch.Id);
        Assert.NotNull(restored);
        Assert.Equal(plan.EpochId, restored.EpochId);
        Assert.Equal(plan.TotalMaxUtf8Bytes, restored.TotalMaxUtf8Bytes);
        Assert.Equal(plan.Selections, restored.Selections);
        Assert.Equal(plan.CreatedAt, restored.CreatedAt);
        Assert.Null(await repository.GetAsync(context.ProjectB.Id, epoch.Id));
    }
}
