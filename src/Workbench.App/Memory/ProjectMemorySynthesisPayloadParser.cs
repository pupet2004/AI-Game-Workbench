using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Workbench.Storage.Leaders;
using Workbench.Storage.Memory;

namespace Workbench.App.Memory;

public sealed class ProjectMemorySynthesisPayloadException : Exception
{
    public ProjectMemorySynthesisPayloadException(string message) : base(message)
    {
    }

    public ProjectMemorySynthesisPayloadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed record ProjectMemorySynthesisPayload(
    IReadOnlyList<ProjectMemorySynthesisItem> Learned,
    IReadOnlyList<ProjectMemorySynthesisItem> Candidates);

public static class ProjectMemorySynthesisPayloadParser
{
    public static ProjectMemorySynthesisPayload Parse(
        string json,
        IReadOnlyDictionary<long, StoredLeaderMessage> messagesBySequence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentNullException.ThrowIfNull(messagesBySequence);
        PayloadDto payload;
        try
        {
            payload = JsonSerializer.Deserialize<PayloadDto>(json, SerializerOptions)
                ?? throw new ProjectMemorySynthesisPayloadException("Synthesis output must be a JSON object.");
        }
        catch (ProjectMemorySynthesisPayloadException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new ProjectMemorySynthesisPayloadException("Synthesis output is not valid JSON.", exception);
        }

        if (payload.Learned is null || payload.Candidates is null)
        {
            throw new ProjectMemorySynthesisPayloadException("Synthesis output requires learned and candidates arrays.");
        }
        if (payload.Learned.Count > 5 || payload.Candidates.Count > 5)
        {
            throw new ProjectMemorySynthesisPayloadException("Synthesis output may contain at most 5 learned items and 5 candidates.");
        }

        return new(
            Convert(payload.Learned, messagesBySequence),
            Convert(payload.Candidates, messagesBySequence));
    }

    private static IReadOnlyList<ProjectMemorySynthesisItem> Convert(
        IReadOnlyList<ItemDto> items,
        IReadOnlyDictionary<long, StoredLeaderMessage> messagesBySequence)
    {
        var converted = new List<ProjectMemorySynthesisItem>(items.Count);
        foreach (var item in items)
        {
            ValidateUtf8(item.Topic, 200, "topic");
            ValidateUtf8(item.Content, 8000, "content");
            if (item.SourceMessageSequences is null)
            {
                throw new ProjectMemorySynthesisPayloadException("source_message_sequences is required.");
            }

            var messageIds = new List<long>();
            foreach (var sequence in item.SourceMessageSequences.Distinct())
            {
                if (!messagesBySequence.TryGetValue(sequence, out var message))
                {
                    throw new ProjectMemorySynthesisPayloadException($"Source message sequence {sequence} does not exist in the archived epoch.");
                }
                messageIds.Add(message.Id);
            }
            converted.Add(new(item.Topic!.Trim(), item.Content!.Trim(), messageIds));
        }
        return converted;
    }

    private static void ValidateUtf8(string? value, int maxBytes, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ProjectMemorySynthesisPayloadException($"Synthesis {name} cannot be blank.");
        }
        if (Encoding.UTF8.GetByteCount(value) > maxBytes)
        {
            throw new ProjectMemorySynthesisPayloadException($"Synthesis {name} exceeds {maxBytes} UTF-8 bytes.");
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.Strict
    };

    private sealed class PayloadDto
    {
        [JsonPropertyName("learned")]
        public List<ItemDto>? Learned { get; init; }

        [JsonPropertyName("candidates")]
        public List<ItemDto>? Candidates { get; init; }
    }

    private sealed class ItemDto
    {
        [JsonPropertyName("topic")]
        public string? Topic { get; init; }

        [JsonPropertyName("content")]
        public string? Content { get; init; }

        [JsonPropertyName("source_message_sequences")]
        public List<long>? SourceMessageSequences { get; init; }
    }
}
