using Workbench.App.SingleInstance;

namespace Workbench.App.Tests;

public sealed class SingleInstanceCoordinatorTests
{
    [Fact]
    public async Task Second_launch_forwards_activation_and_exits_without_ownership()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var mutexName = $"AI.Game.Workbench.Tests.{suffix}.mutex";
        var pipeName = $"AI.Game.Workbench.Tests.{suffix}.pipe";
        using var primary = Acquire(mutexName, pipeName);
        var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        primary.Start(() =>
        {
            activated.SetResult();
            return Task.CompletedTask;
        });

        var result = await Task.Run(() =>
        {
            var isPrimary = SingleInstanceCoordinator.TryAcquirePrimary(mutexName, pipeName, out var secondary);
            return (isPrimary, secondary);
        });

        Assert.False(result.isPrimary);
        Assert.Null(result.secondary);
        await activated.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Disposing_primary_releases_ownership_for_next_launch()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var mutexName = $"AI.Game.Workbench.Tests.{suffix}.mutex";
        var pipeName = $"AI.Game.Workbench.Tests.{suffix}.pipe";
        var primary = Acquire(mutexName, pipeName);
        primary.Dispose();

        var isPrimary = SingleInstanceCoordinator.TryAcquirePrimary(mutexName, pipeName, out var replacement);

        Assert.True(isPrimary);
        Assert.NotNull(replacement);
        replacement!.Dispose();
    }

    private static SingleInstanceCoordinator Acquire(string mutexName, string pipeName)
    {
        Assert.True(SingleInstanceCoordinator.TryAcquirePrimary(mutexName, pipeName, out var coordinator));
        return coordinator!;
    }
}
