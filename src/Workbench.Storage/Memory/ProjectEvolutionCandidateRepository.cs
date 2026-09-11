using System.Globalization;
using Microsoft.Data.Sqlite;
using Workbench.Storage.Database;

namespace Workbench.Storage.Memory;

public sealed class ProjectEvolutionCandidateRepository(WorkbenchDatabase database)
{
    private readonly WorkbenchDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    public async Task<ProjectEvolutionCandidate> SaveAsync(
        ProjectEvolutionCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        Validate(candidate);
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO project_evolution_candidates(
                    id,project_id,epoch_id,result_id,source_ref,object_name,object_kind,change_type,
                    before_text,after_text,impact_class,route_hint,reason,status,created_at)
                VALUES($id,$project,$epoch,$result,$source,$object,$kind,$change,$before,$after,$impact,$route,$reason,$status,$created);
                """;
            Add(command, ("$id", candidate.CandidateId.ToString()), ("$project", candidate.ProjectId.ToString()),
                ("$epoch", (object?)candidate.EpochId?.ToString() ?? DBNull.Value), ("$result", (object?)candidate.ResultId?.ToString() ?? DBNull.Value),
                ("$source", candidate.SourceRef), ("$object", candidate.Object), ("$kind", candidate.ObjectKind),
                ("$change", candidate.ChangeType), ("$before", (object?)candidate.Before ?? DBNull.Value),
                ("$after", (object?)candidate.After ?? DBNull.Value), ("$impact", candidate.ImpactClass),
                ("$route", candidate.RouteHint), ("$reason", candidate.Reason), ("$status", candidate.Status.ToString()),
                ("$created", Format(candidate.CreatedAt)));
            await command.ExecuteNonQueryAsync(cancellationToken);
            var stored = await GetCoreAsync(connection, transaction, candidate.ProjectId, candidate.CandidateId, cancellationToken)
                ?? throw new InvalidOperationException("The Evolution Candidate could not be stored.");
            await transaction.CommitAsync(cancellationToken);
            return stored;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<IReadOnlyList<ProjectEvolutionCandidate>> ListAsync(
        Guid projectId,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("Project identity is required.", nameof(projectId));
        if (limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(limit));
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,project_id,epoch_id,result_id,source_ref,object_name,object_kind,change_type,
                   before_text,after_text,impact_class,route_hint,reason,status,created_at
            FROM project_evolution_candidates
            WHERE project_id=$project
            ORDER BY created_at DESC,id DESC LIMIT $limit;
            """;
        Add(command, ("$project", projectId.ToString()), ("$limit", limit));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<ProjectEvolutionCandidate>();
        while (await reader.ReadAsync(cancellationToken)) result.Add(Read(reader));
        return result;
    }

    /// <summary>
    /// Returns only unresolved candidates. History remains available through
    /// <see cref="ListAsync"/> for audit and provenance views.
    /// </summary>
    public async Task<IReadOnlyList<ProjectEvolutionCandidate>> ListActiveAsync(
        Guid projectId,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("Project identity is required.", nameof(projectId));
        if (limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(limit));
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,project_id,epoch_id,result_id,source_ref,object_name,object_kind,change_type,
                   before_text,after_text,impact_class,route_hint,reason,status,created_at
            FROM project_evolution_candidates
            WHERE project_id=$project AND status IN ('Observed','GovernancePending')
            ORDER BY created_at DESC,id DESC LIMIT $limit;
            """;
        Add(command, ("$project", projectId.ToString()), ("$limit", limit));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<ProjectEvolutionCandidate>();
        while (await reader.ReadAsync(cancellationToken)) result.Add(Read(reader));
        return result;
    }

    public async Task<ProjectEvolutionCandidate> UpdateStatusAsync(
        Guid projectId,
        Guid candidateId,
        ProjectEvolutionCandidateStatus status,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("Project identity is required.", nameof(projectId));
        if (candidateId == Guid.Empty) throw new ArgumentException("Candidate identity is required.", nameof(candidateId));
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));

        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var updated = await UpdateStatusAsync(connection, transaction, projectId, candidateId, status, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return updated;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<bool> TryUpdateStatusAsync(
        Guid projectId,
        Guid candidateId,
        ProjectEvolutionCandidateStatus status,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("Project identity is required.", nameof(projectId));
        if (candidateId == Guid.Empty) throw new ArgumentException("Candidate identity is required.", nameof(candidateId));
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var updated = await TryUpdateStatusAsync(connection, transaction, projectId, candidateId, status, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return updated;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    internal async Task<ProjectEvolutionCandidate> UpdateStatusAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid projectId,
        Guid candidateId,
        ProjectEvolutionCandidateStatus status,
        CancellationToken cancellationToken)
    {
        var current = await GetCoreAsync(connection, transaction, projectId, candidateId, cancellationToken)
            ?? throw new InvalidOperationException("The Evolution Candidate is not owned by this project.");
        if (current.Status == status) return current;
        if (current.Status is ProjectEvolutionCandidateStatus.Accepted or ProjectEvolutionCandidateStatus.Rejected)
            throw new InvalidOperationException("A resolved Evolution Candidate cannot change status.");

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE project_evolution_candidates
            SET status=$status
            WHERE project_id=$project AND id=$id AND status=$current;
            """;
        Add(command, ("$status", status.ToString()), ("$project", projectId.ToString()),
            ("$id", candidateId.ToString()), ("$current", current.Status.ToString()));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("The Evolution Candidate state changed.");
        return await GetCoreAsync(connection, transaction, projectId, candidateId, cancellationToken)
            ?? throw new InvalidOperationException("The updated Evolution Candidate could not be read.");
    }

    internal async Task<bool> TryUpdateStatusAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid projectId,
        Guid candidateId,
        ProjectEvolutionCandidateStatus status,
        CancellationToken cancellationToken)
    {
        var current = await GetCoreAsync(connection, transaction, projectId, candidateId, cancellationToken);
        if (current is null) return false;
        await UpdateStatusAsync(connection, transaction, projectId, candidateId, status, cancellationToken);
        return true;
    }

    public async Task<ProjectEvolutionCandidate?> GetAsync(Guid projectId, Guid candidateId, CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("Project identity is required.", nameof(projectId));
        if (candidateId == Guid.Empty) throw new ArgumentException("Candidate identity is required.", nameof(candidateId));
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await GetCoreAsync(connection, null, projectId, candidateId, cancellationToken);
    }

    private static async Task<ProjectEvolutionCandidate?> GetCoreAsync(SqliteConnection connection, SqliteTransaction? transaction, Guid projectId, Guid candidateId, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id,project_id,epoch_id,result_id,source_ref,object_name,object_kind,change_type,
                   before_text,after_text,impact_class,route_hint,reason,status,created_at
            FROM project_evolution_candidates WHERE project_id=$project AND id=$id;
            """;
        Add(command, ("$project", projectId.ToString()), ("$id", candidateId.ToString()));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    private static ProjectEvolutionCandidate Read(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)),
        reader.IsDBNull(2) ? null : Guid.Parse(reader.GetString(2)),
        reader.IsDBNull(3) ? null : Guid.Parse(reader.GetString(3)), reader.GetString(4), reader.GetString(5),
        reader.GetString(6), reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8),
        reader.IsDBNull(9) ? null : reader.GetString(9), reader.GetString(10), reader.GetString(11),
        reader.GetString(12), Enum.Parse<ProjectEvolutionCandidateStatus>(reader.GetString(13)), Parse(reader.GetString(14)));

    private static void Validate(ProjectEvolutionCandidate candidate)
    {
        if (candidate.CandidateId == Guid.Empty || candidate.ProjectId == Guid.Empty) throw new ArgumentException("Candidate identity is required.");
        foreach (var value in new[] { candidate.SourceRef, candidate.Object, candidate.ObjectKind, candidate.ChangeType, candidate.ImpactClass, candidate.RouteHint, candidate.Reason })
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Candidate fields cannot be blank.");
    }

    private static void Add(SqliteCommand command, params (string Name, object Value)[] values)
    { foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value); }
    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
