using Microsoft.Data.Sqlite;
using Workbench.Core.Projects;
using Workbench.Storage.Database;
using Workbench.Storage.Memory;
using Workbench.Storage.Projects;
using Workbench.Storage.Tests.Database;

namespace Workbench.Storage.Tests.Memory;

public sealed class ProjectLibraryLegacyImportTests
{
    private static readonly Guid ProjectAId = Guid.Parse("10000000-0000-4000-8000-000000000001");
    private static readonly Guid ProjectBId = Guid.Parse("10000000-0000-4000-8000-000000000002");
    private static readonly Guid FirstEntryId = Guid.Parse("20000000-0000-4000-8000-000000000001");
    private static readonly Guid SecondEntryId = Guid.Parse("20000000-0000-4000-8000-000000000002");
    private static readonly Guid OtherProjectEntryId = Guid.Parse("20000000-0000-4000-8000-000000000003");
    private static readonly Guid FirstSessionId = Guid.Parse("30000000-0000-4000-8000-000000000001");
    private static readonly Guid SecondSessionId = Guid.Parse("30000000-0000-4000-8000-000000000002");
    private static readonly Guid TaskId = Guid.Parse("40000000-0000-4000-8000-000000000001");

    [Fact]
    public async Task Migration_011_imports_v8_entries_deterministically_and_preserves_legacy_data()
    {
        await using var temporary = new TemporaryDatabase();
        await CreateVersion8DatabaseAsync(temporary.DatabasePath);
        var database = new WorkbenchDatabase(temporary.DatabasePath);

        await database.InitializeAsync();

        var library = new ProjectLibraryEvolutionRepository(database);
        var projectAObjects = await library.ListObjectsByCategoryAsync(ProjectAId, "DESIGN / RELICS");
        var importedObject = Assert.Single(projectAObjects);
        Assert.Equal(FirstEntryId, importedObject.Id);
        Assert.Equal("Design / Relics", importedObject.Category);
        Assert.Equal("清一色 罗盘", importedObject.Topic);
        Assert.Null(importedObject.CurrentOverview);
        Assert.Equal(0, importedObject.OverviewRevision);

        var nodes = await library.GetTimelineAsync(ProjectAId, importedObject.Id);
        Assert.Equal([SecondEntryId, FirstEntryId], nodes.Select(node => node.Id));
        Assert.Equal(["Second actual state", "First actual state"], nodes.Select(node => node.Content));
        Assert.Equal(new DateOnly(2026, 8, 13), nodes[0].LocalDate);
        Assert.Equal(new DateOnly(2026, 8, 12), nodes[1].LocalDate);
        Assert.Equal(DateTimeOffset.Parse("2026-08-13T06:00:00+02:00"), nodes[0].CreatedAt);
        Assert.Equal(DateTimeOffset.Parse("2026-08-11T23:30:00-05:00"), nodes[1].CreatedAt);

        var firstReferences = await library.GetMaterialReferencesAsync(ProjectAId, FirstEntryId);
        Assert.Equal(["AgentSession", "Reference", "Task"], firstReferences.Select(reference => reference.MaterialKind));
        Assert.Contains(firstReferences, reference => reference.MaterialKind == "AgentSession" && reference.Reference == FirstSessionId.ToString());
        Assert.Contains(firstReferences, reference => reference.MaterialKind == "Task" && reference.Reference == TaskId.ToString());
        Assert.Contains(firstReferences, reference => reference.MaterialKind == "Reference" && reference.Reference == "docs/relic-v2.md");

        Assert.Single(await library.ListObjectsByCategoryAsync(ProjectBId, "Design / Relics"));
        Assert.Null(await library.GetObjectAsync(ProjectAId, OtherProjectEntryId));
        Assert.Equal(3L, await ScalarAsync(database, "SELECT COUNT(*) FROM project_library_entries;"));
        Assert.Equal(3L, await ScalarAsync(database, "SELECT COUNT(*) FROM project_library_timeline_nodes;"));

        var firstCoverage = await library.GetLegacyEntryCoverageAsync(ProjectAId, FirstEntryId);
        Assert.Equal(ProjectLibraryCoverageCategory.FullyCovered, firstCoverage!.Category);
        Assert.Equal(FirstEntryId, firstCoverage.NodeId);
        Assert.Equal(FirstEntryId, firstCoverage.ObjectId);
        Assert.Equal(ProjectLibraryCoverageCategory.FullyCovered,
            (await library.GetLegacyEntryCoverageAsync(ProjectAId, SecondEntryId))!.Category);
        Assert.Equal(ProjectLibraryCoverageCategory.FullyCovered,
            (await library.GetLegacyEntryCoverageAsync(ProjectBId, OtherProjectEntryId))!.Category);
    }

    [Fact]
    public async Task Coverage_distinguishes_partial_uncovered_and_indeterminate_rows()
    {
        await using var partial = new TemporaryDatabase();
        await CreateVersion8DatabaseAsync(partial.DatabasePath);
        var partialDatabase = new WorkbenchDatabase(partial.DatabasePath);
        await partialDatabase.InitializeAsync();
        await ExecuteAsync(partialDatabase, $"DELETE FROM project_library_material_refs WHERE node_id='{FirstEntryId}' AND material_kind='Reference';");
        var partialCoverage = await new ProjectLibraryEvolutionRepository(partialDatabase).GetLegacyEntryCoverageAsync(ProjectAId, FirstEntryId);
        Assert.Equal(ProjectLibraryCoverageCategory.PartiallyCovered, partialCoverage!.Category);

        await using var uncovered = new TemporaryDatabase();
        await CreateVersion8DatabaseAsync(uncovered.DatabasePath);
        var uncoveredDatabase = new WorkbenchDatabase(uncovered.DatabasePath);
        await uncoveredDatabase.InitializeAsync();
        await ExecuteAsync(uncoveredDatabase, $"DELETE FROM project_library_timeline_nodes WHERE id='{SecondEntryId}';");
        var uncoveredCoverage = await new ProjectLibraryEvolutionRepository(uncoveredDatabase).GetLegacyEntryCoverageAsync(ProjectAId, SecondEntryId);
        Assert.Equal(ProjectLibraryCoverageCategory.Uncovered, uncoveredCoverage!.Category);

        await using var indeterminate = new TemporaryDatabase();
        await CreateVersion8DatabaseAsync(indeterminate.DatabasePath);
        var indeterminateDatabase = new WorkbenchDatabase(indeterminate.DatabasePath);
        await indeterminateDatabase.InitializeAsync();
        await ExecuteAsync(indeterminateDatabase, $"UPDATE project_library_timeline_nodes SET content='conflicting content' WHERE id='{FirstEntryId}';");
        var indeterminateCoverage = await new ProjectLibraryEvolutionRepository(indeterminateDatabase).GetLegacyEntryCoverageAsync(ProjectAId, FirstEntryId);
        Assert.Equal(ProjectLibraryCoverageCategory.Indeterminate, indeterminateCoverage!.Category);

        await using var malformed = new TemporaryDatabase();
        await CreateVersion8DatabaseAsync(malformed.DatabasePath);
        var malformedDatabase = new WorkbenchDatabase(malformed.DatabasePath);
        await malformedDatabase.InitializeAsync();
        await ExecuteAsync(malformedDatabase, $"UPDATE project_library_entries SET created_at='not-a-timestamp' WHERE id='{FirstEntryId}';");
        var malformedCoverage = await new ProjectLibraryEvolutionRepository(malformedDatabase).GetLegacyEntryCoverageAsync(ProjectAId, FirstEntryId);
        Assert.Equal(ProjectLibraryCoverageCategory.Indeterminate, malformedCoverage!.Category);
    }

    [Fact]
    public async Task Current_library_writes_do_not_create_legacy_entries()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        var project = new Project(Guid.NewGuid(), "Current", "C:/Current", ProjectType.Generic, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        await new ProjectRepository(database).UpsertAsync(project);
        var library = new ProjectLibraryEvolutionRepository(database);
        var libraryObject = await library.CreateObjectAsync(project.Id, "Design", "Current", DateTimeOffset.UtcNow);
        await library.AddNodeAsync(project.Id, libraryObject.Id, new DateOnly(2026, 8, 15), "Current node", [new("Document", "docs/current.md", "Current")], DateTimeOffset.UtcNow);

        Assert.Equal(0L, await ScalarAsync(database, "SELECT COUNT(*) FROM project_library_entries WHERE project_id='" + project.Id + "';"));
        Assert.Single(await library.ListObjectsAsync(project.Id));
    }

    [Fact]
    public async Task Legacy_reader_remains_available_for_an_unmigrated_entry()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        var project = new Project(Guid.NewGuid(), "Legacy", "C:/Legacy", ProjectType.Generic, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        await new ProjectRepository(database).UpsertAsync(project);
        var entryId = Guid.NewGuid();
        await ExecuteAsync(database, $"INSERT INTO project_library_entries (id,project_id,source_session_id,task_id,category,topic,summary,source_reference,created_at) VALUES ('{entryId}','{project.Id}','{Guid.NewGuid()}',NULL,'Design','Legacy','Legacy only','docs/legacy.md','2026-08-15T00:00:00+00:00');");

        var legacy = Assert.Single(await new ProjectLibraryRepository(database).BrowseAsync(project.Id));
        Assert.Equal(entryId, legacy.Id);
        Assert.Equal(ProjectLibraryCoverageCategory.Uncovered,
            (await new ProjectLibraryEvolutionRepository(database).GetLegacyEntryCoverageAsync(project.Id, entryId))!.Category);
    }

    [Fact]
    public async Task Migration_011_can_be_reapplied_without_duplicate_objects_nodes_or_references()
    {
        await using var temporary = new TemporaryDatabase();
        await CreateVersion8DatabaseAsync(temporary.DatabasePath);
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();

        await using (var connection = database.CreateConnection())
        {
            await connection.OpenAsync();
            var downgradeVersionOnly = connection.CreateCommand();
            downgradeVersionOnly.CommandText = "PRAGMA user_version = 10;";
            await downgradeVersionOnly.ExecuteNonQueryAsync();
        }

        await new WorkbenchDatabase(temporary.DatabasePath).InitializeAsync();

        Assert.Equal(2L, await ScalarAsync(database, "SELECT COUNT(*) FROM project_library_objects;"));
        Assert.Equal(3L, await ScalarAsync(database, "SELECT COUNT(*) FROM project_library_timeline_nodes;"));
        Assert.Equal(6L, await ScalarAsync(database, "SELECT COUNT(*) FROM project_library_material_refs;"));
        Assert.Equal(3L, await ScalarAsync(database, "SELECT COUNT(*) FROM project_library_entries;"));
    }

    [Fact]
    public async Task Imported_library_reads_survive_database_reopen()
    {
        await using var temporary = new TemporaryDatabase();
        await CreateVersion8DatabaseAsync(temporary.DatabasePath);
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();

        var reopened = new WorkbenchDatabase(temporary.DatabasePath);
        await reopened.InitializeAsync();
        var library = new ProjectLibraryEvolutionRepository(reopened);
        var importedObject = Assert.Single(await library.ListObjectsByCategoryAsync(ProjectAId, "Design / Relics"));
        var node = Assert.Single(await library.GetTimelineAsync(ProjectAId, importedObject.Id), item => item.Id == FirstEntryId);

        Assert.Equal("First actual state", node.Content);
        Assert.Contains(await library.GetMaterialReferencesAsync(ProjectAId, node.Id), reference => reference.Reference == "docs/relic-v2.md");
        Assert.Equal(ProjectLibraryCoverageCategory.FullyCovered,
            (await library.GetLegacyEntryCoverageAsync(ProjectAId, FirstEntryId))!.Category);
    }

    private static async Task CreateVersion8DatabaseAsync(string path)
    {
        var database = new WorkbenchDatabase(path);
        await database.InitializeAsync();
        var projects = new ProjectRepository(database);
        var createdAt = DateTimeOffset.Parse("2026-08-01T00:00:00+00:00");
        await projects.UpsertAsync(new(ProjectAId, "A", "C:/Legacy/A", ProjectType.Generic, null, createdAt, createdAt));
        await projects.UpsertAsync(new(ProjectBId, "B", "C:/Legacy/B", ProjectType.Generic, null, createdAt, createdAt));

        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        var setup = connection.CreateCommand();
        setup.CommandText = $"""
            INSERT INTO tasks (id,project_id,title,status,current_revision_id,created_at,updated_at,cancelled_at)
            VALUES ('{TaskId}','{ProjectAId}','Legacy task','Draft','50000000-0000-4000-8000-000000000001','2026-08-01T00:00:00+00:00','2026-08-01T00:00:00+00:00',NULL);
            INSERT INTO project_library_entries (id,project_id,source_session_id,task_id,category,topic,summary,source_reference,created_at)
            VALUES ('{FirstEntryId}','{ProjectAId}','{FirstSessionId}','{TaskId}','Design / Relics','清一色 罗盘','First actual state','docs/relic-v2.md','2026-08-11T23:30:00-05:00');
            INSERT INTO project_library_entries (id,project_id,source_session_id,task_id,category,topic,summary,source_reference,created_at)
            VALUES ('{SecondEntryId}','{ProjectAId}','{SecondSessionId}',NULL,' design   / relics ',' 清一色   罗盘 ','Second actual state',NULL,'2026-08-13T06:00:00+02:00');
            INSERT INTO project_library_entries (id,project_id,source_session_id,task_id,category,topic,summary,source_reference,created_at)
            VALUES ('{OtherProjectEntryId}','{ProjectBId}','{SecondSessionId}',NULL,'Design / Relics','清一色 罗盘','Other project state','asset.png','2026-08-13T00:00:00+00:00');

            DROP TABLE project_library_proposals;
            DROP TABLE project_library_material_refs;
            DROP TABLE project_library_timeline_nodes;
            DROP TABLE project_library_objects;
            DROP TABLE leader_epoch_continuity_selections;
            DROP TABLE leader_epoch_continuity_plans;
            DROP TABLE project_daily_summary_sources;
            DROP TABLE project_daily_summaries;
            DROP TABLE project_memory_preferences;
            PRAGMA user_version = 8;
            """;
        await setup.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarAsync(WorkbenchDatabase database, string sql)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task ExecuteAsync(WorkbenchDatabase database, string sql)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
