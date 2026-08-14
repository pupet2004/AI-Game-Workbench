using Workbench.Core.Memory;
using Workbench.Storage.Memory;

namespace Workbench.App.Memory;

public interface IProjectMemoryApi
{
    Task<DailySummaryDocument?> GetDailySummaryAsync(Guid projectId, DateOnly localDate, CancellationToken cancellationToken = default);
    Task<DailySummaryDocument> UpsertDailySummaryAsync(DailySummaryWrite write, DateTimeOffset savedAt, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DailySummaryMetadata>> ListDailySummaryMetadataAsync(Guid projectId, DateOnly? from = null, DateOnly? through = null, CancellationToken cancellationToken = default);
    Task<ProjectMemoryPreferences> GetPreferencesAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task SavePreferencesAsync(ProjectMemoryPreferences preferences, CancellationToken cancellationToken = default);
}
