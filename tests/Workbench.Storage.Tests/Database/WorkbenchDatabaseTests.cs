using Microsoft.Data.Sqlite;
using Workbench.Storage.Database;

namespace Workbench.Storage.Tests.Database;

public sealed class WorkbenchDatabaseTests
{
    [Fact]
    public async Task New_database_runs_migration_001()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);

        await database.InitializeAsync();

        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('projects', 'project_layouts');";

        Assert.Equal(2L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Migration_sets_user_version_to_1()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);

        await database.InitializeAsync();

        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";

        Assert.Equal(1L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Opening_existing_database_does_not_delete_data()
    {
        await using var temporary = new TemporaryDatabase();
        var firstDatabase = new WorkbenchDatabase(temporary.DatabasePath);
        await firstDatabase.InitializeAsync();

        await using (var connection = firstDatabase.CreateConnection())
        {
            await connection.OpenAsync();
            var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO projects (id, name, root_path, project_type, created_at, last_opened_at) VALUES ('a', 'Project', 'C:/Project', 0, '2026-01-01T00:00:00.0000000+00:00', '2026-01-01T00:00:00.0000000+00:00');";
            await insert.ExecuteNonQueryAsync();
        }

        var reopenedDatabase = new WorkbenchDatabase(temporary.DatabasePath);
        await reopenedDatabase.InitializeAsync();

        await using var reopenedConnection = reopenedDatabase.CreateConnection();
        await reopenedConnection.OpenAsync();
        var count = reopenedConnection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM projects;";
        Assert.Equal(1L, await count.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Foreign_keys_are_enabled()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();

        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys;";

        Assert.Equal(1L, await command.ExecuteScalarAsync());
    }
}
