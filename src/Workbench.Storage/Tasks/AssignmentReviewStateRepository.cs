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

public sealed class AssignmentReviewStateRepository(WorkbenchDatabase database)
{
    private readonly WorkbenchDatabase _database = database;

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
}
