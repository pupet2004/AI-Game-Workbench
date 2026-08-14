namespace Workbench.Storage.Leaders;

public sealed record RecentConversationStats(Guid ProjectId, Guid EpochId, int MessageCount, int Utf8Bytes, long? LastSequence);
public sealed record RecentConversationSlice(Guid ProjectId, Guid EpochId, IReadOnlyList<StoredLeaderMessage> Messages, int Utf8Bytes, int OmittedMessageCount);
