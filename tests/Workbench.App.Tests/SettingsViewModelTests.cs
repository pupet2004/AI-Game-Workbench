using Workbench.App.ViewModels;
using Workbench.App.Services;
using Workbench.Core.Leaders;
using Workbench.Storage.Settings;

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

    [Fact]
    public async Task Agent_runtime_settings_are_persisted_without_project_data()
    {
        await using var context = await AppTestContext.CreateAsync();
        var settings = context.CreateSettings();
        await settings.InitializeAsync();

        settings.IsCodexEnabled = true;
        settings.CodexExecutablePath = "C:\\Tools\\codex.exe";
        settings.IsOpenCodeEnabled = true;
        settings.OpenCodeExecutablePath = "C:\\Tools\\opencode.cmd";
        await settings.SaveAgentSettingsCommand.ExecuteAsync(null);

        var recreated = Workbench.App.Services.AppServices.CreateForDatabasePath(context.DatabasePath, context.Time);
        await recreated.InitializeAsync();
        var codex = await recreated.WorkbenchSettingsRepository.GetAgentRuntimeSettingsAsync("codex");
        var openCode = await recreated.WorkbenchSettingsRepository.GetAgentRuntimeSettingsAsync("opencode");
        Assert.True(codex.IsEnabled);
        Assert.Equal("C:\\Tools\\codex.exe", codex.ExecutablePath);
        Assert.True(openCode.IsEnabled);
        Assert.Equal("C:\\Tools\\opencode.cmd", openCode.ExecutablePath);
        await recreated.DisposeAsync();
    }

    [Fact]
    public async Task Language_selection_applies_immediately_and_survives_service_recreation()
    {
        await using var context = await AppTestContext.CreateAsync();
        var localization = new LocalizationService(context.Services.WorkbenchSettingsRepository);
        await localization.InitializeAsync();
        var settings = new SettingsViewModel(
            context.Services.WorkbenchSettingsRepository,
            () => Task.CompletedTask,
            localization);
        await settings.InitializeAsync();

        settings.SelectedLanguage = settings.LanguageOptions.Single(option => option.Language == WorkbenchLanguage.SimplifiedChinese);
        await settings.ApplyLanguageCommand.ExecuteAsync(null);

        Assert.Equal("项目主页", localization["Home.Title"]);
        var recreated = Workbench.App.Services.AppServices.CreateForDatabasePath(context.DatabasePath, context.Time);
        await recreated.InitializeAsync();
        Assert.Equal(WorkbenchLanguage.SimplifiedChinese, await recreated.WorkbenchSettingsRepository.GetWorkbenchLanguageAsync());
        await recreated.DisposeAsync();
        await localization.SetLanguageAsync(WorkbenchLanguage.English);
    }
}
