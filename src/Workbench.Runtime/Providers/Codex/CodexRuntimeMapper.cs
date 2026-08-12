using System.Text.Json;
using Workbench.Runtime.Agents;

namespace Workbench.Runtime.Providers.Codex;

internal static class CodexRuntimeMapper
{
    private static readonly ProviderId CodexProviderId = new("codex");

    private const AgentCapability ProvenCapabilities =
        AgentCapability.PersistentSession |
        AgentCapability.Resume |
        AgentCapability.StructuredEvents |
        AgentCapability.Stop |
        AgentCapability.Transcript |
        AgentCapability.ParallelSessions;

    public static IReadOnlyList<ModelProfile> MapModels(JsonElement result) =>
        result.GetProperty("data")
            .EnumerateArray()
            .Select(model => new ModelProfile(
                CodexProviderId,
                model.GetProperty("id").GetString()!,
                model.GetProperty("displayName").GetString()!,
                ProvenCapabilities))
            .ToArray();

    public static AgentSession MapSession(
        JsonElement result,
        ProviderAccountId accountId,
        string requestedModelId)
    {
        var now = DateTimeOffset.UtcNow;
        var thread = result.GetProperty("thread");
        var modelId = result.TryGetProperty("model", out var model)
            ? model.GetString() ?? requestedModelId
            : requestedModelId;

        return new AgentSession(
            AgentSessionId.New(),
            accountId,
            CodexProviderId,
            modelId,
            thread.GetProperty("id").GetString(),
            AgentSessionStatus.Ready,
            now,
            now);
    }

    public static AgentEvent? MapNotification(
        string method,
        JsonElement parameters,
        AgentSessionId sessionId,
        string? finalText = null)
    {
        var occurredAt = DateTimeOffset.UtcNow;

        return method switch
        {
            "item/agentMessage/delta" => new AgentTextDelta(
                parameters.GetProperty("delta").GetString() ?? string.Empty,
                occurredAt),
            "turn/started" => new AgentStatusChanged(AgentSessionStatus.Running, occurredAt),
            "turn/completed" => MapCompletion(parameters, sessionId, finalText, occurredAt),
            "error" => new AgentError(parameters.GetRawText(), occurredAt),
            _ => null
        };
    }

    public static IReadOnlyList<AgentEvent> MapTranscript(JsonElement result)
    {
        var occurredAt = DateTimeOffset.UtcNow;
        var transcript = new List<AgentEvent>();

        foreach (var turn in result.GetProperty("thread").GetProperty("turns").EnumerateArray())
        {
            foreach (var item in turn.GetProperty("items").EnumerateArray())
            {
                var type = item.GetProperty("type").GetString();
                if (type == "userMessage")
                {
                    foreach (var input in item.GetProperty("content").EnumerateArray())
                    {
                        if (input.GetProperty("type").GetString() == "text")
                        {
                            transcript.Add(new AgentMessage(
                                AgentMessageRole.User,
                                input.GetProperty("text").GetString() ?? string.Empty,
                                occurredAt));
                        }
                    }
                }
                else if (type == "agentMessage")
                {
                    transcript.Add(new AgentMessage(
                        AgentMessageRole.Assistant,
                        item.GetProperty("text").GetString() ?? string.Empty,
                        occurredAt));
                }
            }
        }

        return transcript;
    }

    private static AgentTurnCompleted MapCompletion(
        JsonElement parameters,
        AgentSessionId sessionId,
        string? finalText,
        DateTimeOffset occurredAt)
    {
        var turn = parameters.GetProperty("turn");
        var status = turn.GetProperty("status").GetString() switch
        {
            "completed" => AgentSessionStatus.Completed,
            "interrupted" => AgentSessionStatus.Interrupted,
            "failed" => AgentSessionStatus.Failed,
            _ => AgentSessionStatus.Running
        };
        var error = turn.TryGetProperty("error", out var errorElement) && errorElement.ValueKind != JsonValueKind.Null
            ? errorElement.GetRawText()
            : null;

        return new AgentTurnCompleted(
            new AgentResult(sessionId, status, finalText, error),
            occurredAt);
    }
}
