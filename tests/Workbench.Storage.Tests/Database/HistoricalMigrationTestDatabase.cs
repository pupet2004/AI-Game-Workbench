using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.Data.Sqlite;
using Workbench.Storage.Database;

namespace Workbench.Storage.Tests.Database;

internal static class HistoricalMigrationTestDatabase
{
    private static readonly HashSet<long> ForeignKeysDisabledVersions = [12, 15, 16, 18];

    public static async Task InitializeThroughAsync(
        WorkbenchDatabase database,
        long targetVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        if (targetVersion < 0) throw new ArgumentOutOfRangeException(nameof(targetVersion));

        var directory = Path.GetDirectoryName(database.DatabasePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var currentVersion = await ReadVersionAsync(connection, cancellationToken);
        if (currentVersion > targetVersion)
        {
            throw new InvalidOperationException($"Database version {currentVersion} is newer than requested historical version {targetVersion}.");
        }

        foreach (var migration in DiscoverMigrations().Where(item => item.Version > currentVersion && item.Version <= targetVersion))
        {
            if (ForeignKeysDisabledVersions.Contains(migration.Version))
            {
                await ExecuteAsync(connection, "PRAGMA foreign_keys = OFF;", cancellationToken);
            }

            try
            {
                await ApplyAsync(connection, migration, cancellationToken);
            }
            finally
            {
                if (ForeignKeysDisabledVersions.Contains(migration.Version))
                {
                    await ExecuteAsync(connection, "PRAGMA foreign_keys = ON;", CancellationToken.None);
                }
            }
        }

        var appliedVersion = await ReadVersionAsync(connection, cancellationToken);
        if (appliedVersion != targetVersion)
        {
            throw new InvalidOperationException($"Historical migration chain stopped at version {appliedVersion}, expected {targetVersion}.");
        }
    }

    private static IReadOnlyList<Migration> DiscoverMigrations() =>
        typeof(WorkbenchDatabase).Assembly
            .GetTypes()
            .Where(type => type.Namespace == "Workbench.Storage.Migrations" && type.Name.StartsWith("Migration", StringComparison.Ordinal))
            .Select(type => new Migration(
                Convert.ToInt64(type.GetField("Version", BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue()),
                type.GetMethod("ApplyAsync", BindingFlags.Public | BindingFlags.Static)
                    ?? throw new InvalidOperationException($"{type.FullName} has no ApplyAsync method.")))
            .OrderBy(migration => migration.Version)
            .ToList();

    private static async Task ApplyAsync(SqliteConnection connection, Migration migration, CancellationToken cancellationToken)
    {
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var task = (Task?)migration.ApplyAsync.Invoke(null, [connection, transaction, cancellationToken])
                ?? throw new InvalidOperationException($"Migration {migration.Version} returned no Task.");
            await task;
            await transaction.CommitAsync(cancellationToken);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<long> ReadVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed record Migration(long Version, MethodInfo ApplyAsync);
}
