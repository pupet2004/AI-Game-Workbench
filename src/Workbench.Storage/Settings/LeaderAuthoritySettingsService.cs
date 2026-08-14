using Workbench.Core.Leaders;

namespace Workbench.Storage.Settings;

public sealed class LeaderAuthoritySettingsService(WorkbenchSettingsRepository workbenchSettings, ProjectSettingsRepository projectSettings)
{
    public async Task<LeaderAuthorityMode> GetEffectiveLeaderAuthorityModeAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var global = workbenchSettings.GetLeaderAuthorityModeAsync(cancellationToken);
        var project = projectSettings.GetLeaderAuthorityModeOverrideAsync(projectId, cancellationToken);
        await Task.WhenAll(global, project);
        return LeaderAuthorityModeResolver.Resolve(global.Result, project.Result);
    }
}
