namespace Workbench.Storage.Memory;

public enum ProjectEvolutionCandidateStatus
{
    Observed,
    GovernancePending,
    Accepted,
    Rejected
}

public sealed record ProjectEvolutionCandidate(
    Guid CandidateId,
    Guid ProjectId,
    Guid? EpochId,
    Guid? ResultId,
    string SourceRef,
    string Object,
    string ObjectKind,
    string ChangeType,
    string? Before,
    string? After,
    string ImpactClass,
    string RouteHint,
    string Reason,
    ProjectEvolutionCandidateStatus Status,
    DateTimeOffset CreatedAt)
{
    public string EvidenceRef => $"workbench:evolution-candidate/{CandidateId:N}";
}
