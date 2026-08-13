using Workbench.Core.Tasks;
using Workbench.Storage.Tasks;
using System.Text.Json;

namespace Workbench.App.Leader;

public sealed record LeaderExecutionRecommendation(string? ProviderHint, string? ModelHint, string? RuntimeHint);

public sealed record LeaderDraftProposal(
    Guid ProjectId,
    string Title,
    string Goal,
    string Scope,
    string OutOfScope,
    IReadOnlyList<string> Acceptance,
    TaskRiskLevel RiskLevel,
    LeaderExecutionRecommendation Recommendation);

public sealed record LeaderDraftProposalResult(bool Succeeded, Guid? TaskId, string? Error)
{
    public static LeaderDraftProposalResult Rejected(string error) => new(false, null, error);
}

public sealed record LeaderStructuredResponse(string Response, LeaderDraftProposal? Proposal)
{
    public static bool TryParse(string? text, Guid projectId, out LeaderStructuredResponse result)
    {
        result = new LeaderStructuredResponse(text ?? string.Empty, null);
        if (string.IsNullOrWhiteSpace(text) || text.TrimStart()[0] != '{') return false;
        try
        {
            using var json = JsonDocument.Parse(text);
            var root = json.RootElement;
            if (!root.TryGetProperty("response", out var response) || response.ValueKind != JsonValueKind.String) return false;
            if (!root.TryGetProperty("draft_proposal", out var draft))
            {
                result = new LeaderStructuredResponse(response.GetString() ?? string.Empty, null);
                return true;
            }
            if (draft.ValueKind != JsonValueKind.Object) return false;
            var profile = draft.GetProperty("recommendedExecutionProfile");
            var proposal = new LeaderDraftProposal(
                projectId,
                draft.GetProperty("title").GetString() ?? string.Empty,
                draft.GetProperty("goal").GetString() ?? string.Empty,
                draft.GetProperty("scope").GetString() ?? string.Empty,
                draft.GetProperty("outOfScope").GetString() ?? string.Empty,
                draft.GetProperty("acceptance").EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray(),
                Enum.Parse<TaskRiskLevel>(draft.GetProperty("riskLevel").GetString() ?? string.Empty, true),
                new LeaderExecutionRecommendation(
                    profile.TryGetProperty("providerHint", out var provider) ? provider.GetString() : null,
                    profile.TryGetProperty("modelHint", out var model) ? model.GetString() : null,
                    profile.TryGetProperty("runtimeHint", out var runtime) ? runtime.GetString() : null));
            result = new LeaderStructuredResponse(response.GetString() ?? string.Empty, proposal);
            return true;
        }
        catch (Exception) { return false; }
    }
}

public static class LeaderResponseSchema
{
    public const string Json = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["response", "draft_proposal"],
          "properties": {
            "response": { "type": "string" },
            "draft_proposal": {
              "anyOf": [
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["title", "goal", "scope", "outOfScope", "acceptance", "riskLevel", "recommendedExecutionProfile"],
                  "properties": {
                    "title": { "type": "string" },
                    "goal": { "type": "string" },
                    "scope": { "type": "string" },
                    "outOfScope": { "type": "string" },
                    "acceptance": { "type": "array", "items": { "type": "string" } },
                    "riskLevel": { "type": "string", "enum": ["Low", "Medium", "High"] },
                    "recommendedExecutionProfile": {
                      "type": "object",
                      "additionalProperties": false,
                      "required": ["providerHint", "modelHint", "runtimeHint"],
                      "properties": {
                        "providerHint": { "type": ["string", "null"] },
                        "modelHint": { "type": ["string", "null"] },
                        "runtimeHint": { "type": ["string", "null"] }
                      }
                    }
                  }
                },
                { "type": "null" }
              ]
            }
          }
        }
        """;
}

public sealed class LeaderDraftProposalBuilder(Guid projectId, TaskRepository tasks)
{
    public async Task<LeaderDraftProposalResult> CreateDraftAsync(
        LeaderDraftProposal proposal,
        ExecutionProfile candidateProfile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        if (proposal.ProjectId != projectId)
        {
            return LeaderDraftProposalResult.Rejected("Proposal project does not match the active project.");
        }

        if (string.IsNullOrWhiteSpace(proposal.Title) ||
            string.IsNullOrWhiteSpace(proposal.Goal) ||
            string.IsNullOrWhiteSpace(proposal.Scope) ||
            string.IsNullOrWhiteSpace(proposal.OutOfScope) ||
            proposal.Acceptance is null || proposal.Acceptance.Count == 0 ||
            proposal.Acceptance.Any(string.IsNullOrWhiteSpace) || candidateProfile is null)
        {
            return LeaderDraftProposalResult.Rejected("Proposal is missing required fields.");
        }

        var taskId = Guid.NewGuid();
        var revision = new TaskRevision(
            taskId, 1, proposal.Goal, proposal.Scope, proposal.OutOfScope,
            proposal.Acceptance, proposal.RiskLevel, candidateProfile,
            "Leader proposal", TaskRevisionApprover.User, DateTimeOffset.UtcNow, null);
        var draft = new TaskDraft(
            taskId, proposal.Title, proposal.Goal, proposal.Scope, proposal.OutOfScope,
            proposal.Acceptance, proposal.RiskLevel, candidateProfile,
            revision.CreatedAt, revision);

        try
        {
            await tasks.CreateAsync(projectId, draft, cancellationToken);
            return new LeaderDraftProposalResult(true, taskId, null);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            return LeaderDraftProposalResult.Rejected(exception.Message);
        }
    }
}
