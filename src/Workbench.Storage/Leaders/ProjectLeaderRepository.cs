using System.Globalization;
using Microsoft.Data.Sqlite;
using Workbench.Storage.Database;

namespace Workbench.Storage.Leaders;

public sealed class ProjectLeaderRepository(WorkbenchDatabase database)
{
    private readonly WorkbenchDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    public async Task<StoredProjectLeader?> GetAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT project_id, current_epoch_id, created_at, updated_at
            FROM project_leaders
            WHERE project_id = $projectId;
            """;
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task CreateIfMissingAsync(
        StoredProjectLeader leader,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(leader);
        if (leader.CurrentEpochId is not null)
        {
            throw new ArgumentException(
                "CreateIfMissingAsync cannot establish a current epoch; use CreateCurrentEpochAsync or SetCurrentEpochAsync.",
                nameof(leader));
        }

        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO project_leaders (project_id, current_epoch_id, created_at, updated_at)
            VALUES ($projectId, $currentEpochId, $createdAt, $updatedAt)
            ON CONFLICT(project_id) DO NOTHING;
            """;
        AddParameters(command, leader);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetCurrentEpochAsync(
        Guid projectId,
        Guid epochId,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var validate = connection.CreateCommand();
        validate.Transaction = transaction;
        validate.CommandText = """
            SELECT COUNT(*)
            FROM leader_session_epochs
            WHERE id = $epochId AND project_id = $projectId AND ended_at IS NULL;
            """;
        validate.Parameters.AddWithValue("$epochId", epochId.ToString());
        validate.Parameters.AddWithValue("$projectId", projectId.ToString());
        if (Convert.ToInt64(await validate.ExecuteScalarAsync(cancellationToken)) != 1)
        {
            throw new InvalidOperationException("The current epoch must be an active epoch owned by the project leader.");
        }

        var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE project_leaders
            SET current_epoch_id = $epochId, updated_at = $updatedAt
            WHERE project_id = $projectId;
            """;
        update.Parameters.AddWithValue("$epochId", epochId.ToString());
        update.Parameters.AddWithValue("$updatedAt", Format(updatedAt));
        update.Parameters.AddWithValue("$projectId", projectId.ToString());
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("The project leader does not exist.");
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task CreateCurrentEpochAsync(
        StoredProjectLeader leader,
        StoredLeaderSessionEpoch epoch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(leader);
        ArgumentNullException.ThrowIfNull(epoch);
        if (leader.ProjectId != epoch.ProjectId || epoch.EndedAt is not null)
        {
            throw new InvalidOperationException("A current epoch must be active and owned by the project leader.");
        }

        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var ensureLeader = connection.CreateCommand();
            ensureLeader.Transaction = transaction;
            ensureLeader.CommandText = """
                INSERT INTO project_leaders (project_id, current_epoch_id, created_at, updated_at)
                VALUES ($projectId, NULL, $createdAt, $updatedAt)
                ON CONFLICT(project_id) DO NOTHING;
                """;
            ensureLeader.Parameters.AddWithValue("$projectId", leader.ProjectId.ToString());
            ensureLeader.Parameters.AddWithValue("$createdAt", Format(leader.CreatedAt));
            ensureLeader.Parameters.AddWithValue("$updatedAt", Format(leader.UpdatedAt));
            await ensureLeader.ExecuteNonQueryAsync(cancellationToken);

            var insertEpoch = connection.CreateCommand();
            insertEpoch.Transaction = transaction;
            insertEpoch.CommandText = """
                INSERT INTO leader_session_epochs (
                    id, project_id, provider_id, provider_account_id, model_id,
                    agent_session_id, external_session_id, working_directory,
                    started_at, last_active_at, ended_at, rollover_reason, handoff_summary)
                VALUES (
                    $id, $projectId, $providerId, $providerAccountId, $modelId,
                    $agentSessionId, $externalSessionId, $workingDirectory,
                    $startedAt, $lastActiveAt, NULL, NULL, NULL);
                """;
            insertEpoch.Parameters.AddWithValue("$id", epoch.Id.ToString());
            insertEpoch.Parameters.AddWithValue("$projectId", epoch.ProjectId.ToString());
            insertEpoch.Parameters.AddWithValue("$providerId", epoch.ProviderId);
            insertEpoch.Parameters.AddWithValue("$providerAccountId", epoch.ProviderAccountId.ToString());
            insertEpoch.Parameters.AddWithValue("$modelId", epoch.ModelId);
            insertEpoch.Parameters.AddWithValue("$agentSessionId", epoch.AgentSessionId.ToString());
            insertEpoch.Parameters.AddWithValue("$externalSessionId", (object?)epoch.ExternalSessionId ?? DBNull.Value);
            insertEpoch.Parameters.AddWithValue("$workingDirectory", (object?)epoch.WorkingDirectory ?? DBNull.Value);
            insertEpoch.Parameters.AddWithValue("$startedAt", Format(epoch.StartedAt));
            insertEpoch.Parameters.AddWithValue("$lastActiveAt", Format(epoch.LastActiveAt));
            await insertEpoch.ExecuteNonQueryAsync(cancellationToken);

            var setCurrent = connection.CreateCommand();
            setCurrent.Transaction = transaction;
            setCurrent.CommandText = """
                UPDATE project_leaders
                SET current_epoch_id = $epochId, updated_at = $updatedAt
                WHERE project_id = $projectId AND current_epoch_id IS NULL;
                """;
            setCurrent.Parameters.AddWithValue("$epochId", epoch.Id.ToString());
            setCurrent.Parameters.AddWithValue("$updatedAt", Format(leader.UpdatedAt));
            setCurrent.Parameters.AddWithValue("$projectId", leader.ProjectId.ToString());
            if (await setCurrent.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("The project leader already has a current epoch.");
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task RolloverAsync(
        Guid projectId,
        Guid oldEpochId,
        StoredLeaderSessionEpoch newEpoch,
        DateTimeOffset endedAt,
        string rolloverReason,
        string handoffSummary,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(newEpoch);
        ArgumentException.ThrowIfNullOrWhiteSpace(rolloverReason);
        ArgumentException.ThrowIfNullOrWhiteSpace(handoffSummary);
        if (newEpoch.ProjectId != projectId || newEpoch.Id == oldEpochId || newEpoch.EndedAt is not null)
        {
            throw new InvalidOperationException("The successor must be a distinct active epoch owned by the same Project Leader.");
        }
        if (newEpoch.StartedAt != endedAt || newEpoch.LastActiveAt != endedAt)
        {
            throw new InvalidOperationException("The successor epoch must start at the rollover timestamp.");
        }

        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var archive = connection.CreateCommand();
            archive.Transaction = transaction;
            archive.CommandText = """
                UPDATE leader_session_epochs
                SET ended_at = $endedAt,
                    rollover_reason = $rolloverReason,
                    handoff_summary = $handoffSummary
                WHERE id = $oldEpochId
                  AND project_id = $projectId
                  AND ended_at IS NULL
                  AND provider_id = $providerId
                  AND provider_account_id = $providerAccountId
                  AND model_id = $modelId
                  AND working_directory IS $workingDirectory
                  AND id = (SELECT current_epoch_id FROM project_leaders WHERE project_id = $projectId);
                """;
            archive.Parameters.AddWithValue("$endedAt", Format(endedAt));
            archive.Parameters.AddWithValue("$rolloverReason", rolloverReason);
            archive.Parameters.AddWithValue("$handoffSummary", handoffSummary);
            archive.Parameters.AddWithValue("$oldEpochId", oldEpochId.ToString());
            archive.Parameters.AddWithValue("$projectId", projectId.ToString());
            archive.Parameters.AddWithValue("$providerId", newEpoch.ProviderId);
            archive.Parameters.AddWithValue("$providerAccountId", newEpoch.ProviderAccountId.ToString());
            archive.Parameters.AddWithValue("$modelId", newEpoch.ModelId);
            archive.Parameters.AddWithValue("$workingDirectory", (object?)newEpoch.WorkingDirectory ?? DBNull.Value);
            if (await archive.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("The old epoch is not the active current epoch for this Project Leader.");
            }

            var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO leader_session_epochs (
                    id, project_id, provider_id, provider_account_id, model_id,
                    agent_session_id, external_session_id, working_directory,
                    started_at, last_active_at, ended_at, rollover_reason, handoff_summary)
                VALUES (
                    $id, $projectId, $providerId, $providerAccountId, $modelId,
                    $agentSessionId, $externalSessionId, $workingDirectory,
                    $startedAt, $lastActiveAt, NULL, NULL, NULL);
                """;
            insert.Parameters.AddWithValue("$id", newEpoch.Id.ToString());
            insert.Parameters.AddWithValue("$projectId", newEpoch.ProjectId.ToString());
            insert.Parameters.AddWithValue("$providerId", newEpoch.ProviderId);
            insert.Parameters.AddWithValue("$providerAccountId", newEpoch.ProviderAccountId.ToString());
            insert.Parameters.AddWithValue("$modelId", newEpoch.ModelId);
            insert.Parameters.AddWithValue("$agentSessionId", newEpoch.AgentSessionId.ToString());
            insert.Parameters.AddWithValue("$externalSessionId", (object?)newEpoch.ExternalSessionId ?? DBNull.Value);
            insert.Parameters.AddWithValue("$workingDirectory", (object?)newEpoch.WorkingDirectory ?? DBNull.Value);
            insert.Parameters.AddWithValue("$startedAt", Format(newEpoch.StartedAt));
            insert.Parameters.AddWithValue("$lastActiveAt", Format(newEpoch.LastActiveAt));
            await insert.ExecuteNonQueryAsync(cancellationToken);

            var switchCurrent = connection.CreateCommand();
            switchCurrent.Transaction = transaction;
            switchCurrent.CommandText = """
                UPDATE project_leaders
                SET current_epoch_id = $newEpochId, updated_at = $endedAt
                WHERE project_id = $projectId AND current_epoch_id = $oldEpochId;
                """;
            switchCurrent.Parameters.AddWithValue("$newEpochId", newEpoch.Id.ToString());
            switchCurrent.Parameters.AddWithValue("$endedAt", Format(endedAt));
            switchCurrent.Parameters.AddWithValue("$projectId", projectId.ToString());
            switchCurrent.Parameters.AddWithValue("$oldEpochId", oldEpochId.ToString());
            if (await switchCurrent.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("The Project Leader current epoch changed during rollover.");
            }

            var queueSynthesis = connection.CreateCommand();
            queueSynthesis.Transaction = transaction;
            queueSynthesis.CommandText = """
                INSERT INTO project_memory_synthesis_jobs (
                    epoch_id, project_id, status, attempt_count, last_attempted_at,
                    completed_at, last_error, created_at, updated_at)
                VALUES ($oldEpochId, $projectId, 'Pending', 0, NULL, NULL, NULL, $endedAt, $endedAt)
                ON CONFLICT(epoch_id) DO NOTHING;
                """;
            queueSynthesis.Parameters.AddWithValue("$oldEpochId", oldEpochId.ToString());
            queueSynthesis.Parameters.AddWithValue("$projectId", projectId.ToString());
            queueSynthesis.Parameters.AddWithValue("$endedAt", Format(endedAt));
            await queueSynthesis.ExecuteNonQueryAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static void AddParameters(SqliteCommand command, StoredProjectLeader leader)
    {
        command.Parameters.AddWithValue("$projectId", leader.ProjectId.ToString());
        command.Parameters.AddWithValue("$currentEpochId", (object?)leader.CurrentEpochId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", Format(leader.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", Format(leader.UpdatedAt));
    }

    private static StoredProjectLeader Read(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1)),
            Parse(reader.GetString(2)),
            Parse(reader.GetString(3)));

    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
