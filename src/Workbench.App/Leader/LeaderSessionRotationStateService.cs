using Workbench.Core.Leaders;
using Workbench.Storage.Leaders;
using Workbench.Storage.Settings;

namespace Workbench.App.Leader;

public sealed class LeaderSessionRotationStateService
{
    private readonly WorkbenchSettingsRepository _workbenchSettings;
    private readonly ProjectSettingsRepository _projectSettings;
    private readonly LeaderSessionEpochRepository _epochs;
    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _timeZone;

    public LeaderSessionRotationStateService(
        WorkbenchSettingsRepository workbenchSettings,
        ProjectSettingsRepository projectSettings,
        LeaderSessionEpochRepository epochs,
        TimeProvider timeProvider,
        TimeZoneInfo? timeZone = null)
    {
        _workbenchSettings = workbenchSettings ?? throw new ArgumentNullException(nameof(workbenchSettings));
        _projectSettings = projectSettings ?? throw new ArgumentNullException(nameof(projectSettings));
        _epochs = epochs ?? throw new ArgumentNullException(nameof(epochs));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _timeZone = timeZone ?? TimeZoneInfo.Local;
    }

    public async Task<LeaderSessionRotationState> GetAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var globalPolicyTask = _workbenchSettings.GetLeaderSessionRotationPolicyAsync(cancellationToken);
        var projectOverrideTask = _projectSettings.GetLeaderSessionRotationPolicyOverrideAsync(projectId, cancellationToken);
        var epochTask = _epochs.GetCurrentForProjectAsync(projectId, cancellationToken);
        await Task.WhenAll(globalPolicyTask, projectOverrideTask, epochTask);

        var effectivePolicy = LeaderSessionRotationPolicyResolver.Resolve(globalPolicyTask.Result, projectOverrideTask.Result);
        var epoch = epochTask.Result;
        var evaluation = LeaderSessionRotationEvaluator.Evaluate(
            effectivePolicy,
            epoch?.LastActiveAt,
            epoch?.EndedAt,
            _timeProvider.GetUtcNow(),
            _timeZone);
        return new LeaderSessionRotationState(effectivePolicy, projectOverrideTask.Result, evaluation);
    }
}
