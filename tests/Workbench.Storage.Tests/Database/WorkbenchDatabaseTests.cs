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
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('projects', 'project_layouts', 'project_leaders', 'leader_session_epochs', 'leader_messages', 'workbench_settings', 'project_settings', 'project_activity_events', 'project_memory_items', 'project_memory_sources', 'project_memory_synthesis_jobs', 'tasks', 'task_revisions', 'worker_executions', 'task_events', 'permission_requests', 'task_grants', 'task_clarifications', 'worker_completion_packages');";

        Assert.Equal(19L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Migration_007_sets_user_version_to_7()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);

        await database.InitializeAsync();

        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";

        Assert.Equal(7L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Migration_006_backfills_epochs_with_user_messages_but_leaves_zero_message_epochs_pending()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        await using (var connection = database.CreateConnection())
        {
            await connection.OpenAsync();
            var setup = connection.CreateCommand();
            setup.CommandText = """
                ALTER TABLE leader_session_epochs DROP COLUMN boot_context_delivered_at;
                INSERT INTO projects VALUES ('00000000-0000-0000-0000-000000000021', 'P', 'C:/Boot', 0, NULL, '2026-01-01T00:00:00+00:00', '2026-01-01T00:00:00+00:00');
                INSERT INTO project_leaders VALUES ('00000000-0000-0000-0000-000000000021', NULL, '2026-01-01T00:00:00+00:00', '2026-01-01T00:00:00+00:00');
                INSERT INTO leader_session_epochs VALUES ('00000000-0000-0000-0000-000000000022', '00000000-0000-0000-0000-000000000021', 'codex', '00000000-0000-0000-0000-000000000023', 'model', '00000000-0000-0000-0000-000000000024', 'old', 'C:/Boot', '2026-01-01T00:00:00+00:00', '2026-01-02T00:00:00+00:00', '2026-01-02T00:00:00+00:00', 'Manual', 'handoff');
                INSERT INTO leader_session_epochs VALUES ('00000000-0000-0000-0000-000000000025', '00000000-0000-0000-0000-000000000021', 'codex', '00000000-0000-0000-0000-000000000023', 'model', '00000000-0000-0000-0000-000000000026', 'fresh', 'C:/Boot', '2026-01-02T00:00:00+00:00', '2026-01-02T00:00:00+00:00', NULL, NULL, NULL);
                UPDATE project_leaders SET current_epoch_id = '00000000-0000-0000-0000-000000000025' WHERE project_id = '00000000-0000-0000-0000-000000000021';
                INSERT INTO leader_messages (epoch_id, sequence, role, text, created_at) VALUES ('00000000-0000-0000-0000-000000000022', 1, 'user', 'existing', '2026-01-01T00:00:00+00:00');
                PRAGMA user_version = 5;
                """;
            await setup.ExecuteNonQueryAsync();
        }

        await new WorkbenchDatabase(temporary.DatabasePath).InitializeAsync();

        await using var reopened = database.CreateConnection();
        await reopened.OpenAsync();
        var query = reopened.CreateCommand();
        query.CommandText = "SELECT id, boot_context_delivered_at FROM leader_session_epochs ORDER BY id;";
        await using var reader = await query.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.False(reader.IsDBNull(1));
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.IsDBNull(1));
    }

    [Fact]
    public async Task Migration_005_preserves_v4_memory()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        await using (var connection = database.CreateConnection())
        {
            await connection.OpenAsync();
            var setup = connection.CreateCommand();
            setup.CommandText = """
                INSERT INTO projects VALUES ('00000000-0000-0000-0000-000000000011', 'P', 'C:/Memory', 0, NULL, '2026-01-01T00:00:00+00:00', '2026-01-01T00:00:00+00:00');
                INSERT INTO project_memory_items VALUES ('00000000-0000-0000-0000-000000000012', '00000000-0000-0000-0000-000000000011', 'Formal', 'Rule', 'Keep me.', 'Active', '2026-01-01T00:00:00+00:00', '2026-01-01T00:00:00+00:00', '2026-01-01T00:00:00+00:00');
                DROP TABLE project_memory_synthesis_jobs;
                ALTER TABLE leader_session_epochs DROP COLUMN boot_context_delivered_at;
                PRAGMA user_version = 4;
                """;
            await setup.ExecuteNonQueryAsync();
        }

        await new WorkbenchDatabase(temporary.DatabasePath).InitializeAsync();

        await using var reopened = database.CreateConnection();
        await reopened.OpenAsync();
        var query = reopened.CreateCommand();
        query.CommandText = "SELECT content FROM project_memory_items WHERE topic = 'Rule';";
        Assert.Equal("Keep me.", await query.ExecuteScalarAsync());
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

    [Fact]
    public async Task Migration_003_preserves_v2_leader_data()
    {
        await using var temporary = new TemporaryDatabase();
        await CreateV2DatabaseAsync(temporary.DatabasePath);
        var database = new WorkbenchDatabase(temporary.DatabasePath);

        await database.InitializeAsync();

        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT COUNT(*) FROM projects), (SELECT COUNT(*) FROM project_leaders), (SELECT COUNT(*) FROM leader_session_epochs), (SELECT COUNT(*) FROM leader_messages), (SELECT value FROM workbench_settings WHERE key = 'leader_session_rotation_policy');";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.Equal(1L, reader.GetInt64(1));
        Assert.Equal(1L, reader.GetInt64(2));
        Assert.Equal(1L, reader.GetInt64(3));
        Assert.True(reader.IsDBNull(4));
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

    private static async Task CreateV2DatabaseAsync(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        await using var connection = new SqliteConnection($"Data Source={databasePath};Foreign Keys=True;Pooling=False");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE projects (id TEXT PRIMARY KEY, name TEXT NOT NULL, root_path TEXT NOT NULL, project_type INTEGER NOT NULL, git_root TEXT NULL, created_at TEXT NOT NULL, last_opened_at TEXT NOT NULL);
            CREATE TABLE project_layouts (project_id TEXT PRIMARY KEY, leader_width REAL NOT NULL, work_width REAL NOT NULL, library_width REAL NOT NULL, focused_pane INTEGER NOT NULL, updated_at TEXT NOT NULL, FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE);
            CREATE TABLE project_leaders (project_id TEXT PRIMARY KEY, current_epoch_id TEXT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL, FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE, FOREIGN KEY(current_epoch_id) REFERENCES leader_session_epochs(id) ON DELETE SET NULL);
            CREATE TABLE leader_session_epochs (id TEXT PRIMARY KEY, project_id TEXT NOT NULL, provider_id TEXT NOT NULL, provider_account_id TEXT NOT NULL, model_id TEXT NOT NULL, agent_session_id TEXT NOT NULL, external_session_id TEXT NULL, working_directory TEXT NULL, started_at TEXT NOT NULL, last_active_at TEXT NOT NULL, ended_at TEXT NULL, rollover_reason TEXT NULL, handoff_summary TEXT NULL, FOREIGN KEY(project_id) REFERENCES project_leaders(project_id) ON DELETE CASCADE);
            CREATE TABLE leader_messages (id INTEGER PRIMARY KEY AUTOINCREMENT, epoch_id TEXT NOT NULL, sequence INTEGER NOT NULL, role TEXT NOT NULL CHECK(role IN ('user', 'assistant')), text TEXT NOT NULL, created_at TEXT NOT NULL, FOREIGN KEY(epoch_id) REFERENCES leader_session_epochs(id) ON DELETE CASCADE, UNIQUE(epoch_id, sequence));
            INSERT INTO projects VALUES ('00000000-0000-0000-0000-000000000001', 'P', 'C:/P', 0, NULL, '2026-01-01T00:00:00+00:00', '2026-01-01T00:00:00+00:00');
            INSERT INTO project_leaders VALUES ('00000000-0000-0000-0000-000000000001', NULL, '2026-01-01T00:00:00+00:00', '2026-01-01T00:00:00+00:00');
            INSERT INTO leader_session_epochs VALUES ('00000000-0000-0000-0000-000000000002', '00000000-0000-0000-0000-000000000001', 'provider', '00000000-0000-0000-0000-000000000003', 'model', '00000000-0000-0000-0000-000000000004', 'external', 'C:/P', '2026-01-01T00:00:00+00:00', '2026-01-01T00:00:00+00:00', NULL, NULL, NULL);
            UPDATE project_leaders SET current_epoch_id = '00000000-0000-0000-0000-000000000002' WHERE project_id = '00000000-0000-0000-0000-000000000001';
            INSERT INTO leader_messages (epoch_id, sequence, role, text, created_at) VALUES ('00000000-0000-0000-0000-000000000002', 1, 'user', 'kept', '2026-01-01T00:00:00+00:00');
            PRAGMA user_version = 2;
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
