using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Workbench.Storage.Database;

namespace Workbench.Storage.Memory;

public sealed class ProjectMemorySynthesisRepository(
    WorkbenchDatabase database,
    TimeProvider? timeProvider = null)
{
    private const int MaxErrorUtf8Bytes = 1000;
    private readonly WorkbenchDatabase _database = database ?? throw new ArgumentNullException(nameof(database));
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<ProjectMemorySynthesisJob> QueueSynthesisForEpochAsync(
        Guid epochId,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO project_memory_synthesis_jobs (
                epoch_id, project_id, status, attempt_count, last_attempted_at,
                completed_at, last_error, created_at, updated_at)
            SELECT id, project_id, 'Pending', 0, NULL, NULL, NULL, $now, $now
            FROM leader_session_epochs
            WHERE id = $epochId AND ended_at IS NOT NULL
            ON CONFLICT(epoch_id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$epochId", epochId.ToString());
        command.Parameters.AddWithValue("$now", Format(now));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return await GetAsync(epochId, cancellationToken)
            ?? throw new InvalidOperationException("Only an archived Leader epoch can be queued for memory synthesis.");
    }

    public async Task<ProjectMemorySynthesisJob?> GetAsync(
        Guid epochId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = $"{SelectSql} WHERE epoch_id = $epochId;";
        command.Parameters.AddWithValue("$epochId", epochId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task<ProjectMemorySynthesisJob?> ClaimNextPendingAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var find = connection.CreateCommand();
            find.Transaction = transaction;
            find.CommandText = """
                SELECT epoch_id
                FROM project_memory_synthesis_jobs
                WHERE project_id = $projectId AND status = 'Pending'
                ORDER BY created_at, epoch_id
                LIMIT 1;
                """;
            find.Parameters.AddWithValue("$projectId", projectId.ToString());
            var value = await find.ExecuteScalarAsync(cancellationToken);
            if (value is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            var epochId = Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!);
            var claim = connection.CreateCommand();
            claim.Transaction = transaction;
            claim.CommandText = """
                UPDATE project_memory_synthesis_jobs
                SET status = 'Running', attempt_count = attempt_count + 1,
                    last_attempted_at = $now, last_error = NULL, updated_at = $now
                WHERE epoch_id = $epochId AND project_id = $projectId AND status = 'Pending';
                """;
            claim.Parameters.AddWithValue("$epochId", epochId.ToString());
            claim.Parameters.AddWithValue("$projectId", projectId.ToString());
            claim.Parameters.AddWithValue("$now", Format(now));
            if (await claim.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return null;
            }

            var read = connection.CreateCommand();
            read.Transaction = transaction;
            read.CommandText = $"{SelectSql} WHERE epoch_id = $epochId;";
            read.Parameters.AddWithValue("$epochId", epochId.ToString());
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("The claimed synthesis job disappeared.");
            }

            var job = Read(reader);
            await transaction.CommitAsync(cancellationToken);
            return job;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task ReturnToPendingAsync(
        Guid epochId,
        string error,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        var now = _timeProvider.GetUtcNow();
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE project_memory_synthesis_jobs
            SET status = 'Pending', completed_at = NULL, last_error = $error, updated_at = $now
            WHERE epoch_id = $epochId AND status = 'Running';
            """;
        command.Parameters.AddWithValue("$epochId", epochId.ToString());
        command.Parameters.AddWithValue("$error", TruncateUtf8(error, MaxErrorUtf8Bytes));
        command.Parameters.AddWithValue("$now", Format(now));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Only a running synthesis job can return to Pending.");
        }
    }

    public Task RecoverRunningAsync(CancellationToken cancellationToken = default)
    {
        // Legacy synthesis jobs are retained for audit and never reactivated by startup.
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public async Task<ProjectMemorySynthesisStatus> GetStatusAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                SUM(CASE WHEN status = 'Pending' THEN 1 ELSE 0 END),
                SUM(CASE WHEN status = 'Running' THEN 1 ELSE 0 END)
            FROM project_memory_synthesis_jobs
            WHERE project_id = $projectId;
            """;
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new ProjectMemorySynthesisStatus(
            reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
            reader.IsDBNull(1) ? 0 : reader.GetInt32(1));
    }

    private const string SelectSql = """
        SELECT epoch_id, project_id, status, attempt_count, last_attempted_at,
               completed_at, last_error, created_at, updated_at
        FROM project_memory_synthesis_jobs
        """;

    private static ProjectMemorySynthesisJob Read(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            Enum.Parse<ProjectMemorySynthesisJobStatus>(reader.GetString(2)),
            reader.GetInt32(3),
            reader.IsDBNull(4) ? null : Parse(reader.GetString(4)),
            reader.IsDBNull(5) ? null : Parse(reader.GetString(5)),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            Parse(reader.GetString(7)),
            Parse(reader.GetString(8)));

    private static string TruncateUtf8(string value, int maxBytes)
    {
        if (Encoding.UTF8.GetByteCount(value) <= maxBytes)
        {
            return value;
        }

        var length = value.Length;
        while (length > 0 && Encoding.UTF8.GetByteCount(value.AsSpan(0, length)) > maxBytes)
        {
            length--;
        }

        return value[..length];
    }

    private static string Format(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
