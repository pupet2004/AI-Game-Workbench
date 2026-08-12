using Workbench.App.ViewModels;
using Workbench.Core.Leaders;

namespace Workbench.App.Tests;

public sealed class SettingsViewModelTests
{
    [Fact]
    public async Task Global_rotation_setting_can_be_changed_and_survives_service_recreation()
    {
        await using var context = await AppTestContext.CreateAsync();
        var settings = context.CreateSettings();
        await settings.InitializeAsync();

        await settings.SetLeaderSessionRotationPolicyAsync(LeaderSessionRotationPolicy.ManualOnly);

        var recreated = Workbench.App.Services.AppServices.CreateForDatabasePath(context.DatabasePath, context.Time);
        await recreated.InitializeAsync();
        Assert.Equal(LeaderSessionRotationPolicy.ManualOnly, await recreated.WorkbenchSettingsRepository.GetLeaderSessionRotationPolicyAsync());
    }

    [Fact]
    public async Task Project_rotation_override_can_be_changed_and_return_to_inherit_global()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var folder = new Support.TemporaryDirectory();
        var workspace = context.CreateWorkspace(await context.Services.ProjectOpenService.OpenAsync(folder.Path));
        await workspace.LibraryPane.InitializeAsync();

        await workspace.LibraryPane.SetLeaderSessionRotationPolicyOverrideAsync(LeaderSessionRotationPolicy.Ask);
        Assert.Equal(LeaderSessionRotationPolicy.Ask, workspace.LibraryPane.RotationPolicyOverride);

        await workspace.LibraryPane.SetLeaderSessionRotationPolicyOverrideAsync(null);
        Assert.Null(workspace.LibraryPane.RotationPolicyOverride);
    }
}
