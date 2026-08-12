using System.Text;
using Workbench.App.Memory;
using Workbench.Storage.Memory;
using Workbench.Storage.Leaders;

namespace Workbench.App.Tests;

public sealed class ProjectMemorySynthesisPromptBuilderTests
{
    [Fact]
    public void Prompt_defines_batch_json_durable_memory_and_formal_safety_without_transcript_replay()
    {
        var prompt = ProjectMemorySynthesisPromptBuilder.Build(
            [Memory("Formal", "Ownership", "Workbench owns project memory.")],
            [Memory("Learned", "Phase", "Building M1.5B.")]);

        Assert.Contains("strict JSON", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("source_message_sequences", prompt, StringComparison.Ordinal);
        Assert.Contains("at most 5", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("explicit user decisions", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Learned", prompt, StringComparison.Ordinal);
        Assert.Contains("Candidate", prompt, StringComparison.Ordinal);
        Assert.Contains("cannot create Formal", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do not read files", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do not use tools", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("review the conversation context already present in this archived session", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("handoff_summary", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("leader_messages", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Formal_and_learned_contexts_keep_newest_complete_items_with_exact_byte_caps()
    {
        var now = DateTimeOffset.Parse("2026-08-13T00:00:00+00:00");
        var formal = Enumerable.Range(0, 20)
            .Select(index => Memory("Formal", $"F{index}", new string('界', 1000), now.AddMinutes(index)))
            .ToArray();
        var learned = Enumerable.Range(0, 20)
            .Select(index => Memory("Learned", $"L{index}", new string('忆', 1000), now.AddMinutes(index)))
            .ToArray();

        var prompt = ProjectMemorySynthesisPromptBuilder.Build(formal, learned);
        var formalSection = Between(prompt, "<formal_memory>\n", "\n</formal_memory>");
        var learnedSection = Between(prompt, "<learned_memory>\n", "\n</learned_memory>");

        Assert.True(Encoding.UTF8.GetByteCount(formalSection) <= 12000);
        Assert.True(Encoding.UTF8.GetByteCount(learnedSection) <= 8000);
        Assert.Contains("F19", formalSection, StringComparison.Ordinal);
        Assert.DoesNotContain("F0", formalSection, StringComparison.Ordinal);
        Assert.Contains("L19", learnedSection, StringComparison.Ordinal);
        Assert.DoesNotContain("L0", learnedSection, StringComparison.Ordinal);
        Assert.DoesNotContain('\uFFFD', prompt);
    }

    [Fact]
    public void Prompt_exposes_only_valid_workbench_sequence_roles_without_replaying_message_text()
    {
        var epochId = Guid.NewGuid();
        var prompt = ProjectMemorySynthesisPromptBuilder.Build(
            [],
            [],
            [
                new StoredLeaderMessage(11, epochId, 1, "user", "SECRET_USER_TEXT", DateTimeOffset.UnixEpoch),
                new StoredLeaderMessage(12, epochId, 2, "assistant", "SECRET_ASSISTANT_TEXT", DateTimeOffset.UnixEpoch)
            ]);

        Assert.Contains("1:user", prompt, StringComparison.Ordinal);
        Assert.Contains("2:assistant", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET_USER_TEXT", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET_ASSISTANT_TEXT", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("11", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("12", prompt, StringComparison.Ordinal);
    }

    private static ProjectMemoryItem Memory(
        string layer,
        string topic,
        string content,
        DateTimeOffset? updated = null)
    {
        var at = updated ?? DateTimeOffset.UnixEpoch;
        return new(Guid.NewGuid(), Guid.NewGuid(), layer, topic, content, "Active", at, at, layer == "Formal" ? at : null);
    }

    private static string Between(string value, string start, string end)
    {
        var startIndex = value.IndexOf(start, StringComparison.Ordinal) + start.Length;
        var endIndex = value.IndexOf(end, startIndex, StringComparison.Ordinal);
        return value[startIndex..endIndex];
    }
}
