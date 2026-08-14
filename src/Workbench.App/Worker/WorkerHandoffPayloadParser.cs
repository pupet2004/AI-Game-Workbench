using System.Text.Json;

namespace Workbench.App.Worker;

public enum WorkerHandoffKind { NeedsLeaderDecision, FinalReport }

public sealed record WorkerHandoffPayload(WorkerHandoffKind Kind, string Message, string? ValidationSummary);

public static class WorkerHandoffPayloadParser
{
    public static bool TryParse(string text, out WorkerHandoffPayload? payload)
    {
        payload = null;
        try
        {
            var parsed = JsonSerializer.Deserialize<WorkerHandoffPayloadDocument>(text);
            if (parsed is null || !Enum.TryParse<WorkerHandoffKind>(parsed.Kind, true, out var kind) || string.IsNullOrWhiteSpace(parsed.Message)) return false;
            payload = new WorkerHandoffPayload(kind, parsed.Message, parsed.ValidationSummary);
            return true;
        }
        catch (JsonException) { return false; }
    }

    private sealed record WorkerHandoffPayloadDocument(string? Kind, string? Message, string? ValidationSummary);
}
