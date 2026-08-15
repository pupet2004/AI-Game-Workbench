using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Workbench.Storage.Migrations;

internal static class Migration018ReviewDecisionSubject
{
    public const long Version = 18;

    public static async Task ApplyAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        var rows = await ReadAndValidateSubjectsAsync(connection, transaction, cancellationToken);

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE task_review_decisions_rebuilt (
                review_decision_id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL,
                task_id TEXT NOT NULL,
                revision_id TEXT NOT NULL,
                source_event_id TEXT NULL UNIQUE,
                final_report_event_id TEXT NOT NULL,
                outcome TEXT NOT NULL CHECK(outcome IN ('Pass', 'Fix', 'Continue', 'AskUser')),
                action_level TEXT NOT NULL CHECK(action_level IN ('L1LocalFix', 'L2TaskRework', 'L3DecisionRequired')),
                authority_mode TEXT NULL CHECK(authority_mode IN ('Cautious', 'Balanced', 'Autonomous')),
                authority_resolution TEXT NOT NULL CHECK(authority_resolution IN ('AutoProceed', 'NotifyAndProceed', 'AskUser')),
                authority_mode_recording TEXT NOT NULL CHECK(authority_mode_recording IN ('Recorded', 'LegacyNotRecorded')),
                created_at TEXT NOT NULL,
                CHECK(
                    (authority_mode_recording = 'Recorded' AND authority_mode IS NOT NULL) OR
                    (authority_mode_recording = 'LegacyNotRecorded' AND authority_mode IS NULL)
                ),
                FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE,
                FOREIGN KEY(task_id, project_id) REFERENCES tasks(id, project_id) ON DELETE CASCADE,
                FOREIGN KEY(revision_id, task_id) REFERENCES task_revisions(id, task_id) ON DELETE CASCADE,
                UNIQUE(project_id, task_id, final_report_event_id)
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        foreach (var row in rows)
        {
            var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO task_review_decisions_rebuilt(
                    review_decision_id,project_id,task_id,revision_id,source_event_id,final_report_event_id,
                    outcome,action_level,authority_mode,authority_resolution,authority_mode_recording,created_at)
                VALUES($id,$project,$task,$revision,$source,$report,$outcome,$action,$authority,$resolution,$recording,$created);
                """;
            Add(insert, "$id", row.DecisionId); Add(insert, "$project", row.ProjectId); Add(insert, "$task", row.TaskId);
            Add(insert, "$revision", row.RevisionId); Add(insert, "$source", row.SourceEventId); Add(insert, "$report", row.FinalReportEventId);
            Add(insert, "$outcome", row.Outcome); Add(insert, "$action", row.ActionLevel); Add(insert, "$authority", row.AuthorityMode);
            Add(insert, "$resolution", row.AuthorityResolution); Add(insert, "$recording", row.AuthorityModeRecording); Add(insert, "$created", row.CreatedAt);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DROP TABLE task_review_decisions;
            ALTER TABLE task_review_decisions_rebuilt RENAME TO task_review_decisions;
            CREATE UNIQUE INDEX ux_task_review_decisions_identity
            ON task_review_decisions(review_decision_id, project_id, task_id, revision_id);
            CREATE INDEX ix_task_review_decisions_project_task_created
            ON task_review_decisions(project_id, task_id, created_at, review_decision_id);
            CREATE INDEX ix_task_review_decisions_subject
            ON task_review_decisions(project_id, task_id, final_report_event_id);
            PRAGMA user_version = 18;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<Row>> ReadAndValidateSubjectsAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT d.review_decision_id,d.project_id,d.task_id,d.revision_id,d.source_event_id,d.outcome,d.action_level,
                   d.authority_mode,d.authority_resolution,d.authority_mode_recording,d.created_at,
                   e.project_id,e.task_id,e.event_type,e.payload_json
            FROM task_review_decisions d
            LEFT JOIN task_events e ON e.id=d.source_event_id
            ORDER BY d.review_decision_id;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<Row>();
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.IsDBNull(4) || reader.IsDBNull(11) || reader.GetString(13) != "LeaderReviewDecisionRecorded")
                throw new InvalidDataException($"Review decision {reader.GetString(0)} has no valid source event.");
            var decisionId = Guid.Parse(reader.GetString(0));
            var projectId = Guid.Parse(reader.GetString(1));
            var taskId = Guid.Parse(reader.GetString(2));
            var revisionId = Guid.Parse(reader.GetString(3));
            if (!Guid.TryParse(reader.GetString(4), out var sourceEventId) || sourceEventId != decisionId || Guid.Parse(reader.GetString(11)) != projectId || Guid.Parse(reader.GetString(12)) != taskId)
                throw new InvalidDataException($"Review decision {decisionId} source identity conflicts.");
            var finalReportId = ReadSubject(reader.GetString(14), decisionId, revisionId);
            rows.Add(new(decisionId, projectId, taskId, revisionId, reader.GetString(4), finalReportId,
                reader.GetString(5), reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetString(8), reader.GetString(9), reader.GetString(10)));
        }
        return rows;
    }

    private static Guid ReadSubject(string payload, Guid decisionId, Guid revisionId)
    {
        try
        {
            using var json = JsonDocument.Parse(payload);
            var root = json.RootElement;
            var payloadRevision = RequiredGuid(root, "TaskRevisionId");
            if (payloadRevision != revisionId) throw new InvalidDataException("Task revision identity conflicts.");
            return RequiredGuid(root, "FinalReportEventId");
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            throw new InvalidDataException($"Malformed review decision source event {decisionId}.", exception);
        }
    }

    private static Guid RequiredGuid(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var result)
            ? result
            : throw new InvalidDataException($"Missing {name}.");

    private static void Add(SqliteCommand command, string name, object? value) => command.Parameters.AddWithValue(name, value switch { Guid guid => guid.ToString(), _ => value ?? DBNull.Value });

    private sealed record Row(Guid DecisionId, Guid ProjectId, Guid TaskId, Guid RevisionId, string SourceEventId, Guid FinalReportEventId, string Outcome, string ActionLevel, string? AuthorityMode, string AuthorityResolution, string AuthorityModeRecording, string CreatedAt);
}
