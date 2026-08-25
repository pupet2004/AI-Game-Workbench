using System.Globalization;
using Microsoft.Data.Sqlite;
using Workbench.Core.Continuity;
using Workbench.Storage.Database;

namespace Workbench.Storage.Continuity;

public sealed class B1EvidenceRepository(WorkbenchDatabase database)
{
    private readonly WorkbenchDatabase _database =
        database ?? throw new ArgumentNullException(nameof(database));

    public async Task<EvidenceRecord> RecordAsync(
        EvidenceRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var existing = await GetCoreAsync(connection, transaction, record.ProjectRef, record.EvidenceRef, cancellationToken);
            if (existing is not null)
            {
                if (!Equals(existing, record))
                    throw new InvalidOperationException("An evidence record with the same reference already has different provenance.");
                await transaction.CommitAsync(cancellationToken);
                return existing;
            }

            var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO b1_evidence_records(
                    project_id,evidence_ref,kind,locator,digest_algorithm,digest_hex,content_length,created_at)
                SELECT id,$evidence,$kind,$locator,$algorithm,$digest,$length,$created
                FROM projects
                WHERE id=$project;
                """;
            Add(insert,
                ("$project", record.ProjectRef.Value.ToString()),
                ("$evidence", record.EvidenceRef.Value),
                ("$kind", record.Kind.ToString()),
                ("$locator", record.Locator),
                ("$algorithm", (object?)record.DigestAlgorithm ?? DBNull.Value),
                ("$digest", (object?)record.DigestHex ?? DBNull.Value),
                ("$length", (object?)record.ContentLength ?? DBNull.Value),
                ("$created", Format(record.CreatedAt)));
            if (await insert.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("The Project does not exist for this evidence record.");

            await transaction.CommitAsync(cancellationToken);
            return record;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<EvidenceRecord?> GetAsync(
        ProjectRef projectRef,
        EvidenceRef evidenceRef,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await GetCoreAsync(connection, transaction: null, projectRef, evidenceRef, cancellationToken);
    }

    public async Task<bool> VerifyAsync(
        ProjectRef projectRef,
        EvidenceRef evidenceRef,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        var record = await GetAsync(projectRef, evidenceRef, cancellationToken);
        return record?.DigestAlgorithm is not null &&
            record.DigestHex is not null &&
            EvidenceDigest.VerifySha256(content.Span, record.DigestHex);
    }

    private static async Task<EvidenceRecord?> GetCoreAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        ProjectRef projectRef,
        EvidenceRef evidenceRef,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT kind,locator,digest_algorithm,digest_hex,content_length,created_at
            FROM b1_evidence_records
            WHERE project_id=$project AND evidence_ref=$evidence;
            """;
        Add(command,
            ("$project", projectRef.Value.ToString()),
            ("$evidence", evidenceRef.Value));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;
        if (!Enum.TryParse<EvidenceKind>(reader.GetString(0), out var kind))
            throw new InvalidDataException("The evidence kind is invalid.");
        return new EvidenceRecord(
            projectRef,
            evidenceRef,
            kind,
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetInt64(4),
            DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }

    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

    private static void Add(SqliteCommand command, params (string Name, object Value)[] values)
    {
        foreach (var (name, value) in values)
            command.Parameters.AddWithValue(name, value);
    }
}
