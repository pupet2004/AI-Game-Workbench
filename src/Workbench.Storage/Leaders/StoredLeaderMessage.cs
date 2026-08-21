namespace Workbench.Storage.Leaders;

public sealed record StoredLeaderMessage(
    long Id,
    Guid EpochId,
    long Sequence,
    string Role,
    string Text,
    DateTimeOffset CreatedAt)
{
    public Guid? ResultId { get; init; }
    public string? SummaryDeltaPayloadJson { get; init; }
    public DateTimeOffset? SummaryPersistedAt { get; init; }
}

public sealed record LeaderResultMetadata
{
    public LeaderResultMetadata(Guid ResultId, string SummaryDeltaPayloadJson)
    {
        if (ResultId == Guid.Empty) throw new ArgumentException("Result identity is required.", nameof(ResultId));
        ArgumentException.ThrowIfNullOrWhiteSpace(SummaryDeltaPayloadJson);
        this.ResultId = ResultId;
        this.SummaryDeltaPayloadJson = SummaryDeltaPayloadJson;
    }

    public Guid ResultId { get; }
    public string SummaryDeltaPayloadJson { get; }
}

public sealed record PendingLeaderSummaryResult(
    long MessageId,
    Guid ProjectId,
    Guid EpochId,
    Guid ResultId,
    string SummaryDeltaPayloadJson,
    DateTimeOffset CreatedAt);
