using Workbench.Core.Projects;
using Workbench.Core.Tasks;
using Workbench.Storage.Database;
using Workbench.Storage.Memory;
using Workbench.Storage.Projects;
using Workbench.Storage.Tasks;
using Workbench.Storage.Tests.Database;

namespace Workbench.Storage.Tests.Memory;

public sealed class ProjectLibraryRepositoryTests
{
    [Fact]
    public async Task Leader_and_worker_submissions_round_trip_idempotently_and_are_project_scoped()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        var projects = new ProjectRepository(database);
        var projectA = Project("A");
        var projectB = Project("B");
        await projects.UpsertAsync(projectA);
        await projects.UpsertAsync(projectB);
        var createdAt = DateTimeOffset.Parse("2026-08-13T09:00:00.0000000+00:00");
        var taskId = Guid.NewGuid();
        var profile = ExecutionProfile.Create("provider", Guid.NewGuid().ToString(), "model", "runtime");
        var revision = new TaskRevision(taskId, 1, "goal", "scope", "out", ["accept"], TaskRiskLevel.Low, profile, "initial", TaskRevisionApprover.User, createdAt: DateTimeOffset.UtcNow, previousRevisionId: null);
        await new TaskRepository(database).CreateAsync(projectA.Id, new TaskDraft(taskId, "Task", "goal", "scope", "out", ["accept"], TaskRiskLevel.Low, profile, DateTimeOffset.UtcNow, revision));
        var library = new ProjectLibraryRepository(database);
        var leader = new LibrarySubmission(Guid.NewGuid(), projectA.Id, Guid.NewGuid(), null, "Design", "Relics", "Leader summary.", "docs/relics.md", createdAt);
        var worker = new LibrarySubmission(Guid.NewGuid(), projectA.Id, Guid.NewGuid(), taskId, "Implementation", "Relics", "Worker summary.", "commit abc123", createdAt.AddMinutes(1));

        await library.SubmitAsync(leader);
        await library.SubmitAsync(worker);
        await library.SubmitAsync(worker);

        var entries = await library.BrowseAsync(projectA.Id);
        Assert.Equal([worker.SubmissionId, leader.SubmissionId], entries.Select(entry => entry.Id));
        Assert.Equal("Implementation", entries[0].Category);
        Assert.Equal("Relics", entries[0].Topic);
        Assert.Equal("Worker summary.", entries[0].Summary);
        Assert.Equal("commit abc123", entries[0].SourceReference);
        Assert.Equal(worker.SourceSessionId, entries[0].SourceSessionId);
        Assert.Equal(worker.TaskId, entries[0].TaskId);
        Assert.Equal(createdAt.AddMinutes(1), entries[0].CreatedAt);
        Assert.Empty(await library.BrowseAsync(projectB.Id));
    }

    [Fact]
    public async Task Browse_filters_category_topic_and_summary_text_with_newest_first_ordering()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        var project = Project("A");
        await new ProjectRepository(database).UpsertAsync(project);
        var library = new ProjectLibraryRepository(database);
        var at = DateTimeOffset.Parse("2026-08-13T09:00:00.0000000+00:00");
        await library.SubmitAsync(Submission(project.Id, "Design", "Relics", "Jade tea cup", at));
        await library.SubmitAsync(Submission(project.Id, "Design", "Bosses", "First boss", at.AddMinutes(1)));
        await library.SubmitAsync(Submission(project.Id, "Implementation", "Relics", "Relics catalog", at.AddMinutes(2)));

        Assert.Equal(["Bosses", "Relics"], (await library.BrowseAsync(project.Id, category: "Design")).Select(entry => entry.Topic));
        Assert.Equal(["Relics catalog", "Jade tea cup"], (await library.BrowseAsync(project.Id, topic: "Relics")).Select(entry => entry.Summary));
        Assert.Equal("Jade tea cup", Assert.Single(await library.BrowseAsync(project.Id, text: "tea")).Summary);
    }

    [Fact]
    public async Task Legacy_submission_is_bridged_to_evolution_archive_idempotently()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        var project = Project("Bridge");
        await new ProjectRepository(database).UpsertAsync(project);
        var submission = new LibrarySubmission(
            Guid.Parse("70000000-0000-4000-8000-000000000001"),
            project.Id,
            Guid.Parse("70000000-0000-4000-8000-000000000002"),
            null,
            "Design",
            "Relics",
            "Actual state",
            "docs/relics.md",
            DateTimeOffset.Parse("2026-08-14T09:00:00+00:00"));
        var legacy = new ProjectLibraryRepository(database);

        await legacy.SubmitAsync(submission);
        await legacy.SubmitAsync(submission);

        var evolution = new ProjectLibraryEvolutionRepository(database);
        var obj = Assert.Single(await evolution.ListObjectsByCategoryAsync(project.Id, "Design"));
        var node = Assert.Single(await evolution.GetTimelineAsync(project.Id, obj.Id));
        Assert.Equal(submission.SubmissionId, node.Id);
        Assert.Equal("Actual state", node.Content);
        Assert.Contains(
            await evolution.GetMaterialReferencesAsync(project.Id, node.Id),
            reference => reference.MaterialKind == "Reference" && reference.Reference == "docs/relics.md");
    }

    private static LibrarySubmission Submission(Guid projectId, string category, string topic, string summary, DateTimeOffset at) =>
        new(Guid.NewGuid(), projectId, Guid.NewGuid(), null, category, topic, summary, null, at);

    private static Project Project(string name) => new(Guid.NewGuid(), name, $"C:/{name}", ProjectType.Generic, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
}
