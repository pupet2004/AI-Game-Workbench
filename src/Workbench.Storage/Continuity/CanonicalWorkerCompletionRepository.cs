using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Workbench.Core.Continuity;
using Workbench.Storage.Database;

namespace Workbench.Storage.Continuity;

public sealed class CanonicalWorkerCompletionRepository(WorkbenchDatabase database)
{
    private readonly WorkbenchDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    public async Task<StoredCanonicalWorkerCompletion> SavePendingAsync(
        CanonicalWorkerCompletionFacts facts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(facts);
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO canonical_worker_completions(
                    id,project_id,source_event_id,task_id,task_revision_id,worker_execution_id,
                    attempt_id,session_binding_id,worker_actor_id,final_report,validation_summary,
                    evidence_refs_json,planned_result_claim_id,planned_validation_claim_id,planned_handoff_id,
                    result_claim_id,validation_claim_id,handoff_id,status,created_at)
                VALUES($id,$project,$source,$task,$revision,$execution,$attempt,$binding,$actor,$report,$validation,
                    $evidence,$planned_result,$planned_validation,$planned_handoff,NULL,NULL,NULL,'PendingBridge',$created)
                ON CONFLICT(project_id,source_event_id) DO NOTHING;
                """;
            Add(insert, "$id", facts.CompletionId.ToString());
            Add(insert, "$project", facts.ProjectRef.Value.ToString());
            Add(insert, "$source", facts.SourceEventId.ToString());
            Add(insert, "$task", facts.TaskId.ToString());
            Add(insert, "$revision", facts.TaskRevisionId.ToString());
            Add(insert, "$execution", facts.WorkerExecutionId.ToString());
            Add(insert, "$attempt", facts.AttemptRef.Value.ToString());
            Add(insert, "$binding", facts.SessionBindingRef.Value.ToString());
            Add(insert, "$actor", facts.WorkerActorRef.Value.ToString());
            Add(insert, "$report", facts.FinalReport);
            Add(insert, "$validation", facts.ValidationSummary);
            Add(insert, "$evidence", JsonSerializer.Serialize(facts.EvidenceRefs.Select(value => value.Value).ToArray()));
            Add(insert, "$planned_result", facts.ResultClaimRef.Value.ToString());
            Add(insert, "$planned_validation", facts.ValidationClaimRef?.Value.ToString());
            Add(insert, "$planned_handoff", facts.HandoffRef.Value.ToString());
            Add(insert, "$created", facts.CompletedAt.ToString("O", CultureInfo.InvariantCulture));
            await insert.ExecuteNonQueryAsync(cancellationToken);

            var stored = await ReadBySourceEventAsync(connection, transaction, facts.ProjectRef.Value, facts.SourceEventId, cancellationToken)
                ?? throw new InvalidDataException("The canonical Worker completion could not be persisted.");
            ValidateIdentity(stored.Facts, facts);
            await transaction.CommitAsync(cancellationToken);
            return stored;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<StoredCanonicalWorkerCompletion?> GetBySourceEventAsync(
        Guid projectId,
        Guid sourceEventId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await ReadBySourceEventAsync(connection, null, projectId, sourceEventId, cancellationToken);
    }

    public async Task<IReadOnlyList<StoredCanonicalWorkerCompletion>> ListPendingAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT source_event_id FROM canonical_worker_completions WHERE project_id=$project AND status='PendingBridge' ORDER BY created_at,id;";
        command.Parameters.AddWithValue("$project", projectId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<StoredCanonicalWorkerCompletion>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var item = await GetBySourceEventAsync(projectId, Guid.Parse(reader.GetString(0)), cancellationToken);
            if (item is not null) result.Add(item);
        }
        return result;
    }

    public async Task MarkGovernedAsync(
        Guid projectId,
        Guid completionId,
        AuthorityDecisionRef decisionRef,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE canonical_worker_completions
            SET status='Governed', governed_at=$at, authority_decision_id=$decision
            WHERE project_id=$project AND id=$id AND status='GovernanceReady';
            """;
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$decision", decisionRef.Value.ToString());
        command.Parameters.AddWithValue("$project", projectId.ToString());
        command.Parameters.AddWithValue("$id", completionId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MarkGovernedByHandoffAsync(
        Guid projectId,
        HandoffRef handoffRef,
        AuthorityDecisionRef decisionRef,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE canonical_worker_completions
            SET status='Governed', governed_at=$at, authority_decision_id=$decision
            WHERE project_id=$project AND handoff_id=$handoff AND status='GovernanceReady';
            """;
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$decision", decisionRef.Value.ToString());
        command.Parameters.AddWithValue("$project", projectId.ToString());
        command.Parameters.AddWithValue("$handoff", handoffRef.Value.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ReconcileGovernedAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE canonical_worker_completions
            SET status='Governed',
                governed_at=COALESCE(governed_at, (
                    SELECT decision.created_at
                    FROM b1_decision_considered_refs refs
                    JOIN b1_authority_decisions decision
                      ON decision.id=refs.decision_id AND decision.project_id=refs.project_id
                    WHERE refs.project_id=canonical_worker_completions.project_id
                      AND refs.ref_kind='Handoff'
                      AND refs.handoff_id=canonical_worker_completions.handoff_id
                    ORDER BY decision.project_commit_sequence
                    LIMIT 1)),
                authority_decision_id=COALESCE(authority_decision_id, (
                    SELECT decision.id
                    FROM b1_decision_considered_refs refs
                    JOIN b1_authority_decisions decision
                      ON decision.id=refs.decision_id AND decision.project_id=refs.project_id
                    WHERE refs.project_id=canonical_worker_completions.project_id
                      AND refs.ref_kind='Handoff'
                      AND refs.handoff_id=canonical_worker_completions.handoff_id
                    ORDER BY decision.project_commit_sequence
                    LIMIT 1))
            WHERE project_id=$project
              AND status='GovernanceReady'
              AND handoff_id IS NOT NULL
              AND EXISTS(
                  SELECT 1
                  FROM b1_decision_considered_refs refs
                  JOIN b1_authority_decisions decision
                    ON decision.id=refs.decision_id AND decision.project_id=refs.project_id
                  WHERE refs.project_id=canonical_worker_completions.project_id
                    AND refs.ref_kind='Handoff'
                    AND refs.handoff_id=canonical_worker_completions.handoff_id);
            """;
        command.Parameters.AddWithValue("$project", projectId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<StoredCanonicalWorkerCompletion?> ReadBySourceEventAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid projectId,
        Guid sourceEventId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id,source_event_id,task_id,task_revision_id,worker_execution_id,attempt_id,
                   session_binding_id,worker_actor_id,final_report,validation_summary,evidence_refs_json,
                   planned_result_claim_id,planned_validation_claim_id,planned_handoff_id,
                   result_claim_id,validation_claim_id,handoff_id,status,created_at,governed_at,authority_decision_id
            FROM canonical_worker_completions
            WHERE project_id=$project AND source_event_id=$source;
            """;
        command.Parameters.AddWithValue("$project", projectId.ToString());
        command.Parameters.AddWithValue("$source", sourceEventId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        var facts = new CanonicalWorkerCompletionFacts(
            Guid.Parse(reader.GetString(0)),
            new ProjectRef(projectId),
            Guid.Parse(reader.GetString(1)),
            Guid.Parse(reader.GetString(2)),
            Guid.Parse(reader.GetString(3)),
            Guid.Parse(reader.GetString(4)),
            new AttemptRef(Guid.Parse(reader.GetString(5))),
            new SessionBindingRef(Guid.Parse(reader.GetString(6))),
            new LogicalActorRef(Guid.Parse(reader.GetString(7))),
            reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            ParseEvidence(reader.GetString(10)),
            new ClaimRef(Guid.Parse(reader.IsDBNull(14) ? reader.GetString(11) : reader.GetString(14))),
            reader.IsDBNull(15)
                ? (reader.IsDBNull(12) ? null : new ClaimRef(Guid.Parse(reader.GetString(12))))
                : new ClaimRef(Guid.Parse(reader.GetString(15))),
            new HandoffRef(Guid.Parse(reader.IsDBNull(16) ? reader.GetString(13) : reader.GetString(16))),
            DateTimeOffset.Parse(reader.GetString(18), CultureInfo.InvariantCulture));
        var status = Enum.Parse<CanonicalWorkerCompletionStatus>(reader.GetString(17), false);
        var persistedAt = DateTimeOffset.Parse(reader.GetString(18), CultureInfo.InvariantCulture);
        DateTimeOffset? governedAt = reader.IsDBNull(19)
            ? null
            : DateTimeOffset.Parse(reader.GetString(19), CultureInfo.InvariantCulture);
        AuthorityDecisionRef? decision = reader.IsDBNull(20)
            ? null
            : new AuthorityDecisionRef(Guid.Parse(reader.GetString(20)));
        return new StoredCanonicalWorkerCompletion(facts, status, persistedAt, governedAt, decision);
    }

    private static IReadOnlyList<EvidenceRef> ParseEvidence(string json)
    {
        var values = JsonSerializer.Deserialize<string[]>(json) ?? [];
        return values.Select(value => new EvidenceRef(value)).ToArray();
    }

    private static void ValidateIdentity(CanonicalWorkerCompletionFacts stored, CanonicalWorkerCompletionFacts requested)
    {
        if (stored.CompletionId != requested.CompletionId ||
            stored.TaskId != requested.TaskId ||
            stored.TaskRevisionId != requested.TaskRevisionId ||
            stored.WorkerExecutionId != requested.WorkerExecutionId ||
            stored.AttemptRef != requested.AttemptRef ||
            stored.SessionBindingRef != requested.SessionBindingRef ||
            stored.WorkerActorRef != requested.WorkerActorRef ||
            !string.Equals(stored.FinalReport, requested.FinalReport, StringComparison.Ordinal) ||
            !string.Equals(stored.ValidationSummary, requested.ValidationSummary, StringComparison.Ordinal) ||
            !stored.EvidenceRefs.SequenceEqual(requested.EvidenceRefs) ||
            stored.ResultClaimRef != requested.ResultClaimRef ||
            stored.ValidationClaimRef != requested.ValidationClaimRef ||
            stored.HandoffRef != requested.HandoffRef ||
            stored.CompletedAt != requested.CompletedAt)
        {
            throw new InvalidOperationException("The completion source event was reused with different Worker facts.");
        }
    }

    private static void Add(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
}
