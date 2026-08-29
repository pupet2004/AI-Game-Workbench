using System.Text;
using Workbench.Runtime.Agents;
using Workbench.Storage.Leaders;
using Workbench.Storage.Memory;
using Workbench.App.Memory;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.Leader;

public interface ILeaderBootContextBuilder
{
    Task<AgentRequest> BuildAsync(
        CoreProject project,
        string originalUserText,
        CancellationToken cancellationToken = default);
}

public sealed class LeaderBootContextBuilder : ILeaderBootContextBuilder
{
    public const int MaxFormalUtf8Bytes = 12000;
    public const int MaxLearnedUtf8Bytes = 8000;
    public const int MaxHandoffUtf8Bytes = LeaderHandoffBuilder.MaxHandoffUtf8Bytes;
    public const int MaxGeneratedContextUtf8Bytes = 32000;
    private const string FormalAuthorityNote =
        "This memory is user-certified and authoritative unless the current user message overrides it.";
    private const string LearnedAuthorityNote =
        "This memory is AI-synthesized, provisional, and lower authority than user-certified memory.";

    private readonly ProjectMemoryRepository? _memories;
    private readonly IProjectMemoryApi? _memoryApi;
    private readonly LeaderEpochContinuityRepository? _continuityPlans;
    private readonly LeaderSessionEpochRepository _epochs;

    public LeaderBootContextBuilder(ProjectMemoryRepository memories, LeaderSessionEpochRepository epochs)
    {
        _memories = memories ?? throw new ArgumentNullException(nameof(memories));
        _epochs = epochs ?? throw new ArgumentNullException(nameof(epochs));
    }

    public LeaderBootContextBuilder(IProjectMemoryApi memoryApi, LeaderSessionEpochRepository epochs, LeaderEpochContinuityRepository continuityPlans)
    {
        _memoryApi = memoryApi ?? throw new ArgumentNullException(nameof(memoryApi));
        _epochs = epochs ?? throw new ArgumentNullException(nameof(epochs));
        _continuityPlans = continuityPlans ?? throw new ArgumentNullException(nameof(continuityPlans));
    }

    public async Task<AgentRequest> BuildAsync(
        CoreProject project,
        string originalUserText,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalUserText);
        if (_memoryApi is not null)
        {
            var current = await _epochs.GetCurrentForProjectAsync(project.Id, cancellationToken);
            var plan = current is null ? null : await _continuityPlans!.GetAsync(project.Id, current.Id, cancellationToken);
            var resolved = plan is null
                ? new ResolvedContinuityBundle(project.Id, [], 0, [])
                : await _memoryApi.ResolveContinuityAsync(project.Id, plan, cancellationToken);
            return BuildSelected(project, resolved.Materials, originalUserText);
        }

        var formal = await _memories!.GetAsync(project.Id, "Formal", "Active", cancellationToken);
        var learned = await _memories.GetAsync(project.Id, "Learned", "Active", cancellationToken);
        var predecessor = await _epochs.GetMostRecentArchivedForProjectAsync(project.Id, cancellationToken);
        return Build(project, formal, learned, predecessor?.HandoffSummary, originalUserText);
    }

    internal static AgentRequest BuildSelected(
        CoreProject project,
        IReadOnlyList<ResolvedContinuityMaterial> materials,
        string originalUserText)
    {
        var builder = new StringBuilder();
        builder.AppendLine("WORKBENCH PROJECT CONTEXT");
        AppendLeaderContracts(builder);
        builder.AppendLine();
        builder.AppendLine("PROJECT");
        builder.Append("Name: ").AppendLine(project.Name);
        builder.Append("Root: ").AppendLine(project.RootPath);
        if (materials.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("SELECTED CONTINUITY MATERIALS");
            foreach (var material in materials)
            {
                builder.AppendLine($"{material.Kind}: {material.Label}");
                builder.AppendLine(material.Content);
            }
        }
        builder.AppendLine();
        builder.AppendLine("CURRENT USER MESSAGE");
        builder.Append(originalUserText);
        return new AgentRequest(builder.ToString());
    }

    internal static AgentRequest Build(
        CoreProject project,
        IReadOnlyList<ProjectMemoryItem> formal,
        IReadOnlyList<ProjectMemoryItem> learned,
        string? handoff,
        string originalUserText)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(formal);
        ArgumentNullException.ThrowIfNull(learned);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalUserText);

        var activeFormal = formal.Where(item => IsActive(item, project.Id, "Formal")).ToArray();
        var selectedFormal = Select(
            activeFormal
                .OrderByDescending(item => item.CertifiedAt)
                .ThenByDescending(item => item.UpdatedAt)
                .ThenBy(item => item.Id),
            ItemBudget(MaxFormalUtf8Bytes, FormalAuthorityNote));
        var formalTopics = activeFormal.Select(item => NormalizeTopic(item.Topic)).ToHashSet(StringComparer.Ordinal);
        var selectedLearned = Select(
            learned.Where(item => IsActive(item, project.Id, "Learned") &&
                                  !formalTopics.Contains(NormalizeTopic(item.Topic)))
                .OrderByDescending(item => item.UpdatedAt)
                .ThenBy(item => item.Id),
            ItemBudget(MaxLearnedUtf8Bytes, LearnedAuthorityNote));

        var builder = new StringBuilder();
        builder.AppendLine("WORKBENCH PROJECT CONTEXT");
        builder.AppendLine();
        builder.AppendLine("Use this context only for the current user request. Resolve conflicts in this order:");
        builder.AppendLine("1. The current explicit user instruction");
        builder.AppendLine("2. Active Formal memory certified by the user");
        builder.AppendLine("3. Active Learned memory synthesized by AI");
        builder.AppendLine("4. The immediately previous session handoff");
        builder.AppendLine();
        AppendLeaderContracts(builder);
        builder.AppendLine();
        builder.AppendLine("PROJECT");
        builder.Append("Name: ").AppendLine(LimitUtf8(project.Name, 512));
        builder.Append("Root: ").AppendLine(LimitUtf8(project.RootPath, 1536));

        AppendMemorySection(
            builder,
            "USER-CERTIFIED PROJECT MEMORY",
            FormalAuthorityNote,
            selectedFormal);
        AppendMemorySection(
            builder,
            "AI-SYNTHESIZED PROJECT MEMORY",
            LearnedAuthorityNote,
            selectedLearned);

        if (!string.IsNullOrWhiteSpace(handoff))
        {
            const string handoffNote = "This is continuity context from the immediately previous runtime session.";
            var handoffBudget = MaxHandoffUtf8Bytes - Encoding.UTF8.GetByteCount(handoffNote) - Encoding.UTF8.GetByteCount(Environment.NewLine);
            builder.AppendLine();
            builder.AppendLine("PREVIOUS SESSION HANDOFF");
            builder.AppendLine(handoffNote);
            builder.AppendLine(LimitUtf8(handoff.Trim(), handoffBudget));
        }

        if (Encoding.UTF8.GetByteCount(builder.ToString()) > MaxGeneratedContextUtf8Bytes)
        {
            throw new InvalidOperationException("Generated Leader boot context exceeds its internal UTF-8 budget.");
        }

        builder.AppendLine();
        builder.AppendLine("CURRENT USER MESSAGE");
        builder.Append(originalUserText);
        return new AgentRequest(builder.ToString());
    }

    private static IReadOnlyList<ProjectMemoryItem> Select(
        IEnumerable<ProjectMemoryItem> ordered,
        int maxUtf8Bytes)
    {
        var selected = new List<ProjectMemoryItem>();
        var used = 0;
        foreach (var item in ordered)
        {
            var bytes = Encoding.UTF8.GetByteCount(FormatItem(item));
            if (bytes > maxUtf8Bytes - used)
            {
                continue;
            }

            selected.Add(item);
            used += bytes;
        }

        return selected;
    }

    private static int ItemBudget(int sectionBudget, string authorityNote) =>
        sectionBudget - Encoding.UTF8.GetByteCount(authorityNote) - Encoding.UTF8.GetByteCount(Environment.NewLine);

    private static void AppendMemorySection(
        StringBuilder builder,
        string heading,
        string authorityNote,
        IReadOnlyList<ProjectMemoryItem> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine(heading);
        builder.AppendLine(authorityNote);
        foreach (var item in items)
        {
            builder.Append(FormatItem(item));
        }
    }

    private static string FormatItem(ProjectMemoryItem item) =>
        $"TOPIC: {item.Topic.Trim()}\nCONTENT:\n{item.Content.Trim()}\n\n";

    private static bool IsActive(ProjectMemoryItem item, Guid projectId, string layer) =>
        item.ProjectId == projectId &&
        string.Equals(item.Layer, layer, StringComparison.Ordinal) &&
        string.Equals(item.Status, "Active", StringComparison.Ordinal);

    private static string NormalizeTopic(string value) =>
        string.Join(' ', value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    private static void AppendLeaderContracts(StringBuilder builder)
    {
        builder.AppendLine("LEADER DELEGATION CONTRACT");
        builder.AppendLine("You are the Project Leader. You may directly discuss, plan, analyze, audit, and perform very small read-only judgments.");
        builder.AppendLine("When you decide real work should be handed to a Worker, you must propose it through draft_proposal and wait for user confirmation.");
        builder.AppendLine("Before confirmation and a real Worker Session, do not claim a Worker started, executed, or returned results.");
        builder.AppendLine("Do not execute delegated work yourself and describe your result as a Worker result. Ordinary turns must set draft_proposal to null.");
        builder.AppendLine("WORKER SCOPE: In Workbench, a Worker is a Workbench-managed execution bound to an Assignment and Attempt.");
        builder.AppendLine("Determine Worker status only from Workbench Assignment, Attempt, Execution, routing, and Handoff state.");
        builder.AppendLine("Do not infer Workbench Workers from runtime-local sub-agents, helper threads, tool calls, or collaboration participants in your current Agent session.");
        builder.AppendLine("If no Workbench-managed execution exists, report that there is no active Workbench Worker, even when runtime-local collaborators are present.");
        builder.AppendLine();
        builder.AppendLine("LIBRARY PROPOSAL CONTRACT");
        builder.AppendLine("Library is the project's long-lived factual evolution archive: what is true, implemented, structured, or materially present.");
        builder.AppendLine("Use stage closure as a judgment point, not an automatic trigger. Do not propose Library updates for every important sentence.");
        builder.AppendLine("Before proposing, you may read relevant Daily Summary or source material. Daily Summary never automatically becomes Library.");
        builder.AppendLine("You decide whether to update Current Overview, update an existing Timeline Node, or create a new Timeline Node.");
        builder.AppendLine("Put an explicit proposal in memory_commands.library_proposal. It remains pending until user confirmation; never claim the Library changed before confirmation.");
        builder.AppendLine("Keep reasons, tradeoffs, and discussion in Daily Summary or source. Library contains factual state and typed source references, not copied source bodies.");
        builder.AppendLine("Ordinary turns set memory_commands to null. New Brain, review completion, Worker completion, message count, idle time, and Daily Summary changes are not automatic Library triggers.");
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
