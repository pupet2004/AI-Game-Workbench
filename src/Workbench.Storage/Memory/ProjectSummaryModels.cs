namespace Workbench.Storage.Memory;

public enum SummaryDeltaKind
{
    Decision,
    Change,
    Constraint,
    RejectedPath,
    Unresolved
}

public sealed record SummarySourceRef
{
    public SummarySourceRef(string SourceKind, string SourceLocator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(SourceKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(SourceLocator);

        this.SourceKind = SourceKind;
        this.SourceLocator = SourceLocator;
    }

    public string SourceKind { get; }
    public string SourceLocator { get; }
}

public sealed record SummaryDelta
{
    public SummaryDelta(
        DateTimeOffset OccurredAt,
        SummaryDeltaKind Kind,
        string Text,
        IReadOnlyList<SummarySourceRef> SourceRefs)
    {
        ValidateKind(Kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(Text);
        ArgumentNullException.ThrowIfNull(SourceRefs);

        this.OccurredAt = OccurredAt;
        this.Kind = Kind;
        this.Text = Text;
        this.SourceRefs = SourceRefs;
    }

    public DateTimeOffset OccurredAt { get; }
    public SummaryDeltaKind Kind { get; }
    public string Text { get; }
    public IReadOnlyList<SummarySourceRef> SourceRefs { get; }

    private static void ValidateKind(SummaryDeltaKind kind)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }
}

public sealed record StoredSummaryEntry
{
    public StoredSummaryEntry(
        Guid EntryId,
        Guid ProjectId,
        DateTimeOffset OccurredAt,
        DateTimeOffset CreatedAt,
        SummaryDeltaKind Kind,
        string Text,
        Guid ResultId,
        int DeltaOrdinal,
        IReadOnlyList<SummarySourceRef> SourceRefs)
    {
        if (EntryId == Guid.Empty) throw new ArgumentException("Entry identity is required.", nameof(EntryId));
        if (ProjectId == Guid.Empty) throw new ArgumentException("Project identity is required.", nameof(ProjectId));
        if (ResultId == Guid.Empty) throw new ArgumentException("Result identity is required.", nameof(ResultId));
        if (DeltaOrdinal < 0) throw new ArgumentOutOfRangeException(nameof(DeltaOrdinal));
        if (!Enum.IsDefined(Kind)) throw new ArgumentOutOfRangeException(nameof(Kind));
        ArgumentException.ThrowIfNullOrWhiteSpace(Text);
        ArgumentNullException.ThrowIfNull(SourceRefs);

        this.EntryId = EntryId;
        this.ProjectId = ProjectId;
        this.OccurredAt = OccurredAt;
        this.CreatedAt = CreatedAt;
        this.Kind = Kind;
        this.Text = Text;
        this.ResultId = ResultId;
        this.DeltaOrdinal = DeltaOrdinal;
        this.SourceRefs = SourceRefs;
    }

    public Guid EntryId { get; }
    public Guid ProjectId { get; }
    public DateTimeOffset OccurredAt { get; }
    public DateTimeOffset CreatedAt { get; }
    public SummaryDeltaKind Kind { get; }
    public string Text { get; }
    public Guid ResultId { get; }
    public int DeltaOrdinal { get; }
    public IReadOnlyList<SummarySourceRef> SourceRefs { get; }
}

public sealed record SummaryQuery
{
    public SummaryQuery(
        Guid ProjectId,
        int Limit,
        IReadOnlyList<SummaryDeltaKind>? Kinds = null,
        DateTimeOffset? OccurredFrom = null,
        DateTimeOffset? OccurredTo = null,
        string? SourceKind = null,
        string? SourceLocator = null)
    {
        if (ProjectId == Guid.Empty) throw new ArgumentException("Project identity is required.", nameof(ProjectId));
        if (Limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(Limit));
        if (OccurredFrom > OccurredTo) throw new ArgumentException("OccurredFrom must not be after OccurredTo.", nameof(OccurredFrom));
        if (Kinds is not null)
        {
            foreach (var kind in Kinds)
            {
                if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(Kinds));
            }
        }

        this.ProjectId = ProjectId;
        this.Limit = Limit;
        this.Kinds = Kinds is { Count: > 0 } ? Kinds : null;
        this.OccurredFrom = OccurredFrom;
        this.OccurredTo = OccurredTo;
        this.SourceKind = SourceKind;
        this.SourceLocator = SourceLocator;
    }

    public Guid ProjectId { get; }
    public int Limit { get; }
    public IReadOnlyList<SummaryDeltaKind>? Kinds { get; }
    public DateTimeOffset? OccurredFrom { get; }
    public DateTimeOffset? OccurredTo { get; }
    public string? SourceKind { get; }
    public string? SourceLocator { get; }
}
