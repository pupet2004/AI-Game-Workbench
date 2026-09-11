using Workbench.Core.Continuity;
using Workbench.Storage.Continuity;
using Workbench.Storage.Memory;

namespace Workbench.App.Continuity;

public sealed class B1AuthorityCommandService(
    B1AuthorityRepository repository,
    B1AuthorityEvaluator evaluator,
    TimeProvider timeProvider,
    ProjectEvolutionCandidateRepository? evolutionCandidates = null,
    CanonicalWorkerCompletionRepository? canonicalCompletions = null)
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
                return committed.Decision;
            }
        }

        throw new B1CommandException(
            B1FailureCode.ConcurrentProjectChange,
            "The Project authority state changed during all commit attempts.");
    }
}
