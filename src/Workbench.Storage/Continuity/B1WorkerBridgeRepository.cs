using System.Globalization;
using Microsoft.Data.Sqlite;
using Workbench.Core.Continuity;
using Workbench.Storage.Database;

namespace Workbench.Storage.Continuity;

public sealed class B1WorkerBridgeRepository(WorkbenchDatabase database)
{
    private readonly WorkbenchDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    public async Task<B1WorkerTaskLink> LinkTaskAsync(B1WorkerTaskLink link, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(link);
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO b1_worker_task_links(project_id,assignment_id,assignment_revision_id,worker_task_id,worker_task_revision_id,created_at)
                SELECT $project,$assignment,$revision,$task,$task_revision,$created
                WHERE EXISTS(SELECT 1 FROM tasks WHERE id=$task AND project_id=$project)
                  AND EXISTS(SELECT 1 FROM task_revisions WHERE id=$task_revision AND task_id=$task);
                """;
            Add(command, ("$project", link.ProjectRef.Value.ToString()), ("$assignment", link.AssignmentRef.Value.ToString()),
                ("$revision", link.AssignmentRevisionRef.Value.ToString()), ("$task", link.WorkerTaskId.ToString()),
                ("$task_revision", link.WorkerTaskRevisionId.ToString()), ("$created", Format(link.CreatedAt)));
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("The Worker Task or TaskRevision is not owned by the Project.");
            await transaction.CommitAsync(cancellationToken);
            return link;
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
    }

    public async Task<B1WorkerTaskLink?> GetTaskLinkAsync(ProjectRef project, Guid taskId, CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection(); await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand(); command.CommandText = "SELECT assignment_id,assignment_revision_id,worker_task_revision_id,created_at FROM b1_worker_task_links WHERE project_id=$project AND worker_task_id=$task";
        Add(command, ("$project", project.Value.ToString()), ("$task", taskId.ToString()));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new B1WorkerTaskLink(project, new AssignmentRef(Guid.Parse(reader.GetString(0))), new RevisionRef(Guid.Parse(reader.GetString(1))), taskId, Guid.Parse(reader.GetString(2)), Parse(reader.GetString(3))) : null;
    }

    public async Task<B1WorkerExecutionLink> LinkExecutionAsync(B1WorkerExecutionLink link, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(link);
        await using var connection = _database.CreateConnection(); await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO b1_worker_execution_links(project_id,attempt_id,worker_execution_id,relation_kind,created_at) SELECT $project,$attempt,$execution,$kind,$created WHERE EXISTS(SELECT 1 FROM b1_attempts WHERE id=$attempt AND project_id=$project) AND EXISTS(SELECT 1 FROM worker_executions WHERE id=$execution AND project_id=$project)";
        Add(command, ("$project", link.ProjectRef.Value.ToString()), ("$attempt", link.AttemptRef.Value.ToString()), ("$execution", link.WorkerExecutionId.ToString()), ("$kind", link.RelationKind.ToString()), ("$created", Format(link.CreatedAt)));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidOperationException("The WorkerExecution is not owned by the Project.");
        return link;
    }

    public async Task<IReadOnlyList<B1WorkerExecutionLink>> ListExecutionLinksAsync(ProjectRef project, AttemptRef attempt, CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection(); await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand(); command.CommandText = "SELECT worker_execution_id,relation_kind,created_at FROM b1_worker_execution_links WHERE project_id=$project AND attempt_id=$attempt ORDER BY created_at,id";
        Add(command, ("$project", project.Value.ToString()), ("$attempt", attempt.Value.ToString()));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); var result = new List<B1WorkerExecutionLink>();
        while (await reader.ReadAsync(cancellationToken)) result.Add(new(project, attempt, Guid.Parse(reader.GetString(0)), Enum.Parse<B1WorkerExecutionRelationKind>(reader.GetString(1)), Parse(reader.GetString(2))));
        return result;
    }

    public async Task<B1WorkerExecutionLink?> GetExecutionLinkAsync(ProjectRef project, Guid executionId, CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection(); await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT attempt_id,relation_kind,created_at FROM b1_worker_execution_links WHERE project_id=$project AND worker_execution_id=$execution";
        Add(command, ("$project", project.Value.ToString()), ("$execution", executionId.ToString()));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new B1WorkerExecutionLink(project, new AttemptRef(Guid.Parse(reader.GetString(0))), executionId, Enum.Parse<B1WorkerExecutionRelationKind>(reader.GetString(1)), Parse(reader.GetString(2)))
            : null;
    }

    public async Task<B1WorkerSessionLink?> GetSessionLinkAsync(ProjectRef project, Guid executionId, CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection(); await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT session_binding_id,agent_session_id,external_session_ref,provider_id,account_id,model_id,created_at FROM b1_worker_session_links WHERE project_id=$project AND worker_execution_id=$execution";
        Add(command, ("$project", project.Value.ToString()), ("$execution", executionId.ToString()));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new B1WorkerSessionLink(project, new SessionBindingRef(Guid.Parse(reader.GetString(0))), executionId, Guid.Parse(reader.GetString(1)), new ExternalSessionRef(reader.GetString(2)), reader.GetString(3), reader.GetString(4), reader.GetString(5), Parse(reader.GetString(6)))
            : null;
    }

    public async Task<B1WorkerSessionLink> LinkSessionAsync(B1WorkerSessionLink link, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(link);
        await using var connection = _database.CreateConnection(); await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand(); command.CommandText = "INSERT INTO b1_worker_session_links(project_id,session_binding_id,worker_execution_id,agent_session_id,external_session_ref,provider_id,account_id,model_id,created_at) SELECT $project,$binding,$execution,$agent,$external,$provider,$account,$model,$created WHERE EXISTS(SELECT 1 FROM worker_executions WHERE id=$execution AND project_id=$project) AND EXISTS(SELECT 1 FROM b1_session_bindings WHERE id=$binding AND project_id=$project)";
        Add(command, ("$project", link.ProjectRef.Value.ToString()), ("$binding", link.SessionBindingRef.Value.ToString()), ("$execution", link.WorkerExecutionId.ToString()), ("$agent", link.AgentSessionId.ToString()), ("$external", link.ExternalSessionRef.Value), ("$provider", link.ProviderId), ("$account", link.AccountId), ("$model", link.ModelId), ("$created", Format(link.CreatedAt)));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidOperationException("The AgentSession or SessionBinding is not owned by the Project."); return link;
    }

    public async Task<B1WorkerExecutionEvidence> RecordEvidenceAsync(B1WorkerExecutionEvidence evidence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        await using var connection = _database.CreateConnection(); await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand(); command.CommandText = "INSERT INTO b1_worker_execution_evidence(project_id,evidence_ref,worker_execution_id,worker_task_id,worker_task_revision_id,attempt_id,verification_result,verification_json,created_at) SELECT $project,$evidence,$execution,$task,$revision,$attempt,$result,$json,$created WHERE EXISTS(SELECT 1 FROM worker_executions WHERE id=$execution AND project_id=$project) AND EXISTS(SELECT 1 FROM tasks WHERE id=$task AND project_id=$project) AND EXISTS(SELECT 1 FROM task_revisions WHERE id=$revision AND task_id=$task) AND EXISTS(SELECT 1 FROM b1_attempts WHERE id=$attempt AND project_id=$project) ON CONFLICT(project_id,evidence_ref) DO NOTHING";
        Add(command, ("$project", evidence.ProjectRef.Value.ToString()), ("$evidence", evidence.EvidenceRef.Value), ("$execution", evidence.WorkerExecutionId.ToString()), ("$task", evidence.WorkerTaskId.ToString()), ("$revision", evidence.WorkerTaskRevisionId.ToString()), ("$attempt", evidence.AttemptRef.Value.ToString()), ("$result", evidence.VerificationResult), ("$json", evidence.VerificationJson), ("$created", Format(evidence.CreatedAt)));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidOperationException("Verification evidence provenance is not owned by the Project."); return evidence;
    }

    public async Task<B1WorkerExecutionEvidence?> GetEvidenceAsync(ProjectRef project, Guid executionId, CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection(); await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand(); command.CommandText = "SELECT evidence_ref,worker_task_id,worker_task_revision_id,attempt_id,verification_result,verification_json,created_at FROM b1_worker_execution_evidence WHERE project_id=$project AND worker_execution_id=$execution";
        Add(command, ("$project", project.Value.ToString()), ("$execution", executionId.ToString()));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new B1WorkerExecutionEvidence(project, new EvidenceRef(reader.GetString(0)), executionId, Guid.Parse(reader.GetString(1)), Guid.Parse(reader.GetString(2)), new AttemptRef(Guid.Parse(reader.GetString(3))), reader.GetString(4), reader.GetString(5), Parse(reader.GetString(6))) : null;
    }

    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static void Add(SqliteCommand command, params (string Name, object Value)[] values) { foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value); }
}
