using System.Text;
using System.Text.Json;
using Workbench.Storage.Leaders;
using Workbench.Storage.Memory;

namespace Workbench.App.Memory;

public sealed class ProjectContinuityMaterialService(
    IProjectMemoryApi memory,
    LeaderSessionEpochRepository epochs,
    LeaderMessageRepository messages,
    ProjectLibraryEvolutionRepository library)
{
    public async Task<ContinuityMaterialCatalog> ListAsync(
        Guid projectId,
        Guid epochId,
        CancellationToken cancellationToken = default)
    {
        var epoch = await epochs.GetAsync(epochId, cancellationToken);
        if (epoch?.ProjectId != projectId)
        {
            throw new InvalidOperationException("The Leader epoch is not owned by this project.");
        }

        var materials = new List<ContinuityMaterialDescriptor>();
        foreach (var daily in await memory.ListDailySummaryMetadataAsync(projectId, cancellationToken: cancellationToken))
        {
            materials.Add(new(
                $"daily:{daily.LocalDate:yyyy-MM-dd}",
                ContinuityMaterialKind.DailySummary,
                projectId,
                daily.LocalDate.ToString("yyyy-MM-dd"),
                daily.UpdatedAt,
                daily.Utf8Bytes));
        }

        if (!string.IsNullOrEmpty(epoch.HandoffSummary))
        {
            materials.Add(new(
                $"handoff:{epoch.Id}",
                ContinuityMaterialKind.BrainHandoff,
                projectId,
                $"Epoch {epoch.Id}",
                epoch.LastActiveAt,
                Encoding.UTF8.GetByteCount(epoch.HandoffSummary)));
        }

        var stats = await messages.GetStatsAsync(projectId, epochId, cancellationToken);
        materials.Add(new(
            $"raw:{epochId}",
            ContinuityMaterialKind.RecentConversation,
            projectId,
            $"Epoch {epochId} · {stats.MessageCount} messages",
            epoch.LastActiveAt,
            stats.Utf8Bytes));

        foreach (var overview in await library.ListOverviewMetadataAsync(projectId, cancellationToken))
        {
            materials.Add(new(
                $"library-overview:{overview.ObjectId}",
                ContinuityMaterialKind.LibraryOverview,
                projectId,
                OverviewLabel(overview.Category, overview.Topic, overview.ObjectId, overview.Revision),
                overview.UpdatedAt,
                overview.Utf8Bytes));
        }

        foreach (var node in await library.ListTimelineMetadataAsync(projectId, cancellationToken))
        {
            materials.Add(new(
                $"library-timeline:{node.NodeId}",
                ContinuityMaterialKind.LibraryTimelineNode,
                projectId,
                TimelineLabel(node.Category, node.Topic, node.ObjectId, node.NodeId, node.LocalDate, node.Revision),
                node.CreatedAt,
                node.Utf8Bytes));
        }

        return new ContinuityMaterialCatalog(projectId, epochId, materials);
    }

    public async Task<ResolvedContinuityBundle> ResolveAsync(
        Guid projectId,
        LeaderEpochContinuityPlan plan,
        CancellationToken cancellationToken = default)
    {
        var materials = new List<ResolvedContinuityMaterial>();
        var omitted = new List<string>();
        var total = 0;

        foreach (var selection in plan.Selections.OrderBy(item => item.Ordinal))
        {
            try
            {
                var item = await ReadAsync(projectId, selection, cancellationToken);
                if (item.Utf8Bytes > selection.MaxUtf8Bytes || total + item.Utf8Bytes > plan.TotalMaxUtf8Bytes)
                {
                    omitted.Add(selection.Reference);
                    continue;
                }

                materials.Add(item);
                total += item.Utf8Bytes;
            }
            catch (InvalidOperationException)
            {
                omitted.Add(selection.Reference);
            }
        }

        return new ResolvedContinuityBundle(projectId, materials, total, omitted);
    }

    private async Task<ResolvedContinuityMaterial> ReadAsync(
        Guid projectId,
        ContinuityMaterialSelection selection,
        CancellationToken cancellationToken)
    {
        if (selection.Kind == ContinuityMaterialKind.LibraryOverview &&
            selection.Reference.StartsWith("library-overview:", StringComparison.Ordinal) &&
            Guid.TryParse(selection.Reference[17..], out var objectId))
        {
            var libraryObject = await library.GetObjectAsync(projectId, objectId, cancellationToken);
            if (string.IsNullOrEmpty(libraryObject?.CurrentOverview))
            {
                throw new InvalidOperationException();
            }

            return new(
                selection.Kind,
                selection.Reference,
                OverviewLabel(libraryObject.Category, libraryObject.Topic, libraryObject.Id, libraryObject.OverviewRevision),
                libraryObject.CurrentOverview,
                Encoding.UTF8.GetByteCount(libraryObject.CurrentOverview));
        }

        if (selection.Kind == ContinuityMaterialKind.LibraryTimelineNode &&
            selection.Reference.StartsWith("library-timeline:", StringComparison.Ordinal) &&
            Guid.TryParse(selection.Reference[17..], out var nodeId))
        {
            var node = await library.GetNodeAsync(projectId, nodeId, cancellationToken)
                ?? throw new InvalidOperationException();
            var libraryObject = await library.GetObjectAsync(projectId, node.ObjectId, cancellationToken)
                ?? throw new InvalidOperationException();
            var references = await library.GetMaterialReferencesAsync(projectId, node.Id, cancellationToken);
            var content = FormatTimelineContent(node.Content, references);
            return new(
                selection.Kind,
                selection.Reference,
                TimelineLabel(libraryObject.Category, libraryObject.Topic, libraryObject.Id, node.Id, node.LocalDate, node.Revision),
                content,
                Encoding.UTF8.GetByteCount(content));
        }

        if (selection.Kind == ContinuityMaterialKind.DailySummary &&
            selection.Reference.StartsWith("daily:", StringComparison.Ordinal) &&
            DateOnly.TryParse(selection.Reference[6..], out var day))
        {
            var daily = await memory.GetDailySummaryAsync(projectId, day, cancellationToken)
                ?? throw new InvalidOperationException();
            return new(selection.Kind, selection.Reference, day.ToString("yyyy-MM-dd"), daily.Content, Encoding.UTF8.GetByteCount(daily.Content));
        }

        if (selection.Kind == ContinuityMaterialKind.BrainHandoff &&
            selection.Reference.StartsWith("handoff:", StringComparison.Ordinal) &&
            Guid.TryParse(selection.Reference[8..], out var handoffEpoch))
        {
            var text = await memory.GetBrainHandoffAsync(projectId, handoffEpoch, cancellationToken)
                ?? throw new InvalidOperationException();
            return new(selection.Kind, selection.Reference, $"Epoch {handoffEpoch}", text, Encoding.UTF8.GetByteCount(text));
        }

        if (selection.Kind == ContinuityMaterialKind.RecentConversation &&
            selection.Reference.StartsWith("raw:", StringComparison.Ordinal) &&
            Guid.TryParse(selection.Reference[4..], out var rawEpoch) &&
            selection.SelectorJson is not null)
        {
            try
            {
                using var json = JsonDocument.Parse(selection.SelectorJson);
                var root = json.RootElement;
                var maxMessages = root.GetProperty("maxMessages").GetInt32();
                var maxUtf8Bytes = root.GetProperty("maxUtf8Bytes").GetInt32();
                var beforeSequence = root.TryGetProperty("beforeSequence", out var before) && before.ValueKind != JsonValueKind.Null
                    ? before.GetInt64()
                    : (long?)null;
                var slice = await memory.ReadRecentConversationAsync(
                    projectId,
                    rawEpoch,
                    beforeSequence,
                    maxMessages,
                    maxUtf8Bytes,
                    cancellationToken);
                var text = string.Join("\n", slice.Messages.Select(item => item.Text));
                return new(selection.Kind, selection.Reference, $"Epoch {rawEpoch}", text, slice.Utf8Bytes);
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException("The raw conversation selector is invalid.", exception);
            }
            catch (KeyNotFoundException exception)
            {
                throw new InvalidOperationException("The raw conversation selector is invalid.", exception);
            }
        }

        throw new InvalidOperationException();
    }

    private static string OverviewLabel(string category, string topic, Guid objectId, int revision) =>
        $"Library Overview · {category} / {topic} · Object {objectId} · revision {revision}";

    private static string TimelineLabel(
        string category,
        string topic,
        Guid objectId,
        Guid nodeId,
        DateOnly localDate,
        int revision) =>
        $"Library Timeline · {category} / {topic} · Object {objectId} · {localDate:yyyy-MM-dd} · Node {nodeId} · revision {revision}";

    private static string FormatTimelineContent(
        string content,
        IReadOnlyList<LibraryMaterialReference> references)
    {
        if (references.Count == 0)
        {
            return content;
        }

        var builder = new StringBuilder(content);
        builder.AppendLine();
        builder.AppendLine();
        builder.Append("MATERIAL REFERENCES");
        foreach (var reference in references)
        {
            builder.AppendLine();
            builder.Append("- ").Append(reference.MaterialKind).Append(" | ").Append(reference.Reference);
            if (!string.IsNullOrEmpty(reference.Label))
            {
                builder.Append(" | ").Append(reference.Label);
            }
        }
        return builder.ToString();
    }
}
