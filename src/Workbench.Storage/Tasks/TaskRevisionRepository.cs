using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Workbench.Core.Tasks;
using Workbench.Storage.Database;

namespace Workbench.Storage.Tasks;

public sealed class TaskRevisionRepository(WorkbenchDatabase database)
{
    private readonly WorkbenchDatabase _database = database;
    public async Task CreateAsync(TaskRevision revision, CancellationToken ct=default)
    {
        await using var c=_database.CreateConnection(); await c.OpenAsync(ct); var q=c.CreateCommand();
        q.CommandText="INSERT INTO task_revisions(id,task_id,revision_number,goal,scope,out_of_scope,acceptance_json,risk_level,recommended_provider_id,recommended_provider_account_id,recommended_model_profile_id,recommended_agent_runtime_id,change_reason,approved_by,created_at,previous_revision_id) VALUES($id,$task,$num,$goal,$scope,$out,$acc,$risk,$p,$pa,$m,$a,$reason,$approved,$created,$prev)";
        q.Parameters.AddWithValue("$id",revision.Id.ToString()); q.Parameters.AddWithValue("$task",revision.TaskId.ToString()); q.Parameters.AddWithValue("$num",revision.RevisionNumber); q.Parameters.AddWithValue("$goal",revision.Goal); q.Parameters.AddWithValue("$scope",revision.Scope); q.Parameters.AddWithValue("$out",revision.OutOfScope); q.Parameters.AddWithValue("$acc",JsonSerializer.Serialize(revision.Acceptance)); q.Parameters.AddWithValue("$risk",revision.RiskLevel.ToString()); q.Parameters.AddWithValue("$p",revision.RecommendedExecutionProfile.ProviderId); q.Parameters.AddWithValue("$pa",revision.RecommendedExecutionProfile.ProviderAccountId); q.Parameters.AddWithValue("$m",revision.RecommendedExecutionProfile.ModelProfileId); q.Parameters.AddWithValue("$a",revision.RecommendedExecutionProfile.AgentRuntimeId); q.Parameters.AddWithValue("$reason",revision.ChangeReason); q.Parameters.AddWithValue("$approved",revision.ApprovedBy.ToString()); q.Parameters.AddWithValue("$created",revision.CreatedAt.ToString("O",CultureInfo.InvariantCulture)); q.Parameters.AddWithValue("$prev",(object?)revision.PreviousRevisionId?.ToString()??DBNull.Value); await q.ExecuteNonQueryAsync(ct);
    }
    public async Task<IReadOnlyList<TaskRevision>> ListAsync(Guid taskId,CancellationToken ct=default){await using var c=_database.CreateConnection();await c.OpenAsync(ct);var q=c.CreateCommand();q.CommandText="SELECT id,task_id,revision_number,goal,scope,out_of_scope,acceptance_json,risk_level,recommended_provider_id,recommended_provider_account_id,recommended_model_profile_id,recommended_agent_runtime_id,change_reason,approved_by,created_at,previous_revision_id FROM task_revisions WHERE task_id=$t ORDER BY revision_number";q.Parameters.AddWithValue("$t",taskId.ToString());await using var r=await q.ExecuteReaderAsync(ct);var list=new List<TaskRevision>();while(await r.ReadAsync(ct))list.Add(Read(r));return list;}
    private static TaskRevision Read(SqliteDataReader r){var p=ExecutionProfile.Create(r.GetString(8),r.GetString(9),r.GetString(10),r.GetString(11));var x=new TaskRevision(Guid.Parse(r.GetString(1)),r.GetInt32(2),r.GetString(3),r.GetString(4),r.GetString(5),JsonSerializer.Deserialize<string[]>(r.GetString(6))!,Enum.Parse<TaskRiskLevel>(r.GetString(7)),p,r.GetString(12),Enum.Parse<TaskRevisionApprover>(r.GetString(13)),DateTimeOffset.Parse(r.GetString(14)),r.IsDBNull(15)?null:Guid.Parse(r.GetString(15)));var f=typeof(TaskRevision).GetField("<Id>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic);f?.SetValue(x,Guid.Parse(r.GetString(0)));return x;}
}
