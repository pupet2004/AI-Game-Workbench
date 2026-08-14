using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Workbench.Core.Leaders;
using Workbench.Core.Tasks;
using Workbench.Storage.Settings;
using Workbench.Storage.Tasks;

namespace Workbench.App.Leader;

public enum LeaderReviewAutoProceedResultKind { NoWork, Completed, Existing, RetryableFailure }
public sealed record LeaderReviewAutoProceedResult(LeaderReviewAutoProceedResultKind Kind, string? Error = null);

public interface ILeaderReviewAutoProceedExecutor
{
    Task<LeaderReviewAutoProceedResult> TryExecuteAsync(Guid projectId, Guid taskId, Guid taskRevisionId, Guid finalReportEventId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LeaderReviewAutoProceedResult>> RecoverAsync(Guid projectId, CancellationToken cancellationToken = default);
}

public sealed class LeaderReviewAutoProceedExecutor(
    TaskRepository tasks,
    AssignmentReviewStateRepository reviewState,
    LeaderAuthoritySettingsService authoritySettings,
    TimeProvider timeProvider) : ILeaderReviewAutoProceedExecutor
{
    public async Task<LeaderReviewAutoProceedResult> TryExecuteAsync(Guid projectId, Guid taskId, Guid taskRevisionId, Guid finalReportEventId, CancellationToken cancellationToken = default)
    {
        var task = await tasks.GetAsync(projectId, taskId, cancellationToken);
        if (task is null || task.Status != TaskLifecycleStatus.Reviewing) return new(LeaderReviewAutoProceedResultKind.NoWork);
        var decision = await reviewState.GetLeaderReviewDecisionAsync(projectId, taskId, taskRevisionId, finalReportEventId, cancellationToken);
        if (decision is null || decision.Outcome != nameof(LeaderReviewOutcome.Pass) || decision.TaskRevisionId != task.CurrentRevisionId) return new(LeaderReviewAutoProceedResultKind.NoWork);

        LeaderAuthorityMode authority;
        try { authority = await authoritySettings.GetEffectiveLeaderAuthorityModeAsync(projectId, cancellationToken); }
        catch (Exception exception) when (exception is not OperationCanceledException) { return new(LeaderReviewAutoProceedResultKind.RetryableFailure, "Authority settings were unavailable."); }
        LeaderAuthorityResolution resolution;
        try { resolution = LeaderAuthorityResolver.Resolve(authority, Enum.Parse<LeaderReviewActionLevel>(decision.ActionLevel), LeaderReviewOutcome.Pass); }
        catch (Exception exception) when (exception is ArgumentException) { return new(LeaderReviewAutoProceedResultKind.RetryableFailure, "The persisted review decision was invalid."); }
        if (resolution != LeaderAuthorityResolution.AutoProceed) return new(LeaderReviewAutoProceedResultKind.NoWork);

        var eventId = CompletionEventId(decision.EventId);
        var payload = JsonSerializer.Serialize(new { ReviewDecisionEventId = decision.EventId, decision.TaskRevisionId, decision.FinalReportEventId, Outcome = decision.Outcome, Authority = authority.ToString(), Resolution = resolution.ToString() });
        try
        {
            var transition = await reviewState.TryTransitionAsync(projectId, taskId, TaskLifecycleStatus.Reviewing, TaskLifecycleStatus.Completed, eventId, "AssignmentAutoCompleted", payload, timeProvider.GetUtcNow(), cancellationToken);
            return transition switch
            {
                AssignmentStateTransitionResult.Applied => new(LeaderReviewAutoProceedResultKind.Completed),
                AssignmentStateTransitionResult.Idempotent => new(LeaderReviewAutoProceedResultKind.Existing),
                _ => await ReloadAfterConflictAsync(projectId, taskId, cancellationToken)
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { return new(LeaderReviewAutoProceedResultKind.RetryableFailure, "Assignment completion persistence failed."); }
    }

    public async Task<IReadOnlyList<LeaderReviewAutoProceedResult>> RecoverAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var decisions = await reviewState.ListReviewingPassDecisionsAsync(projectId, cancellationToken);
        var results = new List<LeaderReviewAutoProceedResult>(decisions.Count);
        foreach (var decision in decisions) results.Add(await TryExecuteAsync(projectId, decision.TaskId, decision.TaskRevisionId, decision.FinalReportEventId, cancellationToken));
        return results;
    }

    private async Task<LeaderReviewAutoProceedResult> ReloadAfterConflictAsync(Guid projectId, Guid taskId, CancellationToken cancellationToken)
    {
        var current = await tasks.GetAsync(projectId, taskId, cancellationToken);
        return current?.Status == TaskLifecycleStatus.Completed ? new(LeaderReviewAutoProceedResultKind.Existing) : new(LeaderReviewAutoProceedResultKind.RetryableFailure, "Assignment state changed before completion.");
    }

    private static Guid CompletionEventId(Guid decisionEventId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"assignment-auto-completed:{decisionEventId:D}"));
        return new Guid(bytes[..16]);
    }
}
