using System.Text.Json;
using System.Text;

namespace Workbench.App.Worker;

public enum WorkerHandoffKind { NeedsLeaderDecision, FinalReport }

public sealed record WorkerHandoffPayload(
    WorkerHandoffKind Kind,
    string Message,
    string? ValidationSummary,
    IReadOnlyList<string> ProposedChanges);

public static class WorkerHandoffPayloadParser
{
    public const string OutputSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "Kind": { "type": "string", "enum": ["NeedsLeaderDecision", "FinalReport"] },
            "Message": { "type": "string" },
            "ValidationSummary": { "type": ["string", "null"] },
            "ProposedChanges": { "type": "array", "items": { "type": "string" } }
          },
          "required": ["Kind", "Message", "ValidationSummary", "ProposedChanges"]
        }
        """;

    public static bool TryParse(string text, out WorkerHandoffPayload? payload)
    {
        payload = null;
        if (TryParseDocument(text, out payload)) return true;

        // ACP can concatenate progress messages and the final structured report.
        // Only one explicit, terminal report is eligible; prose is never a proposal.
        var bytes = Encoding.UTF8.GetBytes(text);
        var offset = 0;
        var reports = 0;
        var terminal = false;
        WorkerHandoffPayload? candidate = null;
        while (offset < bytes.Length)
        {
            var next = bytes.AsSpan(offset).IndexOf((byte)'{');
            if (next < 0) break;
            offset += next;
            try
            {
                var reader = new Utf8JsonReader(bytes.AsSpan(offset));
                using var document = JsonDocument.ParseValue(ref reader);
                offset += checked((int)reader.BytesConsumed);
                if (!TryParseDocument(document.RootElement.GetRawText(), out var parsed)) continue;
                reports++;
                candidate = parsed;
                var suffix = Encoding.UTF8.GetString(bytes.AsSpan(offset)).Trim();
                terminal = suffix.Length == 0 || suffix == "```";
            }
            catch (JsonException)
            {
                offset++;
            }
        }
        if (reports != 1 || !terminal) return false;
        payload = candidate;
        return true;
    }

    private static bool TryParseDocument(string text, out WorkerHandoffPayload? payload)
    {
        payload = null;
        try
        {
            var parsed = JsonSerializer.Deserialize<WorkerHandoffPayloadDocument>(text);
            if (parsed is null || !Enum.TryParse<WorkerHandoffKind>(parsed.Kind, true, out var kind) ||
                !Enum.IsDefined(kind) || string.IsNullOrWhiteSpace(parsed.Message)) return false;
            payload = new WorkerHandoffPayload(
                kind,
                parsed.Message,
                parsed.ValidationSummary,
                parsed.ProposedChanges ?? []);
            return true;
        }
        catch (JsonException) { return false; }
    }

    private sealed record WorkerHandoffPayloadDocument(
        string? Kind,
        string? Message,
        string? ValidationSummary,
        string[]? ProposedChanges);
}
