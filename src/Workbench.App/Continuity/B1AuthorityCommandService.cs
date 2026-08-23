using Workbench.Core.Continuity;
using Workbench.Storage.Continuity;

namespace Workbench.App.Continuity;

public sealed class B1AuthorityCommandService(
    B1AuthorityRepository repository,
    B1AuthorityEvaluator evaluator,
    TimeProvider timeProvider)
{
    private const int MaximumConflictRetries = 3;
    private readonly B1AuthorityRepository _repository =
        repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly B1AuthorityEvaluator _evaluator =
        evaluator ?? throw new ArgumentNullException(nameof(evaluator));
    private readonly TimeProvider _timeProvider =
        timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

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
                return committed.Decision;
            }
        }

        throw new B1CommandException(
            B1FailureCode.ConcurrentProjectChange,
            "The Project authority state changed during all commit attempts.");
    }
}
