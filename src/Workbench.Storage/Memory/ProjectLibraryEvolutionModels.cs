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

internal static class ProjectLibraryIdentity
{
    public static string NormalizeDisplay(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public static string NormalizeKey(string value) =>
        NormalizeDisplay(value).ToUpperInvariant();
}
