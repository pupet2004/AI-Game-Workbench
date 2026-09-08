using System.Text.Json;
using Workbench.App.Continuity;
using Workbench.Core.Continuity;
using Workbench.Storage.Continuity;
using Workbench.Storage.Memory;
using Workbench.Storage.Tasks;
using Workbench.Storage.Workers;

namespace Workbench.App.ProjectWorld;

public enum ProjectEvolutionCategory
{
    Authority,
    Candidate,
    Assignment,
    Worker,
    Handoff,
    Library,
    Artifact
}

public sealed record ProjectEvolutionRecord(
    Guid Id,
    DateTimeOffset OccurredAt,
    ProjectEvolutionCategory Category,
    string ObjectRef,
    string ChangeType,
    string Summary,
    IReadOnlyList<string> SourceRefs,
    string AuthorityStatus);

/// <summary>
/// Builds a bounded, deterministic read model from existing project facts.
/// It owns no persistence and deliberately preserves the identity of each source system.
/// </summary>
public sealed class ProjectEvolutionIndexQuery(
    B1AuthorityRepository authority,
    ProjectLibraryEvolutionRepository library,
    TaskRepository tasks,
    TaskEventRepository taskEvents,
    ProjectEvolutionCandidateRepository candidates)
{
    private const int MaxRecords = 200;

    public async Task<IReadOnlyList<ProjectEvolutionRecord>> ListAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("Project identity is required.", nameof(projectId));

        var records = new List<ProjectEvolutionRecord>();
        foreach (var candidate in await candidates.ListAsync(projectId, cancellationToken: cancellationToken))
        {
            var change = candidate.Before is null && candidate.After is null
                ? candidate.ChangeType
                : $"{candidate.Before ?? "(new)"} → {candidate.After ?? "(removed)"}";
            records.Add(new(
                candidate.CandidateId,
                candidate.CreatedAt,
                ProjectEvolutionCategory.Candidate,
                $"evolution-candidate:{candidate.CandidateId}",
                "EvolutionCandidateObserved",
                $"{candidate.Object}: {change}",
                [$"EvolutionCandidate:{candidate.CandidateId}", $"Source:{candidate.SourceRef}"],
                candidate.Status.ToString()));
        }
        B1ProjectState? state = null;
        try
        {
            state = await authority.LoadProjectStateAsync(new Workbench.Core.Continuity.ProjectRef(projectId), cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException)
        {
            // Legacy-only projects can still expose Library/Worker history.
        }

        if (state is not null)
        {
            foreach (var decision in state.AuthorityDecisions)
            {
                var contributionText = decision.AcceptedStateContributions.Count == 0
                    ? ""
                    : " · " + string.Join("; ", decision.AcceptedStateContributions.Select(item => item.Statement));
                records.Add(new(
                    decision.DecisionRef.Value,
                    decision.CreatedAt,
                    ProjectEvolutionCategory.Authority,
                    $"authority-decision:{decision.DecisionRef.Value}",
                    "AuthorityDecisionRecorded",
                    $"Accepted project authority decision{contributionText}",
                    [$"AuthorityDecision:{decision.DecisionRef.Value}"],
                    "AcceptedProjectState"));
            }
        }

        foreach (var task in (await tasks.ListAsync(projectId, cancellationToken)).Take(40))
        {
            records.Add(new(
                task.TaskId,
                task.UpdatedAt,
                ProjectEvolutionCategory.Assignment,
                $"task:{task.TaskId}",
                "AssignmentObserved",
                $"{task.Title} · {task.Status}",
                [$"Task:{task.TaskId}", $"TaskRevision:{task.CurrentRevisionId}"],
                "Legacy assignment state"));
        }

        foreach (var obj in await library.ListObjectsAsync(projectId, cancellationToken))
        {
            records.Add(new(
                obj.Id,
                obj.UpdatedAt,
                ProjectEvolutionCategory.Library,
                $"library-object:{obj.Id}",
                "LibraryObjectCurrent",
                string.IsNullOrWhiteSpace(obj.CurrentOverview)
                    ? $"{obj.Category} / {obj.Topic}"
                    : $"{obj.Category} / {obj.Topic} · {obj.CurrentOverview}",
                [$"LibraryObject:{obj.Id}"],
                "Library projection; not AcceptedProjectState"));

            foreach (var node in await library.GetTimelineAsync(projectId, obj.Id, cancellationToken))
            {
                records.Add(new(
                    node.Id,
                    node.OccurredAt,
                    ProjectEvolutionCategory.Library,
                    $"library-object:{obj.Id}",
                    "LibraryTimelineNodeRecorded",
                    $"{obj.Topic}: {node.Content}",
                    [$"LibraryObject:{obj.Id}", $"LibraryTimelineNode:{node.Id}"],
                    "Library projection; not AcceptedProjectState"));

                foreach (var material in await library.GetMaterialReferencesAsync(projectId, node.Id, cancellationToken))
                {
                    records.Add(new(
                        material.NodeId,
                        material.CreatedAt,
                        ProjectEvolutionCategory.Artifact,
                        $"artifact:{material.Reference}",
                        "ArtifactReferenced",
                        string.IsNullOrWhiteSpace(material.Label)
                            ? material.Reference
                            : $"{material.Label} · {material.Reference}",
                        [$"LibraryTimelineNode:{material.NodeId}", $"Material:{material.Reference}"],
                        "Source/material reference; not AcceptedProjectState"));
                }
            }
        }

        var reportEvents = await taskEvents.ListForProjectAsync(projectId, "WorkerFinalReportReceived", 100, cancellationToken);
        var handoffEvents = await taskEvents.ListForProjectAsync(projectId, "WorkerToLeaderHandoff", 100, cancellationToken);
        foreach (var item in reportEvents)
        {
            records.Add(new(
                item.EventId,
                item.CreatedAt,
                ProjectEvolutionCategory.Worker,
                $"task:{item.TaskId}",
                "WorkerFinalReportReceived",
                ReadPayload(item.Payload),
                new[] { $"Task:{item.TaskId}", $"TaskEvent:{item.EventId}" }.Concat(item.ExecutionId is null ? Array.Empty<string>() : new[] { $"WorkerExecution:{item.ExecutionId}" }).ToArray(),
                "Legacy Worker completion; not AcceptedProjectState"));
        }

        foreach (var item in handoffEvents)
        {
            records.Add(new(
                item.EventId,
                item.CreatedAt,
                ProjectEvolutionCategory.Handoff,
                $"task:{item.TaskId}",
                "WorkerToLeaderHandoff",
                ReadPayload(item.Payload),
                new[] { $"Task:{item.TaskId}", $"TaskEvent:{item.EventId}" }.Concat(item.ExecutionId is null ? Array.Empty<string>() : new[] { $"WorkerExecution:{item.ExecutionId}" }).ToArray(),
                "Legacy Worker handoff; not AcceptedProjectState"));
        }

        return records
            .OrderByDescending(item => item.OccurredAt)
            .ThenByDescending(item => item.Id)
            .Take(MaxRecords)
            .ToArray();
    }

    private static string ReadPayload(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.TryGetProperty("Message", out var message) && message.ValueKind == JsonValueKind.String)
                return message.GetString() ?? "Worker event";
            if (root.TryGetProperty("Report", out var report) && report.ValueKind == JsonValueKind.String)
                return report.GetString() ?? "Worker event";
        }
        catch (JsonException)
        {
        }

        return payload;
    }
}
