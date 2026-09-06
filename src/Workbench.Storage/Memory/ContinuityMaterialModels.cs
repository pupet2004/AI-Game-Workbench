using Workbench.Core.Continuity;

namespace Workbench.Storage.Memory;

public enum ContinuityMaterialKind { AcceptedProjectState, DailySummary, BrainHandoff, RecentConversation, LegacyFormal, LegacyLearned, LibraryOverview, LibraryTimelineNode, LegacyWorkerCompletion, AssignmentStatus }
public sealed record ContinuityMaterialDescriptor(string Reference, ContinuityMaterialKind Kind, Guid ProjectId, string Label, DateTimeOffset? OccurredAt, int Utf8Bytes);
public sealed record ContinuityMaterialSelection(int Ordinal, ContinuityMaterialKind Kind, string Reference, int MaxUtf8Bytes, string? SelectorJson = null);
public sealed record LeaderEpochContinuityPlan(Guid EpochId, int TotalMaxUtf8Bytes, IReadOnlyList<ContinuityMaterialSelection> Selections, DateTimeOffset CreatedAt);
public sealed record ResolvedContinuityMaterial(
    ContinuityMaterialKind Kind,
    string Reference,
    string Label,
    string Content,
    int Utf8Bytes,
    Guid? ProjectId = null,
    IReadOnlyList<AuthorityDecisionRef>? AuthorityDecisionRefs = null,
    IReadOnlyList<AcceptedStateContributionRef>? AcceptedContributionRefs = null);
public sealed record ResolvedContinuityBundle(Guid ProjectId, IReadOnlyList<ResolvedContinuityMaterial> Materials, int Utf8Bytes, IReadOnlyList<string> OmittedReferences);
public sealed record ContinuityMaterialCatalog(Guid ProjectId, Guid SourceEpochId, IReadOnlyList<ContinuityMaterialDescriptor> Materials);
