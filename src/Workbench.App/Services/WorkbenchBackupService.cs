using Microsoft.Data.Sqlite;
using Workbench.Storage.Database;

namespace Workbench.App.Services;

public sealed record WorkbenchBackupFile(
    string Path,
    DateTimeOffset LastWriteAt,
    long SizeBytes);

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

    public IReadOnlyList<WorkbenchBackupFile> ListDatabaseBackups()
    {
        var directory = Path.GetDirectoryName(_database.DatabasePath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return [];
        }

        var databaseName = Path.GetFileNameWithoutExtension(_database.DatabasePath);
        return Directory
            .EnumerateFiles(directory, $"{databaseName}.backup-*.db", SearchOption.TopDirectoryOnly)
            .Select(path =>
            {
                var file = new FileInfo(path);
                return new WorkbenchBackupFile(
                    file.FullName,
                    file.LastWriteTimeUtc,
                    file.Length);
            })
            .OrderByDescending(file => file.LastWriteAt)
            .ToArray();
    }

    public async Task ValidateDatabaseBackupAsync(
        string backupPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
        var directory = Path.GetDirectoryName(_database.DatabasePath);
        var fullBackupPath = Path.GetFullPath(backupPath);
        var fullDirectory = Path.GetFullPath(directory ?? string.Empty);
        var directoryPrefix = fullDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? fullDirectory
            : fullDirectory + Path.DirectorySeparatorChar;
        if (!fullBackupPath.StartsWith(directoryPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The backup must be stored beside the Workbench database.", nameof(backupPath));
        }
        if (!File.Exists(fullBackupPath))
        {
            throw new FileNotFoundException("The database backup does not exist.", fullBackupPath);
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullBackupPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        var result = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken));
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Database backup integrity check failed: {result}.");
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
