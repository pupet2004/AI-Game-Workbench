using Microsoft.Data.Sqlite;

namespace Workbench.Storage.Migrations;

internal static class Migration013LeaderAuthoritySettings
{
    public const long Version = 13;

    public static async Task ApplyAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        var tableExists = connection.CreateCommand();
        tableExists.Transaction = transaction;
        tableExists.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'project_settings';";
        if (await tableExists.ExecuteScalarAsync(cancellationToken) is null)
        {
            var create = connection.CreateCommand();
            create.Transaction = transaction;
            create.CommandText = """
                CREATE TABLE project_settings (
                    project_id TEXT PRIMARY KEY,
                    leader_session_rotation_policy TEXT NULL,
                    leader_authority_mode TEXT NULL,
                    FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE);
                """;
            await create.ExecuteNonQueryAsync(cancellationToken);
        }
        else if (!await HasLeaderAuthorityColumnAsync(connection, transaction, cancellationToken))
        {
            var alter = connection.CreateCommand();
            alter.Transaction = transaction;
            alter.CommandText = "ALTER TABLE project_settings ADD COLUMN leader_authority_mode TEXT NULL;";
            await alter.ExecuteNonQueryAsync(cancellationToken);
        }

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version = 13;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> HasLeaderAuthorityColumnAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "PRAGMA table_info(project_settings);";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) if (reader.GetString(1) == "leader_authority_mode") return true;
        return false;
    }
}
