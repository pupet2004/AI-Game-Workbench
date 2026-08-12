using Workbench.App.Memory;
using Workbench.Storage.Leaders;

namespace Workbench.App.Tests;

public sealed class ProjectMemorySynthesisPayloadParserTests
{
    private static readonly Guid EpochId = Guid.Parse("10000000-0000-4000-8000-000000000001");

    [Fact]
    public void Valid_synthesis_json_is_parsed_and_sequences_become_stable_message_ids()
    {
        var messages = Messages();
        const string json = """
            {
              "learned": [{"topic":"Current Phase","content":"M1.5B","source_message_sequences":[1]}],
              "candidates": [{"topic":"Memory Ownership","content":"Workbench owns memory.","source_message_sequences":[2]}]
            }
            """;

        var result = ProjectMemorySynthesisPayloadParser.Parse(json, messages);

        Assert.Equal("Current Phase", Assert.Single(result.Learned).Topic);
        Assert.Equal([101L], Assert.Single(result.Learned).SourceMessageIds);
        Assert.Equal("Memory Ownership", Assert.Single(result.Candidates).Topic);
        Assert.Equal([102L], Assert.Single(result.Candidates).SourceMessageIds);
    }

    [Fact]
    public void Invalid_json_is_rejected()
    {
        Assert.Throws<ProjectMemorySynthesisPayloadException>(
            () => ProjectMemorySynthesisPayloadParser.Parse("```json\n{}\n```", Messages()));
    }

    [Theory]
    [InlineData("learned")]
    [InlineData("candidates")]
    public void More_than_five_items_is_rejected(string collection)
    {
        var repeated = string.Join(',', Enumerable.Repeat("{\"topic\":\"T\",\"content\":\"C\",\"source_message_sequences\":[]}", 6));
        var json = collection == "learned"
            ? $"{{\"learned\":[{repeated}],\"candidates\":[]}}"
            : $"{{\"learned\":[],\"candidates\":[{repeated}]}}";

        Assert.Throws<ProjectMemorySynthesisPayloadException>(
            () => ProjectMemorySynthesisPayloadParser.Parse(json, Messages()));
    }

    [Fact]
    public void Oversized_topic_or_content_is_rejected_by_utf8_bytes()
    {
        var topic = new string('界', 67);
        var content = new string('界', 2667);

        Assert.Throws<ProjectMemorySynthesisPayloadException>(() =>
            ProjectMemorySynthesisPayloadParser.Parse(Payload(topic, "ok", []), Messages()));
        Assert.Throws<ProjectMemorySynthesisPayloadException>(() =>
            ProjectMemorySynthesisPayloadParser.Parse(Payload("ok", content, []), Messages()));
    }

    [Fact]
    public void Unknown_message_sequence_is_rejected_before_returning_any_payload()
    {
        var error = Assert.Throws<ProjectMemorySynthesisPayloadException>(() =>
            ProjectMemorySynthesisPayloadParser.Parse(Payload("Rule", "Content", [99]), Messages()));

        Assert.Contains("99", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_required_arrays_or_blank_values_are_rejected()
    {
        Assert.Throws<ProjectMemorySynthesisPayloadException>(() =>
            ProjectMemorySynthesisPayloadParser.Parse("{\"learned\":[]}", Messages()));
        Assert.Throws<ProjectMemorySynthesisPayloadException>(() =>
            ProjectMemorySynthesisPayloadParser.Parse(Payload(" ", "Content", []), Messages()));
    }

    private static string Payload(string topic, string content, IReadOnlyList<long> sequences) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            learned = new[] { new { topic, content, source_message_sequences = sequences } },
            candidates = Array.Empty<object>()
        });

    private static IReadOnlyDictionary<long, StoredLeaderMessage> Messages() =>
        new Dictionary<long, StoredLeaderMessage>
        {
            [1] = new(101, EpochId, 1, "user", "Decision", DateTimeOffset.UnixEpoch),
            [2] = new(102, EpochId, 2, "assistant", "Acknowledged", DateTimeOffset.UnixEpoch)
        };
}
