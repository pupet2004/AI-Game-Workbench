using System.Text;
using System.Text.Json;
using Workbench.Storage.Leaders;
using Workbench.Storage.Memory;
using Workbench.App.Continuity;
using Workbench.Core.Continuity;
using Workbench.Core.Tasks;
using Workbench.Storage.Tasks;
using Workbench.Storage.Workers;
using Workbench.App.Worker;

namespace Workbench.App.Memory;

public sealed class ProjectContinuityMaterialService(
    IProjectMemoryApi memory,
    LeaderSessionEpochRepository epochs,
    LeaderMessageRepository messages,
    ProjectLibraryEvolutionRepository library,
    B1ProjectionService? b1Projections = null,
    TaskRepository? tasks = null,
    TaskEventRepository? taskEvents = null)
{
    private const int InitialBundleMaxUtf8Bytes = 24000;
    private const int InitialLibraryMaxUtf8Bytes = 12000;
    private const int InitialWorkMaxUtf8Bytes = 8000;
    private const int InitialAssignmentMaxUtf8Bytes = 4000;
    private readonly TaskRepository? _tasks = tasks;
    private readonly TaskEventRepository? _taskEvents = taskEvents;

    public async Task<ResolvedContinuityBundle> BuildInitialBundleAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var materials = new List<ResolvedContinuityMaterial>();
        var total = 0;

        if (b1Projections is not null)
        {
            try
            {
                var accepted = await b1Projections.GetAcceptedProjectStateAsync(new ProjectRef(projectId), cancellationToken);
                if (accepted.CurrentContributions.Count > 0)
                {
                    var content = FormatAcceptedState(accepted);
                    AddBounded(materials, ref total, CreateAcceptedStateMaterial(projectId, $"accepted-state:{projectId}", accepted, content), InitialBundleMaxUtf8Bytes);
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException)
            {
            }
        }

        var libraryTotal = 0;
        foreach (var node in await library.ListTimelineMetadataAsync(projectId, cancellationToken))
        {
            if (libraryTotal >= InitialLibraryMaxUtf8Bytes) break;
            try
            {
                var material = await ReadAsync(projectId, new ContinuityMaterialSelection(0, ContinuityMaterialKind.LibraryTimelineNode, $"library-timeline:{node.NodeId}", InitialLibraryMaxUtf8Bytes - libraryTotal), cancellationToken);
                var content = $"PERSISTED LIBRARY PROJECTION (CURRENT LIBRARY STATE; NOT ACCEPTEDPROJECTSTATE)\n{material.Content}";
                material = material with { Content = content, Utf8Bytes = Encoding.UTF8.GetByteCount(content) };
                if (AddBounded(materials, ref total, material, InitialBundleMaxUtf8Bytes, InitialLibraryMaxUtf8Bytes - libraryTotal))
                    libraryTotal += material.Utf8Bytes;
            }
            catch (InvalidOperationException)
            {
            }
        }

        if (_taskEvents is not null)
        {
            var work = await BuildLatestWorkerMaterialAsync(projectId, cancellationToken);
            if (work is not null)
                AddBounded(materials, ref total, work, InitialBundleMaxUtf8Bytes, InitialWorkMaxUtf8Bytes);
        }

        if (_tasks is not null)
        {
            var assignment = await BuildAssignmentMaterialAsync(projectId, cancellationToken);
            if (assignment is not null)
                AddBounded(materials, ref total, assignment, InitialBundleMaxUtf8Bytes, InitialAssignmentMaxUtf8Bytes);
        }

        return new ResolvedContinuityBundle(projectId, materials, total, []);
    }

    private async Task<ResolvedContinuityMaterial?> BuildLatestWorkerMaterialAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var reports = await _taskEvents!.ListForProjectAsync(projectId, "WorkerFinalReportReceived", 100, cancellationToken);
        var handoffs = await _taskEvents.ListForProjectAsync(projectId, "WorkerToLeaderHandoff", 100, cancellationToken);
        var report = reports.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.EventId).FirstOrDefault();
        var handoff = report is null
            ? handoffs.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.EventId).FirstOrDefault()
            : handoffs.Where(item => item.TaskId == report.TaskId)
                .OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.EventId).FirstOrDefault();
        if (report is null && handoff is null) return null;

        var builder = new StringBuilder();
        builder.AppendLine("LATEST COMPLETED WORK (LEGACY WORKER READ MODEL)");
        if (report is not null)
        {
            builder.AppendLine($"FinalReportEventId: {report.EventId}");
            builder.AppendLine($"TaskId: {report.TaskId}");
            AppendEventPayload(builder, "FinalReport", report.Payload);
        }
        if (handoff is not null)
        {
            builder.AppendLine($"HandoffEventId: {handoff.EventId}");
            builder.AppendLine($"TaskId: {handoff.TaskId}");
            AppendEventPayload(builder, "Handoff", handoff.Payload);
        }
        return new(ContinuityMaterialKind.LegacyWorkerCompletion, $"legacy-worker-completion:{report?.EventId ?? handoff!.EventId}", "Latest Worker FinalReport / Handoff", builder.ToString().TrimEnd(), Encoding.UTF8.GetByteCount(builder.ToString()));
    }

    private async Task<ResolvedContinuityMaterial?> BuildAssignmentMaterialAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var tasks = await _tasks!.ListAsync(projectId, cancellationToken);
        if (tasks.Count == 0) return null;
        var builder = new StringBuilder("CURRENT ASSIGNMENT / WORK HISTORY\n");
        foreach (var task in tasks.OrderByDescending(item => item.UpdatedAt).Take(8))
            builder.Append("- ").Append(task.Title).Append(" | status=").Append(task.Status).Append(" | task=").Append(task.TaskId).Append(" | revision=").AppendLine(task.CurrentRevisionId.ToString());
        var content = builder.ToString().TrimEnd();
        return new(ContinuityMaterialKind.AssignmentStatus, $"assignment-status:{projectId}", "Current Assignment Status", content, Encoding.UTF8.GetByteCount(content));
    }

    private static void AppendEventPayload(StringBuilder builder, string label, string payload)
    {
        try
        {
            using var json = JsonDocument.Parse(payload);
            var root = json.RootElement;
            builder.Append(label).Append(": ");
            if (root.TryGetProperty("Message", out var message) && message.ValueKind == JsonValueKind.String)
                builder.AppendLine(message.GetString());
            else
                builder.AppendLine(payload);
            if (root.TryGetProperty("ValidationSummary", out var validation) && validation.ValueKind == JsonValueKind.String)
                builder.Append("ValidationSummary: ").AppendLine(validation.GetString());
        }
        catch (JsonException)
        {
            builder.Append(label).Append(": ").AppendLine(payload);
        }
    }

    private static bool AddBounded(
        List<ResolvedContinuityMaterial> materials,
        ref int total,
        ResolvedContinuityMaterial material,
        int totalBudget,
        int? materialBudget = null)
    {
        if (material.Utf8Bytes > (materialBudget ?? totalBudget) || total + material.Utf8Bytes > totalBudget)
            return false;
        materials.Add(material);
        total += material.Utf8Bytes;
        return true;
    }
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
        AcceptedProjectState? accepted = null;
        if (b1Projections is not null)
        {
            try
            {
                accepted = await b1Projections.GetAcceptedProjectStateAsync(new ProjectRef(projectId), cancellationToken);
            }
            catch (InvalidDataException)
            {
                // Projects without a B1 governance root retain legacy continuity behavior.
            }
        }
        if (accepted?.CurrentContributions.Count > 0)
        {
            var content = FormatAcceptedState(accepted);
            materials.Add(new(
                $"accepted-state:{projectId}",
                ContinuityMaterialKind.AcceptedProjectState,
                projectId,
                "Accepted Project State",
                null,
                Encoding.UTF8.GetByteCount(content)));
        }
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

        if (selection.Kind == ContinuityMaterialKind.AcceptedProjectState &&
            selection.Reference == $"accepted-state:{projectId}")
        {
            if (b1Projections is null) throw new InvalidOperationException();
            var accepted = await b1Projections.GetAcceptedProjectStateAsync(new ProjectRef(projectId), cancellationToken);
            var content = FormatAcceptedState(accepted);
            return CreateAcceptedStateMaterial(projectId, selection.Reference, accepted, content);
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

    internal static string FormatAcceptedState(AcceptedProjectState state)
    {
        var builder = new StringBuilder();
        builder.AppendLine("CURRENT AUTHORITY STATE (USER-ACCEPTED)");
        builder.AppendLine($"ProjectId: {state.ProjectRef.Value}");
        foreach (var contribution in state.CurrentContributions)
        {
            builder.Append("- ").AppendLine(contribution.Statement);
            builder.Append("  AuthorityDecision: ").AppendLine(contribution.AuthorityDecisionRef.Value.ToString());
            builder.Append("  AcceptedContribution: ").AppendLine(contribution.ContributionRef.Value.ToString());
            builder.Append("  Claim: ").AppendLine(contribution.SourceClaimRef?.Value.ToString() ?? "none");
        }
        return builder.ToString().TrimEnd();
    }

    internal static ResolvedContinuityMaterial CreateAcceptedStateMaterial(
        Guid projectId,
        string reference,
        AcceptedProjectState state,
        string? content = null)
    {
        var resolvedContent = content ?? FormatAcceptedState(state);
        return new(
            ContinuityMaterialKind.AcceptedProjectState,
            reference,
            "Accepted Project State",
            resolvedContent,
            Encoding.UTF8.GetByteCount(resolvedContent),
            projectId,
            state.CurrentContributions.Select(item => item.AuthorityDecisionRef).Distinct().ToArray(),
            state.CurrentContributions.Select(item => item.ContributionRef).ToArray());
    }
}
