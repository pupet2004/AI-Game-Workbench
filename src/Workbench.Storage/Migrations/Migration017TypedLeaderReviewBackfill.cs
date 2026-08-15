using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Workbench.Storage.Migrations;

internal static class Migration017TypedLeaderReviewBackfill
{
    public const long Version = 17;

    public static async Task ApplyAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        var decisions = await ReadEventsAsync(connection, transaction, "LeaderReviewDecisionRecorded", cancellationToken);
        var completions = await ReadEventsAsync(connection, transaction, "AssignmentAutoCompleted", cancellationToken);
        var gates = await ReadEventsAsync(connection, transaction, "AssignmentNeedsUserDecision", cancellationToken);
        var responses = await ReadEventsAsync(connection, transaction, "LeaderReviewUserResponseReceived", cancellationToken);

        foreach (var decision in decisions)
        {
            var projection = ParseDecision(decision);
            var completionMatches = completions.Where(e => e.ProjectId == projection.ProjectId && e.TaskId == projection.TaskId && References(e, projection.DecisionId)).ToList();
            var gateMatches = gates.Where(e => e.ProjectId == projection.ProjectId && e.TaskId == projection.TaskId && References(e, projection.DecisionId)).ToList();
            if (completionMatches.Count > 1 || gateMatches.Count > 1 || (completionMatches.Count > 0 && gateMatches.Count > 0))
                throw new InvalidDataException($"Conflicting review evidence for decision {projection.DecisionId}.");
            if (completionMatches.Count == 0 && gateMatches.Count == 0)
                throw new InvalidDataException($"Review decision {projection.DecisionId} has no authoritative resolution event.");

            var resolution = completionMatches.Count == 1
                ? ParseCompletion(completionMatches[0], projection)
                : ParseGate(gateMatches[0], projection);
            await InsertDecisionIfAbsentAsync(connection, transaction, projection, resolution, cancellationToken);

            if (gateMatches.Count == 1)
            {
                var gateEvent = gateMatches[0];
                var responseMatches = responses.Where(e => e.ProjectId == projection.ProjectId && e.TaskId == projection.TaskId && References(e, projection.DecisionId)).ToList();
                if (responseMatches.Count > 1) throw new InvalidDataException($"Conflicting user responses for decision {projection.DecisionId}.");
                var response = responseMatches.Count == 1 ? ParseResponse(responseMatches[0], projection) : null;
                var questionId = response is null
                    ? await FindUniqueQuestionAsync(connection, transaction, projection, cancellationToken)
                    : await ExistingMessageIdAsync(connection, transaction, projection.ProjectId, response.QuestionMessageId, cancellationToken);
                var userId = response is null
                    ? null
                    : await ExistingMessageIdAsync(connection, transaction, projection.ProjectId, response.UserMessageId, cancellationToken);
                await InsertGateIfAbsentAsync(connection, transaction, projection, gateEvent.CreatedAt, response, questionId, userId, cancellationToken);
            }
        }

        var version = connection.CreateCommand();
        version.Transaction = transaction;
        version.CommandText = "PRAGMA user_version = 17;";
        await version.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<Event>> ReadEventsAsync(SqliteConnection connection, SqliteTransaction transaction, string eventType, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT id,project_id,task_id,payload_json,created_at FROM task_events WHERE event_type=$type ORDER BY created_at,id;";
        command.Parameters.AddWithValue("$type", eventType);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var events = new List<Event>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var payload = reader.GetString(3);
            if (eventType != "LeaderReviewDecisionRecorded") ValidateReferencePayload(payload, eventType, Guid.Parse(reader.GetString(0)));
            events.Add(new(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), Guid.Parse(reader.GetString(2)), payload, ParseTimestamp(reader.GetString(4))));
        }
        return events;
    }

    private static DecisionProjection ParseDecision(Event value)
    {
        try
        {
            using var json = JsonDocument.Parse(value.Payload);
            var root = json.RootElement;
            return new(value.EventId, value.ProjectId, value.TaskId,
                RequiredGuid(root, "TaskRevisionId"), RequiredGuid(root, "FinalReportEventId"),
                RequiredEnum(root, "Outcome", "Pass", "Fix", "Continue", "AskUser"),
                RequiredEnum(root, "ActionLevel", "L1LocalFix", "L2TaskRework", "L3DecisionRequired"), value.CreatedAt);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or FormatException or InvalidDataException)
        {
            throw new InvalidDataException($"Malformed review decision event {value.EventId}.", exception);
        }
    }

    private static ResolutionEvidence ParseCompletion(Event value, DecisionProjection decision)
    {
        try
        {
            using var json = JsonDocument.Parse(value.Payload); var root = json.RootElement;
            EnsureIdentity(root, decision);
            var authority = RequiredEnum(root, "Authority", "Cautious", "Balanced", "Autonomous");
            var resolution = RequiredEnum(root, "Resolution", "AutoProceed", "NotifyAndProceed", "AskUser");
            return new(authority, "Recorded", resolution);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or FormatException or InvalidDataException)
        { throw new InvalidDataException($"Malformed completion event for decision {decision.DecisionId}.", exception); }
    }

    private static ResolutionEvidence ParseGate(Event value, DecisionProjection decision)
    {
        try
        {
            using var json = JsonDocument.Parse(value.Payload); var root = json.RootElement;
            EnsureIdentity(root, decision);
            var resolution = RequiredEnum(root, "Resolution", "AutoProceed", "NotifyAndProceed", "AskUser");
            if (resolution != "AskUser") throw new InvalidDataException("AskUser gate has a non-AskUser resolution.");
            return new(null, "LegacyNotRecorded", resolution);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or FormatException or InvalidDataException)
        { throw new InvalidDataException($"Malformed gate event for decision {decision.DecisionId}.", exception); }
    }

    private static ResponseEvidence ParseResponse(Event value, DecisionProjection decision)
    {
        try
        {
            using var json = JsonDocument.Parse(value.Payload); var root = json.RootElement;
            EnsureIdentity(root, decision);
            var taskRevisionId = RequiredGuid(root, "TaskRevisionId");
            if (taskRevisionId != decision.RevisionId) throw new InvalidDataException("Task revision does not match decision.");
            return new(RequiredInt64(root, "QuestionMessageId"), RequiredInt64(root, "UserMessageId"), value.CreatedAt);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or FormatException or InvalidDataException)
        { throw new InvalidDataException($"Malformed response event for decision {decision.DecisionId}.", exception); }
    }

    private static void EnsureIdentity(JsonElement root, DecisionProjection decision)
    {
        if (RequiredGuid(root, "ReviewDecisionEventId") != decision.DecisionId) throw new InvalidDataException("Review decision identity mismatch.");
        if (root.TryGetProperty("TaskRevisionId", out var revision) && revision.ValueKind == JsonValueKind.String && Guid.Parse(revision.GetString()!) != decision.RevisionId) throw new InvalidDataException("Task revision identity mismatch.");
        if (root.TryGetProperty("FinalReportEventId", out var report) && report.ValueKind == JsonValueKind.String && Guid.Parse(report.GetString()!) != decision.FinalReportId) throw new InvalidDataException("Final report identity mismatch.");
    }

    private static bool References(Event value, Guid decisionId)
    {
        try { using var json = JsonDocument.Parse(value.Payload); return json.RootElement.TryGetProperty("ReviewDecisionEventId", out var id) && id.ValueKind == JsonValueKind.String && Guid.TryParse(id.GetString(), out var parsed) && parsed == decisionId; }
        catch (JsonException) { throw new InvalidDataException($"Malformed review evidence event {value.EventId}."); }
    }

    private static void ValidateReferencePayload(string payload, string eventType, Guid eventId)
    {
        try
        {
            using var json = JsonDocument.Parse(payload);
            if (!json.RootElement.TryGetProperty("ReviewDecisionEventId", out var id) || id.ValueKind != JsonValueKind.String || !Guid.TryParse(id.GetString(), out _))
                throw new InvalidDataException($"Known {eventType} event {eventId} has no valid ReviewDecisionEventId.");
        }
        catch (JsonException exception) { throw new InvalidDataException($"Malformed {eventType} event {eventId}.", exception); }
    }

    private static async Task InsertDecisionIfAbsentAsync(SqliteConnection connection, SqliteTransaction transaction, DecisionProjection decision, ResolutionEvidence evidence, CancellationToken cancellationToken)
    {
        var existing = connection.CreateCommand(); existing.Transaction = transaction;
        existing.CommandText = "SELECT project_id,task_id,revision_id,source_event_id,outcome,action_level,authority_mode,authority_resolution,authority_mode_recording,created_at FROM task_review_decisions WHERE review_decision_id=$id;";
        existing.Parameters.AddWithValue("$id", decision.DecisionId.ToString());
        await using var reader = await existing.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            var same = reader.GetString(0) == decision.ProjectId.ToString() && reader.GetString(1) == decision.TaskId.ToString() && reader.GetString(2) == decision.RevisionId.ToString() && reader.GetString(3) == decision.DecisionId.ToString() && reader.GetString(4) == decision.Outcome && reader.GetString(5) == decision.ActionLevel && (reader.IsDBNull(6) ? null : reader.GetString(6)) == evidence.AuthorityMode && reader.GetString(7) == evidence.Resolution && reader.GetString(8) == evidence.Recording && ParseTimestamp(reader.GetString(9)) == decision.CreatedAt;
            if (!same) throw new InvalidDataException($"Existing typed decision conflicts with legacy decision {decision.DecisionId}.");
            return;
        }
        await reader.DisposeAsync();
        var insert = connection.CreateCommand(); insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO task_review_decisions(review_decision_id,project_id,task_id,revision_id,source_event_id,outcome,action_level,authority_mode,authority_resolution,authority_mode_recording,created_at) VALUES($id,$p,$t,$r,$source,$outcome,$action,$authority,$resolution,$recording,$created);";
        Add(insert, "$id", decision.DecisionId); Add(insert, "$p", decision.ProjectId); Add(insert, "$t", decision.TaskId); Add(insert, "$r", decision.RevisionId); Add(insert, "$source", decision.DecisionId); Add(insert, "$outcome", decision.Outcome); Add(insert, "$action", decision.ActionLevel); Add(insert, "$authority", evidence.AuthorityMode); Add(insert, "$resolution", evidence.Resolution); Add(insert, "$recording", evidence.Recording); Add(insert, "$created", decision.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertGateIfAbsentAsync(SqliteConnection connection, SqliteTransaction transaction, DecisionProjection decision, DateTimeOffset openedAt, ResponseEvidence? response, long? questionId, long? userId, CancellationToken cancellationToken)
    {
        var existing = connection.CreateCommand(); existing.Transaction = transaction; existing.CommandText = "SELECT project_id,task_id,revision_id,question_message_id,user_message_id,opened_at,responded_at,state FROM task_review_user_gates WHERE review_decision_id=$id;"; existing.Parameters.AddWithValue("$id", decision.DecisionId.ToString());
        await using var reader = await existing.ExecuteReaderAsync(cancellationToken);
        var expectedState = response is null ? "Open" : "Responded"; var respondedAt = response?.RespondedAt.ToString("O", CultureInfo.InvariantCulture);
        if (await reader.ReadAsync(cancellationToken))
        {
            var same = reader.GetString(0) == decision.ProjectId.ToString() && reader.GetString(1) == decision.TaskId.ToString() && reader.GetString(2) == decision.RevisionId.ToString() && NullableLong(reader, 3) == questionId && NullableLong(reader, 4) == userId && ParseTimestamp(reader.GetString(5)) == openedAt && (reader.IsDBNull(6) ? null : ParseTimestamp(reader.GetString(6))) == response?.RespondedAt && reader.GetString(7) == expectedState;
            if (!same) throw new InvalidDataException($"Existing typed gate conflicts with legacy decision {decision.DecisionId}.");
            return;
        }
        await reader.DisposeAsync();
        var insert = connection.CreateCommand(); insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO task_review_user_gates(review_decision_id,project_id,task_id,revision_id,question_message_id,user_message_id,opened_at,responded_at,state) VALUES($id,$p,$t,$r,$q,$u,$opened,$responded,$state);";
        Add(insert, "$id", decision.DecisionId); Add(insert, "$p", decision.ProjectId); Add(insert, "$t", decision.TaskId); Add(insert, "$r", decision.RevisionId); Add(insert, "$q", questionId); Add(insert, "$u", userId); Add(insert, "$opened", openedAt.ToString("O", CultureInfo.InvariantCulture)); Add(insert, "$responded", respondedAt); Add(insert, "$state", expectedState);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long?> FindUniqueQuestionAsync(SqliteConnection connection, SqliteTransaction transaction, DecisionProjection decision, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "SELECT m.id FROM leader_messages m JOIN leader_session_epochs e ON e.id=m.epoch_id WHERE e.project_id=$p AND instr(m.text,$marker)>0;"; Add(command, "$p", decision.ProjectId); Add(command, "$marker", $"review-user-decision:{decision.ProjectId:D}:{decision.TaskId:D}:{decision.DecisionId:D}");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); long? result = null; var count = 0;
        while (await reader.ReadAsync(cancellationToken)) { count++; result = reader.GetInt64(0); }
        return count == 1 ? result : null;
    }

    private static async Task<long?> ExistingMessageIdAsync(SqliteConnection connection, SqliteTransaction transaction, Guid projectId, long id, CancellationToken cancellationToken)
    {
        if (id <= 0) throw new InvalidDataException("Message locator must be positive.");
        var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "SELECT m.id FROM leader_messages m JOIN leader_session_epochs e ON e.id=m.epoch_id WHERE e.project_id=$p AND m.id=$id;"; Add(command, "$p", projectId); Add(command, "$id", id);
        return await command.ExecuteScalarAsync(cancellationToken) is object value ? Convert.ToInt64(value, CultureInfo.InvariantCulture) : null;
    }

    private static Guid RequiredGuid(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var result) ? result : throw new InvalidDataException($"Missing {name}.");
    private static long RequiredInt64(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.TryGetInt64(out var result) ? result : throw new InvalidDataException($"Missing {name}.");
    private static string RequiredEnum(JsonElement root, string name, params string[] values) { if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || !values.Contains(value.GetString(), StringComparer.Ordinal)) throw new InvalidDataException($"Invalid {name}."); return value.GetString()!; }
    private static DateTimeOffset ParseTimestamp(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static long? NullableLong(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetInt64(index);
    private static void Add(SqliteCommand command, string name, object? value) => command.Parameters.AddWithValue(name, value switch { Guid guid => guid.ToString(), DateTimeOffset time => time.ToString("O", CultureInfo.InvariantCulture), _ => value ?? DBNull.Value });

    private sealed record Event(Guid EventId, Guid ProjectId, Guid TaskId, string Payload, DateTimeOffset CreatedAt);
    private sealed record DecisionProjection(Guid DecisionId, Guid ProjectId, Guid TaskId, Guid RevisionId, Guid FinalReportId, string Outcome, string ActionLevel, DateTimeOffset CreatedAt);
    private sealed record ResolutionEvidence(string? AuthorityMode, string Recording, string Resolution);
    private sealed record ResponseEvidence(long QuestionMessageId, long UserMessageId, DateTimeOffset RespondedAt);
}
