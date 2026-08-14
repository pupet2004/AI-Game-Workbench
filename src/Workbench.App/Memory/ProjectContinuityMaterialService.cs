using System.Text;
using System.Text.Json;
using Workbench.Storage.Leaders;
using Workbench.Storage.Memory;

namespace Workbench.App.Memory;

public sealed class ProjectContinuityMaterialService(
    IProjectMemoryApi memory,
    LeaderSessionEpochRepository epochs,
    LeaderMessageRepository messages)
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
        if (selection.Reference.StartsWith("daily:", StringComparison.Ordinal) &&
            DateOnly.TryParse(selection.Reference[6..], out var day))
        {
            var daily = await memory.GetDailySummaryAsync(projectId, day, cancellationToken)
                ?? throw new InvalidOperationException();
            return new(selection.Kind, selection.Reference, day.ToString("yyyy-MM-dd"), daily.Content, Encoding.UTF8.GetByteCount(daily.Content));
        }

        if (selection.Reference.StartsWith("handoff:", StringComparison.Ordinal) &&
            Guid.TryParse(selection.Reference[8..], out var handoffEpoch))
        {
            var text = await memory.GetBrainHandoffAsync(projectId, handoffEpoch, cancellationToken)
                ?? throw new InvalidOperationException();
            return new(selection.Kind, selection.Reference, $"Epoch {handoffEpoch}", text, Encoding.UTF8.GetByteCount(text));
        }

        if (selection.Reference.StartsWith("raw:", StringComparison.Ordinal) &&
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
}
