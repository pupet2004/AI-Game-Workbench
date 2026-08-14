using Workbench.Core.Projects;
using Workbench.Storage.Database;
using Workbench.Storage.Memory;
using Workbench.Storage.Projects;
using Workbench.Storage.Tests.Database;

namespace Workbench.Storage.Tests.Memory;

public sealed class DailySummaryRepositoryTests
{
    [Fact]
    public async Task Same_project_and_local_date_is_one_mutable_document_with_preserved_creation_time()
    {
        await using var context = await DailySummaryStorageContext.CreateAsync();
        var project = await context.CreateProjectAsync("A");
        var day = new DateOnly(2026, 8, 14);
        var createdAt = DateTimeOffset.Parse("2026-08-14T01:00:00+00:00");
        var updatedAt = createdAt.AddHours(2);

        var created = await context.Summaries.SaveAsync(new(project.Id, day, "First", null, []), createdAt);
        var revised = await context.Summaries.SaveAsync(new(project.Id, day, "Compressed", created.Revision, [new("LeaderEpoch", "epoch-1")]), updatedAt);

        Assert.Equal(new DailySummarySourceReference("LeaderEpoch", "epoch-1"), Assert.Single(Assert.Single(await context.Summaries.ListAsync(project.Id)).Sources));
        Assert.Equal(2, revised.Revision);
        Assert.Equal("Compressed", revised.Content);
        Assert.Equal(createdAt, revised.CreatedAt);
        Assert.Equal(updatedAt, revised.UpdatedAt);
        Assert.Equal(new DailySummarySourceReference("LeaderEpoch", "epoch-1"), Assert.Single(revised.Sources));
    }

    [Fact]
    public async Task Different_days_and_projects_are_isolated_and_list_newest_first()
    {
        await using var context = await DailySummaryStorageContext.CreateAsync();
        var projectA = await context.CreateProjectAsync("A");
        var projectB = await context.CreateProjectAsync("B");
        var older = new DateOnly(2026, 8, 13);
        var newer = new DateOnly(2026, 8, 14);

        await context.Summaries.SaveAsync(new(projectA.Id, older, "Yesterday", null, []), DateTimeOffset.Parse("2026-08-13T10:00:00+00:00"));
        await context.Summaries.SaveAsync(new(projectA.Id, newer, "Today", null, []), DateTimeOffset.Parse("2026-08-14T10:00:00+00:00"));
        await context.Summaries.SaveAsync(new(projectB.Id, newer, "Other project", null, []), DateTimeOffset.Parse("2026-08-14T11:00:00+00:00"));

        Assert.Equal([newer, older], (await context.Summaries.ListMetadataAsync(projectA.Id)).Select(item => item.LocalDate));
        Assert.Equal("Today", (await context.Summaries.GetAsync(projectA.Id, newer))!.Content);
        Assert.Null(await context.Summaries.GetAsync(projectB.Id, older));
        Assert.Equal("Other project", (await context.Summaries.GetAsync(projectB.Id, newer))!.Content);
    }

    [Fact]
    public async Task Summary_survives_repository_reconstruction_and_stale_revision_writes_nothing()
    {
        await using var context = await DailySummaryStorageContext.CreateAsync();
        var project = await context.CreateProjectAsync("A");
        var day = new DateOnly(2026, 8, 14);
        await context.Summaries.SaveAsync(new(project.Id, day, "Original", null, []), DateTimeOffset.Parse("2026-08-14T10:00:00+00:00"));

        await Assert.ThrowsAsync<MemoryRevisionConflictException>(() => context.Summaries.SaveAsync(new(project.Id, day, "Stale", 0, []), DateTimeOffset.Parse("2026-08-14T11:00:00+00:00")));

        var reopened = new DailySummaryRepository(new WorkbenchDatabase(context.DatabasePath));
        var restored = await reopened.GetAsync(project.Id, day);
        Assert.NotNull(restored);
        Assert.Equal("Original", restored.Content);
        Assert.Equal(1, restored.Revision);
    }
}

internal sealed class DailySummaryStorageContext : IAsyncDisposable
{
    private readonly TemporaryDatabase _temporary;
    private DailySummaryStorageContext(TemporaryDatabase temporary, WorkbenchDatabase database)
    {
        _temporary = temporary;
        Database = database;
        Projects = new ProjectRepository(database);
        Summaries = new DailySummaryRepository(database);
    }

    public WorkbenchDatabase Database { get; }
    public string DatabasePath => _temporary.DatabasePath;
    public ProjectRepository Projects { get; }
    public DailySummaryRepository Summaries { get; }
    public static async Task<DailySummaryStorageContext> CreateAsync()
    {
        var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        return new DailySummaryStorageContext(temporary, database);
    }
    public async Task<Project> CreateProjectAsync(string name) { var project = new Project(Guid.NewGuid(), name, $"C:/{name}-{Guid.NewGuid():N}", ProjectType.Generic, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow); await Projects.UpsertAsync(project); return project; }
    public ValueTask DisposeAsync() => _temporary.DisposeAsync();
}
