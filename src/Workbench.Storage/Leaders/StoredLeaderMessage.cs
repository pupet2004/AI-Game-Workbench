namespace Workbench.Storage.Leaders;

public sealed record StoredLeaderMessage(
    long Id,
    Guid EpochId,
    long Sequence,
    string Role,
    string Text,
    DateTimeOffset CreatedAt);
