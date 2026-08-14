using System.Globalization;
using Microsoft.Data.Sqlite;
using Workbench.Storage.Database;

namespace Workbench.Storage.Memory;

public sealed class DailySummaryRepository(WorkbenchDatabase database)
{
    private readonly WorkbenchDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    public async Task<DailySummaryDocument?> GetAsync(Guid projectId, DateOnly localDate, CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT project_id,local_date,content,revision,created_at,updated_at FROM project_daily_summaries WHERE project_id=$projectId AND local_date=$localDate;";
        AddIdentity(command, projectId, localDate);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var document = ReadDocument(reader, []);
        return document with { Sources = await GetSourcesAsync(connection, null, projectId, localDate, cancellationToken) };
    }

    public async Task<IReadOnlyList<DailySummaryDocument>> ListAsync(Guid projectId, DateOnly? from = null, DateOnly? through = null, CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT project_id,local_date,content,revision,created_at,updated_at FROM project_daily_summaries WHERE project_id=$projectId AND ($from IS NULL OR local_date >= $from) AND ($through IS NULL OR local_date <= $through) ORDER BY local_date DESC, updated_at DESC;";
        AddRange(command, projectId, from, through);
        var documents = new List<DailySummaryDocument>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken)) documents.Add(ReadDocument(reader, []));
        }
        for (var index = 0; index < documents.Count; index++)
        {
            var document = documents[index];
            documents[index] = document with { Sources = await GetSourcesAsync(connection, null, document.ProjectId, document.LocalDate, cancellationToken) };
        }
        return documents;
    }

    public async Task<IReadOnlyList<DailySummaryMetadata>> ListMetadataAsync(Guid projectId, DateOnly? from = null, DateOnly? through = null, CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT s.project_id,s.local_date,s.revision,s.created_at,s.updated_at,COUNT(r.source_ref) FROM project_daily_summaries s LEFT JOIN project_daily_summary_sources r ON r.project_id=s.project_id AND r.local_date=s.local_date WHERE s.project_id=$projectId AND ($from IS NULL OR s.local_date >= $from) AND ($through IS NULL OR s.local_date <= $through) GROUP BY s.project_id,s.local_date,s.revision,s.created_at,s.updated_at ORDER BY s.local_date DESC,s.updated_at DESC;";
        AddRange(command, projectId, from, through);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var metadata = new List<DailySummaryMetadata>();
        while (await reader.ReadAsync(cancellationToken)) metadata.Add(new(Guid.Parse(reader.GetString(0)), DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture), reader.GetInt32(2), Parse(reader.GetString(3)), Parse(reader.GetString(4)), reader.GetInt32(5)));
        return metadata;
    }

    public async Task<DailySummaryDocument> SaveAsync(DailySummaryWrite write, DateTimeOffset savedAt, CancellationToken cancellationToken = default)
    {
        Validate(write);
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var revision = write.ExpectedRevision is null ? 1 : write.ExpectedRevision.Value + 1;
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            if (write.ExpectedRevision is null)
            {
                command.CommandText = "INSERT INTO project_daily_summaries (project_id,local_date,content,revision,created_at,updated_at) VALUES ($projectId,$localDate,$content,1,$savedAt,$savedAt);";
            }
            else
            {
                command.CommandText = "UPDATE project_daily_summaries SET content=$content,revision=$revision,updated_at=$savedAt WHERE project_id=$projectId AND local_date=$localDate AND revision=$expectedRevision;";
                command.Parameters.AddWithValue("$revision", revision);
                command.Parameters.AddWithValue("$expectedRevision", write.ExpectedRevision.Value);
            }
            AddIdentity(command, write.ProjectId, write.LocalDate);
            command.Parameters.AddWithValue("$content", write.Content);
            command.Parameters.AddWithValue("$savedAt", Format(savedAt));
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw new MemoryRevisionConflictException("The Daily Summary has changed or already exists.");

            var removeSources = connection.CreateCommand();
            removeSources.Transaction = transaction;
            removeSources.CommandText = "DELETE FROM project_daily_summary_sources WHERE project_id=$projectId AND local_date=$localDate;";
            AddIdentity(removeSources, write.ProjectId, write.LocalDate);
            await removeSources.ExecuteNonQueryAsync(cancellationToken);
            foreach (var source in write.Sources.Distinct())
            {
                var sourceCommand = connection.CreateCommand();
                sourceCommand.Transaction = transaction;
                sourceCommand.CommandText = "INSERT INTO project_daily_summary_sources (project_id,local_date,source_type,source_ref) VALUES ($projectId,$localDate,$sourceType,$sourceRef);";
                AddIdentity(sourceCommand, write.ProjectId, write.LocalDate);
                sourceCommand.Parameters.AddWithValue("$sourceType", source.SourceType);
                sourceCommand.Parameters.AddWithValue("$sourceRef", source.SourceRef);
                await sourceCommand.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return new DailySummaryDocument(write.ProjectId, write.LocalDate, write.Content, revision, write.ExpectedRevision is null ? savedAt : (await GetCreatedAtAsync(connection, write.ProjectId, write.LocalDate, cancellationToken)), savedAt, write.Sources.Distinct().ToArray());
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<DateTimeOffset> GetCreatedAtAsync(SqliteConnection connection, Guid projectId, DateOnly localDate, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = "SELECT created_at FROM project_daily_summaries WHERE project_id=$projectId AND local_date=$localDate;";
        AddIdentity(command, projectId, localDate);
        return Parse((string)(await command.ExecuteScalarAsync(cancellationToken))!);
    }

    private static async Task<IReadOnlyList<DailySummarySourceReference>> GetSourcesAsync(SqliteConnection connection, SqliteTransaction? transaction, Guid projectId, DateOnly localDate, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT source_type,source_ref FROM project_daily_summary_sources WHERE project_id=$projectId AND local_date=$localDate ORDER BY source_type,source_ref;";
        AddIdentity(command, projectId, localDate);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var sources = new List<DailySummarySourceReference>();
        while (await reader.ReadAsync(cancellationToken)) sources.Add(new(reader.GetString(0), reader.GetString(1)));
        return sources;
    }

    private static DailySummaryDocument ReadDocument(SqliteDataReader reader, IReadOnlyList<DailySummarySourceReference> sources) => new(Guid.Parse(reader.GetString(0)), DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture), reader.GetString(2), reader.GetInt32(3), Parse(reader.GetString(4)), Parse(reader.GetString(5)), sources);
    private static void AddIdentity(SqliteCommand command, Guid projectId, DateOnly localDate) { command.Parameters.AddWithValue("$projectId", projectId.ToString()); command.Parameters.AddWithValue("$localDate", localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)); }
    private static void AddRange(SqliteCommand command, Guid projectId, DateOnly? from, DateOnly? through) { command.Parameters.AddWithValue("$projectId", projectId.ToString()); command.Parameters.AddWithValue("$from", from is null ? DBNull.Value : from.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)); command.Parameters.AddWithValue("$through", through is null ? DBNull.Value : through.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)); }
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static void Validate(DailySummaryWrite write) { ArgumentNullException.ThrowIfNull(write); if (write.ProjectId == Guid.Empty) throw new ArgumentException("Project identity is required.", nameof(write)); ArgumentException.ThrowIfNullOrWhiteSpace(write.Content); ArgumentNullException.ThrowIfNull(write.Sources); foreach (var source in write.Sources) { ArgumentNullException.ThrowIfNull(source); ArgumentException.ThrowIfNullOrWhiteSpace(source.SourceType); ArgumentException.ThrowIfNullOrWhiteSpace(source.SourceRef); } }
}
