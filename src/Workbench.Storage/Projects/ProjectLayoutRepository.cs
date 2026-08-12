using System.Globalization;
using Microsoft.Data.Sqlite;
using Workbench.Core.Layout;
using Workbench.Storage.Database;

namespace Workbench.Storage.Projects;

public sealed class ProjectLayoutRepository
{
    private readonly WorkbenchDatabase _database;

    public ProjectLayoutRepository(WorkbenchDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public async Task<ProjectLayout?> GetAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT project_id, leader_width, work_width, library_width, focused_pane, updated_at
            FROM project_layouts
            WHERE project_id = $projectId;
            """;
        command.Parameters.AddWithValue("$projectId", projectId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadLayout(reader) : null;
    }

    public async Task SaveAsync(ProjectLayout layout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layout);

        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO project_layouts (project_id, leader_width, work_width, library_width, focused_pane, updated_at)
            VALUES ($projectId, $leaderWidth, $workWidth, $libraryWidth, $focusedPane, $updatedAt)
            ON CONFLICT(project_id) DO UPDATE SET
                leader_width = excluded.leader_width,
                work_width = excluded.work_width,
                library_width = excluded.library_width,
                focused_pane = excluded.focused_pane,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$projectId", layout.ProjectId.ToString());
        command.Parameters.AddWithValue("$leaderWidth", layout.LeaderWidth);
        command.Parameters.AddWithValue("$workWidth", layout.WorkWidth);
        command.Parameters.AddWithValue("$libraryWidth", layout.LibraryWidth);
        command.Parameters.AddWithValue("$focusedPane", (int)layout.FocusedPane);
        command.Parameters.AddWithValue("$updatedAt", layout.UpdatedAt.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static ProjectLayout ReadLayout(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            reader.GetDouble(1),
            reader.GetDouble(2),
            reader.GetDouble(3),
            (WorkspacePane)reader.GetInt32(4),
            DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
}
