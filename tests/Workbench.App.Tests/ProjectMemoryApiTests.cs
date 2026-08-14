using Workbench.App.Memory;
using Workbench.Core.Memory;

namespace Workbench.App.Tests;

public sealed class ProjectMemoryApiTests
{
    [Fact]
    public async Task Daily_summary_api_writes_only_when_explicitly_called_and_does_not_touch_legacy_memory_systems()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var directory = new Support.TemporaryDirectory();
        var project = (await context.Services.ProjectOpenService.OpenAsync(directory.Path)).Project;
        var day = new DateOnly(2026, 8, 14);

        await context.Services.ProjectMemoryApi.UpsertDailySummaryAsync(new(project.Id, day, "Today", null, []), context.Time.GetUtcNow());

        Assert.Equal("Today", (await context.Services.ProjectMemoryApi.GetDailySummaryAsync(project.Id, day))!.Content);
        Assert.Single(await context.Services.ProjectMemoryApi.ListDailySummaryMetadataAsync(project.Id));
        Assert.Empty(await context.Services.ProjectLibraryRepository.BrowseAsync(project.Id));
        await using var connection = context.Services.Database.CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT COUNT(*) FROM project_memory_items), (SELECT COUNT(*) FROM project_memory_synthesis_jobs), (SELECT COUNT(*) FROM leader_messages), (SELECT COUNT(*) FROM leader_session_epochs);";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0L, reader.GetInt64(0));
        Assert.Equal(0L, reader.GetInt64(1));
        Assert.Equal(0L, reader.GetInt64(2));
        Assert.Equal(0L, reader.GetInt64(3));
    }

    [Fact]
    public async Task Memory_preferences_api_returns_and_persists_user_policy()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var directory = new Support.TemporaryDirectory();
        var project = (await context.Services.ProjectOpenService.OpenAsync(directory.Path)).Project;
        var preferences = new ProjectMemoryPreferences(project.Id, LibraryGranularityMode.Compact, ContinuityMode.HighContinuity, "Asia/Shanghai", null, context.Time.GetUtcNow());

        await context.Services.ProjectMemoryApi.SavePreferencesAsync(preferences);

        Assert.Equal(preferences, await context.Services.ProjectMemoryApi.GetPreferencesAsync(project.Id));
    }
}
