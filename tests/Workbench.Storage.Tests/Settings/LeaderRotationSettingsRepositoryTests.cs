using Microsoft.Data.Sqlite;
using Workbench.Core.Leaders;
using Workbench.Core.Projects;
using Workbench.Storage.Database;
using Workbench.Storage.Projects;
using Workbench.Storage.Settings;
using Workbench.Storage.Tests.Database;

namespace Workbench.Storage.Tests.Settings;

public sealed class LeaderRotationSettingsRepositoryTests
{
    [Fact]
    public async Task Global_rotation_policy_defaults_to_auto()
    {
        await using var context = await SettingsStorageContext.CreateAsync();

        Assert.Equal(LeaderSessionRotationPolicy.Auto, await context.WorkbenchSettings.GetLeaderSessionRotationPolicyAsync());
    }

    [Fact]
    public async Task Global_rotation_policy_round_trips_and_reopens()
    {
        await using var context = await SettingsStorageContext.CreateAsync();
        await context.WorkbenchSettings.SaveLeaderSessionRotationPolicyAsync(LeaderSessionRotationPolicy.Ask);

        var reopened = new WorkbenchSettingsRepository(new WorkbenchDatabase(context.DatabasePath));

        Assert.Equal(LeaderSessionRotationPolicy.Ask, await reopened.GetLeaderSessionRotationPolicyAsync());
    }

    [Fact]
    public async Task Project_override_defaults_to_null_and_can_be_cleared_to_inherit()
    {
        await using var context = await SettingsStorageContext.CreateAsync();
        var project = await context.CreateProjectAsync();

        Assert.Null(await context.ProjectSettings.GetLeaderSessionRotationPolicyOverrideAsync(project.Id));
        await context.ProjectSettings.SaveLeaderSessionRotationPolicyOverrideAsync(project.Id, LeaderSessionRotationPolicy.ManualOnly);
        Assert.Equal(LeaderSessionRotationPolicy.ManualOnly, await context.ProjectSettings.GetLeaderSessionRotationPolicyOverrideAsync(project.Id));

        await context.ProjectSettings.SaveLeaderSessionRotationPolicyOverrideAsync(project.Id, null);

        Assert.Null(await context.ProjectSettings.GetLeaderSessionRotationPolicyOverrideAsync(project.Id));
    }

    [Fact]
    public async Task Project_delete_cascades_project_settings()
    {
        await using var context = await SettingsStorageContext.CreateAsync();
        var project = await context.CreateProjectAsync();
        await context.ProjectSettings.SaveLeaderSessionRotationPolicyOverrideAsync(project.Id, LeaderSessionRotationPolicy.Ask);

        await context.Projects.RemoveAsync(project.Id);

        await using var connection = context.Database.CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM project_settings WHERE project_id = $projectId;";
        command.Parameters.AddWithValue("$projectId", project.Id.ToString());
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Unknown_global_policy_is_rejected()
    {
        await using var context = await SettingsStorageContext.CreateAsync();
        await InsertRawAsync(context.Database, "INSERT INTO workbench_settings (key, value) VALUES ('leader_session_rotation_policy', 'banana');");

        await Assert.ThrowsAsync<InvalidDataException>(() => context.WorkbenchSettings.GetLeaderSessionRotationPolicyAsync());
    }

    [Fact]
    public async Task Unknown_project_policy_is_rejected()
    {
        await using var context = await SettingsStorageContext.CreateAsync();
        var project = await context.CreateProjectAsync();
        await InsertRawAsync(context.Database, $"INSERT INTO project_settings (project_id, leader_session_rotation_policy) VALUES ('{project.Id}', 'banana');");

        await Assert.ThrowsAsync<InvalidDataException>(() => context.ProjectSettings.GetLeaderSessionRotationPolicyOverrideAsync(project.Id));
    }

    private static async Task InsertRawAsync(WorkbenchDatabase database, string sql)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}

internal sealed class SettingsStorageContext : IAsyncDisposable
{
    private readonly TemporaryDatabase _temporary;

    private SettingsStorageContext(TemporaryDatabase temporary, WorkbenchDatabase database)
    {
        _temporary = temporary;
        Database = database;
        Projects = new ProjectRepository(database);
        WorkbenchSettings = new WorkbenchSettingsRepository(database);
        ProjectSettings = new ProjectSettingsRepository(database);
    }

    public WorkbenchDatabase Database { get; }
    public string DatabasePath => _temporary.DatabasePath;
    public ProjectRepository Projects { get; }
    public WorkbenchSettingsRepository WorkbenchSettings { get; }
    public ProjectSettingsRepository ProjectSettings { get; }

    public static async Task<SettingsStorageContext> CreateAsync()
    {
        var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        return new SettingsStorageContext(temporary, database);
    }

    public async Task<Project> CreateProjectAsync()
    {
        var project = new Project(Guid.NewGuid(), "Project", $"C:/Projects/{Guid.NewGuid():N}", ProjectType.Generic, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        await Projects.UpsertAsync(project);
        return project;
    }

    public ValueTask DisposeAsync() => _temporary.DisposeAsync();
}
