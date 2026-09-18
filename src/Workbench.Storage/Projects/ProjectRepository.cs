using System.Globalization;
using Microsoft.Data.Sqlite;
using Workbench.Core.Projects;
using Workbench.Storage.Database;

namespace Workbench.Storage.Projects;

public sealed class ProjectRepository
{
    private readonly WorkbenchDatabase _database;

    public ProjectRepository(WorkbenchDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public async Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, root_path, project_type, git_root, created_at, last_opened_at
            FROM projects
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadProject(reader) : null;
    }

    public async Task<Project?> GetByRootPathAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, root_path, project_type, git_root, created_at, last_opened_at
            FROM projects
            WHERE root_path = $rootPath COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$rootPath", rootPath);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadProject(reader) : null;
    }

    public async Task<IReadOnlyList<Project>> GetRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be greater than zero.");
        }

        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, root_path, project_type, git_root, created_at, last_opened_at
            FROM projects
            WHERE is_visible = 1
            ORDER BY last_opened_at DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var projects = new List<Project>();
        while (await reader.ReadAsync(cancellationToken))
        {
            projects.Add(ReadProject(reader));
        }

        return projects;
    }

    public async Task UpsertAsync(Project project, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO projects (id, name, root_path, project_type, git_root, created_at, last_opened_at, is_visible)
            VALUES ($id, $name, $rootPath, $projectType, $gitRoot, $createdAt, $lastOpenedAt, 1)
            ON CONFLICT(id) DO UPDATE SET
                name = excluded.name,
                root_path = excluded.root_path,
                project_type = excluded.project_type,
                git_root = excluded.git_root,
                created_at = excluded.created_at,
                last_opened_at = excluded.last_opened_at,
                is_visible = 1;
            """;
        AddProjectParameters(command, project);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE projects
            SET is_visible = 0, setup_required = 1
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> IsSetupRequiredAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT setup_required FROM projects WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is not null && Convert.ToInt64(value) != 0;
    }

    public async Task CompleteSetupAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE projects
            SET is_visible = 1, setup_required = 0
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddProjectParameters(SqliteCommand command, Project project)
    {
        command.Parameters.AddWithValue("$id", project.Id.ToString());
        command.Parameters.AddWithValue("$name", project.Name);
        command.Parameters.AddWithValue("$rootPath", project.RootPath);
        command.Parameters.AddWithValue("$projectType", (int)project.Type);
        command.Parameters.AddWithValue("$gitRoot", (object?)project.GitRoot ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", project.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$lastOpenedAt", project.LastOpenedAt.ToString("O", CultureInfo.InvariantCulture));
    }

    private static Project ReadProject(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            (ProjectType)reader.GetInt32(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
}
