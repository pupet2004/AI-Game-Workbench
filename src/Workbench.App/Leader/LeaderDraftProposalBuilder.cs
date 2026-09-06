using Workbench.Core.Tasks;
using Workbench.Core.Continuity;
using Workbench.Storage.Tasks;
using Workbench.Storage.Memory;
using System.Text.Json;
using System.Text;
using System.Globalization;
using System.Text.RegularExpressions;

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

public sealed record LeaderLibraryProposalCommand(
    LibraryProposalAction Action,
    Guid? TargetObjectId,
    Guid? TargetNodeId,
    int? ExpectedNodeRevision,
    int? ExpectedOverviewRevision,
    string Category,
    string Topic,
    DateOnly LocalDate,
    string NodeContent,
    string? CurrentOverview,
    IReadOnlyList<LibraryMaterialReferenceDraft> Materials,
    DateTimeOffset? OccurredAt);

public sealed record LeaderMemoryCommands(LeaderLibraryProposalCommand? LibraryProposal);

public enum LeaderEvolutionImpactClass
{
    WorldRule,
    ProjectStructure,
    CharacterOrObject,
    Content,
    Architecture,
    Unclassified
}

public enum LeaderEvolutionRouteHint
{
    AuthorityConfirmation,
    LibraryProposal,
    NoGovernance,
    Unclassified
}

public sealed record LeaderEvolutionCandidate(
    string Object,
    string ObjectKind,
    string ChangeType,
    string? Before,
    string? After,
    LeaderEvolutionImpactClass ImpactClass,
    LeaderEvolutionRouteHint RouteHint,
    string Reason,
    string SourceRef);

public sealed record LeaderStructuredResponse(
    string Response,
    LeaderDraftProposal? Proposal,
    LeaderMemoryCommands? MemoryCommands = null,
    string? MemoryCommandError = null)
{
    private static readonly Regex SummaryOccurredAtPattern = new(
        @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$",
        RegexOptions.CultureInvariant);

    public IReadOnlyList<SummaryDelta> SummaryDeltas { get; init; } = [];
    public string? SummaryDeltaError { get; init; }
    public AuthorityConfirmationDraft? AuthorityConfirmation { get; init; }
    public IReadOnlyList<LeaderEvolutionCandidate> EvolutionCandidates { get; init; } = [];

    public static bool TryParse(string? text, Guid projectId, out LeaderStructuredResponse result)
    {
        result = new LeaderStructuredResponse(text ?? string.Empty, null);
        text = NormalizeStructuredText(text);
        if (!TryExtractLastEnvelope(text, out var envelopeJson)) return false;
        try
        {
            using var json = JsonDocument.Parse(envelopeJson);
            var root = json.RootElement;
            if (!root.TryGetProperty("response", out var response) || response.ValueKind != JsonValueKind.String) return false;
            LeaderDraftProposal? proposal = null;
            if (root.TryGetProperty("draft_proposal", out var draft) && draft.ValueKind != JsonValueKind.Null)
            {
                if (draft.ValueKind != JsonValueKind.Object) return false;
                var profile = draft.GetProperty("recommendedExecutionProfile");
                proposal = new LeaderDraftProposal(
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
            }

            AuthorityConfirmationDraft? authorityConfirmation = null;
            if (root.TryGetProperty("authority_confirmation", out var authority) && authority.ValueKind != JsonValueKind.Null)
            {
                if (authority.ValueKind != JsonValueKind.Object || proposal is not null)
                    throw new JsonException();
                authorityConfirmation = ParseAuthorityConfirmation(authority, projectId);
            }

            var visibleResponse = response.GetString() ?? string.Empty;
            LeaderMemoryCommands? memoryCommands = null;
            string? memoryCommandError = null;
            if (root.TryGetProperty("memory_commands", out var memory) && memory.ValueKind != JsonValueKind.Null)
            {
                try
                {
                    memoryCommands = ParseMemoryCommands(memory);
                }
                catch (Exception)
                {
                    memoryCommandError = "Library memory command could not be processed.";
                }
            }

            IReadOnlyList<SummaryDelta> summaryDeltas = [];
            string? summaryDeltaError = null;
            try
            {
                if (!root.TryGetProperty("summary_deltas", out var summary)) throw new JsonException();
                summaryDeltas = ParseSummaryDeltas(summary);
            }
            catch (Exception)
            {
                summaryDeltaError = "Summary delta sidecar could not be processed.";
            }

            IReadOnlyList<LeaderEvolutionCandidate> evolutionCandidates = [];
            if (root.TryGetProperty("evolution_candidates", out var candidates) && candidates.ValueKind != JsonValueKind.Null)
            {
                if (candidates.ValueKind != JsonValueKind.Array) throw new JsonException();
                evolutionCandidates = candidates.EnumerateArray().Select(ParseEvolutionCandidate).ToArray();
                if (evolutionCandidates.Count > 3) throw new JsonException();
            }

            result = new LeaderStructuredResponse(visibleResponse, proposal, memoryCommands, memoryCommandError)
            {
                SummaryDeltas = summaryDeltas,
                SummaryDeltaError = summaryDeltaError,
                AuthorityConfirmation = authorityConfirmation,
                EvolutionCandidates = evolutionCandidates
            };
            return true;
        }
        catch (Exception) { return false; }
    }

    private static bool TryExtractLastEnvelope(string? text, out string envelopeJson)
    {
        envelopeJson = string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var utf8 = Encoding.UTF8.GetBytes(text);
        var reader = new Utf8JsonReader(
            utf8,
            isFinalBlock: true,
            state: new JsonReaderState(new JsonReaderOptions { AllowMultipleValues = true }));
        string? last = null;
        try
        {
            while (true)
            {
                using var document = JsonDocument.ParseValue(ref reader);
                var root = document.RootElement;
                if (!root.TryGetProperty("response", out var response) || response.ValueKind != JsonValueKind.String)
                    return false;

                last = root.GetRawText();
                if (!reader.Read()) break;
                if (reader.TokenType != JsonTokenType.StartObject) return false;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        if (last is null) return false;
        envelopeJson = last;
        return true;
    }

    private static string? NormalizeStructuredText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal)) return trimmed;

        var firstLineEnd = trimmed.IndexOf('\n');
        if (firstLineEnd < 0) return null;

        var body = trimmed[(firstLineEnd + 1)..].Trim();
        if (body.EndsWith("```", StringComparison.Ordinal))
        {
            body = body[..^3].TrimEnd();
        }

        return body;
    }

    private static LeaderMemoryCommands ParseMemoryCommands(JsonElement memory)
    {
        if (memory.ValueKind != JsonValueKind.Object) throw new JsonException();
        if (memory.TryGetProperty("daily_summary", out var dailySummary) && dailySummary.ValueKind != JsonValueKind.Null)
            throw new InvalidOperationException("Daily Summary commands are not part of the Leader response envelope.");
        if (!memory.TryGetProperty("library_proposal", out var library) || library.ValueKind == JsonValueKind.Null)
            return new LeaderMemoryCommands(null);
        if (library.ValueKind != JsonValueKind.Object) throw new JsonException();

        var actionText = RequiredString(library, "action");
        if (!Enum.TryParse<LibraryProposalAction>(actionText, false, out var action) || !Enum.IsDefined(action))
            throw new ArgumentException("Unsupported Library Proposal action.");
        var materials = library.GetProperty("materials").EnumerateArray()
            .Select(item => new LibraryMaterialReferenceDraft(
                RequiredString(item, "kind"),
                RequiredString(item, "reference"),
                OptionalString(item, "label")))
            .ToArray();
        return new LeaderMemoryCommands(new LeaderLibraryProposalCommand(
            action,
            OptionalGuid(library, "target_object_id"),
            OptionalGuid(library, "target_node_id"),
            OptionalInt32(library, "expected_node_revision"),
            OptionalInt32(library, "expected_overview_revision"),
            RequiredString(library, "category"),
            RequiredString(library, "topic"),
            DateOnly.ParseExact(RequiredString(library, "local_date"), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            RequiredString(library, "node_content"),
            OptionalString(library, "current_overview"),
            materials,
            OptionalDateTimeOffset(library, "occurred_at")));
    }

    private static AuthorityConfirmationDraft ParseAuthorityConfirmation(JsonElement authority, Guid projectId)
    {
        var title = RequiredString(authority, "title");
        var contributions = authority.GetProperty("contributions").EnumerateArray()
            .Select(item => new AcceptedContributionInstruction(
                RequiredString(item, "statement"),
                new ContributionScopeTarget.Project(new ProjectRef(projectId)),
                null,
                null))
            .ToArray();
        if (contributions.Length == 0)
            throw new JsonException();
        return new AuthorityConfirmationDraft(projectId, title, contributions);
    }

    private static LeaderEvolutionCandidate ParseEvolutionCandidate(JsonElement value) =>
        new(
            RequiredString(value, "object"),
            RequiredString(value, "object_kind"),
            RequiredString(value, "change_type"),
            OptionalString(value, "before"),
            OptionalString(value, "after"),
            ParseEnum<LeaderEvolutionImpactClass>(value, "impact_class"),
            ParseEnum<LeaderEvolutionRouteHint>(value, "route_hint"),
            RequiredString(value, "reason"),
            RequiredString(value, "source_ref"));

    private static T ParseEnum<T>(JsonElement value, string property)
        where T : struct, Enum
    {
        var text = RequiredString(value, property);
        return Enum.TryParse<T>(text, false, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new JsonException();
    }

    private static IReadOnlyList<SummaryDelta> ParseSummaryDeltas(JsonElement summary)
    {
        if (summary.ValueKind == JsonValueKind.Null) return [];
        if (summary.ValueKind != JsonValueKind.Array) throw new JsonException();

        var deltas = new List<SummaryDelta>();
        foreach (var item in summary.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) throw new JsonException();
            var occurredAtText = RequiredString(item, "occurred_at");
            if (!SummaryOccurredAtPattern.IsMatch(occurredAtText) ||
                !DateTimeOffset.TryParse(
                    occurredAtText,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var occurredAt))
            {
                throw new FormatException("Summary occurred_at must use the round-trip format.");
            }

            var kindText = RequiredString(item, "kind");
            if (!Enum.TryParse<SummaryDeltaKind>(kindText, false, out var kind) || !Enum.IsDefined(kind))
            {
                throw new ArgumentException("Unsupported Summary Delta kind.");
            }

            var text = RequiredString(item, "text");
            if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Summary Delta text is required.");

            var sourceArray = item.GetProperty("source_refs");
            if (sourceArray.ValueKind != JsonValueKind.Array) throw new JsonException();
            var sourceRefs = sourceArray.EnumerateArray()
                .Select(source => new SummarySourceRef(
                    RequiredString(source, "source_kind"),
                    RequiredString(source, "source_locator")))
                .ToArray();
            deltas.Add(new SummaryDelta(occurredAt, kind, text, sourceRefs));
        }
        return deltas;
    }

    private static string RequiredString(JsonElement value, string property) =>
        value.GetProperty(property).ValueKind == JsonValueKind.String
            ? value.GetProperty(property).GetString() ?? throw new JsonException()
            : throw new JsonException();

    private static string? OptionalString(JsonElement value, string property)
    {
        var item = value.GetProperty(property);
        return item.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => item.GetString(),
            _ => throw new JsonException()
        };
    }

    private static Guid? OptionalGuid(JsonElement value, string property)
    {
        var text = OptionalString(value, property);
        return text is null ? null : Guid.Parse(text);
    }

    private static int? OptionalInt32(JsonElement value, string property)
    {
        var item = value.GetProperty(property);
        return item.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.Number when item.TryGetInt32(out var number) => number,
            _ => throw new JsonException()
        };
    }

    private static DateTimeOffset? OptionalDateTimeOffset(JsonElement value, string property)
    {
        if (!value.TryGetProperty(property, out var item) || item.ValueKind == JsonValueKind.Null)
            return null;
        if (item.ValueKind != JsonValueKind.String || !DateTimeOffset.TryParse(item.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            throw new JsonException();
        return parsed;
    }
}

public static class LeaderResponseSchema
{
    public const string Json = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["response", "draft_proposal", "memory_commands", "authority_confirmation", "summary_deltas", "evolution_candidates"],
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
            },
            "memory_commands": {
              "anyOf": [
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["daily_summary", "library_proposal"],
                  "properties": {
                    "daily_summary": { "type": "null" },
                    "library_proposal": {
                      "anyOf": [
                        {
                          "type": "object",
                          "additionalProperties": false,
                          "required": ["action", "target_object_id", "target_node_id", "expected_node_revision", "expected_overview_revision", "category", "topic", "local_date", "node_content", "current_overview", "materials", "occurred_at"],
                          "properties": {
                            "action": { "type": "string", "enum": ["CreateNode", "UpdateNode"] },
                            "target_object_id": { "type": ["string", "null"], "description": "Library Object ID only. For a new object use null. Never use an Assignment ID, Attempt ID, Session ID, or other provenance ID." },
                            "target_node_id": { "type": ["string", "null"] },
                            "expected_node_revision": { "type": ["integer", "null"] },
                            "expected_overview_revision": { "type": ["integer", "null"] },
                            "category": { "type": "string" },
                            "topic": { "type": "string" },
                            "local_date": { "type": "string" },
                            "node_content": { "type": "string" },
                            "current_overview": { "type": ["string", "null"] },
                            "materials": {
                              "type": "array",
                              "items": {
                                "type": "object",
                                "additionalProperties": false,
                                "required": ["kind", "reference", "label"],
                                "properties": {
                                  "kind": { "type": "string" },
                                  "reference": { "type": "string" },
                                  "label": { "type": ["string", "null"] }
                                }
                              }
                            },
                            "occurred_at": { "type": ["string", "null"] }
                          }
                        },
                        { "type": "null" }
                      ]
                    }
                  }
                },
                { "type": "null" }
              ]
            },
            "authority_confirmation": {
              "anyOf": [
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["title", "contributions"],
                  "properties": {
                    "title": { "type": "string" },
                    "contributions": {
                      "type": "array",
                      "minItems": 1,
                      "items": {
                        "type": "object",
                        "additionalProperties": false,
                        "required": ["statement"],
                        "properties": { "statement": { "type": "string" } }
                      }
                    }
                  }
                },
                { "type": "null" }
              ]
            },
            "summary_deltas": {
              "anyOf": [
                {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "additionalProperties": false,
                    "required": ["occurred_at", "kind", "text", "source_refs"],
                    "properties": {
                      "occurred_at": { "type": "string" },
                      "kind": { "type": "string", "enum": ["Decision", "Change", "Constraint", "RejectedPath", "Unresolved"] },
                      "text": { "type": "string" },
                      "source_refs": {
                        "type": "array",
                        "items": {
                          "type": "object",
                          "additionalProperties": false,
                          "required": ["source_kind", "source_locator"],
                          "properties": {
                            "source_kind": { "type": "string" },
                            "source_locator": { "type": "string" }
                          }
                        }
                      }
                    }
                  }
                },
                { "type": "null" }
              ]
            },
            "evolution_candidates": {
              "type": "array",
              "maxItems": 3,
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["object", "object_kind", "change_type", "before", "after", "impact_class", "route_hint", "reason", "source_ref"],
                "properties": {
                  "object": { "type": "string" },
                  "object_kind": { "type": "string" },
                  "change_type": { "type": "string" },
                  "before": { "type": ["string", "null"] },
                  "after": { "type": ["string", "null"] },
                  "impact_class": { "type": "string", "enum": ["WorldRule", "ProjectStructure", "CharacterOrObject", "Content", "Architecture", "Unclassified"] },
                  "route_hint": { "type": "string", "enum": ["AuthorityConfirmation", "LibraryProposal", "NoGovernance", "Unclassified"] },
                  "reason": { "type": "string" },
                  "source_ref": { "type": "string" }
                }
              }
            }
          }
        }
        """;
}

public static class LeaderSummaryAdmissionInstruction
{
    private static readonly string[] ReadOnlyMarkers =
    [
        "read-only", "readonly", "audit", "审计", "只读", "查询", "query", "inspect", "检查", "查看", "review current state"
    ];

    public const string Text = """
        WORKBENCH SUMMARY ADMISSION
        A Summary Delta is SPARSE DURABLE RATIONALE, not a routine activity log.
        Emit a Summary Delta only when deleting it would make a future Leader more likely to make a wrong decision, repeat an important dead end, or misunderstand the reason for a current constraint or decision.
        If deletion does not materially increase future decision error, emit no Summary.

        Allowed kinds are only Decision, Change, Constraint, RejectedPath, and Unresolved.
        Do not emit tests passed, build passed, files changed, Worker PASS, git status, ordinary implementation steps, routine tool output, session chatter, or a generic Fact, Note, Progress, Result, or Memory.

        Add source_refs only when a natural source locator exists. When none exists, use source_refs = [].
        Do not fabricate a Source, session locator, Git ref, evidence locator, or other provenance.
        Do not copy evidence bodies, transcripts, logs, or diffs into source_refs.

        For a read-only, audit, inspection, or query request, emit summary_deltas = null.
        Merely re-observing an existing fact is not a Decision or Change. Do not summarize it.
        """;

    public static bool IsReadOnlyRequest(string text) =>
        ReadOnlyMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
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
