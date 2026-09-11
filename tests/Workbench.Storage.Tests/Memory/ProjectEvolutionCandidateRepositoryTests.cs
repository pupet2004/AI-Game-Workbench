using Workbench.Storage.Database;
using Workbench.Storage.Memory;

namespace Workbench.Storage.Tests.Memory;

public sealed class ProjectEvolutionCandidateRepositoryTests
{
    [Fact]
    public async Task Candidate_persists_idempotently_and_survives_reopen()
    {
        await using var temporary = new Workbench.Storage.Tests.Database.TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        var project = new Workbench.Core.Projects.Project(Guid.NewGuid(), "candidate-test", temporary.DatabasePath, Workbench.Core.Projects.ProjectType.Generic, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        await new Workbench.Storage.Projects.ProjectRepository(database).UpsertAsync(project);
        var repository = new ProjectEvolutionCandidateRepository(database);
        var candidate = new ProjectEvolutionCandidate(
            Guid.NewGuid(), project.Id, null, Guid.NewGuid(), "message:1", "timeline", "world", "Update", "old", "new", "High", "AuthorityConfirmation", "because", ProjectEvolutionCandidateStatus.Observed, DateTimeOffset.UtcNow);

        await repository.SaveAsync(candidate);
        await repository.SaveAsync(candidate);
        var reopened = new ProjectEvolutionCandidateRepository(new WorkbenchDatabase(temporary.DatabasePath));
        var stored = Assert.Single(await reopened.ListAsync(project.Id));
        Assert.Equal(candidate.CandidateId, stored.CandidateId);
        Assert.Equal(candidate.Object, stored.Object);
    }

    [Fact]
    public async Task Active_query_excludes_resolved_history_but_history_remains_available()
    {
        await using var temporary = new Workbench.Storage.Tests.Database.TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        var project = new Workbench.Core.Projects.Project(Guid.NewGuid(), "candidate-lifecycle", temporary.DatabasePath, Workbench.Core.Projects.ProjectType.Generic, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        await new Workbench.Storage.Projects.ProjectRepository(database).UpsertAsync(project);
        var repository = new ProjectEvolutionCandidateRepository(database);
        var candidate = new ProjectEvolutionCandidate(
            Guid.NewGuid(), project.Id, null, Guid.NewGuid(), "message:1", "timeline", "world", "Update", "old", "new", "High", "AuthorityConfirmation", "because", ProjectEvolutionCandidateStatus.Observed, DateTimeOffset.UtcNow);

        await repository.SaveAsync(candidate);
        Assert.Single(await repository.ListActiveAsync(project.Id));

        await repository.UpdateStatusAsync(project.Id, candidate.CandidateId, ProjectEvolutionCandidateStatus.GovernancePending);
        Assert.Single(await repository.ListActiveAsync(project.Id));
        await repository.UpdateStatusAsync(project.Id, candidate.CandidateId, ProjectEvolutionCandidateStatus.Accepted);

        Assert.Empty(await repository.ListActiveAsync(project.Id));
        var history = Assert.Single(await repository.ListAsync(project.Id));
        Assert.Equal(ProjectEvolutionCandidateStatus.Accepted, history.Status);
    }
}
