namespace Workbench.Core.Continuity;

public enum CanonicalWorkerCompletionStatus
{
    PendingBridge,
    GovernanceReady,
    Governed
}

public sealed record CanonicalWorkerCompletionFacts
{
    public CanonicalWorkerCompletionFacts(
        Guid completionId,
        ProjectRef projectRef,
        Guid sourceEventId,
        Guid taskId,
        Guid taskRevisionId,
        Guid workerExecutionId,
        AttemptRef attemptRef,
        SessionBindingRef sessionBindingRef,
        LogicalActorRef workerActorRef,
        string finalReport,
        string? validationSummary,
        IReadOnlyList<EvidenceRef> evidenceRefs,
        IReadOnlyList<string> proposedChanges,
        ClaimRef resultClaimRef,
        ClaimRef? validationClaimRef,
        HandoffRef handoffRef,
        DateTimeOffset completedAt)
    {
        if (completionId == Guid.Empty) throw new ArgumentException("Completion identity is required.", nameof(completionId));
        if (sourceEventId == Guid.Empty) throw new ArgumentException("Source event identity is required.", nameof(sourceEventId));
        if (taskId == Guid.Empty) throw new ArgumentException("Task identity is required.", nameof(taskId));
        if (taskRevisionId == Guid.Empty) throw new ArgumentException("Task revision identity is required.", nameof(taskRevisionId));
        if (workerExecutionId == Guid.Empty) throw new ArgumentException("Worker execution identity is required.", nameof(workerExecutionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(finalReport);
        ArgumentNullException.ThrowIfNull(evidenceRefs);
        ArgumentNullException.ThrowIfNull(proposedChanges);
        if (resultClaimRef.Value == Guid.Empty) throw new ArgumentException("Result Claim identity is required.", nameof(resultClaimRef));
        if (validationClaimRef is { } validation && validation.Value == Guid.Empty)
            throw new ArgumentException("Validation Claim identity is invalid.", nameof(validationClaimRef));
        if (handoffRef.Value == Guid.Empty) throw new ArgumentException("Handoff identity is required.", nameof(handoffRef));
        CompletionId = completionId;
        ProjectRef = projectRef;
        SourceEventId = sourceEventId;
        TaskId = taskId;
        TaskRevisionId = taskRevisionId;
        WorkerExecutionId = workerExecutionId;
        AttemptRef = attemptRef;
        SessionBindingRef = sessionBindingRef;
        WorkerActorRef = workerActorRef;
        FinalReport = finalReport;
        ValidationSummary = validationSummary;
        EvidenceRefs = Array.AsReadOnly(evidenceRefs.ToArray());
        ProposedChanges = Array.AsReadOnly(proposedChanges
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray());
        ResultClaimRef = resultClaimRef;
        ValidationClaimRef = validationClaimRef;
        HandoffRef = handoffRef;
        CompletedAt = completedAt;
    }

    public Guid CompletionId { get; }
    public ProjectRef ProjectRef { get; }
    public Guid SourceEventId { get; }
    public Guid TaskId { get; }
    public Guid TaskRevisionId { get; }
    public Guid WorkerExecutionId { get; }
    public AttemptRef AttemptRef { get; }
    public SessionBindingRef SessionBindingRef { get; }
    public LogicalActorRef WorkerActorRef { get; }
    public string FinalReport { get; }
    public string? ValidationSummary { get; }
    public IReadOnlyList<EvidenceRef> EvidenceRefs { get; }
    public IReadOnlyList<string> ProposedChanges { get; }
    public ClaimRef ResultClaimRef { get; }
    public ClaimRef? ValidationClaimRef { get; }
    public HandoffRef HandoffRef { get; }
    public DateTimeOffset CompletedAt { get; }
}

public sealed record StoredCanonicalWorkerCompletion(
    CanonicalWorkerCompletionFacts Facts,
    CanonicalWorkerCompletionStatus Status,
    DateTimeOffset PersistedAt,
    DateTimeOffset? GovernedAt,
    AuthorityDecisionRef? AuthorityDecisionRef);
