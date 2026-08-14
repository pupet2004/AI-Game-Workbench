using Workbench.Core.Memory;
using Workbench.Storage.Memory;
using Workbench.Storage.Settings;
using Workbench.Storage.Leaders;

namespace Workbench.App.Memory;

public sealed class ProjectMemoryApi : IProjectMemoryApi
{
    private readonly DailySummaryRepository _dailySummaries;
    private readonly ProjectMemoryPreferencesRepository _preferences;
    private readonly LeaderSessionEpochRepository _epochs; private readonly LeaderMessageRepository _messages; private readonly ProjectContinuityMaterialService _continuity;
    public ProjectMemoryApi(DailySummaryRepository dailySummaries, ProjectMemoryPreferencesRepository preferences, LeaderSessionEpochRepository epochs, LeaderMessageRepository messages) { _dailySummaries = dailySummaries; _preferences = preferences; _epochs = epochs; _messages = messages; _continuity = new ProjectContinuityMaterialService(this, epochs, messages); }
    public Task<DailySummaryDocument?> GetDailySummaryAsync(Guid projectId, DateOnly localDate, CancellationToken cancellationToken = default) => _dailySummaries.GetAsync(projectId, localDate, cancellationToken);
    public Task<DailySummaryDocument> UpsertDailySummaryAsync(DailySummaryWrite write, DateTimeOffset savedAt, CancellationToken cancellationToken = default) => _dailySummaries.SaveAsync(write, savedAt, cancellationToken);
    public Task<IReadOnlyList<DailySummaryMetadata>> ListDailySummaryMetadataAsync(Guid projectId, DateOnly? from = null, DateOnly? through = null, CancellationToken cancellationToken = default) => _dailySummaries.ListMetadataAsync(projectId, from, through, cancellationToken);
    public Task<ProjectMemoryPreferences> GetPreferencesAsync(Guid projectId, CancellationToken cancellationToken = default) => _preferences.GetAsync(projectId, cancellationToken);
    public Task SavePreferencesAsync(ProjectMemoryPreferences preferences, CancellationToken cancellationToken = default) => _preferences.SaveAsync(preferences, cancellationToken);
    public async Task<string?> GetBrainHandoffAsync(Guid projectId, Guid epochId, CancellationToken cancellationToken = default) { var epoch = await _epochs.GetAsync(epochId, cancellationToken); if (epoch?.ProjectId != projectId) throw new InvalidOperationException("The Leader epoch is not owned by this project."); return epoch.HandoffSummary; }
    public Task<StoredLeaderSessionEpoch> SaveBrainHandoffAsync(Guid projectId, Guid epochId, string? content, CancellationToken cancellationToken = default) => _epochs.SaveActiveHandoffAsync(projectId, epochId, content, cancellationToken);
    public Task<RecentConversationStats> GetRecentConversationStatsAsync(Guid projectId, Guid epochId, CancellationToken cancellationToken = default) => _messages.GetStatsAsync(projectId, epochId, cancellationToken);
    public Task<RecentConversationSlice> ReadRecentConversationAsync(Guid projectId, Guid epochId, long? beforeSequence, int maxMessages, int maxUtf8Bytes, CancellationToken cancellationToken = default) => _messages.GetRecentAsync(projectId, epochId, beforeSequence, maxMessages, maxUtf8Bytes, cancellationToken);
    public Task<ContinuityMaterialCatalog> ListContinuityMaterialsAsync(Guid projectId, Guid sourceEpochId, CancellationToken cancellationToken = default) => _continuity.ListAsync(projectId, sourceEpochId, cancellationToken);
    public Task<ResolvedContinuityBundle> ResolveContinuityAsync(Guid projectId, LeaderEpochContinuityPlan plan, CancellationToken cancellationToken = default) => _continuity.ResolveAsync(projectId, plan, cancellationToken);
}
