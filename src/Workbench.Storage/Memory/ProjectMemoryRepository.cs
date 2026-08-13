using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Workbench.Storage.Database;

namespace Workbench.Storage.Memory;

public sealed class ProjectMemoryRepository(WorkbenchDatabase database)
{
    private readonly WorkbenchDatabase _database = database;
    public async Task AddAsync(ProjectMemoryItem item, IReadOnlyList<ProjectMemorySource> sources, CancellationToken cancellationToken = default)
    {
        await using var c = _database.CreateConnection(); await c.OpenAsync(cancellationToken); await using var tx = await c.BeginTransactionAsync(cancellationToken);
        var q = c.CreateCommand(); q.Transaction = (SqliteTransaction)tx; q.CommandText = "INSERT INTO project_memory_items VALUES ($id,$project,$layer,$topic,$content,$status,$created,$updated,$certified);"; Add(q,item); await q.ExecuteNonQueryAsync(cancellationToken);
        foreach (var source in sources) { var s=c.CreateCommand(); s.Transaction=(SqliteTransaction)tx; s.CommandText="INSERT INTO project_memory_sources VALUES ($id,$type,$ref);"; s.Parameters.AddWithValue("$id",item.Id.ToString()); s.Parameters.AddWithValue("$type",source.SourceType); s.Parameters.AddWithValue("$ref",source.SourceRef); await s.ExecuteNonQueryAsync(cancellationToken); }
        await tx.CommitAsync(cancellationToken);
    }
    public async Task<ProjectMemoryItem?> GetAsync(Guid id, CancellationToken cancellationToken=default) { await using var c=_database.CreateConnection(); await c.OpenAsync(cancellationToken); var q=c.CreateCommand();q.CommandText="SELECT id,project_id,layer,topic,content,status,created_at,updated_at,certified_at FROM project_memory_items WHERE id=$id;";q.Parameters.AddWithValue("$id",id.ToString());await using var r=await q.ExecuteReaderAsync(cancellationToken);return await r.ReadAsync(cancellationToken)?Read(r):null; }
    public async Task<IReadOnlyList<ProjectMemoryItem>> GetAsync(Guid projectId,string layer,string status,CancellationToken cancellationToken=default){await using var c=_database.CreateConnection();await c.OpenAsync(cancellationToken);var q=c.CreateCommand();q.CommandText="SELECT id,project_id,layer,topic,content,status,created_at,updated_at,certified_at FROM project_memory_items WHERE project_id=$project AND layer=$layer AND status=$status ORDER BY created_at;";q.Parameters.AddWithValue("$project",projectId.ToString());q.Parameters.AddWithValue("$layer",layer);q.Parameters.AddWithValue("$status",status);await using var r=await q.ExecuteReaderAsync(cancellationToken);var items=new List<ProjectMemoryItem>();while(await r.ReadAsync(cancellationToken))items.Add(Read(r));return items;}
    public async Task<IReadOnlyList<ProjectMemorySource>> GetSourcesAsync(Guid id,CancellationToken cancellationToken=default){await using var c=_database.CreateConnection();await c.OpenAsync(cancellationToken);var q=c.CreateCommand();q.CommandText="SELECT source_type,source_ref FROM project_memory_sources WHERE memory_id=$id;";q.Parameters.AddWithValue("$id",id.ToString());await using var r=await q.ExecuteReaderAsync(cancellationToken);var sources=new List<ProjectMemorySource>();while(await r.ReadAsync(cancellationToken))sources.Add(new(r.GetString(0),r.GetString(1)));return sources;}
    public async Task SetStatusAsync(Guid id,string status,DateTimeOffset updated,CancellationToken cancellationToken=default){await using var c=_database.CreateConnection();await c.OpenAsync(cancellationToken);var q=c.CreateCommand();q.CommandText="UPDATE project_memory_items SET status=$status,updated_at=$updated WHERE id=$id;";q.Parameters.AddWithValue("$id",id.ToString());q.Parameters.AddWithValue("$status",status);q.Parameters.AddWithValue("$updated",updated.ToString("O",CultureInfo.InvariantCulture));await q.ExecuteNonQueryAsync(cancellationToken);}
    public async Task<ProjectMemoryItem> CertifyCandidateAsync(Guid candidateId, Guid projectId, string? replacementContent, DateTimeOffset certifiedAt, CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            // First write is the candidate compare-and-set so SQLite write serialization arbitrates the race.
            var cas = connection.CreateCommand();
            cas.Transaction = transaction;
            cas.CommandText = """
                UPDATE project_memory_items
                SET status = 'Superseded', updated_at = $updated
                WHERE id = $id AND project_id = $project
                  AND layer = 'Candidate' AND status = 'Active';
                """;
            cas.Parameters.AddWithValue("$id", candidateId.ToString());
            cas.Parameters.AddWithValue("$project", projectId.ToString());
            cas.Parameters.AddWithValue("$updated", Format(certifiedAt));
            if (await cas.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new MemoryCertificationConflictException("Candidate has already been processed or is not owned by this project.");
            }

            var read = connection.CreateCommand();
            read.Transaction = transaction;
            read.CommandText = "SELECT topic, content FROM project_memory_items WHERE id = $id;";
            read.Parameters.AddWithValue("$id", candidateId.ToString());
            string topic;
            string originalContent;
            await using (var reader = await read.ExecuteReaderAsync(cancellationToken))
            {
                if (!await reader.ReadAsync(cancellationToken))
                {
                    throw new InvalidOperationException("The certified candidate no longer exists.");
                }

                topic = reader.GetString(0);
                originalContent = reader.GetString(1);
            }

            var formal = new ProjectMemoryItem(
                Guid.NewGuid(), projectId, "Formal", topic, replacementContent ?? originalContent,
                "Active", certifiedAt, certifiedAt, certifiedAt);
            await InsertItemAsync(connection, transaction, formal, cancellationToken);
            await CopySourcesAsync(connection, transaction, candidateId, formal.Id, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return formal;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task RejectCandidateAsync(Guid candidateId, Guid projectId, DateTimeOffset rejectedAt, CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var cas = connection.CreateCommand();
            cas.Transaction = transaction;
            cas.CommandText = """
                UPDATE project_memory_items
                SET status = 'Rejected', updated_at = $updated
                WHERE id = $id AND project_id = $project
                  AND layer = 'Candidate' AND status = 'Active';
                """;
            cas.Parameters.AddWithValue("$id", candidateId.ToString());
            cas.Parameters.AddWithValue("$project", projectId.ToString());
            cas.Parameters.AddWithValue("$updated", Format(rejectedAt));
            if (await cas.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new MemoryCertificationConflictException("Candidate has already been processed or is not owned by this project.");
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task InsertItemAsync(SqliteConnection connection, SqliteTransaction transaction, ProjectMemoryItem item, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO project_memory_items VALUES ($id,$project,$layer,$topic,$content,$status,$created,$updated,$certified);";
        Add(command, item);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task CopySourcesAsync(SqliteConnection connection, SqliteTransaction transaction, Guid sourceMemoryId, Guid targetMemoryId, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO project_memory_sources (memory_id, source_type, source_ref)
            SELECT $target, source_type, source_ref
            FROM project_memory_sources
            WHERE memory_id = $source;
            """;
        command.Parameters.AddWithValue("$target", targetMemoryId.ToString());
        command.Parameters.AddWithValue("$source", sourceMemoryId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ApplySynthesisAsync(ProjectMemorySynthesisApplication application, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(application);
        ValidateItems(application.Learned, "Learned");
        ValidateItems(application.Candidates, "Candidate");
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await ValidateRunningArchivedJobAsync(connection, transaction, application, cancellationToken);
            var allSourceIds = application.Learned.Concat(application.Candidates)
                .SelectMany(item => item.SourceMessageIds).Distinct().ToArray();
            await ValidateMessageSourcesAsync(connection, transaction, application.EpochId, allSourceIds, cancellationToken);

            foreach (var item in application.Learned)
            {
                await ApplyLearnedAsync(connection, transaction, application, item, cancellationToken);
            }

            foreach (var item in application.Candidates)
            {
                await ApplyCandidateAsync(connection, transaction, application, item, cancellationToken);
            }

            var complete = connection.CreateCommand();
            complete.Transaction = transaction;
            complete.CommandText = """
                UPDATE project_memory_synthesis_jobs
                SET status = 'Completed', completed_at = $completedAt,
                    last_error = NULL, updated_at = $completedAt
                WHERE epoch_id = $epochId AND project_id = $projectId AND status = 'Running';
                """;
            complete.Parameters.AddWithValue("$completedAt", Format(application.CompletedAt));
            complete.Parameters.AddWithValue("$epochId", application.EpochId.ToString());
            complete.Parameters.AddWithValue("$projectId", application.ProjectId.ToString());
            if (await complete.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("The synthesis job is not Running.");
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task ValidateRunningArchivedJobAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ProjectMemorySynthesisApplication application,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM project_memory_synthesis_jobs j
            JOIN leader_session_epochs e ON e.id = j.epoch_id
            WHERE j.epoch_id = $epochId AND j.project_id = $projectId
              AND j.status = 'Running' AND e.project_id = $projectId AND e.ended_at IS NOT NULL;
            """;
        command.Parameters.AddWithValue("$epochId", application.EpochId.ToString());
        command.Parameters.AddWithValue("$projectId", application.ProjectId.ToString());
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) != 1)
        {
            throw new InvalidOperationException("Synthesis apply requires the matching Running archived-epoch job.");
        }
    }

    private static async Task ValidateMessageSourcesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid epochId,
        IReadOnlyList<long> sourceIds,
        CancellationToken cancellationToken)
    {
        foreach (var sourceId in sourceIds)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT COUNT(*) FROM leader_messages WHERE id = $id AND epoch_id = $epochId;";
            command.Parameters.AddWithValue("$id", sourceId);
            command.Parameters.AddWithValue("$epochId", epochId.ToString());
            if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) != 1)
            {
                throw new InvalidOperationException("A synthesis source message does not belong to the archived epoch.");
            }
        }
    }

    private static async Task ApplyLearnedAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ProjectMemorySynthesisApplication application,
        ProjectMemorySynthesisItem item,
        CancellationToken cancellationToken)
    {
        if (await HasEquivalentFromEpochAsync(connection, transaction, application, "Learned", item, cancellationToken))
        {
            return;
        }

        var active = await GetItemsAsync(connection, transaction, application.ProjectId, "Learned", "Active", cancellationToken);
        var matchingTopic = active.Where(existing => Normalize(existing.Topic) == Normalize(item.Topic)).ToArray();
        if (matchingTopic.Any(existing => Normalize(existing.Content) == Normalize(item.Content)))
        {
            return;
        }

        foreach (var existing in matchingTopic)
        {
            await SetStatusAsync(connection, transaction, existing.Id, "Superseded", application.CompletedAt, cancellationToken);
        }

        await InsertSynthesizedAsync(connection, transaction, application, "Learned", item, cancellationToken);
    }

    private static async Task ApplyCandidateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ProjectMemorySynthesisApplication application,
        ProjectMemorySynthesisItem item,
        CancellationToken cancellationToken)
    {
        if (await HasEquivalentFromEpochAsync(connection, transaction, application, "Candidate", item, cancellationToken))
        {
            return;
        }

        var activeCandidates = await GetItemsAsync(connection, transaction, application.ProjectId, "Candidate", "Active", cancellationToken);
        var activeFormal = await GetItemsAsync(connection, transaction, application.ProjectId, "Formal", "Active", cancellationToken);
        if (activeCandidates.Concat(activeFormal).Any(existing =>
                Normalize(existing.Topic) == Normalize(item.Topic) &&
                Normalize(existing.Content) == Normalize(item.Content)))
        {
            return;
        }

        await InsertSynthesizedAsync(connection, transaction, application, "Candidate", item, cancellationToken);
    }

    private static async Task<bool> HasEquivalentFromEpochAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ProjectMemorySynthesisApplication application,
        string layer,
        ProjectMemorySynthesisItem item,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT i.topic, i.content
            FROM project_memory_items i
            JOIN project_memory_sources s ON s.memory_id = i.id
            WHERE i.project_id = $projectId AND i.layer = $layer
              AND s.source_type = 'LeaderEpoch' AND s.source_ref = $epochId;
            """;
        command.Parameters.AddWithValue("$projectId", application.ProjectId.ToString());
        command.Parameters.AddWithValue("$layer", layer);
        command.Parameters.AddWithValue("$epochId", application.EpochId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (Normalize(reader.GetString(0)) == Normalize(item.Topic) &&
                Normalize(reader.GetString(1)) == Normalize(item.Content))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<IReadOnlyList<ProjectMemoryItem>> GetItemsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid projectId,
        string layer,
        string status,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id,project_id,layer,topic,content,status,created_at,updated_at,certified_at FROM project_memory_items WHERE project_id=$project AND layer=$layer AND status=$status;";
        command.Parameters.AddWithValue("$project", projectId.ToString());
        command.Parameters.AddWithValue("$layer", layer);
        command.Parameters.AddWithValue("$status", status);
        var items = new List<ProjectMemoryItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(Read(reader));
        }
        return items;
    }

    private static async Task InsertSynthesizedAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ProjectMemorySynthesisApplication application,
        string layer,
        ProjectMemorySynthesisItem item,
        CancellationToken cancellationToken)
    {
        var memory = new ProjectMemoryItem(
            Guid.NewGuid(), application.ProjectId, layer, item.Topic.Trim(), item.Content.Trim(),
            "Active", application.CompletedAt, application.CompletedAt, null);
        var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO project_memory_items VALUES ($id,$project,$layer,$topic,$content,$status,$created,$updated,$certified);";
        Add(insert, memory);
        await insert.ExecuteNonQueryAsync(cancellationToken);

        await InsertSourceAsync(connection, transaction, memory.Id, new("LeaderEpoch", application.EpochId.ToString()), cancellationToken);
        foreach (var sourceId in item.SourceMessageIds.Distinct())
        {
            await InsertSourceAsync(connection, transaction, memory.Id, new("LeaderMessage", sourceId.ToString(CultureInfo.InvariantCulture)), cancellationToken);
        }
    }

    private static async Task InsertSourceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid memoryId,
        ProjectMemorySource source,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO project_memory_sources VALUES ($id,$type,$ref);";
        command.Parameters.AddWithValue("$id", memoryId.ToString());
        command.Parameters.AddWithValue("$type", source.SourceType);
        command.Parameters.AddWithValue("$ref", source.SourceRef);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task SetStatusAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid id,
        string status,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE project_memory_items SET status=$status,updated_at=$updated WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$updated", Format(updatedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void ValidateItems(IReadOnlyList<ProjectMemorySynthesisItem> items, string layer)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count > 5)
        {
            throw new ArgumentException($"Synthesis may contain at most 5 {layer} items.", nameof(items));
        }
        foreach (var item in items)
        {
            ArgumentNullException.ThrowIfNull(item);
            ValidateUtf8(item.Topic, 200, "topic");
            ValidateUtf8(item.Content, 8000, "content");
            ArgumentNullException.ThrowIfNull(item.SourceMessageIds);
        }
    }

    private static void ValidateUtf8(string value, int maxBytes, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (Encoding.UTF8.GetByteCount(value) > maxBytes)
        {
            throw new ArgumentException($"{name} exceeds {maxBytes} UTF-8 bytes.", name);
        }
    }

    private static string Normalize(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static void Add(SqliteCommand q,ProjectMemoryItem i){q.Parameters.AddWithValue("$id",i.Id.ToString());q.Parameters.AddWithValue("$project",i.ProjectId.ToString());q.Parameters.AddWithValue("$layer",i.Layer);q.Parameters.AddWithValue("$topic",i.Topic);q.Parameters.AddWithValue("$content",i.Content);q.Parameters.AddWithValue("$status",i.Status);q.Parameters.AddWithValue("$created",i.CreatedAt.ToString("O",CultureInfo.InvariantCulture));q.Parameters.AddWithValue("$updated",i.UpdatedAt.ToString("O",CultureInfo.InvariantCulture));q.Parameters.AddWithValue("$certified",i.CertifiedAt is null?DBNull.Value:i.CertifiedAt.Value.ToString("O",CultureInfo.InvariantCulture));}
    private static ProjectMemoryItem Read(SqliteDataReader r)=>new(Guid.Parse(r.GetString(0)),Guid.Parse(r.GetString(1)),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),DateTimeOffset.Parse(r.GetString(6),CultureInfo.InvariantCulture),DateTimeOffset.Parse(r.GetString(7),CultureInfo.InvariantCulture),r.IsDBNull(8)?null:DateTimeOffset.Parse(r.GetString(8),CultureInfo.InvariantCulture));
}
