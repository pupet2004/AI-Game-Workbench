using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Workbench.Storage.Database;

namespace Workbench.Storage.Memory;

public sealed class ProjectLibraryProposalService
{
    private const int MaxPayloadUtf8Bytes = 32768;
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly WorkbenchDatabase _database;
    private readonly ProjectLibraryEvolutionRepository _library;
    private readonly TimeProvider _timeProvider;

    public ProjectLibraryProposalService(WorkbenchDatabase database, TimeProvider? timeProvider = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _library = new ProjectLibraryEvolutionRepository(database);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ProjectLibraryProposal> CreateProposalAsync(
        ProjectLibraryProposalDraft draft,
        CancellationToken cancellationToken = default)
    {
        draft = ProjectLibraryEvolutionRepository.NormalizeProposalDraft(draft);
        var payload = Serialize(draft);
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO project_library_proposals (
                    id,project_id,source_session_id,status,payload_json,created_at,decided_at)
                VALUES ($id,$projectId,$sourceSessionId,'Pending',$payload,$createdAt,NULL);
                """;
            command.Parameters.AddWithValue("$id", draft.ProposalId.ToString());
            command.Parameters.AddWithValue("$projectId", draft.ProjectId.ToString());
            command.Parameters.AddWithValue("$sourceSessionId", draft.SourceSessionId?.ToString() ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$payload", payload);
            command.Parameters.AddWithValue("$createdAt", Format(draft.CreatedAt));
            await command.ExecuteNonQueryAsync(cancellationToken);

            var stored = await ReadAsync(connection, transaction, draft.ProjectId, draft.ProposalId, cancellationToken)
                ?? throw new InvalidOperationException("The Library Proposal could not be stored.");
            if (stored.SourceSessionId != draft.SourceSessionId ||
                !string.Equals(Serialize(stored.Draft), payload, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The Library Proposal identity was already used with different content.");
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

    public async Task<ProjectLibraryProposal?> GetAsync(
        Guid projectId,
        Guid proposalId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(projectId, proposalId);
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await ReadAsync(connection, null, projectId, proposalId, cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectLibraryProposal>> GetPendingAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("Project identity is required.", nameof(projectId));
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,project_id,source_session_id,status,payload_json,created_at,decided_at
            FROM project_library_proposals
            WHERE project_id=$projectId AND status='Pending'
            ORDER BY created_at DESC,id;
            """;
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var proposals = new List<ProjectLibraryProposal>();
        while (await reader.ReadAsync(cancellationToken)) proposals.Add(Read(reader));
        return proposals;
    }

    public Task AcceptAsync(Guid projectId, Guid proposalId, CancellationToken cancellationToken = default) =>
        ApplyAsync(projectId, proposalId, null, cancellationToken);

    public async Task EditAndAcceptAsync(
        Guid projectId,
        Guid proposalId,
        LibraryProposalEdit edit,
        CancellationToken cancellationToken = default)
    {
        edit = ProjectLibraryEvolutionRepository.NormalizeProposalEdit(edit);
        await ApplyAsync(projectId, proposalId, edit, cancellationToken);
    }

    public async Task RejectAsync(
        Guid projectId,
        Guid proposalId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(projectId, proposalId);
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var proposal = await ReadAsync(connection, transaction, projectId, proposalId, cancellationToken)
                ?? throw new InvalidOperationException("The Library Proposal is not owned by this project.");
            if (proposal.Status == LibraryProposalStatus.Rejected)
            {
                await transaction.CommitAsync(cancellationToken);
                return;
            }
            if (proposal.Status == LibraryProposalStatus.Accepted)
            {
                throw new InvalidOperationException("An accepted Library Proposal cannot be rejected.");
            }

            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE project_library_proposals SET status='Rejected',decided_at=$decidedAt WHERE id=$id AND project_id=$projectId AND status='Pending';";
            command.Parameters.AddWithValue("$decidedAt", Format(_timeProvider.GetUtcNow()));
            command.Parameters.AddWithValue("$id", proposalId.ToString());
            command.Parameters.AddWithValue("$projectId", projectId.ToString());
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("The Library Proposal state changed.");
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task ApplyAsync(
        Guid projectId,
        Guid proposalId,
        LibraryProposalEdit? edit,
        CancellationToken cancellationToken)
    {
        ValidateIdentity(projectId, proposalId);
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var proposal = await ReadAsync(connection, transaction, projectId, proposalId, cancellationToken)
                ?? throw new InvalidOperationException("The Library Proposal is not owned by this project.");
            if (proposal.Status == LibraryProposalStatus.Accepted)
            {
                await transaction.CommitAsync(cancellationToken);
                return;
            }
            if (proposal.Status == LibraryProposalStatus.Rejected)
                throw new InvalidOperationException("A rejected Library Proposal cannot be accepted.");

            var draft = proposal.Draft;
            if (edit is not null)
            {
                draft = draft with
                {
                    NodeContent = edit.NodeContent,
                    CurrentOverview = edit.CurrentOverview,
                    Materials = edit.Materials
                };
            }
            draft = ProjectLibraryEvolutionRepository.NormalizeProposalDraft(draft);
            var decidedAt = _timeProvider.GetUtcNow();
            await _library.ApplyProposalAsync(connection, transaction, draft, decidedAt, cancellationToken);

            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE project_library_proposals
                SET status='Accepted',payload_json=$payload,decided_at=$decidedAt
                WHERE id=$id AND project_id=$projectId AND status='Pending';
                """;
            command.Parameters.AddWithValue("$payload", Serialize(draft));
            command.Parameters.AddWithValue("$decidedAt", Format(decidedAt));
            command.Parameters.AddWithValue("$id", proposalId.ToString());
            command.Parameters.AddWithValue("$projectId", projectId.ToString());
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("The Library Proposal state changed.");
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<ProjectLibraryProposal?> ReadAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid projectId,
        Guid proposalId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id,project_id,source_session_id,status,payload_json,created_at,decided_at
            FROM project_library_proposals
            WHERE project_id=$projectId AND id=$id;
            """;
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        command.Parameters.AddWithValue("$id", proposalId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    private static ProjectLibraryProposal Read(SqliteDataReader reader)
    {
        try
        {
            var id = Guid.Parse(reader.GetString(0));
            var projectId = Guid.Parse(reader.GetString(1));
            var sourceSessionId = reader.IsDBNull(2) ? (Guid?)null : Guid.Parse(reader.GetString(2));
            var status = Enum.Parse<LibraryProposalStatus>(reader.GetString(3), false);
            var draft = JsonSerializer.Deserialize<ProjectLibraryProposalDraft>(reader.GetString(4), JsonOptions)
                ?? throw new InvalidOperationException("The stored Library Proposal payload is empty.");
            draft = ProjectLibraryEvolutionRepository.NormalizeProposalDraft(draft);
            if (draft.ProposalId != id || draft.ProjectId != projectId || draft.SourceSessionId != sourceSessionId)
                throw new InvalidOperationException("The stored Library Proposal payload identity is invalid.");
            return new(
                id,
                projectId,
                sourceSessionId,
                status,
                draft,
                Parse(reader.GetString(5)),
                reader.IsDBNull(6) ? null : Parse(reader.GetString(6)));
        }
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException)
        {
            throw new InvalidOperationException("The stored Library Proposal is invalid.", exception);
        }
    }

    private static string Serialize(ProjectLibraryProposalDraft draft)
    {
        var payload = JsonSerializer.Serialize(draft, JsonOptions);
        if (Encoding.UTF8.GetByteCount(payload) > MaxPayloadUtf8Bytes)
            throw new ArgumentException("The Library Proposal payload is too large.", nameof(draft));
        return payload;
    }

    private static void ValidateIdentity(Guid projectId, Guid proposalId)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("Project identity is required.", nameof(projectId));
        if (proposalId == Guid.Empty) throw new ArgumentException("Proposal identity is required.", nameof(proposalId));
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
