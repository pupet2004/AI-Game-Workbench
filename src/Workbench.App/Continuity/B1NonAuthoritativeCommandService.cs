using Workbench.Core.Continuity;
using Workbench.Storage.Continuity;

namespace Workbench.App.Continuity;

public sealed class B1NonAuthoritativeCommandService(B1RoutingRepository routingRepository)
{
    private readonly B1RoutingRepository _routingRepository =
        routingRepository ?? throw new ArgumentNullException(nameof(routingRepository));

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
}
