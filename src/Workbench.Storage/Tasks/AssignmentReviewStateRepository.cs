using System.Globalization;
using Microsoft.Data.Sqlite;
using Workbench.Core.Tasks;
using Workbench.Storage.Database;
using Workbench.Storage.Workers;

namespace Workbench.Storage.Tasks;

public enum AssignmentStateTransitionResult
{
    Applied,
    Idempotent,
    Conflict,
    NotFound
}

public sealed record AssignmentReviewRecoveryState(StoredTask Task, IReadOnlyList<StoredTaskEvent> Events);
public sealed record PendingLeaderReviewReport(Guid TaskId, Guid FinalReportEventId);
public sealed record LeaderReviewDecisionPersistenceRequest(Guid EventId, Guid ProjectId, Guid TaskId, Guid TaskRevisionId, Guid FinalReportEventId, string Outcome, string ActionLevel, string ReviewDepth, string Summary, string? Issue, string NextAction, string? ImportantNote, DateTimeOffset CreatedAt);
public sealed record StoredLeaderReviewDecision(Guid EventId, Guid ProjectId, Guid TaskId, Guid TaskRevisionId, Guid FinalReportEventId, string Outcome, string ActionLevel, string ReviewDepth, string Summary, string? Issue, string NextAction, string? ImportantNote, DateTimeOffset CreatedAt);

public sealed class AssignmentReviewStateRepository(WorkbenchDatabase database)
{
    private readonly WorkbenchDatabase _database = database;

    public async Task<AssignmentStateTransitionResult> TryRecordLeaderReviewDecisionAsync(LeaderReviewDecisionPersistenceRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsValid(request)) return AssignmentStateTransitionResult.Conflict;
        await using var connection = _database.CreateConnection(); await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var task = await ReadTaskAsync(connection, transaction, request.ProjectId, request.TaskId, cancellationToken);
        if (task is null) { await transaction.CommitAsync(cancellationToken); return AssignmentStateTransitionResult.NotFound; }
        if (task.Status != TaskLifecycleStatus.Reviewing || task.CurrentRevisionId != request.TaskRevisionId) { await transaction.CommitAsync(cancellationToken); return AssignmentStateTransitionResult.Conflict; }
        var source = await ReadEventAsync(connection, transaction, request.FinalReportEventId, cancellationToken);
        if (source is null || source.ProjectId != request.ProjectId || source.TaskId != request.TaskId || source.Type != "WorkerFinalReportReceived") { await transaction.CommitAsync(cancellationToken); return AssignmentStateTransitionResult.Conflict; }
        var existing = await ReadEventAsync(connection, transaction, request.EventId, cancellationToken);
        if (existing is not null) { await transaction.CommitAsync(cancellationToken); return existing.Type == "LeaderReviewDecisionRecorded" ? AssignmentStateTransitionResult.Idempotent : AssignmentStateTransitionResult.Conflict; }
        var duplicate = connection.CreateCommand(); duplicate.Transaction = transaction; duplicate.CommandText = "SELECT 1 FROM task_events WHERE project_id=$p AND task_id=$t AND event_type='LeaderReviewDecisionRecorded' AND payload_json LIKE $source LIMIT 1;"; duplicate.Parameters.AddWithValue("$p", request.ProjectId.ToString()); duplicate.Parameters.AddWithValue("$t", request.TaskId.ToString()); duplicate.Parameters.AddWithValue("$source", $"%\"FinalReportEventId\":\"{request.FinalReportEventId}\"%");
        if (await duplicate.ExecuteScalarAsync(cancellationToken) is not null) { await transaction.CommitAsync(cancellationToken); return AssignmentStateTransitionResult.Idempotent; }
        var payload = System.Text.Json.JsonSerializer.Serialize(new { request.TaskRevisionId, FinalReportEventId = request.FinalReportEventId, request.Outcome, request.ActionLevel, request.ReviewDepth, request.Summary, request.Issue, request.NextAction, request.ImportantNote });
        var append = connection.CreateCommand(); append.Transaction = transaction; append.CommandText = "INSERT INTO task_events(id,project_id,task_id,execution_id,event_type,payload_json,created_at) VALUES($i,$p,$t,NULL,'LeaderReviewDecisionRecorded',$x,$a);"; append.Parameters.AddWithValue("$i", request.EventId.ToString()); append.Parameters.AddWithValue("$p", request.ProjectId.ToString()); append.Parameters.AddWithValue("$t", request.TaskId.ToString()); append.Parameters.AddWithValue("$x", payload); append.Parameters.AddWithValue("$a", Format(request.CreatedAt)); await append.ExecuteNonQueryAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return AssignmentStateTransitionResult.Applied;
    }

    public async Task<StoredLeaderReviewDecision?> GetLeaderReviewDecisionAsync(Guid projectId, Guid taskId, Guid taskRevisionId, Guid finalReportEventId, CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection(); await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand(); command.CommandText = "SELECT id,payload_json,created_at FROM task_events WHERE project_id=$p AND task_id=$t AND event_type='LeaderReviewDecisionRecorded' AND payload_json LIKE $s ORDER BY created_at,id LIMIT 1;"; command.Parameters.AddWithValue("$p", projectId.ToString()); command.Parameters.AddWithValue("$t", taskId.ToString()); command.Parameters.AddWithValue("$s", $"%\"TaskRevisionId\":\"{taskRevisionId}\"%\"FinalReportEventId\":\"{finalReportEventId}\"%");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); if (!await reader.ReadAsync(cancellationToken)) return null;
        var payload = System.Text.Json.JsonSerializer.Deserialize<DecisionPayload>(reader.GetString(1))!;
        return new StoredLeaderReviewDecision(Guid.Parse(reader.GetString(0)), projectId, taskId, payload.TaskRevisionId, payload.FinalReportEventId, payload.Outcome, payload.ActionLevel, payload.ReviewDepth, payload.Summary, payload.Issue, payload.NextAction, payload.ImportantNote, DateTimeOffset.Parse(reader.GetString(2)));
    }

    public async Task<IReadOnlyList<PendingLeaderReviewReport>> ListPendingReviewReportsAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection(); await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT final.id, final.task_id
            FROM task_events final
            JOIN tasks task ON task.id = final.task_id AND task.project_id = final.project_id
            WHERE final.project_id = $projectId
              AND task.status = 'Reviewing'
              AND final.event_type = 'WorkerFinalReportReceived'
              AND NOT EXISTS (
                  SELECT 1 FROM task_events decision
                  WHERE decision.project_id = final.project_id
                    AND decision.task_id = final.task_id
                    AND decision.event_type = 'LeaderReviewDecisionRecorded'
                    AND decision.payload_json LIKE '%' || '"FinalReportEventId":"' || final.id || '"' || '%')
            ORDER BY final.created_at, final.id;
            """;
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<PendingLeaderReviewReport>();
        while (await reader.ReadAsync(cancellationToken)) results.Add(new(Guid.Parse(reader.GetString(1)), Guid.Parse(reader.GetString(0))));
        return results;
    }

    public async Task<IReadOnlyList<StoredLeaderReviewDecision>> ListReviewingPassDecisionsAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection(); await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT e.id,e.task_id,e.payload_json,e.created_at FROM task_events e JOIN tasks t ON t.id=e.task_id AND t.project_id=e.project_id WHERE e.project_id=$p AND t.status='Reviewing' AND e.event_type='LeaderReviewDecisionRecorded' AND e.payload_json LIKE '%\"Outcome\":\"Pass\"%' ORDER BY e.created_at,e.id;";
        command.Parameters.AddWithValue("$p", projectId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<StoredLeaderReviewDecision>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var payload = System.Text.Json.JsonSerializer.Deserialize<DecisionPayload>(reader.GetString(2));
            if (payload is null) continue;
            results.Add(new(Guid.Parse(reader.GetString(0)), projectId, Guid.Parse(reader.GetString(1)), payload.TaskRevisionId, payload.FinalReportEventId, payload.Outcome, payload.ActionLevel, payload.ReviewDepth, payload.Summary, payload.Issue, payload.NextAction, payload.ImportantNote, DateTimeOffset.Parse(reader.GetString(3))));
        }
        return results;
    }

    public async Task<AssignmentStateTransitionResult> TryTransitionAsync(
        Guid projectId,
        Guid taskId,
        TaskLifecycleStatus expectedStatus,
        TaskLifecycleStatus nextStatus,
        Guid eventId,
        string eventType,
        string payload,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default)
    {
        if (!AssignmentLifecycle.CanTransition(expectedStatus, nextStatus))
        {
            throw new ArgumentException($"{expectedStatus} cannot transition to {nextStatus}.", nameof(nextStatus));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);

        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var existingEvent = await ReadEventAsync(connection, transaction, eventId, cancellationToken);
        if (existingEvent is not null)
        {
            var result = existingEvent.ProjectId == projectId &&
                         existingEvent.TaskId == taskId &&
                         existingEvent.Type == eventType &&
                         existingEvent.Payload == payload &&
                         await HasStatusAsync(connection, transaction, projectId, taskId, nextStatus, cancellationToken)
                ? AssignmentStateTransitionResult.Idempotent
                : AssignmentStateTransitionResult.Conflict;
            await transaction.CommitAsync(cancellationToken);
            return result;
        }

        var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE tasks
            SET status=$nextStatus, updated_at=$occurredAt, cancelled_at=$cancelledAt
            WHERE project_id=$projectId AND id=$taskId AND status=$expectedStatus;
            """;
        update.Parameters.AddWithValue("$nextStatus", nextStatus.ToString());
        update.Parameters.AddWithValue("$occurredAt", Format(occurredAt));
        update.Parameters.AddWithValue("$cancelledAt", nextStatus == TaskLifecycleStatus.Cancelled ? Format(occurredAt) : DBNull.Value);
        update.Parameters.AddWithValue("$projectId", projectId.ToString());
        update.Parameters.AddWithValue("$taskId", taskId.ToString());
        update.Parameters.AddWithValue("$expectedStatus", expectedStatus.ToString());
        if (await update.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            var result = await ExistsInProjectAsync(connection, transaction, projectId, taskId, cancellationToken)
                ? AssignmentStateTransitionResult.Conflict
                : AssignmentStateTransitionResult.NotFound;
            await transaction.CommitAsync(cancellationToken);
            return result;
        }

        var append = connection.CreateCommand();
        append.Transaction = transaction;
        append.CommandText = """
            INSERT INTO task_events(id,project_id,task_id,execution_id,event_type,payload_json,created_at)
            VALUES($eventId,$projectId,$taskId,NULL,$eventType,$payload,$occurredAt);
            """;
        append.Parameters.AddWithValue("$eventId", eventId.ToString());
        append.Parameters.AddWithValue("$projectId", projectId.ToString());
        append.Parameters.AddWithValue("$taskId", taskId.ToString());
        append.Parameters.AddWithValue("$eventType", eventType);
        append.Parameters.AddWithValue("$payload", payload);
        append.Parameters.AddWithValue("$occurredAt", Format(occurredAt));
        await append.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return AssignmentStateTransitionResult.Applied;
    }

    public async Task<AssignmentReviewRecoveryState?> GetRecoveryStateAsync(
        Guid projectId,
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var task = await ReadTaskAsync(connection, null, projectId, taskId, cancellationToken);
        if (task is null)
        {
            return null;
        }

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,project_id,task_id,execution_id,event_type,payload_json,created_at
            FROM task_events
            WHERE project_id=$projectId AND task_id=$taskId
            ORDER BY created_at,id;
            """;
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        command.Parameters.AddWithValue("$taskId", taskId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var events = new List<StoredTaskEvent>();
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(new StoredTaskEvent(
                Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), Guid.Parse(reader.GetString(2)),
                reader.IsDBNull(3) ? null : Guid.Parse(reader.GetString(3)), reader.GetString(4), reader.GetString(5),
                DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        }

        return new AssignmentReviewRecoveryState(task, events);
    }

    private static async Task<StoredTaskEvent?> ReadEventAsync(SqliteConnection connection, SqliteTransaction transaction, Guid eventId, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id,project_id,task_id,execution_id,event_type,payload_json,created_at FROM task_events WHERE id=$eventId;";
        command.Parameters.AddWithValue("$eventId", eventId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new StoredTaskEvent(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), Guid.Parse(reader.GetString(2)),
                reader.IsDBNull(3) ? null : Guid.Parse(reader.GetString(3)), reader.GetString(4), reader.GetString(5),
                DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind))
            : null;
    }

    private static async Task<bool> HasStatusAsync(SqliteConnection connection, SqliteTransaction transaction, Guid projectId, Guid taskId, TaskLifecycleStatus status, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM tasks WHERE project_id=$projectId AND id=$taskId AND status=$status;";
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        command.Parameters.AddWithValue("$taskId", taskId.ToString());
        command.Parameters.AddWithValue("$status", status.ToString());
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task<bool> ExistsInProjectAsync(SqliteConnection connection, SqliteTransaction transaction, Guid projectId, Guid taskId, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM tasks WHERE project_id=$projectId AND id=$taskId;";
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        command.Parameters.AddWithValue("$taskId", taskId.ToString());
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task<StoredTask?> ReadTaskAsync(SqliteConnection connection, SqliteTransaction? transaction, Guid projectId, Guid taskId, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id,project_id,title,status,current_revision_id,created_at,updated_at,cancelled_at FROM tasks WHERE project_id=$projectId AND id=$taskId;";
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        command.Parameters.AddWithValue("$taskId", taskId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new StoredTask(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), reader.GetString(2),
                Enum.Parse<TaskLifecycleStatus>(reader.GetString(3)), Guid.Parse(reader.GetString(4)),
                DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.IsDBNull(7) ? null : DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind))
            : null;
    }

    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static bool IsValid(LeaderReviewDecisionPersistenceRequest value) => value.EventId != Guid.Empty && value.ProjectId != Guid.Empty && value.TaskId != Guid.Empty && value.TaskRevisionId != Guid.Empty && value.FinalReportEventId != Guid.Empty && value.Outcome is "Pass" or "Fix" or "Continue" or "AskUser" && value.ActionLevel is "L1LocalFix" or "L2TaskRework" or "L3DecisionRequired" && value.ReviewDepth is "ReportOnly" or "EvidenceCheck" or "Deep" && !string.IsNullOrWhiteSpace(value.Summary) && value.Summary.Length <= 600 && !string.IsNullOrWhiteSpace(value.NextAction) && value.NextAction.Length <= 600 && (value.Issue is null || value.Issue.Length <= 600) && (value.ImportantNote is null || value.ImportantNote.Length <= 400);
    private sealed record DecisionPayload(Guid TaskRevisionId, Guid FinalReportEventId, string Outcome, string ActionLevel, string ReviewDepth, string Summary, string? Issue, string NextAction, string? ImportantNote);
}
