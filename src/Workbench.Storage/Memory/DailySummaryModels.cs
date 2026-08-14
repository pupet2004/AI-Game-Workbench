namespace Workbench.Storage.Memory;

public sealed record DailySummarySourceReference(string SourceType, string SourceRef);

public sealed record DailySummaryDocument(
    Guid ProjectId,
    DateOnly LocalDate,
    string Content,
    int Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<DailySummarySourceReference> Sources);

public sealed record DailySummaryWrite(
    Guid ProjectId,
    DateOnly LocalDate,
    string Content,
    int? ExpectedRevision,
    IReadOnlyList<DailySummarySourceReference> Sources);

public sealed record DailySummaryMetadata(
    Guid ProjectId,
    DateOnly LocalDate,
    int Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int SourceCount);
