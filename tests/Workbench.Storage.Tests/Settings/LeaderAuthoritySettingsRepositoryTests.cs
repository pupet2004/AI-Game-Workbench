using Microsoft.Data.Sqlite;
using Workbench.Core.Leaders;
using Workbench.Storage.Database;
using Workbench.Storage.Settings;

namespace Workbench.Storage.Tests.Settings;

public sealed class LeaderAuthoritySettingsRepositoryTests
{
    [Fact]
    public async Task Fresh_settings_default_to_balanced_and_projects_inherit_global()
    {
        await using var context = await SettingsStorageContext.CreateAsync();
        var project = await context.CreateProjectAsync();

        Assert.Equal(LeaderAuthorityMode.Balanced, await context.WorkbenchSettings.GetLeaderAuthorityModeAsync());
        Assert.Null(await context.ProjectSettings.GetLeaderAuthorityModeOverrideAsync(project.Id));
        Assert.Equal(LeaderAuthorityMode.Balanced, await ResolveAsync(context, project.Id));
    }

    [Theory]
    [InlineData(LeaderAuthorityMode.Cautious)]
    [InlineData(LeaderAuthorityMode.Autonomous)]
    public async Task Global_authority_round_trips_and_changes_inheriting_projects(LeaderAuthorityMode authority)
    {
        await using var context = await SettingsStorageContext.CreateAsync();
        var project = await context.CreateProjectAsync();
        await context.WorkbenchSettings.SaveLeaderAuthorityModeAsync(authority);

        Assert.Equal(authority, await ResolveAsync(context, project.Id));
        var reopened = new WorkbenchSettingsRepository(new WorkbenchDatabase(context.DatabasePath));
        Assert.Equal(authority, await reopened.GetLeaderAuthorityModeAsync());
    }

    [Fact]
    public async Task Project_overrides_are_isolated_persisted_and_clear_back_to_global()
    {
        await using var context = await SettingsStorageContext.CreateAsync();
        var projectA = await context.CreateProjectAsync();
        var projectB = await context.CreateProjectAsync();
        await context.WorkbenchSettings.SaveLeaderAuthorityModeAsync(LeaderAuthorityMode.Cautious);
        await context.ProjectSettings.SaveLeaderAuthorityModeOverrideAsync(projectA.Id, LeaderAuthorityMode.Autonomous);

        Assert.Equal(LeaderAuthorityMode.Autonomous, await ResolveAsync(context, projectA.Id));
        Assert.Equal(LeaderAuthorityMode.Cautious, await ResolveAsync(context, projectB.Id));
        await context.WorkbenchSettings.SaveLeaderAuthorityModeAsync(LeaderAuthorityMode.Balanced);
        Assert.Equal(LeaderAuthorityMode.Autonomous, await ResolveAsync(context, projectA.Id));
        Assert.Equal(LeaderAuthorityMode.Balanced, await ResolveAsync(context, projectB.Id));

        var reopened = new ProjectSettingsRepository(new WorkbenchDatabase(context.DatabasePath));
        Assert.Equal(LeaderAuthorityMode.Autonomous, await reopened.GetLeaderAuthorityModeOverrideAsync(projectA.Id));
        await reopened.SaveLeaderAuthorityModeOverrideAsync(projectA.Id, null);
        var reopenedAfterClear = new ProjectSettingsRepository(new WorkbenchDatabase(context.DatabasePath));
        Assert.Null(await reopenedAfterClear.GetLeaderAuthorityModeOverrideAsync(projectA.Id));
        Assert.Equal(LeaderAuthorityMode.Balanced, await ResolveAsync(context, projectA.Id));
    }

    [Fact]
    public async Task Invalid_values_are_rejected_and_invalid_project_does_not_create_settings()
    {
        await using var context = await SettingsStorageContext.CreateAsync();
        await InsertRawAsync(context.Database, "INSERT INTO workbench_settings (key, value) VALUES ('leader_authority_mode', 'banana');");
        await Assert.ThrowsAsync<InvalidDataException>(() => context.WorkbenchSettings.GetLeaderAuthorityModeAsync());

        var unknownProject = Guid.NewGuid();
        await Assert.ThrowsAsync<SqliteException>(() => context.ProjectSettings.SaveLeaderAuthorityModeOverrideAsync(unknownProject, LeaderAuthorityMode.Cautious));
        await using var connection = context.Database.CreateConnection(); await connection.OpenAsync();
        var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM project_settings WHERE project_id=$projectId;"; command.Parameters.AddWithValue("$projectId", unknownProject.ToString());
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }

    private static Task<LeaderAuthorityMode> ResolveAsync(SettingsStorageContext context, Guid projectId) =>
        new LeaderAuthoritySettingsService(context.WorkbenchSettings, context.ProjectSettings).GetEffectiveLeaderAuthorityModeAsync(projectId);

    private static async Task InsertRawAsync(WorkbenchDatabase database, string sql)
    {
        await using var connection = database.CreateConnection(); await connection.OpenAsync(); var command = connection.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync();
    }
}
