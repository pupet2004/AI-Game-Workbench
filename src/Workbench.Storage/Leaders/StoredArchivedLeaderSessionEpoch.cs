namespace Workbench.Storage.Leaders;

/// <summary>Lightweight archived-session data suitable for a history list.</summary>
public sealed record StoredArchivedLeaderSessionEpoch(
    Guid Id,
    DateTimeOffset EndedAt,
    string? RolloverReason,
    string? HandoffSummary,
    int MessageCount);

public sealed record LeaderEpochHistoryCursor(DateTimeOffset EndedAt, Guid Id);

public sealed record LeaderEpochHistoryPage(
    IReadOnlyList<StoredArchivedLeaderSessionEpoch> Epochs,
    LeaderEpochHistoryCursor? NextCursor);
