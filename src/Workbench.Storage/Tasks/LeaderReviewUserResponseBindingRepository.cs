using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Workbench.Storage.Database;
using Workbench.Storage.Reviews;
using Workbench.Storage.Workers;

namespace Workbench.Storage.Tasks;

public sealed record LeaderReviewUserResponseBinding(Guid EventId, Guid ProjectId, Guid TaskId, Guid TaskRevisionId, Guid ReviewDecisionEventId, long QuestionMessageId, long UserMessageId, DateTimeOffset CreatedAt);

// Compatibility facade for callers that have not moved to the App binder yet.
// Typed review state remains the only authority; no legacy event payload is read here.
public sealed class LeaderReviewUserResponseBindingRepository(WorkbenchDatabase database)
{
    private readonly LeaderReviewStateRepository _reviewState = new(database);
    private readonly TaskEventRepository _taskEvents = new(database);

    public async Task<bool> HasSingletonOpenGateAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        (await _reviewState.GetOpenUserGatesAsync(projectId, cancellationToken: cancellationToken)).Count == 1;

    public async Task<LeaderReviewUserResponseBinding?> TryBindAsync(Guid projectId, long userMessageId, CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty || userMessageId <= 0) return null;
        var gates = await _reviewState.GetOpenUserGatesAsync(projectId, cancellationToken: cancellationToken);
        if (gates.Count != 1) return null;
        var gate = gates[0];
        var now = DateTimeOffset.UtcNow;
        if (!await _reviewState.TryBindFirstUserResponseAsync(projectId, gate.TaskId, gate.ReviewDecisionId, userMessageId, now, cancellationToken)) return null;
        var binding = new LeaderReviewUserResponseBinding(DeterministicBindingId(gate.ReviewDecisionId, userMessageId), projectId, gate.TaskId, gate.RevisionId, gate.ReviewDecisionId, gate.QuestionMessageId ?? 0, userMessageId, now);
        try
        {
            var payload = JsonSerializer.Serialize(new { TaskRevisionId = gate.RevisionId, ReviewDecisionEventId = gate.ReviewDecisionId, QuestionMessageId = gate.QuestionMessageId, UserMessageId = userMessageId });
            await _taskEvents.AppendAsync(new StoredTaskEvent(binding.EventId, projectId, gate.TaskId, null, "LeaderReviewUserResponseReceived", payload, now), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Typed state is authoritative; the compatibility event must not change the bind result.
        }
        return binding;
    }

    public Task<long?> GetBoundUserMessageIdAsync(Guid projectId, Guid taskId, Guid reviewDecisionEventId, CancellationToken cancellationToken = default) =>
        _reviewState.GetBoundUserMessageIdAsync(projectId, taskId, reviewDecisionEventId, cancellationToken);

    private static Guid DeterministicBindingId(Guid decisionId, long messageId) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"review-user-response:{decisionId:D}:{messageId}"))[..16]);
}
