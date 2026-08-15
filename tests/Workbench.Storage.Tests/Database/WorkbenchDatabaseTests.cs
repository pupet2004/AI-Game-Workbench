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
    public async Task Latest_migrations_set_user_version_to_16()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);

        await database.InitializeAsync();

        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";

        Assert.Equal(16L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Migrating_v11_database_to_v16_preserves_continuity_data_and_is_idempotent()
    {
        await using var temporary = new TemporaryDatabase();
        await CreateV11MigrationFixtureAsync(temporary.DatabasePath);

        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await using (var before = database.CreateConnection())
        {
            await before.OpenAsync();
            var version = before.CreateCommand();
            version.CommandText = "PRAGMA user_version;";
            Assert.Equal(11L, await version.ExecuteScalarAsync());
        }

        await database.InitializeAsync();

        var firstSnapshot = await ReadV11ContinuitySnapshotAsync(database);
        Assert.Equal(16L, firstSnapshot.UserVersion);
        Assert.Equal("Migration Project", firstSnapshot.ProjectName);
        Assert.Equal("00000000-0000-0000-0000-000000000102", firstSnapshot.TaskId);
        Assert.Equal("00000000-0000-0000-0000-000000000103", firstSnapshot.CurrentRevisionId);
        Assert.Equal("ReadyToStart", firstSnapshot.TaskStatus);
        Assert.Equal(firstSnapshot.TaskId, firstSnapshot.RevisionTaskId);
        Assert.Equal("Preserve migration data", firstSnapshot.RevisionGoal);
        Assert.Equal(firstSnapshot.TaskId, firstSnapshot.EventTaskId);
        Assert.Equal("{\"kind\":\"fixture\"}", firstSnapshot.EventPayload);
        Assert.Equal("leader message", firstSnapshot.LeaderMessage);
        Assert.Equal("manual", firstSnapshot.WorkbenchSetting);
        Assert.Equal("AfterTenUserMessages", firstSnapshot.RotationPolicy);
        Assert.Null(firstSnapshot.LeaderAuthorityMode);
        Assert.Equal(1L, firstSnapshot.LeaderAuthorityColumnCount);
        Assert.Equal("Daily continuity", firstSnapshot.DailySummary);
        Assert.Equal(1L, firstSnapshot.DailySummarySourceCount);
        Assert.Equal("Legacy library entry", firstSnapshot.LegacyLibrarySummary);
        Assert.Equal("Current library overview", firstSnapshot.LibraryOverview);
        Assert.Equal("Library timeline node", firstSnapshot.LibraryTimelineContent);
        Assert.Equal("docs/design.md", firstSnapshot.LibraryMaterialReference);
        Assert.Equal(0L, firstSnapshot.ForeignKeyViolationCount);

        await new WorkbenchDatabase(temporary.DatabasePath).InitializeAsync();

        var secondSnapshot = await ReadV11ContinuitySnapshotAsync(database);
        Assert.Equal(firstSnapshot, secondSnapshot);
    }

    [Fact]
    public async Task Migration_011_creates_evolution_tables_and_preserves_legacy_library()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);

        await database.InitializeAsync();

        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table'
              AND name IN (
                  'project_library_entries',
                  'project_library_objects',
                  'project_library_timeline_nodes',
                  'project_library_material_refs',
                  'project_library_proposals');
            """;

        Assert.Equal(5L, await command.ExecuteScalarAsync());
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

    private static async Task CreateV11MigrationFixtureAsync(string databasePath)
    {
        var database = new WorkbenchDatabase(databasePath);
        await database.InitializeAsync();

        await using var connection = database.CreateConnection();
        await connection.OpenAsync();

        var disableForeignKeys = connection.CreateCommand();
        disableForeignKeys.CommandText = "PRAGMA foreign_keys = OFF;";
        await disableForeignKeys.ExecuteNonQueryAsync();

        var restoreV11Schema = connection.CreateCommand();
        restoreV11Schema.CommandText = """
            CREATE TABLE tasks_v11 (
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL,
                title TEXT NOT NULL,
                status TEXT NOT NULL CHECK(status IN ('Draft','ReadyToStart','Cancelled')),
                current_revision_id TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                cancelled_at TEXT NULL,
                FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE);
            DROP TABLE tasks;
            ALTER TABLE tasks_v11 RENAME TO tasks;
            CREATE INDEX ix_tasks_project_status ON tasks(project_id,status);
            CREATE INDEX ix_tasks_project_created ON tasks(project_id,created_at);

            CREATE TABLE project_settings_v11 (
                project_id TEXT PRIMARY KEY,
                leader_session_rotation_policy TEXT NULL,
                FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE);
            DROP TABLE project_settings;
            ALTER TABLE project_settings_v11 RENAME TO project_settings;
            PRAGMA user_version = 11;
            """;
        await restoreV11Schema.ExecuteNonQueryAsync();

        var enableForeignKeys = connection.CreateCommand();
        enableForeignKeys.CommandText = "PRAGMA foreign_keys = ON;";
        await enableForeignKeys.ExecuteNonQueryAsync();

        var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO projects (
                id,name,root_path,project_type,git_root,created_at,last_opened_at)
            VALUES (
                '00000000-0000-0000-0000-000000000101','Migration Project','C:/Migration',0,NULL,
                '2026-08-15T01:00:00+00:00','2026-08-15T01:00:00+00:00');

            INSERT INTO project_leaders (project_id,current_epoch_id,created_at,updated_at)
            VALUES (
                '00000000-0000-0000-0000-000000000101',NULL,
                '2026-08-15T01:00:00+00:00','2026-08-15T01:00:00+00:00');
            INSERT INTO leader_session_epochs (
                id,project_id,provider_id,provider_account_id,model_id,agent_session_id,
                external_session_id,working_directory,started_at,last_active_at,ended_at,
                rollover_reason,handoff_summary,boot_context_delivered_at)
            VALUES (
                '00000000-0000-0000-0000-000000000104',
                '00000000-0000-0000-0000-000000000101','codex',
                '00000000-0000-0000-0000-000000000105','model',
                '00000000-0000-0000-0000-000000000106','external','C:/Migration',
                '2026-08-15T01:00:00+00:00','2026-08-15T01:05:00+00:00',NULL,NULL,NULL,
                '2026-08-15T01:01:00+00:00');
            UPDATE project_leaders
            SET current_epoch_id = '00000000-0000-0000-0000-000000000104'
            WHERE project_id = '00000000-0000-0000-0000-000000000101';
            INSERT INTO leader_messages (epoch_id,sequence,role,text,created_at)
            VALUES (
                '00000000-0000-0000-0000-000000000104',1,'user','leader message',
                '2026-08-15T01:02:00+00:00');
            INSERT INTO workbench_settings (key,value) VALUES ('migration_fixture_setting','manual');
            INSERT INTO project_settings (project_id,leader_session_rotation_policy)
            VALUES ('00000000-0000-0000-0000-000000000101','AfterTenUserMessages');

            INSERT INTO tasks (
                id,project_id,title,status,current_revision_id,created_at,updated_at,cancelled_at)
            VALUES (
                '00000000-0000-0000-0000-000000000102',
                '00000000-0000-0000-0000-000000000101','Migration Task','ReadyToStart',
                '00000000-0000-0000-0000-000000000103',
                '2026-08-15T01:10:00+00:00','2026-08-15T01:10:00+00:00',NULL);
            INSERT INTO task_revisions (
                id,task_id,revision_number,goal,scope,out_of_scope,acceptance_json,risk_level,
                recommended_provider_id,recommended_provider_account_id,recommended_model_profile_id,
                recommended_agent_runtime_id,change_reason,approved_by,created_at,previous_revision_id)
            VALUES (
                '00000000-0000-0000-0000-000000000103',
                '00000000-0000-0000-0000-000000000102',1,'Preserve migration data','Storage','None',
                '[]','Low','codex','account','profile','runtime','Initial','Leader',
                '2026-08-15T01:10:00+00:00',NULL);
            INSERT INTO task_events (
                id,project_id,task_id,execution_id,event_type,status,payload_json,created_at)
            VALUES (
                '00000000-0000-0000-0000-000000000107',
                '00000000-0000-0000-0000-000000000101',
                '00000000-0000-0000-0000-000000000102',NULL,'FixtureCreated','ReadyToStart',
                '{"kind":"fixture"}','2026-08-15T01:11:00+00:00');

            INSERT INTO project_daily_summaries (
                project_id,local_date,content,revision,created_at,updated_at)
            VALUES (
                '00000000-0000-0000-0000-000000000101','2026-08-15','Daily continuity',1,
                '2026-08-15T02:00:00+00:00','2026-08-15T02:00:00+00:00');
            INSERT INTO project_daily_summary_sources (
                project_id,local_date,source_type,source_ref)
            VALUES (
                '00000000-0000-0000-0000-000000000101','2026-08-15','Task',
                '00000000-0000-0000-0000-000000000102');

            INSERT INTO project_library_entries (
                id,project_id,source_session_id,task_id,category,topic,summary,source_reference,created_at)
            VALUES (
                '00000000-0000-0000-0000-000000000108',
                '00000000-0000-0000-0000-000000000101','external',
                '00000000-0000-0000-0000-000000000102','Architecture','Persistence',
                'Legacy library entry','docs/legacy.md','2026-08-15T03:00:00+00:00');
            INSERT INTO project_library_objects (
                id,project_id,category,topic,category_key,topic_key,current_overview,
                overview_revision,created_at,updated_at)
            VALUES (
                '00000000-0000-0000-0000-000000000109',
                '00000000-0000-0000-0000-000000000101','Architecture','Kernel',
                'architecture','kernel','Current library overview',2,
                '2026-08-15T03:10:00+00:00','2026-08-15T03:20:00+00:00');
            INSERT INTO project_library_timeline_nodes (
                id,object_id,local_date,content,revision,created_at,updated_at)
            VALUES (
                '00000000-0000-0000-0000-000000000110',
                '00000000-0000-0000-0000-000000000109','2026-08-15','Library timeline node',1,
                '2026-08-15T03:15:00+00:00','2026-08-15T03:15:00+00:00');
            INSERT INTO project_library_material_refs (
                node_id,material_kind,reference,label,created_at)
            VALUES (
                '00000000-0000-0000-0000-000000000110','Document','docs/design.md','Design',
                '2026-08-15T03:16:00+00:00');
            """;
        await insert.ExecuteNonQueryAsync();
    }

    private static async Task<V11ContinuitySnapshot> ReadV11ContinuitySnapshotAsync(WorkbenchDatabase database)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync();

        var query = connection.CreateCommand();
        query.CommandText = """
            SELECT
                (SELECT user_version FROM pragma_user_version),
                (SELECT name FROM projects WHERE id = '00000000-0000-0000-0000-000000000101'),
                (SELECT id FROM tasks WHERE id = '00000000-0000-0000-0000-000000000102'),
                (SELECT current_revision_id FROM tasks WHERE id = '00000000-0000-0000-0000-000000000102'),
                (SELECT status FROM tasks WHERE id = '00000000-0000-0000-0000-000000000102'),
                (SELECT task_id FROM task_revisions WHERE id = '00000000-0000-0000-0000-000000000103'),
                (SELECT goal FROM task_revisions WHERE id = '00000000-0000-0000-0000-000000000103'),
                (SELECT task_id FROM task_events WHERE id = '00000000-0000-0000-0000-000000000107'),
                (SELECT payload_json FROM task_events WHERE id = '00000000-0000-0000-0000-000000000107'),
                (SELECT text FROM leader_messages WHERE epoch_id = '00000000-0000-0000-0000-000000000104'),
                (SELECT value FROM workbench_settings WHERE key = 'migration_fixture_setting'),
                (SELECT leader_session_rotation_policy FROM project_settings
                    WHERE project_id = '00000000-0000-0000-0000-000000000101'),
                (SELECT leader_authority_mode FROM project_settings
                    WHERE project_id = '00000000-0000-0000-0000-000000000101'),
                (SELECT COUNT(*) FROM pragma_table_info('project_settings')
                    WHERE name = 'leader_authority_mode'),
                (SELECT content FROM project_daily_summaries
                    WHERE project_id = '00000000-0000-0000-0000-000000000101' AND local_date = '2026-08-15'),
                (SELECT COUNT(*) FROM project_daily_summary_sources
                    WHERE project_id = '00000000-0000-0000-0000-000000000101' AND local_date = '2026-08-15'),
                (SELECT summary FROM project_library_entries
                    WHERE id = '00000000-0000-0000-0000-000000000108'),
                (SELECT current_overview FROM project_library_objects
                    WHERE id = '00000000-0000-0000-0000-000000000109'),
                (SELECT content FROM project_library_timeline_nodes
                    WHERE id = '00000000-0000-0000-0000-000000000110'),
                (SELECT reference FROM project_library_material_refs
                    WHERE node_id = '00000000-0000-0000-0000-000000000110'),
                (SELECT COUNT(*) FROM pragma_foreign_key_check);
            """;
        await using var reader = await query.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new V11ContinuitySnapshot(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetString(10),
            reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.GetInt64(13),
            reader.GetString(14),
            reader.GetInt64(15),
            reader.GetString(16),
            reader.GetString(17),
            reader.GetString(18),
            reader.GetString(19),
            reader.GetInt64(20));
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

    private sealed record V11ContinuitySnapshot(
        long UserVersion,
        string ProjectName,
        string TaskId,
        string CurrentRevisionId,
        string TaskStatus,
        string RevisionTaskId,
        string RevisionGoal,
        string EventTaskId,
        string EventPayload,
        string LeaderMessage,
        string WorkbenchSetting,
        string RotationPolicy,
        string? LeaderAuthorityMode,
        long LeaderAuthorityColumnCount,
        string DailySummary,
        long DailySummarySourceCount,
        string LegacyLibrarySummary,
        string LibraryOverview,
        string LibraryTimelineContent,
        string LibraryMaterialReference,
        long ForeignKeyViolationCount);
}
