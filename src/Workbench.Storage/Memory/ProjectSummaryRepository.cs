using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Workbench.Storage.Database;

namespace Workbench.Storage.Memory;

public sealed class ProjectSummaryRepository
{
    private readonly WorkbenchDatabase _database;

    public ProjectSummaryRepository(WorkbenchDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public async Task<IReadOnlyList<StoredSummaryEntry>> AppendAsync(
        Guid projectId,
        Guid resultId,
        IReadOnlyList<SummaryDelta> deltas,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("Project identity is required.", nameof(projectId));
        if (resultId == Guid.Empty) throw new ArgumentException("Result identity is required.", nameof(resultId));
        ArgumentNullException.ThrowIfNull(deltas);
        if (deltas.Count == 0) throw new ArgumentException("At least one summary delta is required.", nameof(deltas));
        ValidateDeltas(deltas);

        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var stored = new List<StoredSummaryEntry>(deltas.Count);
            for (var ordinal = 0; ordinal < deltas.Count; ordinal++)
            {
                var delta = deltas[ordinal];
                var entryId = Guid.NewGuid();
                var inserted = await InsertParentAsync(
                    connection,
                    transaction,
                    entryId,
                    projectId,
                    resultId,
                    ordinal,
                    delta,
                    createdAt,
                    cancellationToken);

                if (inserted)
                {
                    await InsertSourceRefsAsync(connection, transaction, entryId, delta.SourceRefs, cancellationToken);
                    stored.Add(new StoredSummaryEntry(
                        entryId,
                        projectId,
                        delta.OccurredAt,
                        createdAt,
                        delta.Kind,
                        delta.Text,
                        resultId,
                        ordinal,
                        delta.SourceRefs.ToArray()));
                    continue;
                }

                var existing = await GetByIdentityAsync(connection, transaction, resultId, ordinal, cancellationToken)
                    ?? throw new InvalidOperationException("The summary identity conflict could not be resolved.");
                if (!SemanticallyMatches(existing, projectId, delta))
                {
                    throw new InvalidOperationException(
                        $"Summary result {resultId} ordinal {ordinal} was already persisted with a different payload.");
                }

                stored.Add(existing);
            }

            await transaction.CommitAsync(cancellationToken);
            return stored;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<IReadOnlyList<StoredSummaryEntry>> QueryAsync(
        SummaryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        var sql = new StringBuilder("""
            SELECT entry_id, project_id, occurred_at, created_at, kind, text, result_id, delta_ordinal
            FROM project_summary_entries AS entry
            WHERE entry.project_id = $projectId
            """);
        command.Parameters.AddWithValue("$projectId", query.ProjectId.ToString());

        if (query.Kinds is { Count: > 0 })
        {
            sql.Append(" AND entry.kind IN (");
            for (var index = 0; index < query.Kinds.Count; index++)
            {
                if (index > 0) sql.Append(", ");
                var parameterName = $"$kind{index}";
                sql.Append(parameterName);
                command.Parameters.AddWithValue(parameterName, query.Kinds[index].ToString());
            }
            sql.Append(')');
        }

        if (query.OccurredFrom is not null)
        {
            sql.Append(" AND entry.occurred_at >= $occurredFrom");
            command.Parameters.AddWithValue("$occurredFrom", Format(query.OccurredFrom.Value));
        }

        if (query.OccurredTo is not null)
        {
            sql.Append(" AND entry.occurred_at <= $occurredTo");
            command.Parameters.AddWithValue("$occurredTo", Format(query.OccurredTo.Value));
        }

        if (query.SourceKind is not null || query.SourceLocator is not null)
        {
            sql.Append(" AND EXISTS (SELECT 1 FROM project_summary_source_refs AS source WHERE source.entry_id = entry.entry_id");
            if (query.SourceKind is not null)
            {
                sql.Append(" AND source.source_kind = $sourceKind");
                command.Parameters.AddWithValue("$sourceKind", query.SourceKind);
            }
            if (query.SourceLocator is not null)
            {
                sql.Append(" AND source.source_locator = $sourceLocator");
                command.Parameters.AddWithValue("$sourceLocator", query.SourceLocator);
            }
            sql.Append(')');
        }

        sql.Append(" ORDER BY entry.occurred_at DESC, entry.created_at DESC, entry.entry_id LIMIT $limit;");
        command.Parameters.AddWithValue("$limit", query.Limit);
        command.CommandText = sql.ToString();

        var rows = new List<EntryRow>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(ReadRow(reader));
            }
        }

        var entries = new List<StoredSummaryEntry>(rows.Count);
        foreach (var row in rows)
        {
            var sourceRefs = await GetSourceRefsAsync(connection, null, row.EntryId, cancellationToken);
            entries.Add(row.ToStored(sourceRefs));
        }
        return entries;
    }

    private static void ValidateDeltas(IReadOnlyList<SummaryDelta> deltas)
    {
        foreach (var delta in deltas)
        {
            ArgumentNullException.ThrowIfNull(delta);
            foreach (var sourceRef in delta.SourceRefs)
            {
                ArgumentNullException.ThrowIfNull(sourceRef);
            }
        }
    }

    private static async Task<bool> InsertParentAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid entryId,
        Guid projectId,
        Guid resultId,
        int ordinal,
        SummaryDelta delta,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO project_summary_entries
                (entry_id, project_id, occurred_at, created_at, kind, text, result_id, delta_ordinal)
            VALUES
                ($entryId, $projectId, $occurredAt, $createdAt, $kind, $text, $resultId, $ordinal)
            ON CONFLICT(result_id, delta_ordinal) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$entryId", entryId.ToString());
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        command.Parameters.AddWithValue("$occurredAt", Format(delta.OccurredAt));
        command.Parameters.AddWithValue("$createdAt", Format(createdAt));
        command.Parameters.AddWithValue("$kind", delta.Kind.ToString());
        command.Parameters.AddWithValue("$text", delta.Text);
        command.Parameters.AddWithValue("$resultId", resultId.ToString());
        command.Parameters.AddWithValue("$ordinal", ordinal);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static async Task InsertSourceRefsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid entryId,
        IReadOnlyList<SummarySourceRef> sourceRefs,
        CancellationToken cancellationToken)
    {
        for (var ordinal = 0; ordinal < sourceRefs.Count; ordinal++)
        {
            var sourceRef = sourceRefs[ordinal];
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO project_summary_source_refs (entry_id, ordinal, source_kind, source_locator)
                VALUES ($entryId, $ordinal, $sourceKind, $sourceLocator);
                """;
            command.Parameters.AddWithValue("$entryId", entryId.ToString());
            command.Parameters.AddWithValue("$ordinal", ordinal);
            command.Parameters.AddWithValue("$sourceKind", sourceRef.SourceKind);
            command.Parameters.AddWithValue("$sourceLocator", sourceRef.SourceLocator);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<StoredSummaryEntry?> GetByIdentityAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid resultId,
        int ordinal,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT entry_id, project_id, occurred_at, created_at, kind, text, result_id, delta_ordinal
            FROM project_summary_entries
            WHERE result_id = $resultId AND delta_ordinal = $ordinal;
            """;
        command.Parameters.AddWithValue("$resultId", resultId.ToString());
        command.Parameters.AddWithValue("$ordinal", ordinal);

        EntryRow? row;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            row = await reader.ReadAsync(cancellationToken) ? ReadRow(reader) : null;
        }
        if (row is null) return null;

        var sourceRefs = await GetSourceRefsAsync(connection, transaction, row.EntryId, cancellationToken);
        return row.ToStored(sourceRefs);
    }

    private static async Task<IReadOnlyList<SummarySourceRef>> GetSourceRefsAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid entryId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT source_kind, source_locator
            FROM project_summary_source_refs
            WHERE entry_id = $entryId
            ORDER BY ordinal;
            """;
        command.Parameters.AddWithValue("$entryId", entryId.ToString());
        var sourceRefs = new List<SummarySourceRef>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            sourceRefs.Add(new SummarySourceRef(reader.GetString(0), reader.GetString(1)));
        }
        return sourceRefs;
    }

    private static EntryRow ReadRow(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        Guid.Parse(reader.GetString(1)),
        Parse(reader.GetString(2)),
        Parse(reader.GetString(3)),
        Enum.Parse<SummaryDeltaKind>(reader.GetString(4)),
        reader.GetString(5),
        Guid.Parse(reader.GetString(6)),
        reader.GetInt32(7));

    private static bool SemanticallyMatches(StoredSummaryEntry existing, Guid projectId, SummaryDelta delta) =>
        existing.ProjectId == projectId &&
        existing.OccurredAt == delta.OccurredAt &&
        existing.Kind == delta.Kind &&
        existing.Text == delta.Text &&
        existing.SourceRefs.SequenceEqual(delta.SourceRefs);

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private sealed record EntryRow(
        Guid EntryId,
        Guid ProjectId,
        DateTimeOffset OccurredAt,
        DateTimeOffset CreatedAt,
        SummaryDeltaKind Kind,
        string Text,
        Guid ResultId,
        int DeltaOrdinal)
    {
        public StoredSummaryEntry ToStored(IReadOnlyList<SummarySourceRef> sourceRefs) =>
            new(EntryId, ProjectId, OccurredAt, CreatedAt, Kind, Text, ResultId, DeltaOrdinal, sourceRefs);
    }
}
