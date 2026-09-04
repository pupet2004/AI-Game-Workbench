using Microsoft.Data.Sqlite;
using Workbench.Core.Tasks;
using Workbench.Core.Workers;
using Workbench.Storage.Database;

namespace Workbench.Storage.Workers;

public sealed record StoredWorkerExecution(
    Guid ExecutionId, Guid ProjectId, Guid TaskId, TaskRevisionReference ExecutionStartRevision,
    TaskRevisionReference CurrentAcknowledgedRevision, string BaseCommit, string TargetBranch,
    ProviderAccountBinding ProviderAccount, ExecutionProfile ExecutionProfile, string WorkerBranch,
    string WorkerWorktreePath, WorkerExecutionState State, string? AgentSessionId,
    string? ExternalSessionId, string? WorkingDirectory, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    string? WorkspaceBaselineJson = null);

public sealed class WorkerExecutionRepository(WorkbenchDatabase database)
{
    private readonly WorkbenchDatabase _database = database;

    public async Task CreateAsync(StoredWorkerExecution execution, CancellationToken cancellationToken = default)
    {
        if (execution.State == WorkerExecutionState.Running &&
            (string.IsNullOrWhiteSpace(execution.AgentSessionId) || string.IsNullOrWhiteSpace(execution.WorkingDirectory)))
            throw new InvalidOperationException("Running execution requires session identity and working directory.");

        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO worker_executions(
                id, project_id, task_id, execution_start_revision_id, execution_start_revision_number,
                current_ack_revision_id, current_ack_revision_number, base_commit, target_branch,
                provider_id, provider_account_id, model_profile_id, agent_runtime_id,
                worker_branch, worker_worktree_path, state, agent_session_id, external_session_id,
                working_directory, created_at, updated_at, workspace_baseline_json)
            VALUES(
                $id, $project, $task, $start_revision, $start_number,
                $current_revision, $current_number, $base, $target,
                $provider, $account, $model, $runtime,
                $branch, $worktree, $state, $agent, $external,
                $working, $created, $updated, $baseline);
            """;
        command.Parameters.AddWithValue("$id", execution.ExecutionId.ToString());
        command.Parameters.AddWithValue("$project", execution.ProjectId.ToString());
        command.Parameters.AddWithValue("$task", execution.TaskId.ToString());
        command.Parameters.AddWithValue("$start_revision", execution.ExecutionStartRevision.RevisionId.ToString());
        command.Parameters.AddWithValue("$start_number", execution.ExecutionStartRevision.RevisionNumber);
        command.Parameters.AddWithValue("$current_revision", execution.CurrentAcknowledgedRevision.RevisionId.ToString());
        command.Parameters.AddWithValue("$current_number", execution.CurrentAcknowledgedRevision.RevisionNumber);
        command.Parameters.AddWithValue("$base", execution.BaseCommit);
        command.Parameters.AddWithValue("$target", execution.TargetBranch);
        command.Parameters.AddWithValue("$provider", execution.ProviderAccount.ProviderId);
        command.Parameters.AddWithValue("$account", execution.ProviderAccount.AccountId);
        command.Parameters.AddWithValue("$model", execution.ExecutionProfile.ModelProfileId);
        command.Parameters.AddWithValue("$runtime", execution.ExecutionProfile.AgentRuntimeId);
        command.Parameters.AddWithValue("$branch", execution.WorkerBranch);
        command.Parameters.AddWithValue("$worktree", execution.WorkerWorktreePath);
        command.Parameters.AddWithValue("$state", execution.State.ToString());
        command.Parameters.AddWithValue("$agent", (object?)execution.AgentSessionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$external", (object?)execution.ExternalSessionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$working", (object?)execution.WorkingDirectory ?? DBNull.Value);
        command.Parameters.AddWithValue("$created", execution.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updated", execution.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$baseline", (object?)execution.WorkspaceBaselineJson ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task<StoredWorkerExecution?> GetAsync(Guid projectId, Guid executionId, CancellationToken cancellationToken = default) =>
        ReadOneAsync("WHERE project_id=$project AND id=$id", command =>
        {
            command.Parameters.AddWithValue("$project", projectId.ToString());
            command.Parameters.AddWithValue("$id", executionId.ToString());
        }, cancellationToken);

    public Task<StoredWorkerExecution?> GetByAgentSessionIdAsync(Guid projectId, Guid taskId, Guid agentSessionId, CancellationToken cancellationToken = default) =>
        ReadOneAsync("WHERE project_id=$project AND task_id=$task AND agent_session_id=$agent", command =>
        {
            command.Parameters.AddWithValue("$project", projectId.ToString());
            command.Parameters.AddWithValue("$task", taskId.ToString());
            command.Parameters.AddWithValue("$agent", agentSessionId.ToString());
        }, cancellationToken);

    public async Task<IReadOnlyList<StoredWorkerExecution>> ListAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = SelectSql + " WHERE project_id=$project ORDER BY created_at, id";
        command.Parameters.AddWithValue("$project", projectId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<StoredWorkerExecution>();
        while (await reader.ReadAsync(cancellationToken)) result.Add(Read(reader));
        return result;
    }

    public async Task UpdateStateAsync(Guid projectId, Guid taskId, Guid executionId, WorkerExecutionState state, CancellationToken cancellationToken = default)
    {
        var current = await GetAsync(projectId, executionId, cancellationToken) ?? throw new KeyNotFoundException("Worker execution was not found.");
        if (current.TaskId != taskId) throw new InvalidOperationException("Execution task mismatch.");
        if (state == WorkerExecutionState.Running &&
            (string.IsNullOrWhiteSpace(current.AgentSessionId) || string.IsNullOrWhiteSpace(current.WorkingDirectory) ||
             !SamePath(current.WorkingDirectory, current.WorkerWorktreePath)))
            throw new InvalidOperationException("Running execution requires matching session identity and worktree.");

        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE worker_executions SET state=$state, updated_at=$updated WHERE project_id=$project AND task_id=$task AND id=$id";
        command.Parameters.AddWithValue("$state", state.ToString());
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$project", projectId.ToString());
        command.Parameters.AddWithValue("$task", taskId.ToString());
        command.Parameters.AddWithValue("$id", executionId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task PersistSessionIdentityAsync(Guid projectId, Guid taskId, Guid executionId, string? agentSessionId, string? externalSessionId, string workingDirectory, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory)) throw new ArgumentException("Working directory required", nameof(workingDirectory));
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE worker_executions SET agent_session_id=$agent, external_session_id=$external, working_directory=$working, updated_at=$updated WHERE project_id=$project AND task_id=$task AND id=$id";
        command.Parameters.AddWithValue("$agent", (object?)agentSessionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$external", (object?)externalSessionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$working", workingDirectory);
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$project", projectId.ToString());
        command.Parameters.AddWithValue("$task", taskId.ToString());
        command.Parameters.AddWithValue("$id", executionId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> AcknowledgeAsync(Guid executionId, Guid taskId, int expected, int target, Guid revisionId, CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE worker_executions SET current_ack_revision_id=$revision, current_ack_revision_number=$number, updated_at=$updated WHERE id=$id AND task_id=$task AND current_ack_revision_number=$expected AND $number>current_ack_revision_number AND EXISTS(SELECT 1 FROM task_revisions r WHERE r.id=$revision AND r.task_id=$task AND r.revision_number=$number)";
        command.Parameters.AddWithValue("$revision", revisionId.ToString());
        command.Parameters.AddWithValue("$number", target);
        command.Parameters.AddWithValue("$expected", expected);
        command.Parameters.AddWithValue("$id", executionId.ToString());
        command.Parameters.AddWithValue("$task", taskId.ToString());
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> AdvanceAcknowledgedRevisionAsync(Guid projectId, Guid taskId, Guid executionId, int expectedCurrentRevision, Guid targetRevision, int targetRevisionNumber, CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "UPDATE worker_executions SET current_ack_revision_id=$target, current_ack_revision_number=$targetNumber, updated_at=$updated WHERE project_id=$project AND task_id=$task AND id=$execution AND current_ack_revision_number=$expected AND $targetNumber > current_ack_revision_number AND EXISTS(SELECT 1 FROM task_revisions revision WHERE revision.id=$target AND revision.task_id=$task AND revision.revision_number=$targetNumber)";
        command.Parameters.AddWithValue("$target", targetRevision.ToString());
        command.Parameters.AddWithValue("$targetNumber", targetRevisionNumber);
        command.Parameters.AddWithValue("$expected", expectedCurrentRevision);
        command.Parameters.AddWithValue("$project", projectId.ToString());
        command.Parameters.AddWithValue("$task", taskId.ToString());
        command.Parameters.AddWithValue("$execution", executionId.ToString());
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        var changed = await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        await transaction.CommitAsync(cancellationToken);
        return changed;
    }

    private async Task<StoredWorkerExecution?> ReadOneAsync(string predicate, Action<SqliteCommand> bind, CancellationToken cancellationToken)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = SelectSql + " " + predicate;
        bind(command);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    private const string SelectSql = "SELECT id, project_id, task_id, execution_start_revision_id, execution_start_revision_number, current_ack_revision_id, current_ack_revision_number, base_commit, target_branch, provider_id, provider_account_id, model_profile_id, agent_runtime_id, worker_branch, worker_worktree_path, state, agent_session_id, external_session_id, working_directory, created_at, updated_at, workspace_baseline_json FROM worker_executions";

    private static StoredWorkerExecution Read(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), Guid.Parse(reader.GetString(2)),
        new TaskRevisionReference(Guid.Parse(reader.GetString(2)), Guid.Parse(reader.GetString(3)), reader.GetInt32(4)),
        new TaskRevisionReference(Guid.Parse(reader.GetString(2)), Guid.Parse(reader.GetString(5)), reader.GetInt32(6)),
        reader.GetString(7), reader.GetString(8), ProviderAccountBinding.Create(reader.GetString(9), reader.GetString(10)),
        ExecutionProfile.Create(reader.GetString(9), reader.GetString(10), reader.GetString(11), reader.GetString(12)),
        reader.GetString(13), reader.GetString(14), Enum.Parse<WorkerExecutionState>(reader.GetString(15)),
        reader.IsDBNull(16) ? null : reader.GetString(16), reader.IsDBNull(17) ? null : reader.GetString(17),
        reader.IsDBNull(18) ? null : reader.GetString(18), DateTimeOffset.Parse(reader.GetString(19)), DateTimeOffset.Parse(reader.GetString(20)),
        reader.IsDBNull(21) ? null : reader.GetString(21));

    private static bool SamePath(string first, string second)
    {
        var left = Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var right = Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return OperatingSystem.IsWindows() ? string.Equals(left, right, StringComparison.OrdinalIgnoreCase) : string.Equals(left, right, StringComparison.Ordinal);
    }
}
