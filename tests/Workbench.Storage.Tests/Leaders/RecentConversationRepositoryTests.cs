using Workbench.Storage.Leaders;

namespace Workbench.Storage.Tests.Leaders;

public sealed class RecentConversationRepositoryTests
{
    [Fact]
    public async Task Active_handoff_can_be_written_or_cleared_but_archived_handoff_is_frozen()
    {
        await using var context = await LeaderStorageContext.CreateAsync();
        var active = context.CreateEpoch(context.ProjectA.Id);
        var archived = context.CreateEpoch(context.ProjectA.Id) with { EndedAt = context.T1 };
        await context.CreateCurrentEpochAsync(active);
        await context.EnsureLeaderAsync(context.ProjectA.Id);
        await context.Epochs.SaveAsync(archived);

        Assert.Equal("focus", (await context.Epochs.SaveActiveHandoffAsync(context.ProjectA.Id, active.Id, "focus")).HandoffSummary);
        Assert.Null((await context.Epochs.SaveActiveHandoffAsync(context.ProjectA.Id, active.Id, null)).HandoffSummary);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.Epochs.SaveActiveHandoffAsync(context.ProjectA.Id, archived.Id, "late"));
    }

    [Fact]
    public async Task Recent_slice_returns_complete_project_owned_messages_in_ascending_sequence()
    {
        await using var context = await LeaderStorageContext.CreateAsync();
        var epoch = context.CreateEpoch(context.ProjectA.Id);
        await context.CreateCurrentEpochAsync(epoch);
        await context.Messages.AppendAsync(epoch.Id, "user", "A", context.T0);
        await context.Messages.AppendAsync(epoch.Id, "assistant", "B", context.T0);
        await context.Messages.AppendAsync(epoch.Id, "user", "C", context.T0);

        var slice = await context.Messages.GetRecentAsync(context.ProjectA.Id, epoch.Id, null, 2, 2);

        Assert.Equal([2L, 3L], slice.Messages.Select(item => item.Sequence));
        Assert.Equal(1, slice.OmittedMessageCount);
        Assert.Equal(2, slice.Utf8Bytes);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.Messages.GetRecentAsync(context.ProjectB.Id, epoch.Id, null, 2, 2));
    }
}
