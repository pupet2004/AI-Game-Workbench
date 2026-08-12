using System.Globalization;
using Workbench.Storage.Database;

namespace Workbench.Storage.Memory;

public sealed class ProjectActivityRepository(WorkbenchDatabase database)
{
    private readonly WorkbenchDatabase _database = database;
    public async Task AddAsync(ProjectActivityEvent item, CancellationToken cancellationToken = default)
    {
        await using var c = _database.CreateConnection(); await c.OpenAsync(cancellationToken); var q = c.CreateCommand();
        q.CommandText = "INSERT INTO project_activity_events VALUES ($id,$project,$type,$summary,$sourceType,$sourceRef,$occurred,$created);";
        q.Parameters.AddWithValue("$id", item.Id.ToString()); q.Parameters.AddWithValue("$project", item.ProjectId.ToString()); q.Parameters.AddWithValue("$type", item.EventType); q.Parameters.AddWithValue("$summary", item.Summary); q.Parameters.AddWithValue("$sourceType", item.SourceType); q.Parameters.AddWithValue("$sourceRef", (object?)item.SourceRef ?? DBNull.Value); q.Parameters.AddWithValue("$occurred", item.OccurredAt.ToString("O", CultureInfo.InvariantCulture)); q.Parameters.AddWithValue("$created", item.CreatedAt.ToString("O", CultureInfo.InvariantCulture)); await q.ExecuteNonQueryAsync(cancellationToken);
    }
}
