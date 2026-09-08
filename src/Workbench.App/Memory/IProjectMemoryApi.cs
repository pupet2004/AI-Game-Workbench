using Workbench.Core.Memory;
using Workbench.Core.Continuity;
using Workbench.Storage.Memory;
using Workbench.Storage.Leaders;

namespace Workbench.App.Memory;

public interface IProjectMemoryApi
{
    Task<AcceptedProjectState> GetAcceptedProjectStateAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<DailySummaryDocument?> GetDailySummaryAsync(Guid projectId, DateOnly localDate, CancellationToken cancellationToken = default);
    Task<DailySummaryDocument> UpsertDailySummaryAsync(DailySummaryWrite write, DateTimeOffset savedAt, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DailySummaryMetadata>> ListDailySummaryMetadataAsync(Guid projectId, DateOnly? from = null, DateOnly? through = null, CancellationToken cancellationToken = default);
    Task<ProjectMemoryPreferences> GetPreferencesAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task SavePreferencesAsync(ProjectMemoryPreferences preferences, CancellationToken cancellationToken = default);
    Task<string?> GetBrainHandoffAsync(Guid projectId, Guid epochId, CancellationToken cancellationToken = default);
    Task<StoredLeaderSessionEpoch> SaveBrainHandoffAsync(Guid projectId, Guid epochId, string? content, CancellationToken cancellationToken = default);
    Task<RecentConversationStats> GetRecentConversationStatsAsync(Guid projectId, Guid epochId, CancellationToken cancellationToken = default);
    Task<RecentConversationSlice> ReadRecentConversationAsync(Guid projectId, Guid epochId, long? beforeSequence, int maxMessages, int maxUtf8Bytes, CancellationToken cancellationToken = default);
    Task<ContinuityMaterialCatalog> ListContinuityMaterialsAsync(Guid projectId, Guid sourceEpochId, CancellationToken cancellationToken = default);
    Task<ResolvedContinuityBundle> ResolveContinuityAsync(Guid projectId, LeaderEpochContinuityPlan plan, CancellationToken cancellationToken = default);
    Task<ResolvedContinuityBundle> BuildInitialContinuityBundleAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectEvolutionCandidate>> ListEvolutionCandidatesAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<ProjectLibraryProposal> CreateLibraryProposalAsync(ProjectLibraryProposalDraft draft, CancellationToken cancellationToken = default);
    Task<ProjectLibraryProposal?> GetLibraryProposalAsync(Guid projectId, Guid proposalId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectLibraryProposal>> GetPendingLibraryProposalsAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task AcceptLibraryProposalAsync(Guid projectId, Guid proposalId, CancellationToken cancellationToken = default);
    Task EditAndAcceptLibraryProposalAsync(Guid projectId, Guid proposalId, LibraryProposalEdit edit, CancellationToken cancellationToken = default);
    Task RejectLibraryProposalAsync(Guid projectId, Guid proposalId, CancellationToken cancellationToken = default);
}
