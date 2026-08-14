using Microsoft.Data.Sqlite;
using System.Globalization;
using Workbench.Storage.Memory;

namespace Workbench.Storage.Migrations;

internal static class Migration011ProjectLibraryEvolution
{
    public const long Version = 11;

    public static async Task ApplyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS project_library_objects (
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL,
                category TEXT NOT NULL,
                topic TEXT NOT NULL,
                category_key TEXT NOT NULL,
                topic_key TEXT NOT NULL,
                current_overview TEXT NULL,
                overview_revision INTEGER NOT NULL CHECK(overview_revision >= 0),
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE,
                UNIQUE(project_id, category_key, topic_key));
            CREATE INDEX IF NOT EXISTS ix_library_objects_project_category
                ON project_library_objects(project_id, category_key, topic_key);

            CREATE TABLE IF NOT EXISTS project_library_timeline_nodes (
                id TEXT PRIMARY KEY,
                object_id TEXT NOT NULL,
                local_date TEXT NOT NULL,
                content TEXT NOT NULL,
                revision INTEGER NOT NULL CHECK(revision >= 1),
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                FOREIGN KEY(object_id) REFERENCES project_library_objects(id) ON DELETE CASCADE);
            CREATE INDEX IF NOT EXISTS ix_library_nodes_object_time
                ON project_library_timeline_nodes(object_id, local_date DESC, created_at DESC, id);

            CREATE TABLE IF NOT EXISTS project_library_material_refs (
                node_id TEXT NOT NULL,
                material_kind TEXT NOT NULL,
                reference TEXT NOT NULL,
                label TEXT NULL,
                created_at TEXT NOT NULL,
                PRIMARY KEY(node_id, material_kind, reference),
                FOREIGN KEY(node_id) REFERENCES project_library_timeline_nodes(id) ON DELETE CASCADE);

            CREATE TABLE IF NOT EXISTS project_library_proposals (
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL,
                source_session_id TEXT NOT NULL,
                status TEXT NOT NULL CHECK(status IN ('Pending','Accepted','Rejected')),
                payload_json TEXT NOT NULL,
                created_at TEXT NOT NULL,
                decided_at TEXT NULL,
                FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE);
            CREATE INDEX IF NOT EXISTS ix_library_proposals_project_status
                ON project_library_proposals(project_id, status, created_at DESC, id);

            """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        await ImportLegacyEntriesAsync(connection, transaction, cancellationToken);

        var versionCommand = connection.CreateCommand();
        versionCommand.Transaction = transaction;
        versionCommand.CommandText = "PRAGMA user_version = 11;";
        await versionCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static async Task ImportLegacyEntriesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var legacyEntries = await ReadLegacyEntriesAsync(connection, transaction, cancellationToken);
        var groups = legacyEntries
            .GroupBy(entry => new
            {
                entry.ProjectId,
                CategoryKey = ProjectLibraryIdentity.NormalizeKey(entry.Category),
                TopicKey = ProjectLibraryIdentity.NormalizeKey(entry.Topic)
            })
            .OrderBy(group => group.Key.ProjectId, StringComparer.Ordinal)
            .ThenBy(group => group.Key.CategoryKey, StringComparer.Ordinal)
            .ThenBy(group => group.Key.TopicKey, StringComparer.Ordinal);

        foreach (var group in groups)
        {
            var ordered = group
                .OrderBy(entry => entry.CreatedAt)
                .ThenBy(entry => entry.Id, StringComparer.Ordinal)
                .ToArray();
            var earliest = ordered[0];
            var latest = ordered[^1];
            var objectId = await InsertObjectAsync(
                connection,
                transaction,
                earliest,
                latest,
                group.Key.CategoryKey,
                group.Key.TopicKey,
                cancellationToken);

            foreach (var entry in ordered)
            {
                await InsertNodeAsync(connection, transaction, objectId, entry, cancellationToken);
                await InsertReferenceAsync(
                    connection,
                    transaction,
                    entry.Id,
                    "AgentSession",
                    entry.SourceSessionId,
                    "Legacy source session",
                    entry.CreatedAt,
                    cancellationToken);
                if (entry.TaskId is not null)
                {
                    await InsertReferenceAsync(
                        connection,
                        transaction,
                        entry.Id,
                        "Task",
                        entry.TaskId,
                        "Legacy task",
                        entry.CreatedAt,
                        cancellationToken);
                }
                if (entry.SourceReference is not null)
                {
                    await InsertReferenceAsync(
                        connection,
                        transaction,
                        entry.Id,
                        "Reference",
                        entry.SourceReference,
                        "Legacy source reference",
                        entry.CreatedAt,
                        cancellationToken);
                }
            }
        }
    }

    private static async Task<IReadOnlyList<LegacyLibraryEntry>> ReadLegacyEntriesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id,project_id,source_session_id,task_id,category,topic,summary,source_reference,created_at
            FROM project_library_entries;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var entries = new List<LegacyLibraryEntry>();
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                DateTimeOffset.Parse(reader.GetString(8), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        }
        return entries;
    }

    private static async Task<string> InsertObjectAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        LegacyLibraryEntry earliest,
        LegacyLibraryEntry latest,
        string categoryKey,
        string topicKey,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO project_library_objects (
                id,project_id,category,topic,category_key,topic_key,current_overview,
                overview_revision,created_at,updated_at)
            VALUES ($id,$projectId,$category,$topic,$categoryKey,$topicKey,NULL,0,$createdAt,$updatedAt);
            """;
        command.Parameters.AddWithValue("$id", earliest.Id);
        command.Parameters.AddWithValue("$projectId", earliest.ProjectId);
        command.Parameters.AddWithValue("$category", ProjectLibraryIdentity.NormalizeDisplay(earliest.Category));
        command.Parameters.AddWithValue("$topic", ProjectLibraryIdentity.NormalizeDisplay(earliest.Topic));
        command.Parameters.AddWithValue("$categoryKey", categoryKey);
        command.Parameters.AddWithValue("$topicKey", topicKey);
        command.Parameters.AddWithValue("$createdAt", Format(earliest.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", Format(latest.CreatedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);

        var lookup = connection.CreateCommand();
        lookup.Transaction = transaction;
        lookup.CommandText = """
            SELECT id FROM project_library_objects
            WHERE project_id=$projectId AND category_key=$categoryKey AND topic_key=$topicKey;
            """;
        lookup.Parameters.AddWithValue("$projectId", earliest.ProjectId);
        lookup.Parameters.AddWithValue("$categoryKey", categoryKey);
        lookup.Parameters.AddWithValue("$topicKey", topicKey);
        return (string)(await lookup.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("The imported Library Object could not be resolved."));
    }

    private static async Task InsertNodeAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string objectId,
        LegacyLibraryEntry entry,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO project_library_timeline_nodes (
                id,object_id,local_date,content,revision,created_at,updated_at)
            VALUES ($id,$objectId,$localDate,$content,1,$createdAt,$createdAt);
            """;
        command.Parameters.AddWithValue("$id", entry.Id);
        command.Parameters.AddWithValue("$objectId", objectId);
        command.Parameters.AddWithValue(
            "$localDate",
            DateOnly.FromDateTime(entry.CreatedAt.UtcDateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$content", entry.Summary);
        command.Parameters.AddWithValue("$createdAt", Format(entry.CreatedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertReferenceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string nodeId,
        string materialKind,
        string reference,
        string label,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO project_library_material_refs (
                node_id,material_kind,reference,label,created_at)
            VALUES ($nodeId,$materialKind,$reference,$label,$createdAt);
            """;
        command.Parameters.AddWithValue("$nodeId", nodeId);
        command.Parameters.AddWithValue("$materialKind", materialKind);
        command.Parameters.AddWithValue("$reference", reference);
        command.Parameters.AddWithValue("$label", label);
        command.Parameters.AddWithValue("$createdAt", Format(createdAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private sealed record LegacyLibraryEntry(
        string Id,
        string ProjectId,
        string SourceSessionId,
        string? TaskId,
        string Category,
        string Topic,
        string Summary,
        string? SourceReference,
        DateTimeOffset CreatedAt);
}
