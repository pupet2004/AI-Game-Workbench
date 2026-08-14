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

    [Fact]
    public async Task Delivered_epoch_keeps_thin_plan_metadata_but_consumes_only_its_selection_rows()
    {
        await using var context = await LeaderStorageContext.CreateAsync();
        var delivered = context.CreateEpoch(context.ProjectA.Id);
        var other = context.CreateEpoch(context.ProjectB.Id);
        await context.CreateCurrentEpochAsync(delivered);
        await context.CreateCurrentEpochAsync(other);
        var repository = new LeaderEpochContinuityRepository(context.Database);
        var deliveredPlan = new LeaderEpochContinuityPlan(delivered.Id, 9000,
        [
            new(0, ContinuityMaterialKind.DailySummary, "daily:2026-08-14", 4000),
            new(1, ContinuityMaterialKind.BrainHandoff, $"handoff:{delivered.Id}", 5000)
        ], context.T0);
        var otherPlan = new LeaderEpochContinuityPlan(other.Id, 1000,
            [new(0, ContinuityMaterialKind.RecentConversation, $"raw:{other.Id}", 1000, "{\"maxMessages\":1}")], context.T0);
        await repository.SaveAsync(context.ProjectA.Id, deliveredPlan);
        await repository.SaveAsync(context.ProjectB.Id, otherPlan);

        Assert.Equal(deliveredPlan.Selections, (await repository.GetAsync(context.ProjectA.Id, delivered.Id))!.Selections);
        await context.Epochs.MarkBootContextDeliveredAsync(delivered.Id, context.T1);
        await context.Epochs.MarkBootContextDeliveredAsync(delivered.Id, context.T0);

        var consumed = await repository.GetAsync(context.ProjectA.Id, delivered.Id);
        Assert.NotNull(consumed);
        Assert.Equal(9000, consumed!.TotalMaxUtf8Bytes);
        Assert.Equal(context.T0, consumed.CreatedAt);
        Assert.Empty(consumed.Selections);
        Assert.Equal(context.T1, (await context.Epochs.GetAsync(delivered.Id))!.BootContextDeliveredAt);
        Assert.Equal(otherPlan.Selections, (await repository.GetAsync(context.ProjectB.Id, other.Id))!.Selections);
    }
}
