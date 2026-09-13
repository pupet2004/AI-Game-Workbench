using Workbench.Core.Continuity;
using Workbench.Storage.Continuity;
using Workbench.Storage.Memory;

namespace Workbench.App.Continuity;

public sealed class B1AuthorityCommandService(
    B1AuthorityRepository repository,
    B1AuthorityEvaluator evaluator,
    TimeProvider timeProvider,
    ProjectEvolutionCandidateRepository? evolutionCandidates = null,
    CanonicalWorkerCompletionRepository? canonicalCompletions = null,
    ProjectSummaryRepository? projectSummaries = null)
{
    private const int MaximumConflictRetries = 3;
    private readonly B1AuthorityRepository _repository =
        repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly B1AuthorityEvaluator _evaluator =
        evaluator ?? throw new ArgumentNullException(nameof(evaluator));
    private readonly TimeProvider _timeProvider =
        timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly ProjectEvolutionCandidateRepository? _evolutionCandidates = evolutionCandidates;
    private readonly CanonicalWorkerCompletionRepository? _canonicalCompletions = canonicalCompletions;
    private readonly ProjectSummaryRepository? _projectSummaries = projectSummaries;

    public Task<AuthorityDecision> EstablishLogicalActorAsync(
        EstablishLogicalActorCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return ExecuteAsync(
            command.ProjectRef,
            command,
            (state, current, decisionRef, createdAt) =>
                _evaluator.Evaluate(state, current, decisionRef, createdAt),
            cancellationToken);
    }

    public Task<AuthorityDecision> EstablishResponsibilityAsync(
        EstablishResponsibilityCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return ExecuteAsync(
            command.ProjectRef,
            command,
            (state, current, decisionRef, createdAt) =>
                _evaluator.Evaluate(state, current, decisionRef, createdAt),
            cancellationToken);
    }

    public Task<AuthorityDecision> DelegateAssignmentAsync(
        DelegateAssignmentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return ExecuteAsync(
            command.ProjectRef,
            command,
            (state, current, decisionRef, createdAt) =>
                _evaluator.Evaluate(state, current, decisionRef, createdAt),
            cancellationToken);
    }

    public Task<AuthorityDecision> DecideAssignmentAsync(
        DecideAssignmentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return ExecuteAsync(
            command.ProjectRef,
            command,
            (state, current, decisionRef, createdAt) =>
                _evaluator.Evaluate(state, current, decisionRef, createdAt),
            cancellationToken);
    }

    public Task<AuthorityDecision> ActivateAssignmentRevisionAsync(
        ActivateAssignmentRevisionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return ExecuteAsync(
            command.ProjectRef,
            command,
            (state, current, decisionRef, createdAt) =>
                _evaluator.Evaluate(state, current, decisionRef, createdAt),
            cancellationToken);
    }

    public Task<AuthorityDecision> AuthorAcceptedStateAsync(
        AuthorAcceptedStateCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return ExecuteAsync(
            command.ProjectRef,
            command,
            (state, current, decisionRef, createdAt) =>
                _evaluator.Evaluate(state, current, decisionRef, createdAt),
            cancellationToken);
    }

    private async Task<AuthorityDecision> ExecuteAsync<TCommand>(
        ProjectRef projectRef,
        TCommand command,
        Func<B1ProjectState, TCommand, AuthorityDecisionRef, DateTimeOffset, ValidatedAuthorityDecision> evaluate,
        CancellationToken cancellationToken)
    {
        for (var retry = 0; retry <= MaximumConflictRetries; retry++)
        {
            var state = await _repository.LoadProjectStateAsync(projectRef, cancellationToken);
            var validated = evaluate(
                state,
                command,
                new AuthorityDecisionRef(Guid.NewGuid()),
                _timeProvider.GetUtcNow());

            var result = await _repository.TryCommitAsync(validated, cancellationToken);
            if (result is AuthorityCommitResult.Committed committed)
            {
                if (_evolutionCandidates is not null)
                {
                    foreach (var candidateId in committed.Decision.ConsideredRefs
                                 .OfType<ConsideredRef.EvolutionCandidate>()
                                 .Select(value => value.CandidateId)
                                 .Distinct())
                    {
                        await _evolutionCandidates.TryUpdateStatusAsync(
                            projectRef.Value,
                            candidateId,
                            ProjectEvolutionCandidateStatus.Accepted,
                            cancellationToken);
                    }
                }
                if (_canonicalCompletions is not null)
                {
                    foreach (var handoff in committed.Decision.ConsideredRefs
                                 .OfType<ConsideredRef.Handoff>()
                                 .Select(value => value.HandoffRef)
                                 .Distinct())
                    {
                        try
                        {
                            await _canonicalCompletions.MarkGovernedByHandoffAsync(
                                projectRef.Value,
                                handoff,
                                committed.Decision.DecisionRef,
                                cancellationToken);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch
                        {
                            // The Authority Decision is already durable; reconciliation can complete the marker later.
                        }
                    }
                }
                if (_projectSummaries is not null)
                {
                    try
                    {
                        await AppendCanonicalDecisionSummaryAsync(
                            state,
                            committed.Decision,
                            cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch
                    {
                        // Accepted state is authoritative. Summary is a
                        // recoverable continuity projection and must not
                        // turn a durable Authority Decision into a failure.
                    }
                }
                return committed.Decision;
            }
        }

        throw new B1CommandException(
            B1FailureCode.ConcurrentProjectChange,
            "The Project authority state changed during all commit attempts.");
    }

    private async Task AppendCanonicalDecisionSummaryAsync(
        B1ProjectState stateBeforeCommit,
        AuthorityDecision decision,
        CancellationToken cancellationToken)
    {
        var summaries = _projectSummaries;
        if (summaries is null)
            return;

        var handoffRefs = decision.ConsideredRefs
            .OfType<ConsideredRef.Handoff>()
            .Select(value => value.HandoffRef)
            .Distinct()
            .ToArray();
        if (handoffRefs.Length == 0)
            return;

        var claims = stateBeforeCommit.Claims.ToDictionary(value => value.ClaimRef);
        var handoffs = stateBeforeCommit.Handoffs.ToDictionary(value => value.HandoffRef);
        var deltas = new List<SummaryDelta>();
        foreach (var handoffRef in handoffRefs)
        {
            if (!handoffs.TryGetValue(handoffRef, out var handoff))
                continue;

            var result = claims.GetValueOrDefault(handoff.ResultClaimRef)?.Payload as ClaimPayload.Result;
            var disposition = decision.AssignmentDispositionEffect?.Disposition;
            var kind = disposition switch
            {
                AssignmentDisposition.Rejected => SummaryDeltaKind.RejectedPath,
                AssignmentDisposition.RevisionRequired => SummaryDeltaKind.Unresolved,
                _ => SummaryDeltaKind.Change
            };
            var outcome = disposition switch
            {
                AssignmentDisposition.Rejected => "rejected",
                AssignmentDisposition.RevisionRequired => "requires revision",
                _ => "accepted"
            };
            var resultText = result?.Statement ?? "Worker result";
            var contributionText = decision.AcceptedStateContributions.Count == 0
                ? string.Empty
                : $" Accepted state: {string.Join("; ", decision.AcceptedStateContributions.Select(value => value.Statement))}";
            var text = $"Authority {outcome} Worker result: {resultText}.{contributionText}";
            var sources = new List<SummarySourceRef>
            {
                new("AuthorityDecision", decision.DecisionRef.Value.ToString()),
                new("Handoff", handoffRef.Value.ToString())
            };
            if (handoff.ResultClaimRef.Value != Guid.Empty)
                sources.Add(new("Claim", handoff.ResultClaimRef.Value.ToString()));
            deltas.Add(new SummaryDelta(decision.CreatedAt, kind, text, sources));
        }

        if (deltas.Count > 0)
        {
            await summaries.AppendAsync(
                decision.ProjectRef.Value,
                decision.DecisionRef.Value,
                deltas,
                decision.CreatedAt,
                cancellationToken);
        }
    }
}
