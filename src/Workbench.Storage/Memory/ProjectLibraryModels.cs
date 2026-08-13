namespace Workbench.Storage.Memory;

public sealed record LibrarySubmission(Guid SubmissionId, Guid ProjectId, Guid SourceSessionId, Guid? TaskId,
    string Category, string Topic, string Summary, string? SourceReference, DateTimeOffset CreatedAt);

public sealed record ProjectLibraryEntry(Guid Id, Guid ProjectId, Guid SourceSessionId, Guid? TaskId,
    string Category, string Topic, string Summary, string? SourceReference, DateTimeOffset CreatedAt);
