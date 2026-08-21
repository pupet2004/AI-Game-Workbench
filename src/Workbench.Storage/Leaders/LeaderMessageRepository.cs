using System.Globalization;
using Microsoft.Data.Sqlite;
using Workbench.Storage.Database;

namespace Workbench.Storage.Leaders;

public sealed class LeaderMessageRepository(WorkbenchDatabase database)
{
    private static readonly HashSet<string> Roles = new(StringComparer.Ordinal) { "user", "assistant" };
    private readonly WorkbenchDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    public async Task<IReadOnlyList<StoredLeaderMessage>> GetAllAsync(
        Guid epochId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, epoch_id, sequence, role, text, created_at,
                   result_id, summary_delta_payload_json, summary_persisted_at
            FROM leader_messages
            WHERE epoch_id = $epochId
            ORDER BY sequence;
            """;
        command.Parameters.AddWithValue("$epochId", epochId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var messages = new List<StoredLeaderMessage>();
        while (await reader.ReadAsync(cancellationToken))
        {
            messages.Add(ReadMessage(reader));
        }

        return messages;
    }

    public async Task<long> GetNextSequenceAsync(Guid epochId, CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await GetNextSequenceAsync(connection, null, epochId, cancellationToken);
    }

    public async Task<StoredLeaderMessage> AppendAsync(
        Guid epochId,
        string role,
        string text,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default,
        LeaderResultMetadata? metadata = null) =>
        await AppendCoreAsync(epochId, role, text, createdAt, metadata, cancellationToken);

    public async Task<IReadOnlyList<PendingLeaderSummaryResult>> GetPendingSummaryResultsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT message.id, epoch.project_id, message.epoch_id, message.result_id,
                   message.summary_delta_payload_json, message.created_at
            FROM leader_messages AS message
            INNER JOIN leader_session_epochs AS epoch ON epoch.id = message.epoch_id
            WHERE message.role = 'assistant'
              AND message.result_id IS NOT NULL
              AND message.summary_delta_payload_json IS NOT NULL
              AND message.summary_persisted_at IS NULL
            ORDER BY message.created_at, message.id;
            """;
        var results = new List<PendingLeaderSummaryResult>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new PendingLeaderSummaryResult(
                reader.GetInt64(0),
                Guid.Parse(reader.GetString(1)),
                Guid.Parse(reader.GetString(2)),
                Guid.Parse(reader.GetString(3)),
                reader.GetString(4),
                Parse(reader.GetString(5))));
        }
        return results;
    }

    public async Task<bool> MarkSummaryPersistedAsync(
        long messageId,
        Guid resultId,
        DateTimeOffset persistedAt,
        CancellationToken cancellationToken = default)
    {
        if (messageId <= 0) throw new ArgumentOutOfRangeException(nameof(messageId));
        if (resultId == Guid.Empty) throw new ArgumentException("Result identity is required.", nameof(resultId));
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE leader_messages
            SET summary_persisted_at = COALESCE(summary_persisted_at, $persistedAt)
            WHERE id = $messageId
              AND result_id = $resultId
              AND role = 'assistant'
              AND summary_delta_payload_json IS NOT NULL;
            """;
        command.Parameters.AddWithValue("$persistedAt", Format(persistedAt));
        command.Parameters.AddWithValue("$messageId", messageId);
        command.Parameters.AddWithValue("$resultId", resultId.ToString());
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private async Task<StoredLeaderMessage> AppendCoreAsync(
        Guid epochId,
        string role,
        string text,
        DateTimeOffset createdAt,
        LeaderResultMetadata? metadata,
        CancellationToken cancellationToken)
    {
        ValidateRole(role);
        ArgumentNullException.ThrowIfNull(text);
        if (metadata is not null && role != "assistant")
        {
            throw new ArgumentException("Summary metadata is only valid for assistant messages.", nameof(metadata));
        }
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var sequence = await GetNextSequenceAsync(connection, transaction, epochId, cancellationToken);
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = metadata is null
            ? """
                INSERT INTO leader_messages (epoch_id, sequence, role, text, created_at)
                VALUES ($epochId, $sequence, $role, $text, $createdAt);
                SELECT last_insert_rowid();
                """
            : """
                INSERT INTO leader_messages
                    (epoch_id, sequence, role, text, created_at, result_id, summary_delta_payload_json)
                VALUES
                    ($epochId, $sequence, $role, $text, $createdAt, $resultId, $summaryDeltaPayloadJson);
                SELECT last_insert_rowid();
                """;
        command.Parameters.AddWithValue("$epochId", epochId.ToString());
        command.Parameters.AddWithValue("$sequence", sequence);
        command.Parameters.AddWithValue("$role", role);
        command.Parameters.AddWithValue("$text", text);
        command.Parameters.AddWithValue("$createdAt", Format(createdAt));
        if (metadata is not null)
        {
            command.Parameters.AddWithValue("$resultId", metadata.ResultId.ToString());
            command.Parameters.AddWithValue("$summaryDeltaPayloadJson", metadata.SummaryDeltaPayloadJson);
        }
        var id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return new StoredLeaderMessage(
            id,
            epochId,
            sequence,
            role,
            text,
            createdAt)
        {
            ResultId = metadata?.ResultId,
            SummaryDeltaPayloadJson = metadata?.SummaryDeltaPayloadJson
        };
    }

    public async Task<RecentConversationStats> GetStatsAsync(Guid projectId, Guid epochId, CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection(); await connection.OpenAsync(cancellationToken);
        await EnsureOwnedAsync(connection, projectId, epochId, cancellationToken);
        var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*),COALESCE(SUM(length(CAST(text AS BLOB))),0),MAX(sequence) FROM leader_messages WHERE epoch_id=$epochId;"; command.Parameters.AddWithValue("$epochId", epochId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); await reader.ReadAsync(cancellationToken);
        return new RecentConversationStats(projectId, epochId, reader.GetInt32(0), reader.GetInt32(1), reader.IsDBNull(2) ? null : reader.GetInt64(2));
    }

    public async Task<RecentConversationSlice> GetRecentAsync(Guid projectId, Guid epochId, long? beforeSequence, int maxMessages, int maxUtf8Bytes, CancellationToken cancellationToken = default)
    {
        if (maxMessages <= 0 || maxUtf8Bytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxMessages));
        await using var connection = _database.CreateConnection(); await connection.OpenAsync(cancellationToken); await EnsureOwnedAsync(connection, projectId, epochId, cancellationToken);
        var command = connection.CreateCommand(); command.CommandText = "SELECT id,epoch_id,sequence,role,text,created_at,result_id,summary_delta_payload_json,summary_persisted_at FROM leader_messages WHERE epoch_id=$epochId AND ($before IS NULL OR sequence < $before) ORDER BY sequence DESC;"; command.Parameters.AddWithValue("$epochId", epochId.ToString()); command.Parameters.AddWithValue("$before", (object?)beforeSequence ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); var selected = new List<StoredLeaderMessage>(); var bytes = 0; var omitted = 0;
        while (await reader.ReadAsync(cancellationToken)) { var item = ReadMessage(reader); var itemBytes = System.Text.Encoding.UTF8.GetByteCount(item.Text); if (selected.Count < maxMessages && bytes + itemBytes <= maxUtf8Bytes) { selected.Add(item); bytes += itemBytes; } else omitted++; }
        selected.Reverse(); return new RecentConversationSlice(projectId, epochId, selected, bytes, omitted);
    }

    private static async Task EnsureOwnedAsync(SqliteConnection connection, Guid projectId, Guid epochId, CancellationToken cancellationToken)
    { var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM leader_session_epochs WHERE id=$epochId AND project_id=$projectId;"; command.Parameters.AddWithValue("$epochId", epochId.ToString()); command.Parameters.AddWithValue("$projectId", projectId.ToString()); if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) != 1) throw new InvalidOperationException("The Leader epoch is not owned by this project."); }

    private static async Task<long> GetNextSequenceAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid epochId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(MAX(sequence), 0) + 1 FROM leader_messages WHERE epoch_id = $epochId;";
        command.Parameters.AddWithValue("$epochId", epochId.ToString());
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static void ValidateRole(string role)
    {
        if (!Roles.Contains(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role), role, "Leader message role must be 'user' or 'assistant'.");
        }
    }

    private static StoredLeaderMessage ReadMessage(SqliteDataReader reader)
    {
        var role = reader.GetString(3);
        ValidateRole(role);
        return new StoredLeaderMessage(
            reader.GetInt64(0),
            Guid.Parse(reader.GetString(1)),
            reader.GetInt64(2),
            role,
            reader.GetString(4),
            Parse(reader.GetString(5)))
        {
            ResultId = reader.IsDBNull(6) ? null : Guid.Parse(reader.GetString(6)),
            SummaryDeltaPayloadJson = reader.IsDBNull(7) ? null : reader.GetString(7),
            SummaryPersistedAt = reader.IsDBNull(8) ? null : Parse(reader.GetString(8))
        };
    }

    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
