using Workbench.Core.Memory;
using Workbench.Storage.Memory;
using Workbench.Storage.Settings;

namespace Workbench.App.Memory;

public sealed class ProjectMemoryApi : IProjectMemoryApi
{
    private readonly DailySummaryRepository _dailySummaries;
    private readonly ProjectMemoryPreferencesRepository _preferences;
    public ProjectMemoryApi(DailySummaryRepository dailySummaries, ProjectMemoryPreferencesRepository preferences) { _dailySummaries = dailySummaries; _preferences = preferences; }
    public Task<DailySummaryDocument?> GetDailySummaryAsync(Guid projectId, DateOnly localDate, CancellationToken cancellationToken = default) => _dailySummaries.GetAsync(projectId, localDate, cancellationToken);
    public Task<DailySummaryDocument> UpsertDailySummaryAsync(DailySummaryWrite write, DateTimeOffset savedAt, CancellationToken cancellationToken = default) => _dailySummaries.SaveAsync(write, savedAt, cancellationToken);
    public Task<IReadOnlyList<DailySummaryMetadata>> ListDailySummaryMetadataAsync(Guid projectId, DateOnly? from = null, DateOnly? through = null, CancellationToken cancellationToken = default) => _dailySummaries.ListMetadataAsync(projectId, from, through, cancellationToken);
    public Task<ProjectMemoryPreferences> GetPreferencesAsync(Guid projectId, CancellationToken cancellationToken = default) => _preferences.GetAsync(projectId, cancellationToken);
    public Task SavePreferencesAsync(ProjectMemoryPreferences preferences, CancellationToken cancellationToken = default) => _preferences.SaveAsync(preferences, cancellationToken);
}
