using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Workbench.Core.Tasks;
using Workbench.Storage.Database;

namespace Workbench.Storage.Tasks;

public sealed record StoredTask(Guid TaskId, Guid ProjectId, string Title, TaskLifecycleStatus Status, Guid CurrentRevisionId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? CancelledAt);

public sealed class TaskRepository(WorkbenchDatabase database)
{
    private readonly WorkbenchDatabase _database = database;
    public async Task CreateAsync(Guid projectId, TaskDraft task, CancellationToken ct = default)
    {
        await using var c = _database.CreateConnection(); await c.OpenAsync(ct); await using var tx = await c.BeginTransactionAsync(ct);
        var r = task.CurrentRevision; var cmd = c.CreateCommand(); cmd.Transaction = (SqliteTransaction)tx;
        cmd.CommandText = "INSERT INTO tasks(id,project_id,title,status,current_revision_id,created_at,updated_at) VALUES($tid,$pid,$title,$status,$rid,$at,$at); INSERT INTO task_revisions(id,task_id,revision_number,goal,scope,out_of_scope,acceptance_json,risk_level,recommended_provider_id,recommended_provider_account_id,recommended_model_profile_id,recommended_agent_runtime_id,change_reason,approved_by,created_at) VALUES($rid,$tid,$rn,$goal,$scope,$out,$acc,$risk,$p,$pa,$m,$a,$reason,$approved,$at);";
        cmd.Parameters.AddWithValue("$rid", r.Id.ToString()); cmd.Parameters.AddWithValue("$tid", task.TaskId.ToString()); cmd.Parameters.AddWithValue("$rn", r.RevisionNumber); cmd.Parameters.AddWithValue("$goal", r.Goal); cmd.Parameters.AddWithValue("$scope", r.Scope); cmd.Parameters.AddWithValue("$out", r.OutOfScope); cmd.Parameters.AddWithValue("$acc", JsonSerializer.Serialize(r.Acceptance)); cmd.Parameters.AddWithValue("$risk", r.RiskLevel.ToString()); cmd.Parameters.AddWithValue("$p", r.RecommendedExecutionProfile.ProviderId); cmd.Parameters.AddWithValue("$pa", r.RecommendedExecutionProfile.ProviderAccountId); cmd.Parameters.AddWithValue("$m", r.RecommendedExecutionProfile.ModelProfileId); cmd.Parameters.AddWithValue("$a", r.RecommendedExecutionProfile.AgentRuntimeId); cmd.Parameters.AddWithValue("$reason", r.ChangeReason); cmd.Parameters.AddWithValue("$approved", r.ApprovedBy.ToString()); cmd.Parameters.AddWithValue("$at", r.CreatedAt.ToString("O", CultureInfo.InvariantCulture)); cmd.Parameters.AddWithValue("$pid", projectId.ToString()); cmd.Parameters.AddWithValue("$title", task.Title); cmd.Parameters.AddWithValue("$status", task.Status.ToString()); await cmd.ExecuteNonQueryAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task<StoredTask?> GetAsync(Guid projectId, Guid taskId, CancellationToken ct = default)
    { await using var c = _database.CreateConnection(); await c.OpenAsync(ct); var q=c.CreateCommand(); q.CommandText="SELECT id,project_id,title,status,current_revision_id,created_at,updated_at,cancelled_at FROM tasks WHERE project_id=$p AND id=$t"; q.Parameters.AddWithValue("$p",projectId.ToString()); q.Parameters.AddWithValue("$t",taskId.ToString()); await using var rd=await q.ExecuteReaderAsync(ct); return await rd.ReadAsync(ct)?new StoredTask(Guid.Parse(rd.GetString(0)),Guid.Parse(rd.GetString(1)),rd.GetString(2),Enum.Parse<TaskLifecycleStatus>(rd.GetString(3)),Guid.Parse(rd.GetString(4)),DateTimeOffset.Parse(rd.GetString(5)),DateTimeOffset.Parse(rd.GetString(6)),rd.IsDBNull(7)?null:DateTimeOffset.Parse(rd.GetString(7))):null; }
    public async Task UpdateStatusAsync(Guid projectId, Guid taskId, TaskLifecycleStatus status, CancellationToken ct=default){await using var c=_database.CreateConnection();await c.OpenAsync(ct);var q=c.CreateCommand();q.CommandText="UPDATE tasks SET status=$s,updated_at=$at,cancelled_at=$ca WHERE project_id=$p AND id=$t";q.Parameters.AddWithValue("$s",status.ToString());q.Parameters.AddWithValue("$at",DateTimeOffset.UtcNow.ToString("O"));q.Parameters.AddWithValue("$ca",status==TaskLifecycleStatus.Cancelled?DateTimeOffset.UtcNow.ToString("O"):(object)DBNull.Value);q.Parameters.AddWithValue("$p",projectId.ToString());q.Parameters.AddWithValue("$t",taskId.ToString());await q.ExecuteNonQueryAsync(ct);}
}
