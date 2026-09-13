using Workbench.Storage.Database;

namespace Workbench.App.Services;

public sealed class WorkbenchBackupService
{
    private readonly WorkbenchDatabase _database;
    private readonly TimeProvider _timeProvider;

    public WorkbenchBackupService(
        WorkbenchDatabase database,
        TimeProvider? timeProvider = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<string> CreateDatabaseBackupAsync(
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_database.DatabasePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("The Workbench database has no parent directory.");
        }

        Directory.CreateDirectory(directory);
        var databaseName = Path.GetFileNameWithoutExtension(_database.DatabasePath);
        var timestamp = _timeProvider.GetUtcNow().ToString("yyyyMMdd-HHmmssfff");
        var backupPath = Path.Combine(directory, $"{databaseName}.backup-{timestamp}.db");
        var suffix = 1;
        while (File.Exists(backupPath))
        {
            backupPath = Path.Combine(directory, $"{databaseName}.backup-{timestamp}-{suffix++}.db");
        }

        try
        {
            await using var connection = _database.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "VACUUM INTO $backupPath;";
            command.Parameters.AddWithValue("$backupPath", backupPath);
            await command.ExecuteNonQueryAsync(cancellationToken);

            await using var backupConnection = new WorkbenchDatabase(backupPath).CreateConnection();
            await backupConnection.OpenAsync(cancellationToken);
            await using var integrityCommand = backupConnection.CreateCommand();
            integrityCommand.CommandText = "PRAGMA integrity_check;";
            var integrityResult = Convert.ToString(await integrityCommand.ExecuteScalarAsync(cancellationToken));
            if (!string.Equals(integrityResult, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Database backup integrity check failed: {integrityResult}.");
            }

            return backupPath;
        }
        catch
        {
            TryDeleteBackup(backupPath);
            throw;
        }
    }

    private static void TryDeleteBackup(string backupPath)
    {
        try
        {
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }
        }
        catch
        {
            // Preserve the original backup failure; cleanup is best effort.
        }
    }
}
