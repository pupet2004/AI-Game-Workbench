namespace Workbench.Storage.Memory;

public sealed record ProjectLibraryObject(
    Guid Id,
    Guid ProjectId,
    string Category,
    string Topic,
    string CategoryKey,
    string TopicKey,
    string? CurrentOverview,
    int OverviewRevision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ProjectLibraryTimelineNode(
    Guid Id,
    Guid ObjectId,
    DateOnly LocalDate,
    string Content,
    int Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ProjectLibraryOverviewMetadata(
    Guid ObjectId,
    Guid ProjectId,
    string Category,
    string Topic,
    int Revision,
    DateTimeOffset UpdatedAt,
    int Utf8Bytes);

public sealed record ProjectLibraryTimelineNodeMetadata(
    Guid NodeId,
    Guid ObjectId,
    Guid ProjectId,
    string Category,
    string Topic,
    DateOnly LocalDate,
    int Revision,
    DateTimeOffset CreatedAt,
    int Utf8Bytes);

public sealed record LibraryMaterialReference(
    Guid NodeId,
    string MaterialKind,
    string Reference,
    string? Label,
    DateTimeOffset CreatedAt);

public sealed record LibraryMaterialReferenceDraft(
    string MaterialKind,
    string Reference,
    string? Label);

public enum LibraryProposalAction
{
    CreateNode,
    UpdateNode
}

public enum LibraryProposalStatus
{
    Pending,
    Accepted,
    Rejected
}

public sealed record ProjectLibraryProposal(
    Guid Id,
    Guid ProjectId,
    Guid SourceSessionId,
    LibraryProposalStatus Status,
    ProjectLibraryProposalDraft Draft,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt);

public sealed record ProjectLibraryProposalDraft(
    Guid ProposalId,
    Guid ProjectId,
    Guid SourceSessionId,
    LibraryProposalAction Action,
    Guid? TargetObjectId,
    Guid? TargetNodeId,
    int? ExpectedNodeRevision,
    int? ExpectedOverviewRevision,
    string Category,
    string Topic,
    DateOnly LocalDate,
    string NodeContent,
    string? CurrentOverview,
    IReadOnlyList<LibraryMaterialReferenceDraft> Materials,
    DateTimeOffset CreatedAt);

public sealed record LibraryProposalEdit(
    string NodeContent,
    string? CurrentOverview,
    IReadOnlyList<LibraryMaterialReferenceDraft> Materials);

internal static class ProjectLibraryIdentity
{
    public static string NormalizeDisplay(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public static string NormalizeKey(string value) =>
        NormalizeDisplay(value).ToUpperInvariant();
}
