using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Workbench.Core.Leaders;
using Workbench.Core.Tasks;
using Workbench.Storage.Leaders;
using Workbench.Storage.Settings;
using Workbench.Storage.Tasks;

namespace Workbench.App.Leader;

public enum LeaderReviewAskUserGateResultKind { NoWork, Opened, Existing, RetryableFailure }
public sealed record LeaderReviewAskUserGateResult(LeaderReviewAskUserGateResultKind Kind, string? Error = null);

public interface ILeaderReviewAskUserGate
{
    Task<LeaderReviewAskUserGateResult> TryOpenAsync(Guid projectId, Guid taskId, Guid taskRevisionId, Guid finalReportEventId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LeaderReviewAskUserGateResult>> RecoverAsync(Guid projectId, CancellationToken cancellationToken = default);
}

public sealed class LeaderReviewAskUserGate(
    TaskRepository tasks,
    AssignmentReviewStateRepository reviewState,
    LeaderAuthoritySettingsService authoritySettings,
    ProjectLeaderRepository leaders,
    TimeProvider timeProvider) : ILeaderReviewAskUserGate
{
    public async Task<LeaderReviewAskUserGateResult> TryOpenAsync(Guid projectId, Guid taskId, Guid taskRevisionId, Guid finalReportEventId, CancellationToken cancellationToken = default)
    {
        var task = await tasks.GetAsync(projectId, taskId, cancellationToken);
        if (task is null || (task.Status != TaskLifecycleStatus.Reviewing && task.Status != TaskLifecycleStatus.NeedsUserDecision)) return new(LeaderReviewAskUserGateResultKind.NoWork);
        var decision = await reviewState.GetLeaderReviewDecisionAsync(projectId, taskId, taskRevisionId, finalReportEventId, cancellationToken);
        if (decision is null || decision.TaskRevisionId != task.CurrentRevisionId) return new(LeaderReviewAskUserGateResultKind.NoWork);

        LeaderAuthorityResolution resolution;
        try
        {
            var authority = await authoritySettings.GetEffectiveLeaderAuthorityModeAsync(projectId, cancellationToken);
            resolution = LeaderAuthorityResolver.Resolve(authority, Enum.Parse<LeaderReviewActionLevel>(decision.ActionLevel), Enum.Parse<LeaderReviewOutcome>(decision.Outcome));
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { return new(LeaderReviewAskUserGateResultKind.RetryableFailure, "Authority settings or review decision were invalid."); }
        if (resolution != LeaderAuthorityResolution.AskUser) return new(LeaderReviewAskUserGateResultKind.NoWork);

        var leader = await leaders.GetAsync(projectId, cancellationToken);
        if (leader?.CurrentEpochId is not Guid epochId) return new(LeaderReviewAskUserGateResultKind.RetryableFailure, "The current Leader conversation is unavailable.");
        var question = BuildQuestion(projectId, taskId, decision);
        var eventId = GateEventId(decision.EventId);
        var payload = JsonSerializer.Serialize(new { ReviewDecisionEventId = decision.EventId, decision.TaskRevisionId, decision.FinalReportEventId, Resolution = resolution.ToString() });
        try
        {
            var result = await reviewState.TryOpenUserDecisionGateAsync(projectId, taskId, decision.TaskRevisionId, decision.EventId, epochId, eventId, payload, question, timeProvider.GetUtcNow(), cancellationToken);
            return result switch
            {
                UserDecisionGateResult.Applied => new(LeaderReviewAskUserGateResultKind.Opened),
                UserDecisionGateResult.Idempotent => new(LeaderReviewAskUserGateResultKind.Existing),
                _ => new(LeaderReviewAskUserGateResultKind.RetryableFailure, result.ToString())
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { return new(LeaderReviewAskUserGateResultKind.RetryableFailure, "User decision gate persistence failed."); }
    }

    public async Task<IReadOnlyList<LeaderReviewAskUserGateResult>> RecoverAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var decisions = await reviewState.ListUserDecisionGateCandidatesAsync(projectId, cancellationToken);
        var results = new List<LeaderReviewAskUserGateResult>(decisions.Count);
        foreach (var decision in decisions) results.Add(await TryOpenAsync(projectId, decision.TaskId, decision.TaskRevisionId, decision.FinalReportEventId, cancellationToken));
        return results;
    }

    private static string BuildQuestion(Guid projectId, Guid taskId, StoredLeaderReviewDecision decision)
    {
        var parts = new List<string> { decision.Summary };
        if (!string.IsNullOrWhiteSpace(decision.Issue)) parts.Add($"核心问题：{decision.Issue}");
        parts.Add($"需要你决定：{decision.NextAction}");
        if (!string.IsNullOrWhiteSpace(decision.ImportantNote)) parts.Add($"说明：{decision.ImportantNote}");
        parts.Add($"[{AssignmentReviewStateRepository.UserDecisionQuestionMarker(projectId, taskId, decision.EventId)}]");
        return string.Join("\n\n", parts);
    }

    private static Guid GateEventId(Guid decisionEventId) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"assignment-needs-user-decision:{decisionEventId:D}"))[..16]);
}
