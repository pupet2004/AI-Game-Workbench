using Workbench.App.Leader;
using Workbench.Core.Leaders;
using Workbench.Storage.Leaders;

namespace Workbench.App.Tests;

public sealed class LeaderSessionRotationStateServiceTests
{
    [Fact]
    public async Task Due_auto_policy_exposes_next_send_rotation_state_without_creating_an_epoch()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var folder = new Support.TemporaryDirectory();
        var project = (await context.Services.ProjectOpenService.OpenAsync(folder.Path)).Project;
        await context.Services.WorkbenchSettingsRepository.SaveLeaderSessionRotationPolicyAsync(LeaderSessionRotationPolicy.Auto);
        var epoch = CreateEpoch(project.Id, "2026-08-12T22:00:00+00:00");
        await context.Services.ProjectLeaderRepository.CreateCurrentEpochAsync(
            new StoredProjectLeader(project.Id, null, epoch.StartedAt, epoch.StartedAt), epoch);
        context.Time.SetUtcNow(DateTimeOffset.Parse("2026-08-13T09:00:00+00:00"));
        var service = context.CreateRotationStateService(TimeZoneInfo.Utc);

        var state = await service.GetAsync(project.Id);

        Assert.True(state.Evaluation.IsDue);
        Assert.Equal(LeaderSessionRotationPolicy.Auto, state.EffectivePolicy);
        Assert.Equal(epoch.Id, (await context.Services.ProjectLeaderRepository.GetAsync(project.Id))!.CurrentEpochId);
        Assert.Single(await context.Services.LeaderSessionEpochRepository.GetAllForProjectAsync(project.Id));
    }

    [Fact]
    public async Task Project_override_wins_and_can_return_to_global()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var folder = new Support.TemporaryDirectory();
        var projectId = (await context.Services.ProjectOpenService.OpenAsync(folder.Path)).Project.Id;
        await context.Services.WorkbenchSettingsRepository.SaveLeaderSessionRotationPolicyAsync(LeaderSessionRotationPolicy.ManualOnly);
        await context.Services.ProjectSettingsRepository.SaveLeaderSessionRotationPolicyOverrideAsync(projectId, LeaderSessionRotationPolicy.Auto);
        var service = context.CreateRotationStateService(TimeZoneInfo.Utc);

        Assert.Equal(LeaderSessionRotationPolicy.Auto, (await service.GetAsync(projectId)).EffectivePolicy);

        await context.Services.ProjectSettingsRepository.SaveLeaderSessionRotationPolicyOverrideAsync(projectId, null);

        Assert.Equal(LeaderSessionRotationPolicy.ManualOnly, (await service.GetAsync(projectId)).EffectivePolicy);
    }

    private static StoredLeaderSessionEpoch CreateEpoch(Guid projectId, string lastActiveAt)
    {
        var lastActive = DateTimeOffset.Parse(lastActiveAt);
        return new StoredLeaderSessionEpoch(Guid.NewGuid(), projectId, "provider", Guid.NewGuid(), "model", Guid.NewGuid(), "external", "C:/Project", lastActive, lastActive, null, null, null);
    }
}
