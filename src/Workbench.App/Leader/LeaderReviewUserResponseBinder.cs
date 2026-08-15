using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Workbench.Storage.Reviews;
using Workbench.Storage.Tasks;
using Workbench.Storage.Workers;

namespace Workbench.App.Leader;

public interface ILeaderReviewUserResponseBinder
{
    Task<bool> HasSingletonOpenGateAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<LeaderReviewUserResponseBinding?> BindAsync(Guid projectId, long userMessageId, CancellationToken cancellationToken = default);
    Task<long?> GetBoundUserMessageIdAsync(Guid projectId, Guid taskId, Guid reviewDecisionEventId, CancellationToken cancellationToken = default);
}

public sealed class LeaderReviewUserResponseBinder(LeaderReviewStateRepository reviewState, TaskEventRepository taskEvents, TimeProvider timeProvider) : ILeaderReviewUserResponseBinder
{
    public async Task<bool> HasSingletonOpenGateAsync(Guid projectId, CancellationToken cancellationToken = default) => (await reviewState.GetOpenUserGatesAsync(projectId, cancellationToken: cancellationToken)).Count == 1;

    public async Task<LeaderReviewUserResponseBinding?> BindAsync(Guid projectId, long userMessageId, CancellationToken cancellationToken = default)
    {
        var gates = await reviewState.GetOpenUserGatesAsync(projectId, cancellationToken: cancellationToken);
        if (gates.Count != 1) return null;
        var gate = gates[0];
        var now = timeProvider.GetUtcNow();
        if (!await reviewState.TryBindFirstUserResponseAsync(projectId, gate.TaskId, gate.ReviewDecisionId, userMessageId, now, cancellationToken)) return null;
        var binding = new LeaderReviewUserResponseBinding(DeterministicBindingId(gate.ReviewDecisionId, userMessageId), projectId, gate.TaskId, gate.RevisionId, gate.ReviewDecisionId, gate.QuestionMessageId ?? 0, userMessageId, now);
        try
        {
            var payload = JsonSerializer.Serialize(new { TaskRevisionId = gate.RevisionId, ReviewDecisionEventId = gate.ReviewDecisionId, QuestionMessageId = gate.QuestionMessageId, UserMessageId = userMessageId });
            await taskEvents.AppendAsync(new StoredTaskEvent(binding.EventId, projectId, gate.TaskId, null, "LeaderReviewUserResponseReceived", payload, now), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The typed gate binding is authoritative; the legacy event is best-effort audit compatibility.
        }
        return binding;
    }

    public Task<long?> GetBoundUserMessageIdAsync(Guid projectId, Guid taskId, Guid reviewDecisionEventId, CancellationToken cancellationToken = default) => reviewState.GetBoundUserMessageIdAsync(projectId, taskId, reviewDecisionEventId, cancellationToken);

    private static Guid DeterministicBindingId(Guid decisionId, long messageId) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"review-user-response:{decisionId:D}:{messageId}"))[..16]);
}
