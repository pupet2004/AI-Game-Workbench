using Workbench.Core.Projects;
using Workbench.Storage.Database;
using Workbench.Storage.Projects;
using Workbench.Storage.Memory;
using Workbench.Storage.Tests.Database;

namespace Workbench.Storage.Tests.Memory;

public sealed class ProjectMemoryFoundationTests
{
    [Fact]
    public async Task Candidate_round_trips_with_sources_and_is_project_scoped()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        var projects = new ProjectRepository(database);
        var a = CreateProject("A"); var b = CreateProject("B");
        await projects.UpsertAsync(a); await projects.UpsertAsync(b);
        var service = new ProjectMemoryService(new ProjectActivityRepository(database), new ProjectMemoryRepository(database));

        var candidate = await service.CreateCandidateAsync(a.Id, "Architecture", "Workbench owns project memory.", [new ProjectMemorySource("Manual", "foundation")]);

        Assert.Equal(candidate.Id, Assert.Single(await service.GetPendingCandidatesAsync(a.Id)).Id);
        Assert.Empty(await service.GetPendingCandidatesAsync(b.Id));
        Assert.Equal("Manual", Assert.Single(await service.GetSourcesAsync(candidate.Id)).SourceType);
    }

    [Fact]
    public async Task Accept_and_reject_preserve_candidate_and_formal_safety()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        var project = CreateProject("A"); await new ProjectRepository(database).UpsertAsync(project);
        var service = new ProjectMemoryService(new ProjectActivityRepository(database), new ProjectMemoryRepository(database));
        var candidate = await service.CreateCandidateAsync(project.Id, "Rule", "Original.", [new ProjectMemorySource("Manual", "one")]);

        var formal = await service.EditAndAcceptCandidateAsync(candidate.Id, "Edited.");
        await service.RejectCandidateAsync((await service.CreateCandidateAsync(project.Id, "Reject", "No.", [new ProjectMemorySource("Manual", "two")])).Id);

        Assert.Equal("Edited.", formal.Content);
        Assert.Equal("Original.", (await service.GetMemoryItemAsync(candidate.Id))!.Content);
        Assert.Equal("Superseded", (await service.GetMemoryItemAsync(candidate.Id))!.Status);
        Assert.Single(await service.GetFormalMemoriesAsync(project.Id));
        Assert.Empty(await service.GetPendingCandidatesAsync(project.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAgentMemoryAsync(project.Id, "Formal", "Bad.", "Formal", []));
    }

    [Fact]
    public async Task Bounded_activity_and_memory_content_are_rejected()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath); await database.InitializeAsync();
        var project = CreateProject("A"); await new ProjectRepository(database).UpsertAsync(project);
        var service = new ProjectMemoryService(new ProjectActivityRepository(database), new ProjectMemoryRepository(database));

        await Assert.ThrowsAsync<ArgumentException>(() => service.RecordManualActivityAsync(project.Id, "Manual", new string('a', 1001), null));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateCandidateAsync(project.Id, "Topic", new string('a', 8001), []));
    }

    private static Project CreateProject(string name) => new(Guid.NewGuid(), name, $"C:/{name}", ProjectType.Generic, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
}
