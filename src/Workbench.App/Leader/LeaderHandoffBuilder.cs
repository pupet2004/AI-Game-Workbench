using System.Text;
using Workbench.Storage.Leaders;
using Workbench.App.Skills;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.Leader;

public static class LeaderHandoffBuilder
{
    public const int MaxHandoffUtf8Bytes = 6000;

    public static readonly string SemanticPrompt = $"""
        {WorkbenchSkillCatalog.Load(WorkbenchSkillRole.Leader)}

        You are writing a shift handoff for a fresh runtime session that will serve the same logical Project Main Leader.

        Use only the user-visible conversation from the current Session Epoch. Ignore any inherited WORKBENCH PROJECT CONTEXT envelope from the Epoch's first turn; do not summarize or repeat inherited project memory or the prior handoff. Do not read files. Do not run commands. Do not use tools. Do not request approval.

        Return a concise structured handoff below roughly 1500 tokens using exactly these headings:
        CURRENT FOCUS
        COMPLETED
        DECISIONS
        CURRENT STATE
        OPEN ISSUES
        NEXT STEP
        RELEVANT SOURCES

        Include only sources explicitly present in the conversation. Do not invent sources.
        """;

    public static string BuildFallback(
        CoreProject project,
        StoredLeaderSessionEpoch previousEpoch,
        IReadOnlyList<StoredLeaderMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(previousEpoch);
        ArgumentNullException.ThrowIfNull(messages);

        var projectName = LimitUtf8(project.Name, 512);
        var projectRoot = LimitUtf8(project.RootPath, 1536);
        var prefix = $"""
            CURRENT FOCUS
            Deterministic Workbench handoff; semantic handoff was unavailable.

            COMPLETED
            See the bounded recent conversation excerpt below.

            DECISIONS
            No additional decisions inferred by Workbench.

            CURRENT STATE
            Project: {projectName}
            Root: {projectRoot}
            Previous Epoch StartedAt: {previousEpoch.StartedAt:O}
            Previous Epoch LastActiveAt: {previousEpoch.LastActiveAt:O}

            OPEN ISSUES
            See the recent conversation excerpt.

            NEXT STEP
            Continue from the most recent visible user/assistant exchange.

            RELEVANT SOURCES
            No sources added beyond explicit conversation content.

            RECENT CONVERSATION

            """;

        prefix = LimitUtf8(prefix, MaxHandoffUtf8Bytes);
        var remaining = MaxHandoffUtf8Bytes - Encoding.UTF8.GetByteCount(prefix);
        var selected = new List<string>();
        for (var index = messages.Count - 1; index >= 0 && remaining > 0; index--)
        {
            var role = messages[index].Role == "user" ? "User" : "Assistant";
            var label = $"{role}:\n";
            var suffix = "\n\n";
            var fixedBytes = Encoding.UTF8.GetByteCount(label + suffix);
            if (fixedBytes > remaining)
            {
                break;
            }

            var boundedText = LimitUtf8(messages[index].Text, remaining - fixedBytes);
            if (boundedText.Length == 0)
            {
                continue;
            }

            var excerpt = label + boundedText + suffix;
            selected.Insert(0, excerpt);
            remaining -= Encoding.UTF8.GetByteCount(excerpt);
        }

        return prefix + string.Concat(selected);
    }

    private static string LimitUtf8(string value, int maxBytes)
    {
        if (Encoding.UTF8.GetByteCount(value) <= maxBytes)
        {
            return value;
        }

        var builder = new StringBuilder();
        var used = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (used + rune.Utf8SequenceLength > maxBytes)
            {
                break;
            }

            builder.Append(rune.ToString());
            used += rune.Utf8SequenceLength;
        }

        return builder.ToString();
    }
}
