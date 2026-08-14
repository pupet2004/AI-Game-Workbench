using System.Text.Json;
using Workbench.App.Worker;
using Workbench.Core.Tasks;
using Workbench.Storage.Projects;
using Workbench.Storage.Tasks;
using Workbench.Storage.Workers;

namespace Workbench.App.Leader;

public sealed record LeaderReviewFinalReport(string Body, string? ValidationSummary);

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
    TaskLifecycleStatus AssignmentStatus);

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

        var bindings = taskEvents
            .Where(item => item.Type == "WorkerToLeaderHandoff")
            .Select(item => TryReadHandoff(item.Payload))
            .Where(item => item is not null)
            .Cast<WorkerHandoff>()
            .Where(item => item.ProjectId == projectId &&
                           item.TaskId == taskId &&
                           item.TaskRevisionId == currentRevision.Id &&
                           item.Kind == WorkerHandoffKind.FinalReport &&
                           item.SourceEventId == finalReportEventId &&
                           item.WorkerSessionId.Value == finalReport.WorkerSessionId)
            .ToArray();
        if (bindings.Length != 1)
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
            task.Status);
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
            return JsonSerializer.Deserialize<WorkerHandoff>(payload);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record FinalReportEventPayload(Guid WorkerSessionId, string Message, string? ValidationSummary);
}
