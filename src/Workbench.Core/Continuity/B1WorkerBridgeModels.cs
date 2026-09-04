namespace Workbench.Core.Continuity;

public enum B1WorkerExecutionRelationKind
{
    Initial,
    Retry,
    ProviderReplacement,
    Continuation
}

public sealed record B1WorkerTaskLink(
    ProjectRef ProjectRef,
    AssignmentRef AssignmentRef,
    RevisionRef AssignmentRevisionRef,
    Guid WorkerTaskId,
    Guid WorkerTaskRevisionId,
    DateTimeOffset CreatedAt);

public sealed record B1WorkerExecutionLink(
    ProjectRef ProjectRef,
    AttemptRef AttemptRef,
    Guid WorkerExecutionId,
    B1WorkerExecutionRelationKind RelationKind,
    DateTimeOffset CreatedAt);

public sealed record B1WorkerSessionLink(
    ProjectRef ProjectRef,
    SessionBindingRef SessionBindingRef,
    Guid WorkerExecutionId,
    Guid AgentSessionId,
    ExternalSessionRef ExternalSessionRef,
    string ProviderId,
    string AccountId,
    string ModelId,
    DateTimeOffset CreatedAt);

public sealed record B1WorkerExecutionEvidence(
    ProjectRef ProjectRef,
    EvidenceRef EvidenceRef,
    Guid WorkerExecutionId,
    Guid WorkerTaskId,
    Guid WorkerTaskRevisionId,
    AttemptRef AttemptRef,
    string VerificationResult,
    string VerificationJson,
    DateTimeOffset CreatedAt);

public sealed record B1WorkerReviewContext(
    AssignmentRef AssignmentRef,
    RevisionRef AssignmentRevisionRef,
    AttemptRef AttemptRef,
    Guid WorkerTaskId,
    Guid WorkerTaskRevisionId,
    Guid WorkerExecutionId,
    SessionBindingRef? SessionBindingRef,
    Guid? AgentSessionId,
    ExternalSessionRef? ExternalSessionRef,
    string? ProviderId,
    string? AccountId,
    string? ModelId,
    string? VerificationResult,
    EvidenceRef? VerificationEvidenceRef,
    string? VerificationJson);
