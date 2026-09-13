using System.Security.Cryptography;
using System.Text;
using Workbench.Core.Continuity;
using Workbench.Storage.Continuity;

namespace Workbench.App.Continuity;

/// <summary>
/// Converts a durable Worker completion into non-authoritative governance
/// material. This service may persist Completion, Claims, and a Handoff, but it
/// must never create an Authority Decision or write AcceptedProjectState.
/// </summary>
public sealed class CanonicalWorkerCompletionBridgeService(
    CanonicalWorkerCompletionRepository completionRepository,
    B1ClaimHandoffRepository claimHandoffRepository,
    TimeProvider timeProvider)
{
    private readonly CanonicalWorkerCompletionRepository _completionRepository =
        completionRepository ?? throw new ArgumentNullException(nameof(completionRepository));
    private readonly B1ClaimHandoffRepository _claimHandoffRepository =
        claimHandoffRepository ?? throw new ArgumentNullException(nameof(claimHandoffRepository));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <summary>
    /// Creates stable identities for the non-authoritative governance package
    /// associated with one Worker completion source event.
    /// </summary>
    public CanonicalWorkerCompletionFacts CreateFacts(
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
        DateTimeOffset? completedAt = null,
        IReadOnlyList<string>? proposedChanges = null)
    {
        var source = sourceEventId.ToString("N");
        return new(
            DeterministicGuid($"canonical-worker-completion:{source}:completion"),
            projectRef,
            sourceEventId,
            taskId,
            taskRevisionId,
            workerExecutionId,
            attemptRef,
            sessionBindingRef,
            workerActorRef,
            finalReport,
            string.IsNullOrWhiteSpace(validationSummary) ? null : validationSummary,
            evidenceRefs,
            proposedChanges ?? [],
            new ClaimRef(DeterministicGuid($"canonical-worker-completion:{source}:result")),
            string.IsNullOrWhiteSpace(validationSummary)
                ? null
                : new ClaimRef(DeterministicGuid($"canonical-worker-completion:{source}:validation")),
            new HandoffRef(DeterministicGuid($"canonical-worker-completion:{source}:handoff")),
            completedAt ?? _timeProvider.GetUtcNow());
    }

    /// <summary>
    /// Persists Completion and bridges it to Claims and a Handoff. The returned
    /// status is GovernanceReady until an Authority command governs the
    /// Handoff.
    /// </summary>
    public async Task<StoredCanonicalWorkerCompletion> BridgeAsync(
        CanonicalWorkerCompletionFacts facts,
        UserPrincipalRef authenticatedOperatorRef,
        HandoffRef? expectedStoredHandoffRef = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var stored = await _completionRepository.SavePendingAsync(facts, cancellationToken);
        if (stored.Status is CanonicalWorkerCompletionStatus.GovernanceReady or CanonicalWorkerCompletionStatus.Governed)
            return stored;

        var resultClaim = new Claim(
            facts.ResultClaimRef,
            facts.ProjectRef,
            new ClaimantRef.LogicalActor(facts.WorkerActorRef),
            facts.SessionBindingRef,
            new ClaimPayload.Result(facts.FinalReport),
            facts.EvidenceRefs,
            facts.CompletedAt);
        var claims = new List<Claim> { resultClaim };
        var validationRefs = Array.Empty<ClaimRef>();
        if (facts.ValidationClaimRef is { } validationRef && !string.IsNullOrWhiteSpace(facts.ValidationSummary))
        {
            claims.Add(new Claim(
                validationRef,
                facts.ProjectRef,
                new ClaimantRef.LogicalActor(facts.WorkerActorRef),
                facts.SessionBindingRef,
                new ClaimPayload.Validation(facts.ValidationSummary),
                facts.EvidenceRefs,
                facts.CompletedAt));
            validationRefs = [validationRef];
        }
        var proposedClaims = facts.ProposedChanges
            .Select((statement, index) => new Claim(
                new ClaimRef(DeterministicGuid($"canonical-worker-completion:{facts.SourceEventId:N}:proposal:{index}")),
                facts.ProjectRef,
                new ClaimantRef.LogicalActor(facts.WorkerActorRef),
                facts.SessionBindingRef,
                new ClaimPayload.ProposedStateContribution(
                    statement,
                    new ContributionScopeRef.Project(facts.ProjectRef),
                    null),
                facts.EvidenceRefs,
                facts.CompletedAt))
            .ToArray();
        claims.AddRange(proposedClaims);

        var handoff = new Handoff(
            facts.HandoffRef,
            facts.AttemptRef,
            facts.ResultClaimRef,
            validationRefs,
            [],
            proposedClaims.Select(value => value.ClaimRef).ToArray(),
            [],
            facts.EvidenceRefs,
            facts.CompletedAt);
        await _claimHandoffRepository.RecordCanonicalHandoffAndSelectAsync(
            facts.ProjectRef,
            authenticatedOperatorRef,
            facts.CompletionId,
            handoff,
            claims,
            expectedStoredHandoffRef,
            cancellationToken);
        return await _completionRepository.GetBySourceEventAsync(
                   facts.ProjectRef.Value, facts.SourceEventId, cancellationToken)
               ?? throw new InvalidDataException("The canonical Worker completion disappeared after bridging.");
    }

    public async Task<int> RecoverAsync(
        Guid projectId,
        UserPrincipalRef authenticatedOperatorRef,
        CancellationToken cancellationToken = default)
    {
        await _completionRepository.ReconcileGovernedAsync(projectId, cancellationToken);
        var recovered = 0;
        foreach (var pending in await _completionRepository.ListPendingAsync(projectId, cancellationToken))
        {
            try
            {
                var result = await BridgeAsync(pending.Facts, authenticatedOperatorRef, cancellationToken: cancellationToken);
                if (result.Status is CanonicalWorkerCompletionStatus.GovernanceReady or CanonicalWorkerCompletionStatus.Governed)
                    recovered++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // A later project-open/reconciliation pass can retry the durable PendingBridge row.
            }
        }

        return recovered;
    }

    private static Guid DeterministicGuid(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }
}
