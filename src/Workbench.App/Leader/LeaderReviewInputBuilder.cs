using System.Text.Json;
using Workbench.App.Worker;
using Workbench.Core.Tasks;
using Workbench.Storage.Projects;
using Workbench.Storage.Tasks;
using Workbench.Storage.Workers;

namespace Workbench.App.Leader;

public sealed record LeaderReviewFinalReport(string Body, string? ValidationSummary);

public enum LeaderReviewHandoffBindingKind
{
    Canonical,
    LegacyCompat
}

public sealed record LeaderReviewInput(
    Guid ProjectId,
    string ProjectName,
    Guid TaskId,
    Guid TaskRevisionId,
    string Goal,
    IReadOnlyList<string> AcceptanceCriteria,
    string Scope,
    string OutOfScope,
    Guid WorkerSessionId,
    Guid FinalReportEventId,
    LeaderReviewFinalReport FinalReport,
    TaskLifecycleStatus AssignmentStatus,
    LeaderReviewHandoffBindingKind HandoffBinding = LeaderReviewHandoffBindingKind.Canonical);

public sealed class LeaderReviewInputBuilder(
    ProjectRepository projects,
    TaskRepository tasks,
    TaskRevisionRepository revisions,
    TaskEventRepository events)
{
    private const int EventReadLimit = 200;

    public async Task<LeaderReviewInput?> BuildAsync(
        Guid projectId,
        Guid taskId,
        Guid finalReportEventId,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty || taskId == Guid.Empty || finalReportEventId == Guid.Empty)
        {
            return null;
        }

        var project = await projects.GetByIdAsync(projectId, cancellationToken);
        var task = await tasks.GetAsync(projectId, taskId, cancellationToken);
        if (project is null || task is null || task.Status != TaskLifecycleStatus.Reviewing)
        {
            return null;
        }

        var currentRevision = (await revisions.ListAsync(projectId, taskId, cancellationToken))
            .SingleOrDefault(item => item.Id == task.CurrentRevisionId);
        if (currentRevision is null)
        {
            return null;
        }

        var taskEvents = await events.ListAsync(projectId, taskId, EventReadLimit, cancellationToken);
        var finalReportEvent = taskEvents.SingleOrDefault(item =>
            item.EventId == finalReportEventId && item.Type == "WorkerFinalReportReceived");
        if (finalReportEvent is null || !TryReadFinalReport(finalReportEvent.Payload, out var finalReport))
        {
            return null;
        }

        var compatibleHandoffs = taskEvents
            .Where(item => item.Type == "WorkerToLeaderHandoff")
            .Select(item => TryReadHandoff(item.Payload))
            .Where(item => item is not null)
            .Cast<WorkerHandoff>()
            .Where(item => item.ProjectId == projectId &&
                           item.TaskId == taskId &&
                           item.TaskRevisionId == currentRevision.Id &&
                           item.Kind == WorkerHandoffKind.FinalReport &&
                           item.WorkerSessionId.Value == finalReport.WorkerSessionId)
            .ToArray();

        if (compatibleHandoffs.Length != 1)
        {
            return null;
        }

        var handoff = compatibleHandoffs[0];
        LeaderReviewHandoffBindingKind bindingKind;
        if (handoff.SourceEventId == finalReportEventId)
        {
            bindingKind = LeaderReviewHandoffBindingKind.Canonical;
        }
        else if (handoff.SourceEventId is null &&
                 handoff.Message == finalReport.Message &&
                 handoff.ValidationSummary == finalReport.ValidationSummary)
        {
            bindingKind = LeaderReviewHandoffBindingKind.LegacyCompat;
        }
        else
        {
            return null;
        }

        return new LeaderReviewInput(
            project.Id,
            project.Name,
            task.TaskId,
            currentRevision.Id,
            currentRevision.Goal,
            Array.AsReadOnly(currentRevision.Acceptance.ToArray()),
            currentRevision.Scope,
            currentRevision.OutOfScope,
            finalReport.WorkerSessionId,
            finalReportEvent.EventId,
            new LeaderReviewFinalReport(finalReport.Message, finalReport.ValidationSummary),
            task.Status,
            bindingKind);
    }

    private static bool TryReadFinalReport(string payload, out FinalReportEventPayload value)
    {
        value = default!;
        try
        {
            var parsed = JsonSerializer.Deserialize<FinalReportEventPayload>(payload);
            if (parsed is null || parsed.WorkerSessionId == Guid.Empty || string.IsNullOrWhiteSpace(parsed.Message))
            {
                return false;
            }

            value = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static WorkerHandoff? TryReadHandoff(string payload)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<HandoffEventPayload>(payload);
            if (parsed is null || parsed.ProjectId == Guid.Empty || parsed.TaskId == Guid.Empty || !TryReadGuid(parsed.WorkerSessionId, out var workerSessionId))
            {
                return null;
            }

            return new WorkerHandoff(parsed.ProjectId, parsed.TaskId, new Workbench.Runtime.Agents.AgentSessionId(workerSessionId),
                parsed.WorkerLabel ?? string.Empty, parsed.Status, parsed.Message ?? string.Empty, parsed.CreatedAt,
                parsed.Kind, parsed.ValidationSummary, parsed.SourceEventId, parsed.TaskRevisionId);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record FinalReportEventPayload(Guid WorkerSessionId, string Message, string? ValidationSummary);
    private sealed record HandoffEventPayload(
        Guid ProjectId,
        Guid TaskId,
        JsonElement WorkerSessionId,
        string? WorkerLabel,
        Workbench.Runtime.Agents.AgentSessionStatus Status,
        string? Message,
        DateTimeOffset CreatedAt,
        WorkerHandoffKind Kind,
        string? ValidationSummary,
        Guid? SourceEventId,
        Guid TaskRevisionId);

    private static bool TryReadGuid(JsonElement value, out Guid result)
    {
        if (value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out result)) return true;
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("Value", out var nested) &&
            nested.ValueKind == JsonValueKind.String && Guid.TryParse(nested.GetString(), out result)) return true;
        result = Guid.Empty;
        return false;
    }
}
