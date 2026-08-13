using Microsoft.Data.Sqlite;
using Workbench.Core.Projects;
using Workbench.Storage.Database;
using Workbench.Storage.Memory;
using Workbench.Storage.Projects;
using Workbench.Storage.Tests.Database;

namespace Workbench.Storage.Tests.Memory;

public sealed class ProjectMemoryCertificationTests
{
    // ── Accept atomicity ──────────────────────────────────────────────

    [Fact]
    public async Task Accept_candidate_atomically_supersedes_and_creates_formal()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        var project = CreateProject("A");
        await new ProjectRepository(database).UpsertAsync(project);
        var service = NewService(database);
        var candidate = await service.CreateCandidateAsync(project.Id, "Rule", "Original.", [new ProjectMemorySource("Manual", "one")]);

        var formal = await service.AcceptCandidateAsync(candidate.Id);

        Assert.Equal("Formal", formal.Layer);
        Assert.Equal("Active", formal.Status);
        Assert.NotNull(formal.CertifiedAt);
        Assert.Equal("Rule", formal.Topic);
        Assert.Equal("Original.", formal.Content);
        Assert.Equal("Superseded", (await service.GetMemoryItemAsync(candidate.Id))!.Status);
        Assert.Single(await service.GetFormalMemoriesAsync(project.Id));
        Assert.Contains(new ProjectMemorySource("Manual", "one"), await service.GetSourcesAsync(formal.Id));
    }

    [Fact]
    public async Task Accept_failure_rolls_back_candidate_and_formal()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        var project = CreateProject("A");
        await new ProjectRepository(database).UpsertAsync(project);
        var service = NewService(database);
        var candidate = await service.CreateCandidateAsync(project.Id, "Rule", "Original.", [new ProjectMemorySource("Manual", "one")]);

        await InstallTriggerAsync(database, """
            CREATE TRIGGER fail_candidate_supersede
            BEFORE UPDATE OF status ON project_memory_items
            WHEN NEW.status = 'Superseded'
            BEGIN SELECT RAISE(ABORT, 'forced supersede failure'); END;
            """);

        await Assert.ThrowsAnyAsync<SqliteException>(() => service.AcceptCandidateAsync(candidate.Id));

        Assert.Equal("Active", (await service.GetMemoryItemAsync(candidate.Id))!.Status);
        Assert.Empty(await service.GetFormalMemoriesAsync(project.Id));
    }

    [Fact]
    public async Task Accept_formal_insert_failure_rolls_back_candidate()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        var project = CreateProject("A");
        await new ProjectRepository(database).UpsertAsync(project);
        var service = NewService(database);
        var candidate = await service.CreateCandidateAsync(project.Id, "Rule", "Original.", [new ProjectMemorySource("Manual", "one")]);

        await InstallTriggerAsync(database, """
            CREATE TRIGGER fail_formal_insert
            BEFORE INSERT ON project_memory_items
            WHEN NEW.layer = 'Formal'
            BEGIN SELECT RAISE(ABORT, 'forced formal insert failure'); END;
            """);

        await Assert.ThrowsAnyAsync<SqliteException>(() => service.AcceptCandidateAsync(candidate.Id));

        Assert.Equal("Active", (await service.GetMemoryItemAsync(candidate.Id))!.Status);
        Assert.Empty(await service.GetFormalMemoriesAsync(project.Id));
    }

    [Fact]
    public async Task Accept_source_failure_rolls_back_entire_certification()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        var project = CreateProject("A");
        await new ProjectRepository(database).UpsertAsync(project);
        var service = NewService(database);
        var candidate = await service.CreateCandidateAsync(project.Id, "Rule", "Original.", [new ProjectMemorySource("Manual", "one")]);

        await InstallTriggerAsync(database, """
            CREATE TRIGGER fail_source_copy
            BEFORE INSERT ON project_memory_sources
            BEGIN SELECT RAISE(ABORT, 'forced source copy failure'); END;
            """);

        await Assert.ThrowsAnyAsync<SqliteException>(() => service.AcceptCandidateAsync(candidate.Id));

        Assert.Equal("Active", (await service.GetMemoryItemAsync(candidate.Id))!.Status);
        Assert.Empty(await service.GetFormalMemoriesAsync(project.Id));
        Assert.Equal("Manual", Assert.Single(await service.GetSourcesAsync(candidate.Id)).SourceType);
    }

    [Fact]
    public async Task Accept_cancellation_leaves_candidate_active()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        var project = CreateProject("A");
        await new ProjectRepository(database).UpsertAsync(project);
        var service = NewService(database);
        var candidate = await service.CreateCandidateAsync(project.Id, "Rule", "Original.", [new ProjectMemorySource("Manual", "one")]);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.AcceptCandidateAsync(candidate.Id, cts.Token));

        Assert.Equal("Active", (await service.GetMemoryItemAsync(candidate.Id))!.Status);
        Assert.Empty(await service.GetFormalMemoriesAsync(project.Id));
    }

    [Fact]
    public async Task Second_accept_does_not_create_second_formal()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        var project = CreateProject("A");
        await new ProjectRepository(database).UpsertAsync(project);
        var service = NewService(database);
        var candidate = await service.CreateCandidateAsync(project.Id, "Rule", "Original.", [new ProjectMemorySource("Manual", "one")]);

        await service.AcceptCandidateAsync(candidate.Id);

        await Assert.ThrowsAsync<MemoryCertificationConflictException>(() => service.AcceptCandidateAsync(candidate.Id));
        Assert.Single(await service.GetFormalMemoriesAsync(project.Id));
    }

    // ── Edit + Accept ─────────────────────────────────────────────────

    [Fact]
    public async Task Edit_accept_is_atomic()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        var project = CreateProject("A");
        await new ProjectRepository(database).UpsertAsync(project);
        var service = NewService(database);
        var candidate = await service.CreateCandidateAsync(project.Id, "Rule", "Original.", [new ProjectMemorySource("Manual", "one")]);

        var formal = await service.EditAndAcceptCandidateAsync(candidate.Id, "Edited.");

        Assert.Equal("Edited.", formal.Content);
        Assert.Equal("Formal", formal.Layer);
        Assert.Equal("Active", formal.Status);
        Assert.NotNull(formal.CertifiedAt);
        Assert.Equal("Superseded", (await service.GetMemoryItemAsync(candidate.Id))!.Status);
    }

    [Fact]
    public async Task Edit_accept_preserves_original_candidate_content()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        var project = CreateProject("A");
        await new ProjectRepository(database).UpsertAsync(project);
        var service = NewService(database);
        var candidate = await service.CreateCandidateAsync(project.Id, "Rule", "Original.", [new ProjectMemorySource("Manual", "one")]);

        await service.EditAndAcceptCandidateAsync(candidate.Id, "Edited.");

        Assert.Equal("Original.", (await service.GetMemoryItemAsync(candidate.Id))!.Content);
        Assert.Equal("Original.", candidate.Content);
    }

    [Fact]
    public async Task Edit_accept_creates_only_edited_formal()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        var project = CreateProject("A");
        await new ProjectRepository(database).UpsertAsync(project);
        var service = NewService(database);
        var candidate = await service.CreateCandidateAsync(project.Id, "Rule", "Original.", [new ProjectMemorySource("Manual", "one")]);

        await service.EditAndAcceptCandidateAsync(candidate.Id, "Edited.");

        var formal = Assert.Single(await service.GetFormalMemoriesAsync(project.Id));
        Assert.Equal("Edited.", formal.Content);
        Assert.Equal("Rule", formal.Topic);
    }

    [Fact]
    public async Task Second_edit_accept_does_not_create_another_formal()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        var project = CreateProject("A");
        await new ProjectRepository(database).UpsertAsync(project);
        var service = NewService(database);
        var candidate = await service.CreateCandidateAsync(project.Id, "Rule", "Original.", [new ProjectMemorySource("Manual", "one")]);

        await service.EditAndAcceptCandidateAsync(candidate.Id, "Edited.");

        await Assert.ThrowsAsync<MemoryCertificationConflictException>(() => service.EditAndAcceptCandidateAsync(candidate.Id, "Again."));
        Assert.Single(await service.GetFormalMemoriesAsync(project.Id));
    }

    // ── Reject ────────────────────────────────────────────────────────

    [Fact]
    public async Task Reject_uses_active_compare_and_set()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        var project = CreateProject("A");
        await new ProjectRepository(database).UpsertAsync(project);
        var service = NewService(database);
        var candidate = await service.CreateCandidateAsync(project.Id, "Rule", "Original.", [new ProjectMemorySource("Manual", "one")]);

        await service.RejectCandidateAsync(candidate.Id);

        Assert.Equal("Rejected", (await service.GetMemoryItemAsync(candidate.Id))!.Status);
        Assert.Empty(await service.GetFormalMemoriesAsync(project.Id));
        Assert.Empty(await service.GetPendingCandidatesAsync(project.Id));
    }

    [Fact]
    public async Task Reject_after_accept_fails()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        var project = CreateProject("A");
        await new ProjectRepository(database).UpsertAsync(project);
        var service = NewService(database);
        var candidate = await service.CreateCandidateAsync(project.Id, "Rule", "Original.", [new ProjectMemorySource("Manual", "one")]);

        await service.AcceptCandidateAsync(candidate.Id);

        await Assert.ThrowsAsync<MemoryCertificationConflictException>(() => service.RejectCandidateAsync(candidate.Id));
        Assert.Equal("Superseded", (await service.GetMemoryItemAsync(candidate.Id))!.Status);
        Assert.Single(await service.GetFormalMemoriesAsync(project.Id));
    }

    [Fact]
    public async Task Accept_after_reject_fails()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        var project = CreateProject("A");
        await new ProjectRepository(database).UpsertAsync(project);
        var service = NewService(database);
        var candidate = await service.CreateCandidateAsync(project.Id, "Rule", "Original.", [new ProjectMemorySource("Manual", "one")]);

        await service.RejectCandidateAsync(candidate.Id);

        await Assert.ThrowsAsync<MemoryCertificationConflictException>(() => service.AcceptCandidateAsync(candidate.Id));
        Assert.Equal("Rejected", (await service.GetMemoryItemAsync(candidate.Id))!.Status);
        Assert.Empty(await service.GetFormalMemoriesAsync(project.Id));
    }

    [Fact]
    public async Task Second_reject_is_not_silent_success()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        var project = CreateProject("A");
        await new ProjectRepository(database).UpsertAsync(project);
        var service = NewService(database);
        var candidate = await service.CreateCandidateAsync(project.Id, "Rule", "Original.", [new ProjectMemorySource("Manual", "one")]);

        await service.RejectCandidateAsync(candidate.Id);

        await Assert.ThrowsAsync<MemoryCertificationConflictException>(() => service.RejectCandidateAsync(candidate.Id));
    }

    // ── Concurrency ───────────────────────────────────────────────────

    [Fact]
    public async Task Concurrent_accepts_create_exactly_one_formal()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        var project = CreateProject("A");
        await new ProjectRepository(database).UpsertAsync(project);
        var candidate = await NewService(database).CreateCandidateAsync(project.Id, "Rule", "Original.", [new ProjectMemorySource("Manual", "one")]);

        var a = NewService(database);
        var b = NewService(database);
        var results = await Task.WhenAll(TryAccept(a, candidate.Id), TryAccept(b, candidate.Id));

        Assert.Equal(1, results.Count(ok => ok));
        Assert.Single(await a.GetFormalMemoriesAsync(project.Id));
        Assert.NotEqual("Active", (await a.GetMemoryItemAsync(candidate.Id))!.Status);
    }

    [Fact]
    public async Task Concurrent_accept_and_edit_accept_create_exactly_one_formal()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        var project = CreateProject("A");
        await new ProjectRepository(database).UpsertAsync(project);
        var candidate = await NewService(database).CreateCandidateAsync(project.Id, "Rule", "Original.", [new ProjectMemorySource("Manual", "one")]);

        var accept = NewService(database);
        var edit = NewService(database);
        var results = await Task.WhenAll(TryAccept(accept, candidate.Id), TryEditAccept(edit, candidate.Id, "Edited."));

        Assert.Equal(1, results.Count(ok => ok));
        var formal = Assert.Single(await accept.GetFormalMemoriesAsync(project.Id));
        Assert.Contains(formal.Content, new[] { "Original.", "Edited." });
        Assert.NotEqual("Active", (await accept.GetMemoryItemAsync(candidate.Id))!.Status);
    }

    [Fact]
    public async Task Concurrent_accept_and_reject_have_one_winner()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        var project = CreateProject("A");
        await new ProjectRepository(database).UpsertAsync(project);
        var candidate = await NewService(database).CreateCandidateAsync(project.Id, "Rule", "Original.", [new ProjectMemorySource("Manual", "one")]);

        var acceptor = NewService(database);
        var rejector = NewService(database);
        var results = await Task.WhenAll(TryAccept(acceptor, candidate.Id), TryReject(rejector, candidate.Id));

        Assert.Equal(1, results.Count(ok => ok));
        var final = await acceptor.GetMemoryItemAsync(candidate.Id);
        if (final!.Status == "Rejected")
        {
            Assert.Empty(await acceptor.GetFormalMemoriesAsync(project.Id));
        }
        else
        {
            Assert.Single(await acceptor.GetFormalMemoriesAsync(project.Id));
            Assert.Equal("Superseded", final.Status);
        }
    }

    // ── Project isolation ─────────────────────────────────────────────

    [Fact]
    public async Task Project_isolation_prevents_cross_project_certification()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        var a = CreateProject("A");
        var b = CreateProject("B");
        await new ProjectRepository(database).UpsertAsync(a);
        await new ProjectRepository(database).UpsertAsync(b);
        var repository = new ProjectMemoryRepository(database);
        var service = NewService(database);
        var candidate = await service.CreateCandidateAsync(a.Id, "Rule", "Original.", [new ProjectMemorySource("Manual", "one")]);

        await Assert.ThrowsAsync<MemoryCertificationConflictException>(() =>
            repository.CertifyCandidateAsync(candidate.Id, b.Id, null, DateTimeOffset.UtcNow));

        Assert.Equal("Active", (await service.GetMemoryItemAsync(candidate.Id))!.Status);
        Assert.Empty(await service.GetFormalMemoriesAsync(a.Id));
        Assert.Empty(await service.GetFormalMemoriesAsync(b.Id));
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static async Task<bool> TryAccept(ProjectMemoryService service, Guid id)
    {
        try
        {
            await service.AcceptCandidateAsync(id);
            return true;
        }
        catch (Exception exception) when (exception is MemoryCertificationConflictException or SqliteException)
        {
            return false;
        }
    }

    private static async Task<bool> TryEditAccept(ProjectMemoryService service, Guid id, string content)
    {
        try
        {
            await service.EditAndAcceptCandidateAsync(id, content);
            return true;
        }
        catch (Exception exception) when (exception is MemoryCertificationConflictException or SqliteException)
        {
            return false;
        }
    }

    private static async Task<bool> TryReject(ProjectMemoryService service, Guid id)
    {
        try
        {
            await service.RejectCandidateAsync(id);
            return true;
        }
        catch (Exception exception) when (exception is MemoryCertificationConflictException or SqliteException)
        {
            return false;
        }
    }

    private static async Task InstallTriggerAsync(WorkbenchDatabase database, string sql)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static ProjectMemoryService NewService(WorkbenchDatabase database) =>
        new(new ProjectActivityRepository(database), new ProjectMemoryRepository(database));

    private static Project CreateProject(string name) =>
        new(Guid.NewGuid(), name, $"C:/{name}", ProjectType.Generic, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
}
