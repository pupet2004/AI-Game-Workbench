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
                started_at, last_active_at, ended_at, rollover_reason, handoff_summary)
            VALUES (
                $id, $projectId, $providerId, $providerAccountId, $modelId,
                $agentSessionId, $externalSessionId, $workingDirectory,
                $startedAt, $lastActiveAt, $endedAt, $rolloverReason, $handoffSummary)
            ON CONFLICT(id) DO UPDATE SET
                last_active_at = excluded.last_active_at;
            """;
        AddParameters(command, epoch);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
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
               started_at, last_active_at, ended_at, rollover_reason, handoff_summary
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
            reader.IsDBNull(12) ? null : reader.GetString(12));

    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
