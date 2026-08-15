using System.Globalization;
using Microsoft.Data.Sqlite;
using Workbench.Core.Leaders;
using Workbench.Storage.Database;

namespace Workbench.Storage.Reviews;

public enum LeaderReviewWriteResult { Applied, Existing, Conflict, NotFound }

public sealed record LeaderReviewDecisionWriteRequest(
    Guid ReviewDecisionId,
    Guid ProjectId,
    Guid TaskId,
    Guid RevisionId,
    string Outcome,
    string ActionLevel,
    string AuthorityResolution,
    LeaderAuthorityMode AuthorityMode,
    DateTimeOffset CreatedAt);

public sealed record LeaderReviewDecisionRecord(
    Guid ReviewDecisionId,
    Guid ProjectId,
    Guid TaskId,
    Guid RevisionId,
    string Outcome,
    string ActionLevel,
    LeaderAuthorityMode? AuthorityMode,
    string AuthorityModeRecording,
    string AuthorityResolution,
    DateTimeOffset CreatedAt);

public sealed record LeaderReviewUserGateWriteRequest(
    Guid ReviewDecisionId,
    Guid ProjectId,
    Guid TaskId,
    Guid RevisionId,
    long? QuestionMessageId,
    DateTimeOffset OpenedAt);

public sealed record LeaderReviewUserGateRecord(
    Guid ReviewDecisionId,
    Guid ProjectId,
    Guid TaskId,
    Guid RevisionId,
    long? QuestionMessageId,
    long? UserMessageId,
    DateTimeOffset OpenedAt,
    DateTimeOffset? RespondedAt,
    string State);

public sealed class LeaderReviewStateRepository(WorkbenchDatabase database)
{
    private readonly WorkbenchDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    public async Task<LeaderReviewWriteResult> InsertDecisionIfAbsentAsync(LeaderReviewDecisionWriteRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        await using var connection = _database.CreateConnection(); await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var existing = await ReadDecisionAsync(connection, transaction, request.ProjectId, request.TaskId, request.ReviewDecisionId, cancellationToken);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return Matches(existing, request) ? LeaderReviewWriteResult.Existing : LeaderReviewWriteResult.Conflict;
        }
        var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO task_review_decisions(review_decision_id,project_id,task_id,revision_id,source_event_id,outcome,action_level,authority_mode,authority_resolution,authority_mode_recording,created_at) VALUES($id,$p,$t,$r,$source,$outcome,$action,$authority,$resolution,'Recorded',$created);";
        Add(command, "$id", request.ReviewDecisionId); Add(command, "$p", request.ProjectId); Add(command, "$t", request.TaskId); Add(command, "$r", request.RevisionId); Add(command, "$source", request.ReviewDecisionId); Add(command, "$outcome", request.Outcome); Add(command, "$action", request.ActionLevel); Add(command, "$authority", request.AuthorityMode.ToString()); Add(command, "$resolution", request.AuthorityResolution); Add(command, "$created", Format(request.CreatedAt));
        try { await command.ExecuteNonQueryAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return LeaderReviewWriteResult.Applied; }
        catch (SqliteException) { await transaction.RollbackAsync(CancellationToken.None); return LeaderReviewWriteResult.Conflict; }
    }

    public async Task<LeaderReviewDecisionRecord?> GetDecisionAsync(Guid projectId, Guid taskId, Guid reviewDecisionId, CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection(); await connection.OpenAsync(cancellationToken);
        return await ReadDecisionAsync(connection, null, projectId, taskId, reviewDecisionId, cancellationToken);
    }

    public async Task<LeaderReviewWriteResult> OpenUserGateIfAbsentAsync(LeaderReviewUserGateWriteRequest request, CancellationToken cancellationToken = default)
    {
        if (request.ReviewDecisionId == Guid.Empty || request.ProjectId == Guid.Empty || request.TaskId == Guid.Empty || request.RevisionId == Guid.Empty) return LeaderReviewWriteResult.Conflict;
        await using var connection = _database.CreateConnection(); await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var existing = await ReadGateAsync(connection, transaction, request.ProjectId, request.TaskId, request.ReviewDecisionId, cancellationToken);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return existing.State == "Open" && existing.RevisionId == request.RevisionId && existing.QuestionMessageId == request.QuestionMessageId && existing.OpenedAt == request.OpenedAt ? LeaderReviewWriteResult.Existing : LeaderReviewWriteResult.Conflict;
        }
        var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO task_review_user_gates(review_decision_id,project_id,task_id,revision_id,question_message_id,user_message_id,opened_at,responded_at,state) VALUES($id,$p,$t,$r,$q,NULL,$opened,NULL,'Open');";
        Add(command, "$id", request.ReviewDecisionId); Add(command, "$p", request.ProjectId); Add(command, "$t", request.TaskId); Add(command, "$r", request.RevisionId); Add(command, "$q", request.QuestionMessageId); Add(command, "$opened", Format(request.OpenedAt));
        try { await command.ExecuteNonQueryAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return LeaderReviewWriteResult.Applied; }
        catch (SqliteException) { await transaction.RollbackAsync(CancellationToken.None); return LeaderReviewWriteResult.Conflict; }
    }

    public async Task<IReadOnlyList<LeaderReviewUserGateRecord>> GetOpenUserGatesAsync(Guid projectId, Guid? taskId = null, CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection(); await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand(); command.CommandText = "SELECT review_decision_id,project_id,task_id,revision_id,question_message_id,user_message_id,opened_at,responded_at,state FROM task_review_user_gates WHERE project_id=$p AND state='Open' AND ($t IS NULL OR task_id=$t) ORDER BY review_decision_id;"; Add(command, "$p", projectId); Add(command, "$t", taskId);
        return await ReadGatesAsync(command, cancellationToken);
    }

    public async Task<bool> TryBindFirstUserResponseAsync(Guid projectId, Guid taskId, Guid reviewDecisionId, long userMessageId, DateTimeOffset respondedAt, CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty || taskId == Guid.Empty || reviewDecisionId == Guid.Empty || userMessageId <= 0) return false;
        await using var connection = _database.CreateConnection(); await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "UPDATE task_review_user_gates SET state='Responded',user_message_id=$user,responded_at=$responded WHERE review_decision_id=$id AND project_id=$p AND task_id=$t AND state='Open' AND user_message_id IS NULL AND responded_at IS NULL AND EXISTS (SELECT 1 FROM leader_messages m JOIN leader_session_epochs e ON e.id=m.epoch_id WHERE m.id=$user AND e.project_id=$p);";
        Add(command, "$user", userMessageId); Add(command, "$responded", Format(respondedAt)); Add(command, "$id", reviewDecisionId); Add(command, "$p", projectId); Add(command, "$t", taskId);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<long?> GetBoundUserMessageIdAsync(Guid projectId, Guid taskId, Guid reviewDecisionId, CancellationToken cancellationToken = default)
    {
        var gate = await GetGateAsync(projectId, taskId, reviewDecisionId, cancellationToken);
        return gate?.State == "Responded" ? gate.UserMessageId : null;
    }

    public async Task<LeaderReviewUserGateRecord?> GetGateAsync(Guid projectId, Guid taskId, Guid reviewDecisionId, CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection(); await connection.OpenAsync(cancellationToken);
        return await ReadGateAsync(connection, null, projectId, taskId, reviewDecisionId, cancellationToken);
    }

    private static async Task<LeaderReviewDecisionRecord?> ReadDecisionAsync(SqliteConnection connection, SqliteTransaction? transaction, Guid projectId, Guid taskId, Guid id, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "SELECT review_decision_id,project_id,task_id,revision_id,outcome,action_level,authority_mode,authority_mode_recording,authority_resolution,created_at FROM task_review_decisions WHERE project_id=$p AND task_id=$t AND review_decision_id=$id;"; Add(command, "$p", projectId); Add(command, "$t", taskId); Add(command, "$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), Guid.Parse(reader.GetString(2)), Guid.Parse(reader.GetString(3)), reader.GetString(4), reader.GetString(5), reader.IsDBNull(6) ? null : Enum.Parse<LeaderAuthorityMode>(reader.GetString(6)), reader.GetString(7), reader.GetString(8), Parse(reader.GetString(9)));
    }

    private static async Task<LeaderReviewUserGateRecord?> ReadGateAsync(SqliteConnection connection, SqliteTransaction? transaction, Guid projectId, Guid taskId, Guid id, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "SELECT review_decision_id,project_id,task_id,revision_id,question_message_id,user_message_id,opened_at,responded_at,state FROM task_review_user_gates WHERE project_id=$p AND task_id=$t AND review_decision_id=$id;"; Add(command, "$p", projectId); Add(command, "$t", taskId); Add(command, "$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return ReadGate(reader);
    }

    private static async Task<IReadOnlyList<LeaderReviewUserGateRecord>> ReadGatesAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); var result = new List<LeaderReviewUserGateRecord>();
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadGate(reader));
        return result;
    }

    private static LeaderReviewUserGateRecord ReadGate(SqliteDataReader reader) => new(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), Guid.Parse(reader.GetString(2)), Guid.Parse(reader.GetString(3)), reader.IsDBNull(4) ? null : reader.GetInt64(4), reader.IsDBNull(5) ? null : reader.GetInt64(5), Parse(reader.GetString(6)), reader.IsDBNull(7) ? null : Parse(reader.GetString(7)), reader.GetString(8));
    private static bool Matches(LeaderReviewDecisionRecord existing, LeaderReviewDecisionWriteRequest request) => existing.RevisionId == request.RevisionId && existing.Outcome == request.Outcome && existing.ActionLevel == request.ActionLevel && existing.AuthorityMode == request.AuthorityMode && existing.AuthorityModeRecording == "Recorded" && existing.AuthorityResolution == request.AuthorityResolution && existing.CreatedAt == request.CreatedAt;
    private static void Validate(LeaderReviewDecisionWriteRequest request) { if (request.ReviewDecisionId == Guid.Empty || request.ProjectId == Guid.Empty || request.TaskId == Guid.Empty || request.RevisionId == Guid.Empty || !Enum.IsDefined(request.AuthorityMode)) throw new ArgumentException("Invalid review decision identity or authority mode."); }
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static void Add(SqliteCommand command, string name, object? value) => command.Parameters.AddWithValue(name, value switch { Guid guid => guid.ToString(), DateTimeOffset time => Format(time), _ => value ?? DBNull.Value });
}
