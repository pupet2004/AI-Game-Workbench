using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Workbench.Storage.Database;

namespace Workbench.Storage.Tasks;

public sealed record LeaderReviewUserResponseBinding(Guid EventId, Guid ProjectId, Guid TaskId, Guid TaskRevisionId, Guid ReviewDecisionEventId, long QuestionMessageId, long UserMessageId, DateTimeOffset CreatedAt);

public sealed class LeaderReviewUserResponseBindingRepository(WorkbenchDatabase database)
{
    private readonly WorkbenchDatabase _database = database;

    public async Task<bool> HasSingletonOpenGateAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection(); await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM (SELECT d.id FROM task_events d JOIN tasks t ON t.id=d.task_id AND t.project_id=d.project_id JOIN leader_messages q ON q.epoch_id=(SELECT current_epoch_id FROM project_leaders WHERE project_id=d.project_id) AND q.role='user' AND q.text LIKE '%' || 'review-user-decision:' || d.project_id || ':' || d.task_id || ':' || d.id || '%' WHERE d.project_id=$p AND t.status='NeedsUserDecision' AND d.event_type='LeaderReviewDecisionRecorded' AND NOT EXISTS (SELECT 1 FROM task_events b WHERE b.project_id=d.project_id AND b.task_id=d.task_id AND b.event_type='LeaderReviewUserResponseReceived' AND b.payload_json LIKE '%' || '\"ReviewDecisionEventId\":\"' || d.id || '\"%'));"; command.Parameters.AddWithValue("$p", projectId.ToString()); return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    public async Task<LeaderReviewUserResponseBinding?> TryBindAsync(Guid projectId, long userMessageId, CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty || userMessageId <= 0) return null;
        await using var connection = _database.CreateConnection(); await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var message = connection.CreateCommand(); message.Transaction = transaction; message.CommandText = "SELECT m.id,m.epoch_id FROM leader_messages m JOIN project_leaders p ON p.current_epoch_id=m.epoch_id WHERE p.project_id=$p AND m.id=$m AND m.role='user';"; message.Parameters.AddWithValue("$p", projectId.ToString()); message.Parameters.AddWithValue("$m", userMessageId);
        await using var messageReader = await message.ExecuteReaderAsync(cancellationToken);
        if (!await messageReader.ReadAsync(cancellationToken)) { await transaction.CommitAsync(cancellationToken); return null; }
        var epochId = Guid.Parse(messageReader.GetString(1)); await messageReader.DisposeAsync();

        var replay = connection.CreateCommand(); replay.Transaction = transaction; replay.CommandText = "SELECT id,task_id,payload_json,created_at FROM task_events WHERE project_id=$p AND event_type='LeaderReviewUserResponseReceived' AND payload_json LIKE '%' || '\"UserMessageId\":' || $m || '%' LIMIT 1;"; replay.Parameters.AddWithValue("$p", projectId.ToString()); replay.Parameters.AddWithValue("$m", userMessageId);
        await using var replayReader = await replay.ExecuteReaderAsync(cancellationToken);
        if (await replayReader.ReadAsync(cancellationToken))
        {
            var replayPayload = JsonSerializer.Deserialize<BindingPayload>(replayReader.GetString(2))!; await transaction.CommitAsync(cancellationToken);
            return new(Guid.Parse(replayReader.GetString(0)), projectId, Guid.Parse(replayReader.GetString(1)), replayPayload.TaskRevisionId, replayPayload.ReviewDecisionEventId, replayPayload.QuestionMessageId, replayPayload.UserMessageId, DateTimeOffset.Parse(replayReader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
        }
        await replayReader.DisposeAsync();

        var candidates = connection.CreateCommand(); candidates.Transaction = transaction; candidates.CommandText = "SELECT d.id,d.task_id,d.payload_json,q.id FROM task_events d JOIN tasks t ON t.id=d.task_id AND t.project_id=d.project_id JOIN leader_messages q ON q.epoch_id=$e AND q.role='user' AND q.text LIKE '%' || 'review-user-decision:' || d.project_id || ':' || d.task_id || ':' || d.id || '%' WHERE d.project_id=$p AND t.status='NeedsUserDecision' AND d.event_type='LeaderReviewDecisionRecorded' AND NOT EXISTS (SELECT 1 FROM task_events b WHERE b.project_id=d.project_id AND b.task_id=d.task_id AND b.event_type='LeaderReviewUserResponseReceived' AND b.payload_json LIKE '%' || '\"ReviewDecisionEventId\":\"' || d.id || '\"%');"; candidates.Parameters.AddWithValue("$e", epochId.ToString()); candidates.Parameters.AddWithValue("$p", projectId.ToString());
        await using var reader = await candidates.ExecuteReaderAsync(cancellationToken); var rows = new List<(Guid DecisionId, Guid TaskId, Guid RevisionId, long QuestionId)>();
        while (await reader.ReadAsync(cancellationToken)) { var payload = JsonSerializer.Deserialize<DecisionPayload>(reader.GetString(2)); if (payload is not null) rows.Add((Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), payload.TaskRevisionId, reader.GetInt64(3))); }
        if (rows.Count != 1) { await transaction.CommitAsync(cancellationToken); return null; }
        var row = rows[0]; var eventId = DeterministicBindingId(row.DecisionId, userMessageId);
        var existing = connection.CreateCommand(); existing.Transaction = transaction; existing.CommandText = "SELECT id,payload_json,created_at FROM task_events WHERE project_id=$p AND task_id=$t AND event_type='LeaderReviewUserResponseReceived' AND payload_json LIKE '%' || '\"ReviewDecisionEventId\":\"' || $d || '\"%' LIMIT 1;"; existing.Parameters.AddWithValue("$p", projectId.ToString()); existing.Parameters.AddWithValue("$t", row.TaskId.ToString()); existing.Parameters.AddWithValue("$d", row.DecisionId.ToString());
        await using var existingReader = await existing.ExecuteReaderAsync(cancellationToken); if (await existingReader.ReadAsync(cancellationToken)) { var existingPayload = JsonSerializer.Deserialize<BindingPayload>(existingReader.GetString(1))!; await transaction.CommitAsync(cancellationToken); return new(Guid.Parse(existingReader.GetString(0)), projectId, row.TaskId, existingPayload.TaskRevisionId, existingPayload.ReviewDecisionEventId, existingPayload.QuestionMessageId, existingPayload.UserMessageId, DateTimeOffset.Parse(existingReader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)); }
        await existingReader.DisposeAsync();
        var payloadJson = JsonSerializer.Serialize(new { TaskRevisionId = row.RevisionId, ReviewDecisionEventId = row.DecisionId, QuestionMessageId = row.QuestionId, UserMessageId = userMessageId });
        var insert = connection.CreateCommand(); insert.Transaction = transaction; insert.CommandText = "INSERT INTO task_events(id,project_id,task_id,execution_id,event_type,payload_json,created_at) VALUES($i,$p,$t,NULL,'LeaderReviewUserResponseReceived',$x,$a);"; insert.Parameters.AddWithValue("$i", eventId.ToString()); insert.Parameters.AddWithValue("$p", projectId.ToString()); insert.Parameters.AddWithValue("$t", row.TaskId.ToString()); insert.Parameters.AddWithValue("$x", payloadJson); var now = DateTimeOffset.UtcNow; insert.Parameters.AddWithValue("$a", now.ToString("O", CultureInfo.InvariantCulture)); await insert.ExecuteNonQueryAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        return new(eventId, projectId, row.TaskId, row.RevisionId, row.DecisionId, row.QuestionId, userMessageId, now);
    }

    public async Task<long?> GetBoundUserMessageIdAsync(Guid projectId, Guid taskId, Guid reviewDecisionEventId, CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection(); await connection.OpenAsync(cancellationToken); var command = connection.CreateCommand(); command.CommandText = "SELECT payload_json FROM task_events WHERE project_id=$p AND task_id=$t AND event_type='LeaderReviewUserResponseReceived' AND payload_json LIKE '%' || '\"ReviewDecisionEventId\":\"' || $d || '\"%' ORDER BY created_at,id LIMIT 1;"; command.Parameters.AddWithValue("$p", projectId.ToString()); command.Parameters.AddWithValue("$t", taskId.ToString()); command.Parameters.AddWithValue("$d", reviewDecisionEventId.ToString()); var payload = await command.ExecuteScalarAsync(cancellationToken) as string; return payload is null ? null : JsonSerializer.Deserialize<BindingPayload>(payload)!.UserMessageId;
    }

    private static Guid DeterministicBindingId(Guid decisionId, long messageId) => new(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"review-user-response:{decisionId:D}:{messageId}"))[..16]);
    private sealed record DecisionPayload(Guid TaskRevisionId, Guid FinalReportEventId, string Outcome, string ActionLevel, string ReviewDepth, string Summary, string? Issue, string NextAction, string? ImportantNote);
    private sealed record BindingPayload(Guid TaskRevisionId, Guid ReviewDecisionEventId, long QuestionMessageId, long UserMessageId);
}
