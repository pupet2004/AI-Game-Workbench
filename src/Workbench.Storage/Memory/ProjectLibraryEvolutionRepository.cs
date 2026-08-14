using System.Globalization;
using Microsoft.Data.Sqlite;
using Workbench.Storage.Database;

namespace Workbench.Storage.Memory;

public sealed class ProjectLibraryEvolutionRepository(WorkbenchDatabase database)
{
    private const int MaxCategoryLength = 100;
    private const int MaxTopicLength = 200;
    private const int MaxOverviewLength = 4000;
    private const int MaxNodeContentLength = 4000;
    private const int MaxMaterialKindLength = 100;
    private const int MaxReferenceLength = 1000;
    private const int MaxLabelLength = 200;

    private readonly WorkbenchDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    public async Task<ProjectLibraryObject> CreateObjectAsync(
        Guid projectId,
        string category,
        string topic,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(projectId, category, topic);
        var displayCategory = ProjectLibraryIdentity.NormalizeDisplay(category);
        var displayTopic = ProjectLibraryIdentity.NormalizeDisplay(topic);
        var categoryKey = ProjectLibraryIdentity.NormalizeKey(category);
        var topicKey = ProjectLibraryIdentity.NormalizeKey(topic);
        var objectId = Guid.NewGuid();

        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            if (!await ProjectExistsAsync(connection, transaction, projectId, cancellationToken))
            {
                throw new InvalidOperationException("The project does not exist.");
            }

            var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT OR IGNORE INTO project_library_objects (
                    id, project_id, category, topic, category_key, topic_key,
                    current_overview, overview_revision, created_at, updated_at)
                VALUES ($id, $projectId, $category, $topic, $categoryKey, $topicKey,
                    NULL, 0, $createdAt, $createdAt);
                """;
            insert.Parameters.AddWithValue("$id", objectId.ToString());
            insert.Parameters.AddWithValue("$projectId", projectId.ToString());
            insert.Parameters.AddWithValue("$category", displayCategory);
            insert.Parameters.AddWithValue("$topic", displayTopic);
            insert.Parameters.AddWithValue("$categoryKey", categoryKey);
            insert.Parameters.AddWithValue("$topicKey", topicKey);
            insert.Parameters.AddWithValue("$createdAt", Format(createdAt));
            await insert.ExecuteNonQueryAsync(cancellationToken);

            var result = await ReadObjectByKeyAsync(
                connection,
                transaction,
                projectId,
                categoryKey,
                topicKey,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result ?? throw new InvalidOperationException("The Library Object could not be created.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<ProjectLibraryObject?> GetObjectAsync(
        Guid projectId,
        Guid objectId,
        CancellationToken cancellationToken = default)
    {
        ValidateGuid(projectId, nameof(projectId));
        ValidateGuid(objectId, nameof(objectId));
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = $"{ObjectSelect} WHERE project_id=$projectId AND id=$objectId;";
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        command.Parameters.AddWithValue("$objectId", objectId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadObject(reader) : null;
    }

    public async Task<ProjectLibraryObject> UpdateOverviewAsync(
        Guid projectId,
        Guid objectId,
        string? overview,
        int expectedRevision,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        ValidateGuid(projectId, nameof(projectId));
        ValidateGuid(objectId, nameof(objectId));
        if (expectedRevision < 0) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        var normalizedOverview = NormalizeOptional(overview, MaxOverviewLength, nameof(overview));

        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE project_library_objects
            SET current_overview=$overview,
                overview_revision=overview_revision+1,
                updated_at=$updatedAt
            WHERE id=$objectId AND project_id=$projectId AND overview_revision=$expectedRevision;
            """;
        command.Parameters.AddWithValue("$overview", (object?)normalizedOverview ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedAt", Format(updatedAt));
        command.Parameters.AddWithValue("$objectId", objectId.ToString());
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        command.Parameters.AddWithValue("$expectedRevision", expectedRevision);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new LibraryRevisionConflictException("The Library Current Overview has changed or is not owned by this project.");
        }

        return await GetObjectAsync(projectId, objectId, cancellationToken)
            ?? throw new InvalidOperationException("The updated Library Object could not be read.");
    }

    public async Task<ProjectLibraryTimelineNode> AddNodeAsync(
        Guid projectId,
        Guid objectId,
        DateOnly localDate,
        string content,
        IReadOnlyList<LibraryMaterialReferenceDraft> references,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        ValidateGuid(projectId, nameof(projectId));
        ValidateGuid(objectId, nameof(objectId));
        var normalizedContent = ValidateRequiredText(content, MaxNodeContentLength, nameof(content));
        var normalizedReferences = NormalizeReferences(references);
        var nodeId = Guid.NewGuid();

        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            if (!await ObjectOwnedByProjectAsync(connection, transaction, projectId, objectId, cancellationToken))
            {
                throw new InvalidOperationException("The Library Object is not owned by this project.");
            }

            var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO project_library_timeline_nodes (
                    id, object_id, local_date, content, revision, created_at, updated_at)
                VALUES ($id, $objectId, $localDate, $content, 1, $createdAt, $createdAt);
                """;
            insert.Parameters.AddWithValue("$id", nodeId.ToString());
            insert.Parameters.AddWithValue("$objectId", objectId.ToString());
            insert.Parameters.AddWithValue("$localDate", Format(localDate));
            insert.Parameters.AddWithValue("$content", normalizedContent);
            insert.Parameters.AddWithValue("$createdAt", Format(createdAt));
            await insert.ExecuteNonQueryAsync(cancellationToken);
            await InsertReferencesAsync(connection, transaction, nodeId, normalizedReferences, createdAt, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new ProjectLibraryTimelineNode(nodeId, objectId, localDate, normalizedContent, 1, createdAt, createdAt);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<ProjectLibraryTimelineNode> UpdateNodeAsync(
        Guid projectId,
        Guid nodeId,
        string content,
        int expectedRevision,
        IReadOnlyList<LibraryMaterialReferenceDraft> references,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        ValidateGuid(projectId, nameof(projectId));
        ValidateGuid(nodeId, nameof(nodeId));
        if (expectedRevision < 1) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        var normalizedContent = ValidateRequiredText(content, MaxNodeContentLength, nameof(content));
        var normalizedReferences = NormalizeReferences(references);

        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE project_library_timeline_nodes
                SET content=$content, revision=revision+1, updated_at=$updatedAt
                WHERE id=$nodeId AND revision=$expectedRevision
                  AND EXISTS (
                      SELECT 1 FROM project_library_objects object
                      WHERE object.id=project_library_timeline_nodes.object_id
                        AND object.project_id=$projectId);
                """;
            update.Parameters.AddWithValue("$content", normalizedContent);
            update.Parameters.AddWithValue("$updatedAt", Format(updatedAt));
            update.Parameters.AddWithValue("$nodeId", nodeId.ToString());
            update.Parameters.AddWithValue("$expectedRevision", expectedRevision);
            update.Parameters.AddWithValue("$projectId", projectId.ToString());
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new LibraryRevisionConflictException("The Library Timeline Node has changed or is not owned by this project.");
            }

            var deleteReferences = connection.CreateCommand();
            deleteReferences.Transaction = transaction;
            deleteReferences.CommandText = "DELETE FROM project_library_material_refs WHERE node_id=$nodeId;";
            deleteReferences.Parameters.AddWithValue("$nodeId", nodeId.ToString());
            await deleteReferences.ExecuteNonQueryAsync(cancellationToken);
            await InsertReferencesAsync(connection, transaction, nodeId, normalizedReferences, updatedAt, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }

        return await GetNodeAsync(projectId, nodeId, cancellationToken)
            ?? throw new InvalidOperationException("The updated Library Timeline Node could not be read.");
    }

    public async Task<ProjectLibraryTimelineNode?> GetNodeAsync(
        Guid projectId,
        Guid nodeId,
        CancellationToken cancellationToken = default)
    {
        ValidateGuid(projectId, nameof(projectId));
        ValidateGuid(nodeId, nameof(nodeId));
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = $"{NodeSelect} JOIN project_library_objects object ON object.id=node.object_id WHERE object.project_id=$projectId AND node.id=$nodeId;";
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        command.Parameters.AddWithValue("$nodeId", nodeId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadNode(reader) : null;
    }

    public async Task<IReadOnlyList<ProjectLibraryTimelineNode>> GetTimelineAsync(
        Guid projectId,
        Guid objectId,
        CancellationToken cancellationToken = default)
    {
        ValidateGuid(projectId, nameof(projectId));
        ValidateGuid(objectId, nameof(objectId));
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = $"{NodeSelect} JOIN project_library_objects object ON object.id=node.object_id WHERE object.project_id=$projectId AND object.id=$objectId ORDER BY node.local_date DESC,node.created_at DESC,node.id;";
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        command.Parameters.AddWithValue("$objectId", objectId.ToString());
        return await ReadNodesAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<LibraryMaterialReference>> GetMaterialReferencesAsync(
        Guid projectId,
        Guid nodeId,
        CancellationToken cancellationToken = default)
    {
        ValidateGuid(projectId, nameof(projectId));
        ValidateGuid(nodeId, nameof(nodeId));
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT reference.node_id,reference.material_kind,reference.reference,reference.label,reference.created_at
            FROM project_library_material_refs reference
            JOIN project_library_timeline_nodes node ON node.id=reference.node_id
            JOIN project_library_objects object ON object.id=node.object_id
            WHERE object.project_id=$projectId AND node.id=$nodeId
            ORDER BY reference.material_kind,reference.reference;
            """;
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        command.Parameters.AddWithValue("$nodeId", nodeId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var references = new List<LibraryMaterialReference>();
        while (await reader.ReadAsync(cancellationToken))
        {
            references.Add(new(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                ParseTimestamp(reader.GetString(4))));
        }
        return references;
    }

    public async Task<IReadOnlyList<ProjectLibraryObject>> ListObjectsByCategoryAsync(
        Guid projectId,
        string category,
        CancellationToken cancellationToken = default)
    {
        ValidateGuid(projectId, nameof(projectId));
        var categoryKey = ProjectLibraryIdentity.NormalizeKey(
            ValidateRequiredText(category, MaxCategoryLength, nameof(category)));
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = $"{ObjectSelect} WHERE project_id=$projectId AND category_key=$categoryKey ORDER BY topic_key,id;";
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        command.Parameters.AddWithValue("$categoryKey", categoryKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var objects = new List<ProjectLibraryObject>();
        while (await reader.ReadAsync(cancellationToken)) objects.Add(ReadObject(reader));
        return objects;
    }

    public async Task<IReadOnlyList<ProjectLibraryObject>> ListObjectsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        ValidateGuid(projectId, nameof(projectId));
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = $"{ObjectSelect} WHERE project_id=$projectId ORDER BY category_key,topic_key,id;";
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var objects = new List<ProjectLibraryObject>();
        while (await reader.ReadAsync(cancellationToken)) objects.Add(ReadObject(reader));
        return objects;
    }

    public async Task<IReadOnlyList<ProjectLibraryOverviewMetadata>> ListOverviewMetadataAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        ValidateGuid(projectId, nameof(projectId));
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,project_id,category,topic,overview_revision,updated_at,
                   length(CAST(current_overview AS BLOB))
            FROM project_library_objects
            WHERE project_id=$projectId AND current_overview IS NOT NULL
              AND length(CAST(current_overview AS BLOB)) > 0
            ORDER BY category_key,topic_key,id;
            """;
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<ProjectLibraryOverviewMetadata>();
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                ParseTimestamp(reader.GetString(5)),
                reader.GetInt32(6)));
        }
        return items;
    }

    public async Task<IReadOnlyList<ProjectLibraryTimelineNodeMetadata>> ListTimelineMetadataAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        ValidateGuid(projectId, nameof(projectId));
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT node.id,node.object_id,object.project_id,object.category,object.topic,
                   node.local_date,node.revision,node.created_at,
                   length(CAST(node.content AS BLOB))
            FROM project_library_timeline_nodes node
            JOIN project_library_objects object ON object.id=node.object_id
            WHERE object.project_id=$projectId
            ORDER BY node.local_date DESC,node.created_at DESC,node.id;
            """;
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<ProjectLibraryTimelineNodeMetadata>();
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                Guid.Parse(reader.GetString(2)),
                reader.GetString(3),
                reader.GetString(4),
                DateOnly.ParseExact(reader.GetString(5), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetInt32(6),
                ParseTimestamp(reader.GetString(7)),
                reader.GetInt32(8)));
        }
        return items;
    }

    public async Task<IReadOnlyList<ProjectLibraryTimelineNode>> BrowseNodesByDateAsync(
        Guid projectId,
        DateOnly? from = null,
        DateOnly? through = null,
        CancellationToken cancellationToken = default)
    {
        ValidateGuid(projectId, nameof(projectId));
        if (from is not null && through is not null && from > through)
        {
            throw new ArgumentException("The start date must not be after the end date.", nameof(from));
        }
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = $"{NodeSelect} JOIN project_library_objects object ON object.id=node.object_id WHERE object.project_id=$projectId AND ($from IS NULL OR node.local_date >= $from) AND ($through IS NULL OR node.local_date <= $through) ORDER BY node.local_date DESC,node.created_at DESC,node.id;";
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        command.Parameters.AddWithValue("$from", from is null ? DBNull.Value : Format(from.Value));
        command.Parameters.AddWithValue("$through", through is null ? DBNull.Value : Format(through.Value));
        return await ReadNodesAsync(command, cancellationToken);
    }

    internal async Task ApplyProposalAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ProjectLibraryProposalDraft draft,
        DateTimeOffset appliedAt,
        CancellationToken cancellationToken)
    {
        draft = NormalizeProposalDraft(draft);
        if (!await ProjectExistsAsync(connection, transaction, draft.ProjectId, cancellationToken))
        {
            throw new InvalidOperationException("The proposal project does not exist.");
        }

        var categoryKey = ProjectLibraryIdentity.NormalizeKey(draft.Category);
        var topicKey = ProjectLibraryIdentity.NormalizeKey(draft.Topic);
        var libraryObject = draft.TargetObjectId is null
            ? await ReadObjectByKeyAsync(connection, transaction, draft.ProjectId, categoryKey, topicKey, cancellationToken)
            : await ReadObjectByIdAsync(connection, transaction, draft.ProjectId, draft.TargetObjectId.Value, cancellationToken);

        if (draft.Action == LibraryProposalAction.CreateNode && libraryObject is null && draft.TargetObjectId is null)
        {
            var objectId = Guid.NewGuid();
            var insertObject = connection.CreateCommand();
            insertObject.Transaction = transaction;
            insertObject.CommandText = """
                INSERT INTO project_library_objects (
                    id,project_id,category,topic,category_key,topic_key,current_overview,
                    overview_revision,created_at,updated_at)
                VALUES ($id,$projectId,$category,$topic,$categoryKey,$topicKey,NULL,0,$createdAt,$createdAt);
                """;
            insertObject.Parameters.AddWithValue("$id", objectId.ToString());
            insertObject.Parameters.AddWithValue("$projectId", draft.ProjectId.ToString());
            insertObject.Parameters.AddWithValue("$category", draft.Category);
            insertObject.Parameters.AddWithValue("$topic", draft.Topic);
            insertObject.Parameters.AddWithValue("$categoryKey", categoryKey);
            insertObject.Parameters.AddWithValue("$topicKey", topicKey);
            insertObject.Parameters.AddWithValue("$createdAt", Format(appliedAt));
            await insertObject.ExecuteNonQueryAsync(cancellationToken);
            libraryObject = await ReadObjectByIdAsync(connection, transaction, draft.ProjectId, objectId, cancellationToken);
        }

        if (libraryObject is null)
        {
            throw new InvalidOperationException("The target Library Object is not owned by this project.");
        }
        if (!string.Equals(libraryObject.CategoryKey, categoryKey, StringComparison.Ordinal) ||
            !string.Equals(libraryObject.TopicKey, topicKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The proposal identity does not match the target Library Object.");
        }

        if (draft.CurrentOverview is not null && libraryObject.OverviewRevision != draft.ExpectedOverviewRevision)
        {
            throw new LibraryRevisionConflictException("The Library Current Overview has changed.");
        }

        if (draft.Action == LibraryProposalAction.CreateNode)
        {
            var nodeId = Guid.NewGuid();
            var insertNode = connection.CreateCommand();
            insertNode.Transaction = transaction;
            insertNode.CommandText = """
                INSERT INTO project_library_timeline_nodes (
                    id,object_id,local_date,content,revision,created_at,updated_at)
                VALUES ($id,$objectId,$localDate,$content,1,$createdAt,$createdAt);
                """;
            insertNode.Parameters.AddWithValue("$id", nodeId.ToString());
            insertNode.Parameters.AddWithValue("$objectId", libraryObject.Id.ToString());
            insertNode.Parameters.AddWithValue("$localDate", Format(draft.LocalDate));
            insertNode.Parameters.AddWithValue("$content", draft.NodeContent);
            insertNode.Parameters.AddWithValue("$createdAt", Format(appliedAt));
            await insertNode.ExecuteNonQueryAsync(cancellationToken);
            await InsertReferencesAsync(connection, transaction, nodeId, draft.Materials, appliedAt, cancellationToken);
        }
        else
        {
            var node = await ReadNodeByIdAsync(connection, transaction, draft.ProjectId, draft.TargetNodeId!.Value, cancellationToken)
                ?? throw new InvalidOperationException("The target Library Timeline Node is not owned by this project.");
            if (node.ObjectId != libraryObject.Id)
            {
                throw new InvalidOperationException("The target Library Timeline Node does not belong to the target Object.");
            }
            if (node.LocalDate != draft.LocalDate)
            {
                throw new InvalidOperationException("The proposal date does not match the target Library Timeline Node.");
            }
            if (node.Revision != draft.ExpectedNodeRevision)
            {
                throw new LibraryRevisionConflictException("The Library Timeline Node has changed.");
            }

            var updateNode = connection.CreateCommand();
            updateNode.Transaction = transaction;
            updateNode.CommandText = """
                UPDATE project_library_timeline_nodes
                SET content=$content,revision=revision+1,updated_at=$updatedAt
                WHERE id=$nodeId AND revision=$expectedRevision;
                """;
            updateNode.Parameters.AddWithValue("$content", draft.NodeContent);
            updateNode.Parameters.AddWithValue("$updatedAt", Format(appliedAt));
            updateNode.Parameters.AddWithValue("$nodeId", node.Id.ToString());
            updateNode.Parameters.AddWithValue("$expectedRevision", draft.ExpectedNodeRevision!.Value);
            if (await updateNode.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new LibraryRevisionConflictException("The Library Timeline Node has changed.");
            }

            var deleteReferences = connection.CreateCommand();
            deleteReferences.Transaction = transaction;
            deleteReferences.CommandText = "DELETE FROM project_library_material_refs WHERE node_id=$nodeId;";
            deleteReferences.Parameters.AddWithValue("$nodeId", node.Id.ToString());
            await deleteReferences.ExecuteNonQueryAsync(cancellationToken);
            await InsertReferencesAsync(connection, transaction, node.Id, draft.Materials, appliedAt, cancellationToken);
        }

        if (draft.CurrentOverview is not null)
        {
            var updateOverview = connection.CreateCommand();
            updateOverview.Transaction = transaction;
            updateOverview.CommandText = """
                UPDATE project_library_objects
                SET current_overview=$overview,overview_revision=overview_revision+1,updated_at=$updatedAt
                WHERE id=$objectId AND project_id=$projectId AND overview_revision=$expectedRevision;
                """;
            updateOverview.Parameters.AddWithValue("$overview", draft.CurrentOverview);
            updateOverview.Parameters.AddWithValue("$updatedAt", Format(appliedAt));
            updateOverview.Parameters.AddWithValue("$objectId", libraryObject.Id.ToString());
            updateOverview.Parameters.AddWithValue("$projectId", draft.ProjectId.ToString());
            updateOverview.Parameters.AddWithValue("$expectedRevision", draft.ExpectedOverviewRevision!.Value);
            if (await updateOverview.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new LibraryRevisionConflictException("The Library Current Overview has changed.");
            }
        }
    }

    internal static ProjectLibraryProposalDraft NormalizeProposalDraft(ProjectLibraryProposalDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ValidateGuid(draft.ProposalId, nameof(draft.ProposalId));
        ValidateGuid(draft.ProjectId, nameof(draft.ProjectId));
        ValidateGuid(draft.SourceSessionId, nameof(draft.SourceSessionId));
        if (!Enum.IsDefined(draft.Action)) throw new ArgumentOutOfRangeException(nameof(draft.Action));
        if (draft.TargetObjectId == Guid.Empty || draft.TargetNodeId == Guid.Empty) throw new ArgumentException("Target identity is invalid.", nameof(draft));
        var category = ProjectLibraryIdentity.NormalizeDisplay(ValidateRequiredText(draft.Category, MaxCategoryLength, nameof(draft.Category)));
        var topic = ProjectLibraryIdentity.NormalizeDisplay(ValidateRequiredText(draft.Topic, MaxTopicLength, nameof(draft.Topic)));
        var content = ValidateRequiredText(draft.NodeContent, MaxNodeContentLength, nameof(draft.NodeContent));
        var overview = NormalizeOptional(draft.CurrentOverview, MaxOverviewLength, nameof(draft.CurrentOverview));
        var materials = NormalizeReferences(draft.Materials);
        if (draft.LocalDate == DateOnly.MinValue) throw new ArgumentException("Local date is required.", nameof(draft.LocalDate));

        if (draft.Action == LibraryProposalAction.CreateNode)
        {
            if (draft.TargetNodeId is not null || draft.ExpectedNodeRevision is not null)
                throw new ArgumentException("CreateNode cannot target an existing Timeline Node.", nameof(draft));
        }
        else if (draft.TargetObjectId is null || draft.TargetNodeId is null || draft.ExpectedNodeRevision is null or < 1)
        {
            throw new ArgumentException("UpdateNode requires target identities and a positive expected revision.", nameof(draft));
        }

        if (overview is not null && draft.ExpectedOverviewRevision is null or < 0)
            throw new ArgumentException("An Overview replacement requires its expected revision.", nameof(draft));

        return draft with
        {
            Category = category,
            Topic = topic,
            NodeContent = content,
            CurrentOverview = overview,
            Materials = materials
        };
    }

    internal static LibraryProposalEdit NormalizeProposalEdit(LibraryProposalEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        return edit with
        {
            NodeContent = ValidateRequiredText(edit.NodeContent, MaxNodeContentLength, nameof(edit.NodeContent)),
            CurrentOverview = NormalizeOptional(edit.CurrentOverview, MaxOverviewLength, nameof(edit.CurrentOverview)),
            Materials = NormalizeReferences(edit.Materials)
        };
    }

    private const string ObjectSelect = "SELECT id,project_id,category,topic,category_key,topic_key,current_overview,overview_revision,created_at,updated_at FROM project_library_objects";
    private const string NodeSelect = "SELECT node.id,node.object_id,node.local_date,node.content,node.revision,node.created_at,node.updated_at FROM project_library_timeline_nodes node";

    private static async Task<ProjectLibraryObject?> ReadObjectByKeyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid projectId,
        string categoryKey,
        string topicKey,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"{ObjectSelect} WHERE project_id=$projectId AND category_key=$categoryKey AND topic_key=$topicKey;";
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        command.Parameters.AddWithValue("$categoryKey", categoryKey);
        command.Parameters.AddWithValue("$topicKey", topicKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadObject(reader) : null;
    }

    private static async Task<ProjectLibraryObject?> ReadObjectByIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid projectId,
        Guid objectId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"{ObjectSelect} WHERE project_id=$projectId AND id=$objectId;";
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        command.Parameters.AddWithValue("$objectId", objectId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadObject(reader) : null;
    }

    private static async Task<ProjectLibraryTimelineNode?> ReadNodeByIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid projectId,
        Guid nodeId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"{NodeSelect} JOIN project_library_objects object ON object.id=node.object_id WHERE object.project_id=$projectId AND node.id=$nodeId;";
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        command.Parameters.AddWithValue("$nodeId", nodeId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadNode(reader) : null;
    }

    private static async Task<bool> ProjectExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM projects WHERE id=$projectId;";
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static async Task<bool> ObjectOwnedByProjectAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid projectId,
        Guid objectId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM project_library_objects WHERE id=$objectId AND project_id=$projectId;";
        command.Parameters.AddWithValue("$objectId", objectId.ToString());
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static async Task InsertReferencesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid nodeId,
        IReadOnlyList<LibraryMaterialReferenceDraft> references,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        foreach (var reference in references)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO project_library_material_refs (node_id,material_kind,reference,label,created_at) VALUES ($nodeId,$kind,$reference,$label,$createdAt);";
            command.Parameters.AddWithValue("$nodeId", nodeId.ToString());
            command.Parameters.AddWithValue("$kind", reference.MaterialKind);
            command.Parameters.AddWithValue("$reference", reference.Reference);
            command.Parameters.AddWithValue("$label", (object?)reference.Label ?? DBNull.Value);
            command.Parameters.AddWithValue("$createdAt", Format(createdAt));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<IReadOnlyList<ProjectLibraryTimelineNode>> ReadNodesAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var nodes = new List<ProjectLibraryTimelineNode>();
        while (await reader.ReadAsync(cancellationToken)) nodes.Add(ReadNode(reader));
        return nodes;
    }

    private static ProjectLibraryObject ReadObject(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        Guid.Parse(reader.GetString(1)),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.GetInt32(7),
        ParseTimestamp(reader.GetString(8)),
        ParseTimestamp(reader.GetString(9)));

    private static ProjectLibraryTimelineNode ReadNode(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        Guid.Parse(reader.GetString(1)),
        DateOnly.ParseExact(reader.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture),
        reader.GetString(3),
        reader.GetInt32(4),
        ParseTimestamp(reader.GetString(5)),
        ParseTimestamp(reader.GetString(6)));

    private static IReadOnlyList<LibraryMaterialReferenceDraft> NormalizeReferences(
        IReadOnlyList<LibraryMaterialReferenceDraft> references)
    {
        ArgumentNullException.ThrowIfNull(references);
        var normalized = new List<LibraryMaterialReferenceDraft>();
        var identities = new HashSet<(string Kind, string Reference)>();
        foreach (var reference in references)
        {
            ArgumentNullException.ThrowIfNull(reference);
            var kind = ValidateRequiredText(reference.MaterialKind, MaxMaterialKindLength, nameof(reference.MaterialKind));
            var locator = ValidateRequiredText(reference.Reference, MaxReferenceLength, nameof(reference.Reference));
            var label = NormalizeOptional(reference.Label, MaxLabelLength, nameof(reference.Label));
            if (identities.Add((kind, locator))) normalized.Add(new(kind, locator, label));
        }
        return normalized;
    }

    private static void ValidateIdentity(Guid projectId, string category, string topic)
    {
        ValidateGuid(projectId, nameof(projectId));
        ValidateRequiredText(category, MaxCategoryLength, nameof(category));
        ValidateRequiredText(topic, MaxTopicLength, nameof(topic));
    }

    private static void ValidateGuid(Guid value, string name)
    {
        if (value == Guid.Empty) throw new ArgumentException("Identity is required.", name);
    }

    private static string ValidateRequiredText(string value, int maxLength, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        var normalized = value.Trim();
        if (normalized.Length > maxLength) throw new ArgumentException("Text is too long.", name);
        return normalized;
    }

    private static string? NormalizeOptional(string? value, int maxLength, string name)
    {
        if (value is null) return null;
        var normalized = value.Trim();
        if (normalized.Length == 0 || normalized.Length > maxLength) throw new ArgumentException("Text is invalid.", name);
        return normalized;
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static string Format(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
