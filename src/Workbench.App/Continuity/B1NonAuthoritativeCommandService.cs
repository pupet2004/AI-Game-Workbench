using Workbench.Core.Continuity;
using Workbench.Storage.Continuity;

namespace Workbench.App.Continuity;

public sealed class B1NonAuthoritativeCommandService(
    B1RoutingRepository routingRepository,
    B1ClaimHandoffRepository claimHandoffRepository)
{
    private readonly B1RoutingRepository _routingRepository =
        routingRepository ?? throw new ArgumentNullException(nameof(routingRepository));
    private readonly B1ClaimHandoffRepository _claimHandoffRepository =
        claimHandoffRepository ?? throw new ArgumentNullException(nameof(claimHandoffRepository));

    public Task<Attempt> CreateAttemptAsync(
        CreateAttemptCommand command,
        CancellationToken cancellationToken = default) =>
        _routingRepository.CreateAttemptAsync(command, cancellationToken);

    public Task SelectCurrentAttemptAsync(
        SelectCurrentAttemptCommand command,
        CancellationToken cancellationToken = default) =>
        _routingRepository.SelectCurrentAttemptAsync(command, cancellationToken);

    public Task<SessionBinding> CreateSessionBindingAsync(
        CreateSessionBindingCommand command,
        CancellationToken cancellationToken = default) =>
        _routingRepository.CreateSessionBindingAsync(command, cancellationToken);

    public Task SelectCurrentSessionBindingAsync(
        SelectCurrentSessionBindingCommand command,
        CancellationToken cancellationToken = default) =>
        _routingRepository.SelectCurrentSessionBindingAsync(command, cancellationToken);

    public Task ClearCurrentSessionBindingAsync(
        ClearCurrentSessionBindingCommand command,
        CancellationToken cancellationToken = default) =>
        _routingRepository.ClearCurrentSessionBindingAsync(command, cancellationToken);

    public Task<Attempt> CreateAttemptAndSelectAsync(
        CreateAttemptCommand create,
        AttemptRef? expectedStored,
        CancellationToken cancellationToken = default) =>
        _routingRepository.CreateAttemptAndSelectAsync(create, expectedStored, cancellationToken);

    public Task<SessionBinding> CreateSessionBindingAndSelectAsync(
        CreateSessionBindingCommand create,
        SessionBindingRef? expectedStored,
        CancellationToken cancellationToken = default) =>
        _routingRepository.CreateSessionBindingAndSelectAsync(create, expectedStored, cancellationToken);

    public Task<Claim> RecordClaimAsync(
        RecordClaimCommand command,
        CancellationToken cancellationToken = default) =>
        _claimHandoffRepository.RecordClaimAsync(command, cancellationToken);

    public Task<Handoff> CreateHandoffAsync(
        CreateHandoffCommand command,
        CancellationToken cancellationToken = default) =>
        _claimHandoffRepository.CreateHandoffAsync(command, cancellationToken);

    public Task SelectContinuationHandoffAsync(
        SelectContinuationHandoffCommand command,
        CancellationToken cancellationToken = default) =>
        _claimHandoffRepository.SelectContinuationHandoffAsync(command, cancellationToken);

    public Task<Handoff> CreateHandoffAndSelectAsync(
        CreateHandoffCommand create,
        HandoffRef? expectedStored,
        CancellationToken cancellationToken = default) =>
        _claimHandoffRepository.CreateHandoffAndSelectAsync(create, expectedStored, cancellationToken);

}
