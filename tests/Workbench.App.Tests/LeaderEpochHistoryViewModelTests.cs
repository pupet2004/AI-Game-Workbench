using Workbench.App.ViewModels.Leader;
using Workbench.Storage.Leaders;

namespace Workbench.App.Tests;

public sealed class LeaderEpochHistoryViewModelTests
{
    [Fact]
    public async Task Archived_transcripts_are_not_loaded_until_their_card_is_expanded()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var archived = new StoredLeaderSessionEpoch(
            Guid.NewGuid(), context.ProjectA.Id, "fake-provider", context.AccountId.Value, "model-a",
            Guid.NewGuid(), "old-session", context.ProjectA.RootPath, context.T0, context.T0,
            null, null, null);
        await context.Leaders.CreateCurrentEpochAsync(
            new Workbench.Storage.Leaders.StoredProjectLeader(context.ProjectA.Id, null, context.T0, context.T0), archived);
        await context.Epochs.ArchiveAsync(archived.Id, context.T1, "Manual", "Continue the earlier work.");
        await context.Messages.AppendAsync(archived.Id, "user", "old question", context.T0);

        var history = new LeaderEpochHistoryViewModel(context.ProjectA.Id, context.Epochs, context.Messages);
        await history.InitializeAsync();

        var card = Assert.Single(history.ArchivedEpochs);
        Assert.Empty(card.Messages);
        Assert.False(card.IsExpanded);
        await card.ToggleAsync();
        Assert.True(card.IsExpanded);
        Assert.Equal("old question", Assert.Single(card.Messages).Text);
    }
}
