using System.Text;
using Workbench.Storage.Memory;
using Workbench.Storage.Leaders;

namespace Workbench.App.Memory;

public static class ProjectMemorySynthesisPromptBuilder
{
    public const int FormalContextMaxUtf8Bytes = 12000;
    public const int LearnedContextMaxUtf8Bytes = 8000;

    public static string Build(
        IReadOnlyList<ProjectMemoryItem> formal,
        IReadOnlyList<ProjectMemoryItem> learned,
        IReadOnlyList<StoredLeaderMessage>? archivedMessages = null)
    {
        ArgumentNullException.ThrowIfNull(formal);
        ArgumentNullException.ThrowIfNull(learned);
        var formalContext = BuildContext(formal, FormalContextMaxUtf8Bytes);
        var learnedContext = BuildContext(learned, LearnedContextMaxUtf8Bytes);
        var validSequences = archivedMessages is null
            ? string.Empty
            : string.Join(", ", archivedMessages
                .OrderBy(message => message.Sequence)
                .Select(message => $"{message.Sequence}:{message.Role}"));
        return $$"""
            You are performing one internal Workbench memory-maintenance turn.
            Review the conversation context already present in this archived session. Do not ask for a transcript and do not repeat it.

            Return strict JSON only, with no markdown fences, reasoning, or explanation:
            {
              "learned": [{"topic":"...","content":"...","source_message_sequences":[1,2]}],
              "candidates": [{"topic":"...","content":"...","source_message_sequences":[3,4]}]
            }

            Produce at most 5 Learned items and at most 5 Candidate items. Include only durable, future-useful project information.
            Learned describes tentative current understanding such as development phase or direction. Candidate is for long-lived architecture decisions, design decisions, product rules, constraints, project-specific user preferences, and stable workflow rules.
            Explicit user decisions such as "adopt A", "use this", or "do not do this" are high-value Candidate evidence.
            Temporary debug state, current time, Git HEAD, failed tests, temporary paths, casual conversation, and assistant speculation belong in neither collection.
            You may propose Candidate memory, but you cannot create Formal memory. Formal always requires later user certification.
            Use only this archived session's existing context and the bounded Workbench memory below. Do not read files, do not run commands, do not use tools, and do not request approval.
            Every source_message_sequences value must be a real message sequence from this archived session.
            Valid Workbench message sequences (sequence:role only; message text remains in session context): {{validSequences}}

            <formal_memory>
            {{formalContext}}
            </formal_memory>
            <learned_memory>
            {{learnedContext}}
            </learned_memory>
            """;
    }

    private static string BuildContext(IReadOnlyList<ProjectMemoryItem> items, int maxUtf8Bytes)
    {
        var builder = new StringBuilder();
        foreach (var item in items
                     .Where(item => item.Status == "Active")
                     .OrderByDescending(item => item.UpdatedAt)
                     .ThenByDescending(item => item.Id))
        {
            var line = $"- {item.Topic}: {item.Content}";
            var candidate = builder.Length == 0 ? line : $"{builder}\n{line}";
            if (Encoding.UTF8.GetByteCount(candidate) > maxUtf8Bytes)
            {
                continue;
            }
            if (builder.Length > 0)
            {
                builder.Append('\n');
            }
            builder.Append(line);
        }
        return builder.ToString();
    }
}
