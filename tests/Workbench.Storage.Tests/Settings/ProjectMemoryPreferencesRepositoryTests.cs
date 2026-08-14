using Workbench.Core.Memory;
using Workbench.Core.Projects;
using Workbench.Storage.Database;
using Workbench.Storage.Projects;
using Workbench.Storage.Settings;
using Workbench.Storage.Tests.Database;

namespace Workbench.Storage.Tests.Settings;

public sealed class ProjectMemoryPreferencesRepositoryTests
{
    [Fact]
    public async Task Missing_preferences_return_balanced_defaults()
    {
        await using var context = await MemoryPreferencesStorageContext.CreateAsync();
        var project = await context.CreateProjectAsync();

        var preferences = await context.Preferences.GetAsync(project.Id);

        Assert.Equal(LibraryGranularityMode.Balanced, preferences.LibraryGranularity);
        Assert.Equal(ContinuityMode.Balanced, preferences.Continuity);
        Assert.Equal(TimeZoneInfo.Local.Id, preferences.TimeZoneId);
        Assert.Null(preferences.CustomInstructions);
    }

    [Fact]
    public async Task Saved_preferences_survive_repository_reconstruction()
    {
        await using var context = await MemoryPreferencesStorageContext.CreateAsync();
        var project = await context.CreateProjectAsync();
        var savedAt = DateTimeOffset.Parse("2026-08-14T12:00:00+00:00");

        await context.Preferences.SaveAsync(new ProjectMemoryPreferences(project.Id, LibraryGranularityMode.Detailed, ContinuityMode.LowToken, "Asia/Shanghai", "Keep design tradeoffs.", savedAt));

        var reopened = new ProjectMemoryPreferencesRepository(new WorkbenchDatabase(context.DatabasePath));
        var restored = await reopened.GetAsync(project.Id);
        Assert.Equal(LibraryGranularityMode.Detailed, restored.LibraryGranularity);
        Assert.Equal(ContinuityMode.LowToken, restored.Continuity);
        Assert.Equal("Asia/Shanghai", restored.TimeZoneId);
        Assert.Equal("Keep design tradeoffs.", restored.CustomInstructions);
        Assert.Equal(savedAt, restored.UpdatedAt);
    }
}

internal sealed class MemoryPreferencesStorageContext : IAsyncDisposable
{
    private readonly TemporaryDatabase _temporary;
    private MemoryPreferencesStorageContext(TemporaryDatabase temporary, WorkbenchDatabase database) { _temporary = temporary; Database = database; Projects = new ProjectRepository(database); Preferences = new ProjectMemoryPreferencesRepository(database); }
    public WorkbenchDatabase Database { get; }
    public string DatabasePath => _temporary.DatabasePath;
    public ProjectRepository Projects { get; }
    public ProjectMemoryPreferencesRepository Preferences { get; }
    public static async Task<MemoryPreferencesStorageContext> CreateAsync() { var temporary = new TemporaryDatabase(); var database = new WorkbenchDatabase(temporary.DatabasePath); await database.InitializeAsync(); return new MemoryPreferencesStorageContext(temporary, database); }
    public async Task<Project> CreateProjectAsync() { var project = new Project(Guid.NewGuid(), "Project", $"C:/Project-{Guid.NewGuid():N}", ProjectType.Generic, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow); await Projects.UpsertAsync(project); return project; }
    public ValueTask DisposeAsync() => _temporary.DisposeAsync();
}
