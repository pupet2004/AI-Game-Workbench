using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Workbench.Core.Leaders;
using Workbench.Core.Tasks;
using Workbench.Storage.Leaders;
using Workbench.Storage.Settings;
using Workbench.Storage.Tasks;
using Workbench.Storage.Reviews;

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
    LeaderReviewStateRepository typedReviewState,
    LeaderAuthoritySettingsService authoritySettings,
    ProjectLeaderRepository leaders,
    LeaderMessageRepository messages,
    TimeProvider timeProvider) : ILeaderReviewAskUserGate
{
    public async Task<LeaderReviewAskUserGateResult> TryOpenAsync(Guid projectId, Guid taskId, Guid taskRevisionId, Guid finalReportEventId, CancellationToken cancellationToken = default)
    {
        var task = await tasks.GetAsync(projectId, taskId, cancellationToken);
        if (task is null || (task.Status != TaskLifecycleStatus.Reviewing && task.Status != TaskLifecycleStatus.NeedsUserDecision)) return new(LeaderReviewAskUserGateResultKind.NoWork);
        var decision = await typedReviewState.GetDecisionByFinalReportAsync(projectId, taskId, finalReportEventId, cancellationToken);
        if (decision is null || decision.RevisionId != taskRevisionId || decision.RevisionId != task.CurrentRevisionId) return new(LeaderReviewAskUserGateResultKind.NoWork);

        LeaderAuthorityResolution resolution;
        try
        {
            resolution = Enum.Parse<LeaderAuthorityResolution>(decision.AuthorityResolution);
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { return new(LeaderReviewAskUserGateResultKind.RetryableFailure, "Authority settings or review decision were invalid."); }
        if (resolution != LeaderAuthorityResolution.AskUser) return new(LeaderReviewAskUserGateResultKind.NoWork);

        var leader = await leaders.GetAsync(projectId, cancellationToken);
        if (leader?.CurrentEpochId is not Guid epochId) return new(LeaderReviewAskUserGateResultKind.RetryableFailure, "The current Leader conversation is unavailable.");
        StoredLeaderReviewDecision? legacyDetail = null;
        try
        {
            legacyDetail = await reviewState.GetLeaderReviewDecisionAsync(projectId, taskId, decision.RevisionId, finalReportEventId, cancellationToken);
        }
        catch (JsonException)
        {
            // Typed review state remains authoritative when the legacy explanation payload is corrupt.
        }
        catch (FormatException)
        {
            // Preserve recovery from typed state when a legacy timestamp or identifier is malformed.
        }
        catch (InvalidOperationException)
        {
            // A malformed legacy payload must not block the typed AskUser gate.
        }
        var question = BuildQuestion(projectId, taskId, decision, legacyDetail);
        var eventId = GateEventId(decision.ReviewDecisionId);
        var payload = JsonSerializer.Serialize(new { ReviewDecisionEventId = decision.ReviewDecisionId, TaskRevisionId = decision.RevisionId, decision.FinalReportEventId, Resolution = resolution.ToString() });
        try
        {
            var openedAt = timeProvider.GetUtcNow();
            var result = await reviewState.TryOpenUserDecisionGateAsync(projectId, taskId, decision.RevisionId, decision.ReviewDecisionId, epochId, eventId, payload, question, openedAt, cancellationToken);
            var questionMessage = (await messages.GetAllAsync(epochId, cancellationToken)).LastOrDefault(item => item.Text.Contains(AssignmentReviewStateRepository.UserDecisionQuestionMarker(projectId, taskId, decision.ReviewDecisionId), StringComparison.Ordinal));
            if (questionMessage is null) return new(LeaderReviewAskUserGateResultKind.RetryableFailure, "User decision question was unavailable.");
            var gate = await typedReviewState.OpenUserGateIfAbsentAsync(new LeaderReviewUserGateWriteRequest(decision.ReviewDecisionId, projectId, taskId, decision.RevisionId, questionMessage.Id, openedAt), cancellationToken);
            if (gate == LeaderReviewWriteResult.Conflict) return new(LeaderReviewAskUserGateResultKind.RetryableFailure, gate.ToString());
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
        var allTypedGates = await typedReviewState.GetUserGatesAsync(projectId, cancellationToken);
        var gates = allTypedGates.Where(item => item.State == "Open").ToArray();
        var results = new List<LeaderReviewAskUserGateResult>(gates.Length);
        foreach (var gate in gates)
        {
            var decision = await typedReviewState.GetDecisionAsync(projectId, gate.TaskId, gate.ReviewDecisionId, cancellationToken);
            if (decision is not null)
            {
                results.Add(await TryOpenAsync(projectId, gate.TaskId, decision.RevisionId, decision.FinalReportEventId, cancellationToken));
            }
        }
        return results;
    }

    private static string BuildQuestion(Guid projectId, Guid taskId, LeaderReviewDecisionRecord decision, StoredLeaderReviewDecision? legacyDetail)
    {
        if (legacyDetail is not null)
        {
            var detailed = new List<string> { legacyDetail.Summary };
            if (!string.IsNullOrWhiteSpace(legacyDetail.Issue)) detailed.Add($"核心问题：{legacyDetail.Issue}");
            detailed.Add($"需要你决定：{legacyDetail.NextAction}");
            if (!string.IsNullOrWhiteSpace(legacyDetail.ImportantNote)) detailed.Add($"说明：{legacyDetail.ImportantNote}");
            detailed.Add($"[{AssignmentReviewStateRepository.UserDecisionQuestionMarker(projectId, taskId, decision.ReviewDecisionId)}]");
            return string.Join("\n\n", detailed);
        }
        var parts = new List<string> { $"Review outcome: {decision.Outcome}." };
        parts.Add($"需要你决定：{decision.ActionLevel}");
        parts.Add($"[{AssignmentReviewStateRepository.UserDecisionQuestionMarker(projectId, taskId, decision.ReviewDecisionId)}]");
        return string.Join("\n\n", parts);
    }

    private static Guid GateEventId(Guid decisionEventId) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"assignment-needs-user-decision:{decisionEventId:D}"))[..16]);
}
