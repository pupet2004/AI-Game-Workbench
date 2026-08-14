using System.Globalization;
using Microsoft.Data.Sqlite;
using Workbench.Storage.Database;

namespace Workbench.Storage.Leaders;

public sealed class LeaderSessionEpochRepository(WorkbenchDatabase database)
{
    private readonly WorkbenchDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    public Task<StoredLeaderSessionEpoch?> GetAsync(Guid epochId, CancellationToken cancellationToken = default) =>
        GetSingleAsync("WHERE id = $id", (command) => command.Parameters.AddWithValue("$id", epochId.ToString()), cancellationToken);

    public Task<StoredLeaderSessionEpoch?> GetCurrentForProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default) =>
        GetSingleAsync(
            "WHERE id = (SELECT current_epoch_id FROM project_leaders WHERE project_id = $projectId)",
            command => command.Parameters.AddWithValue("$projectId", projectId.ToString()),
            cancellationToken);

    public async Task<IReadOnlyList<StoredLeaderSessionEpoch>> GetAllForProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = $"{SelectSql} WHERE project_id = $projectId ORDER BY started_at, id;";
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var epochs = new List<StoredLeaderSessionEpoch>();
        while (await reader.ReadAsync(cancellationToken))
        {
            epochs.Add(Read(reader));
        }

        return epochs;
    }

    public Task<StoredLeaderSessionEpoch?> GetMostRecentArchivedForProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default) =>
        GetSingleAsync(
            "WHERE project_id = $projectId AND ended_at IS NOT NULL ORDER BY ended_at DESC, started_at DESC, id DESC LIMIT 1",
            command => command.Parameters.AddWithValue("$projectId", projectId.ToString()),
            cancellationToken);

    public async Task<LeaderEpochHistoryPage> GetArchivedPageAsync(
        Guid projectId,
        int pageSize,
        LeaderEpochHistoryCursor? before = null,
        CancellationToken cancellationToken = default)
    {
        if (pageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }

        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT e.id, e.ended_at, e.rollover_reason, e.handoff_summary,
                   (SELECT COUNT(*) FROM leader_messages m WHERE m.epoch_id = e.id)
            FROM leader_session_epochs e
            WHERE e.project_id = $projectId AND e.ended_at IS NOT NULL
              AND ($beforeEndedAt IS NULL OR e.ended_at < $beforeEndedAt
                   OR (e.ended_at = $beforeEndedAt AND e.id < $beforeId))
            ORDER BY e.ended_at DESC, e.id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        command.Parameters.AddWithValue("$beforeEndedAt", before is null ? DBNull.Value : Format(before.EndedAt));
        command.Parameters.AddWithValue("$beforeId", before is null ? DBNull.Value : before.Id.ToString());
        command.Parameters.AddWithValue("$limit", pageSize + 1);
        var rows = new List<StoredArchivedLeaderSessionEpoch>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new StoredArchivedLeaderSessionEpoch(
                Guid.Parse(reader.GetString(0)), Parse(reader.GetString(1)),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetInt32(4)));
        }

        var hasMore = rows.Count > pageSize;
        if (hasMore) rows.RemoveAt(rows.Count - 1);
        var last = rows.LastOrDefault();
        return new LeaderEpochHistoryPage(rows, hasMore && last is not null
            ? new LeaderEpochHistoryCursor(last.EndedAt, last.Id) : null);
    }

    public async Task SaveAsync(StoredLeaderSessionEpoch epoch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(epoch);
        ArgumentException.ThrowIfNullOrWhiteSpace(epoch.ProviderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(epoch.ModelId);
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var existingCommand = connection.CreateCommand();
        existingCommand.Transaction = transaction;
        existingCommand.CommandText = $"{SelectSql} WHERE id = $id;";
        existingCommand.Parameters.AddWithValue("$id", epoch.Id.ToString());
        StoredLeaderSessionEpoch? existing = null;
        await using (var reader = await existingCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                existing = Read(reader);
            }
        }

        if (existing is not null && existing with { LastActiveAt = epoch.LastActiveAt } != epoch)
        {
            throw new InvalidOperationException(
                "An existing Leader session epoch is immutable except for its last-active timestamp; use ArchiveAsync to end it.");
        }

        if (existing is null && epoch.EndedAt is null)
        {
            throw new InvalidOperationException(
                "A new active Leader session epoch must be created atomically with its current pointer.");
        }

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO leader_session_epochs (
                id, project_id, provider_id, provider_account_id, model_id,
                agent_session_id, external_session_id, working_directory,
                started_at, last_active_at, ended_at, rollover_reason, handoff_summary,
                boot_context_delivered_at)
            VALUES (
                $id, $projectId, $providerId, $providerAccountId, $modelId,
                $agentSessionId, $externalSessionId, $workingDirectory,
                $startedAt, $lastActiveAt, $endedAt, $rolloverReason, $handoffSummary,
                $bootContextDeliveredAt)
            ON CONFLICT(id) DO UPDATE SET
                last_active_at = excluded.last_active_at;
            """;
        AddParameters(command, epoch);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task MarkBootContextDeliveredAsync(
        Guid epochId,
        DateTimeOffset deliveredAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE leader_session_epochs
            SET boot_context_delivered_at = COALESCE(boot_context_delivered_at, $deliveredAt)
            WHERE id = $id AND ended_at IS NULL;
            """;
        command.Parameters.AddWithValue("$id", epochId.ToString());
        command.Parameters.AddWithValue("$deliveredAt", Format(deliveredAt));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Boot context can only be delivered to an active Leader epoch.");
        }

        var consumeSelections = connection.CreateCommand();
        consumeSelections.Transaction = transaction;
        consumeSelections.CommandText = "DELETE FROM leader_epoch_continuity_selections WHERE epoch_id = $id;";
        consumeSelections.Parameters.AddWithValue("$id", epochId.ToString());
        await consumeSelections.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<StoredLeaderSessionEpoch> SaveActiveHandoffAsync(Guid projectId, Guid epochId, string? content, CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection(); await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "UPDATE leader_session_epochs SET handoff_summary=$content WHERE id=$epochId AND project_id=$projectId AND ended_at IS NULL;";
        command.Parameters.AddWithValue("$content", (object?)content ?? DBNull.Value); command.Parameters.AddWithValue("$epochId", epochId.ToString()); command.Parameters.AddWithValue("$projectId", projectId.ToString());
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidOperationException("Only an active project-owned Leader epoch may update its Handoff.");
        return (await GetAsync(epochId, cancellationToken))!;
    }

    public async Task ArchiveAsync(
        Guid epochId,
        DateTimeOffset endedAt,
        string? rolloverReason,
        string? handoffSummary,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var archive = connection.CreateCommand();
        archive.Transaction = transaction;
        archive.CommandText = """
            UPDATE leader_session_epochs
            SET ended_at = $endedAt, rollover_reason = $rolloverReason, handoff_summary = $handoffSummary
            WHERE id = $id;
            """;
        archive.Parameters.AddWithValue("$endedAt", Format(endedAt));
        archive.Parameters.AddWithValue("$rolloverReason", (object?)rolloverReason ?? DBNull.Value);
        archive.Parameters.AddWithValue("$handoffSummary", (object?)handoffSummary ?? DBNull.Value);
        archive.Parameters.AddWithValue("$id", epochId.ToString());
        await archive.ExecuteNonQueryAsync(cancellationToken);

        var clear = connection.CreateCommand();
        clear.Transaction = transaction;
        clear.CommandText = """
            UPDATE project_leaders
            SET current_epoch_id = NULL, updated_at = $endedAt
            WHERE current_epoch_id = $id;
            """;
        clear.Parameters.AddWithValue("$endedAt", Format(endedAt));
        clear.Parameters.AddWithValue("$id", epochId.ToString());
        await clear.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<StoredLeaderSessionEpoch?> GetSingleAsync(
        string predicate,
        Action<SqliteCommand> addParameters,
        CancellationToken cancellationToken)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = $"{SelectSql} {predicate};";
        addParameters(command);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    private const string SelectSql = """
        SELECT id, project_id, provider_id, provider_account_id, model_id,
               agent_session_id, external_session_id, working_directory,
               started_at, last_active_at, ended_at, rollover_reason, handoff_summary,
               boot_context_delivered_at
        FROM leader_session_epochs
        """;

    private static void AddParameters(SqliteCommand command, StoredLeaderSessionEpoch epoch)
    {
        command.Parameters.AddWithValue("$id", epoch.Id.ToString());
        command.Parameters.AddWithValue("$projectId", epoch.ProjectId.ToString());
        command.Parameters.AddWithValue("$providerId", epoch.ProviderId);
        command.Parameters.AddWithValue("$providerAccountId", epoch.ProviderAccountId.ToString());
        command.Parameters.AddWithValue("$modelId", epoch.ModelId);
        command.Parameters.AddWithValue("$agentSessionId", epoch.AgentSessionId.ToString());
        command.Parameters.AddWithValue("$externalSessionId", (object?)epoch.ExternalSessionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$workingDirectory", (object?)epoch.WorkingDirectory ?? DBNull.Value);
        command.Parameters.AddWithValue("$startedAt", Format(epoch.StartedAt));
        command.Parameters.AddWithValue("$lastActiveAt", Format(epoch.LastActiveAt));
        command.Parameters.AddWithValue("$endedAt", epoch.EndedAt is null ? DBNull.Value : Format(epoch.EndedAt.Value));
        command.Parameters.AddWithValue("$rolloverReason", (object?)epoch.RolloverReason ?? DBNull.Value);
        command.Parameters.AddWithValue("$handoffSummary", (object?)epoch.HandoffSummary ?? DBNull.Value);
        command.Parameters.AddWithValue("$bootContextDeliveredAt", epoch.BootContextDeliveredAt is null
            ? DBNull.Value
            : Format(epoch.BootContextDeliveredAt.Value));
    }

    private static StoredLeaderSessionEpoch Read(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), reader.GetString(2),
            Guid.Parse(reader.GetString(3)), reader.GetString(4), Guid.Parse(reader.GetString(5)),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            Parse(reader.GetString(8)), Parse(reader.GetString(9)),
            reader.IsDBNull(10) ? null : Parse(reader.GetString(10)),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.IsDBNull(13) ? null : Parse(reader.GetString(13)));

    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
