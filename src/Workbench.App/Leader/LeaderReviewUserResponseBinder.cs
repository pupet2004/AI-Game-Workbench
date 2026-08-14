using Workbench.Storage.Tasks;

namespace Workbench.App.Leader;

public interface ILeaderReviewUserResponseBinder
{
    Task<bool> HasSingletonOpenGateAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<LeaderReviewUserResponseBinding?> BindAsync(Guid projectId, long userMessageId, CancellationToken cancellationToken = default);
    Task<long?> GetBoundUserMessageIdAsync(Guid projectId, Guid taskId, Guid reviewDecisionEventId, CancellationToken cancellationToken = default);
}

public sealed class LeaderReviewUserResponseBinder(LeaderReviewUserResponseBindingRepository repository) : ILeaderReviewUserResponseBinder
{
    public Task<bool> HasSingletonOpenGateAsync(Guid projectId, CancellationToken cancellationToken = default) => repository.HasSingletonOpenGateAsync(projectId, cancellationToken);
    public Task<LeaderReviewUserResponseBinding?> BindAsync(Guid projectId, long userMessageId, CancellationToken cancellationToken = default) => repository.TryBindAsync(projectId, userMessageId, cancellationToken);
    public Task<long?> GetBoundUserMessageIdAsync(Guid projectId, Guid taskId, Guid reviewDecisionEventId, CancellationToken cancellationToken = default) => repository.GetBoundUserMessageIdAsync(projectId, taskId, reviewDecisionEventId, cancellationToken);
}
