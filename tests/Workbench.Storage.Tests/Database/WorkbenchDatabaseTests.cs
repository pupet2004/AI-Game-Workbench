using Microsoft.Data.Sqlite;
using Workbench.Storage.Database;

namespace Workbench.Storage.Tests.Database;

public sealed class WorkbenchDatabaseTests
{
    [Fact]
    public async Task New_database_runs_all_migrations()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);

        await database.InitializeAsync();

        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('projects', 'project_layouts', 'project_leaders', 'leader_session_epochs', 'leader_messages');";

        Assert.Equal(5L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Migration_002_sets_user_version_to_2()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);

        await database.InitializeAsync();

        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";

        Assert.Equal(2L, await command.ExecuteScalarAsync());
    }

    [Theory]
    [InlineData("project_leaders")]
    [InlineData("leader_session_epochs")]
    [InlineData("leader_messages")]
    public async Task Migration_002_creates_leader_tables(string tableName)
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);

        await database.InitializeAsync();

        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", tableName);
        Assert.Equal(1L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Migrating_v1_database_preserves_projects_and_layouts()
    {
        await using var temporary = new TemporaryDatabase();
        await CreateV1DatabaseAsync(temporary.DatabasePath);
        var database = new WorkbenchDatabase(temporary.DatabasePath);

        await database.InitializeAsync();

        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT COUNT(*) FROM projects), (SELECT COUNT(*) FROM project_layouts);";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.Equal(1L, reader.GetInt64(1));
    }

    private static async Task CreateV1DatabaseAsync(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        await using var connection = new SqliteConnection($"Data Source={databasePath};Foreign Keys=True;Pooling=False");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE projects (
                id TEXT PRIMARY KEY, name TEXT NOT NULL, root_path TEXT NOT NULL,
                project_type INTEGER NOT NULL, git_root TEXT NULL,
                created_at TEXT NOT NULL, last_opened_at TEXT NOT NULL);
            CREATE UNIQUE INDEX ix_projects_root_path ON projects(root_path COLLATE NOCASE);
            CREATE TABLE project_layouts (
                project_id TEXT PRIMARY KEY, leader_width REAL NOT NULL,
                work_width REAL NOT NULL, library_width REAL NOT NULL,
                focused_pane INTEGER NOT NULL, updated_at TEXT NOT NULL,
                FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE);
            INSERT INTO projects VALUES ('00000000-0000-0000-0000-000000000001', 'P', 'C:/P', 0, NULL, '2026-01-01T00:00:00+00:00', '2026-01-01T00:00:00+00:00');
            INSERT INTO project_layouts VALUES ('00000000-0000-0000-0000-000000000001', 0.3, 0.4, 0.3, 0, '2026-01-01T00:00:00+00:00');
            PRAGMA user_version = 1;
            """;
        await command.ExecuteNonQueryAsync();
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
