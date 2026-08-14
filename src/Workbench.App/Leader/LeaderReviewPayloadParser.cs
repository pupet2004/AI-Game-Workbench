using System.Text.Json;
using Workbench.Core.Tasks;

namespace Workbench.App.Leader;

public enum LeaderReviewDepth { ReportOnly, EvidenceCheck, Deep }

public sealed record LeaderReviewDecision(
    Guid TaskId, Guid TaskRevisionId, Guid FinalReportEventId,
    LeaderReviewOutcome Outcome, LeaderReviewActionLevel ActionLevel, LeaderReviewDepth ReviewDepth,
    string Summary, string? Issue, string NextAction, string? ImportantNote);

public sealed class LeaderReviewPayloadException(string message) : Exception(message);

public static class LeaderReviewPayloadParser
{
    private const int SummaryLimit = 600;
    private const int IssueLimit = 600;
    private const int NextActionLimit = 600;
    private const int ImportantNoteLimit = 400;

    public static LeaderReviewDecision Parse(string json)
    {
        try
        {
            var value = JsonSerializer.Deserialize<Document>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new LeaderReviewPayloadException("Review decision is required.");
            var outcome = ParseOutcome(value.Outcome);
            var action = ParseAction(value.ActionLevel);
            var depth = ParseDepth(value.ReviewDepth);
            if (outcome == LeaderReviewOutcome.AskUser && action != LeaderReviewActionLevel.L3DecisionRequired)
                throw new LeaderReviewPayloadException("ASK_USER requires L3_DECISION_REQUIRED.");
            return new LeaderReviewDecision(ParseGuid(value.TaskId, "TaskId"), ParseGuid(value.TaskRevisionId, "TaskRevisionId"),
                ParseGuid(value.FinalReportEventId, "FinalReportEventId"), outcome, action, depth,
                Required(value.Summary, "Summary", SummaryLimit), Optional(value.Issue, "Issue", IssueLimit),
                Required(value.NextAction, "NextAction", NextActionLimit), Optional(value.ImportantNote, "ImportantNote", ImportantNoteLimit));
        }
        catch (JsonException) { throw new LeaderReviewPayloadException("Review decision JSON is malformed."); }
    }

    private static LeaderReviewOutcome ParseOutcome(string? value) => value switch
    { "PASS" => LeaderReviewOutcome.Pass, "FIX" => LeaderReviewOutcome.Fix, "CONTINUE" => LeaderReviewOutcome.Continue, "ASK_USER" => LeaderReviewOutcome.AskUser, _ => throw new LeaderReviewPayloadException("Outcome is invalid.") };
    private static LeaderReviewActionLevel ParseAction(string? value) => value switch
    { "L1_LOCAL_FIX" => LeaderReviewActionLevel.L1LocalFix, "L2_TASK_REWORK" => LeaderReviewActionLevel.L2TaskRework, "L3_DECISION_REQUIRED" => LeaderReviewActionLevel.L3DecisionRequired, _ => throw new LeaderReviewPayloadException("ActionLevel is invalid.") };
    private static LeaderReviewDepth ParseDepth(string? value) => value switch
    { "REPORT_ONLY" => LeaderReviewDepth.ReportOnly, "EVIDENCE_CHECK" => LeaderReviewDepth.EvidenceCheck, "DEEP" => LeaderReviewDepth.Deep, _ => throw new LeaderReviewPayloadException("ReviewDepth is invalid.") };
    private static Guid ParseGuid(string? value, string name) => Guid.TryParse(value, out var id) && id != Guid.Empty ? id : throw new LeaderReviewPayloadException($"{name} is invalid.");
    private static string Required(string? value, string name, int max) => string.IsNullOrWhiteSpace(value) ? throw new LeaderReviewPayloadException($"{name} is required.") : Bounded(value, name, max);
    private static string? Optional(string? value, string name, int max) => value is null ? null : Bounded(value, name, max);
    private static string Bounded(string value, string name, int max) => value.Length > max ? throw new LeaderReviewPayloadException($"{name} is too long.") : value;
    private sealed record Document(string? TaskId, string? TaskRevisionId, string? FinalReportEventId, string? Outcome, string? ActionLevel, string? ReviewDepth, string? Summary, string? Issue, string? NextAction, string? ImportantNote);
}
