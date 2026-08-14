using Microsoft.Data.Sqlite;
using Workbench.Core.Projects;
using Workbench.Storage.Database;
using Workbench.Storage.Memory;
using Workbench.Storage.Projects;
using Workbench.Storage.Tests.Database;

namespace Workbench.Storage.Tests.Memory;

public sealed class ProjectLibraryEvolutionRepositoryTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-08-14T08:00:00+00:00");
    private static readonly DateOnly Day = new(2026, 8, 14);

    [Fact]
    public async Task Create_object_reuses_stable_identity_for_canonical_category_and_topic()
    {
        await using var fixture = await Fixture.CreateAsync();

        var first = await fixture.Library.CreateObjectAsync(fixture.Project.Id, " Design / Relics ", "清一色   罗盘", T0);
        var repeated = await fixture.Library.CreateObjectAsync(fixture.Project.Id, "design / relics", " 清一色 罗盘 ", T0.AddMinutes(1));

        Assert.Equal(first.Id, repeated.Id);
        Assert.Equal("Design / Relics", first.Category);
        Assert.Equal("清一色 罗盘", first.Topic);
        Assert.Equal("DESIGN / RELICS", first.CategoryKey);
        Assert.Equal("清一色 罗盘", first.TopicKey);
        Assert.Single(await fixture.Library.ListObjectsByCategoryAsync(fixture.Project.Id, " DESIGN /  RELICS "));
    }

    [Fact]
    public async Task Canonical_identity_is_isolated_by_project()
    {
        await using var fixture = await Fixture.CreateAsync();
        var other = Project("Other");
        await fixture.Projects.UpsertAsync(other);

        var first = await fixture.Library.CreateObjectAsync(fixture.Project.Id, "Design", "Relics", T0);
        var second = await fixture.Library.CreateObjectAsync(other.Id, "Design", "Relics", T0);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Null(await fixture.Library.GetObjectAsync(fixture.Project.Id, second.Id));
        Assert.Null(await fixture.Library.GetObjectAsync(other.Id, first.Id));
    }

    [Fact]
    public async Task Current_overview_updates_one_projection_with_optimistic_revision()
    {
        await using var fixture = await Fixture.CreateAsync();
        var obj = await fixture.Library.CreateObjectAsync(fixture.Project.Id, "Design", "Relics", T0);

        var first = await fixture.Library.UpdateOverviewAsync(fixture.Project.Id, obj.Id, "Current mechanism", 0, T0.AddMinutes(1));
        var second = await fixture.Library.UpdateOverviewAsync(fixture.Project.Id, obj.Id, "Current mechanism and visual", 1, T0.AddMinutes(2));

        Assert.Equal(2, second.OverviewRevision);
        Assert.Equal("Current mechanism and visual", second.CurrentOverview);
        await Assert.ThrowsAsync<LibraryRevisionConflictException>(() =>
            fixture.Library.UpdateOverviewAsync(fixture.Project.Id, obj.Id, "Stale", 1, T0.AddMinutes(3)));
        var persisted = await fixture.Library.GetObjectAsync(fixture.Project.Id, obj.Id);
        Assert.Equal("Current mechanism and visual", persisted!.CurrentOverview);
        Assert.Equal(2, persisted.OverviewRevision);
    }

    [Fact]
    public async Task Timeline_allows_multiple_same_day_nodes_and_orders_newest_first()
    {
        await using var fixture = await Fixture.CreateAsync();
        var obj = await fixture.Library.CreateObjectAsync(fixture.Project.Id, "Design", "Relics", T0);

        var first = await fixture.Library.AddNodeAsync(fixture.Project.Id, obj.Id, Day, "First state", [], T0.AddMinutes(1));
        var second = await fixture.Library.AddNodeAsync(fixture.Project.Id, obj.Id, Day, "Second state", [], T0.AddMinutes(2));

        Assert.Equal([second.Id, first.Id], (await fixture.Library.GetTimelineAsync(fixture.Project.Id, obj.Id)).Select(node => node.Id));
    }

    [Fact]
    public async Task Timeline_orders_same_day_nodes_by_instant_across_utc_offsets()
    {
        await using var fixture = await Fixture.CreateAsync();
        var obj = await fixture.Library.CreateObjectAsync(fixture.Project.Id, "Design", "Relics", T0);
        var older = await fixture.Library.AddNodeAsync(
            fixture.Project.Id,
            obj.Id,
            Day,
            "Older instant",
            [],
            DateTimeOffset.Parse("2026-08-14T10:00:00+02:00"));
        var newer = await fixture.Library.AddNodeAsync(
            fixture.Project.Id,
            obj.Id,
            Day,
            "Newer instant",
            [],
            DateTimeOffset.Parse("2026-08-14T09:00:00+00:00"));

        Assert.Equal([newer.Id, older.Id], (await fixture.Library.GetTimelineAsync(fixture.Project.Id, obj.Id)).Select(node => node.Id));
    }

    [Fact]
    public async Task Category_and_time_queries_use_the_same_objects_and_nodes()
    {
        await using var fixture = await Fixture.CreateAsync();
        var relic = await fixture.Library.CreateObjectAsync(fixture.Project.Id, "Design", "Relics", T0);
        var boss = await fixture.Library.CreateObjectAsync(fixture.Project.Id, "Design", "Boss", T0);
        var code = await fixture.Library.CreateObjectAsync(fixture.Project.Id, "Implementation", "Save", T0);
        var relicNode = await fixture.Library.AddNodeAsync(fixture.Project.Id, relic.Id, Day, "Relic", [], T0.AddMinutes(1));
        var bossNode = await fixture.Library.AddNodeAsync(fixture.Project.Id, boss.Id, Day, "Boss", [], T0.AddMinutes(2));
        var codeNode = await fixture.Library.AddNodeAsync(fixture.Project.Id, code.Id, Day, "Save", [], T0.AddMinutes(3));

        Assert.Equal([boss.Id, relic.Id], (await fixture.Library.ListObjectsByCategoryAsync(fixture.Project.Id, "Design")).Select(item => item.Id));
        Assert.Equal(
            [codeNode.Id, bossNode.Id, relicNode.Id],
            (await fixture.Library.BrowseNodesByDateAsync(fixture.Project.Id, Day, Day)).Select(node => node.Id));
    }

    [Fact]
    public async Task Material_reference_round_trips_without_a_content_column()
    {
        await using var fixture = await Fixture.CreateAsync();
        var obj = await fixture.Library.CreateObjectAsync(fixture.Project.Id, "Design", "Relics", T0);
        var node = await fixture.Library.AddNodeAsync(
            fixture.Project.Id,
            obj.Id,
            Day,
            "Implemented state",
            [new("File", "assets/relic-v3.png", "Design V3"), new("GitCommit", "abc123", null)],
            T0.AddMinutes(1));

        var references = await fixture.Library.GetMaterialReferencesAsync(fixture.Project.Id, node.Id);
        Assert.Equal(["File", "GitCommit"], references.Select(reference => reference.MaterialKind));
        Assert.Equal("assets/relic-v3.png", references[0].Reference);
        Assert.Equal("Design V3", references[0].Label);
        Assert.Equal(0L, await fixture.ScalarAsync(
            "SELECT COUNT(*) FROM pragma_table_info('project_library_material_refs') WHERE lower(name) IN ('content','body','bytes','blob');"));
    }

    [Fact]
    public async Task Stale_node_update_keeps_content_and_materials_unchanged()
    {
        await using var fixture = await Fixture.CreateAsync();
        var obj = await fixture.Library.CreateObjectAsync(fixture.Project.Id, "Design", "Relics", T0);
        var node = await fixture.Library.AddNodeAsync(
            fixture.Project.Id,
            obj.Id,
            Day,
            "Original",
            [new("Document", "docs/original.md", null)],
            T0.AddMinutes(1));
        await fixture.Library.UpdateNodeAsync(
            fixture.Project.Id,
            node.Id,
            "Revised",
            1,
            [new("Document", "docs/revised.md", null)],
            T0.AddMinutes(2));

        await Assert.ThrowsAsync<LibraryRevisionConflictException>(() => fixture.Library.UpdateNodeAsync(
            fixture.Project.Id,
            node.Id,
            "Stale",
            1,
            [new("Document", "docs/stale.md", null)],
            T0.AddMinutes(3)));

        Assert.Equal("Revised", (await fixture.Library.GetNodeAsync(fixture.Project.Id, node.Id))!.Content);
        Assert.Equal("docs/revised.md", Assert.Single(await fixture.Library.GetMaterialReferencesAsync(fixture.Project.Id, node.Id)).Reference);
    }

    [Fact]
    public async Task Object_timeline_and_materials_survive_database_restart()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        var project = Project("Restart");
        await new ProjectRepository(database).UpsertAsync(project);
        var first = new ProjectLibraryEvolutionRepository(database);
        var obj = await first.CreateObjectAsync(project.Id, "Design", "Relics", T0);
        var node = await first.AddNodeAsync(project.Id, obj.Id, Day, "Durable", [new("GitCommit", "abc123", null)], T0);

        var reopenedDatabase = new WorkbenchDatabase(temporary.DatabasePath);
        await reopenedDatabase.InitializeAsync();
        var reopened = new ProjectLibraryEvolutionRepository(reopenedDatabase);

        Assert.Equal(node.Id, Assert.Single(await reopened.GetTimelineAsync(project.Id, obj.Id)).Id);
        Assert.Equal("abc123", Assert.Single(await reopened.GetMaterialReferencesAsync(project.Id, node.Id)).Reference);
    }

    [Fact]
    public async Task Library_writes_do_not_create_other_memory_or_leader_records()
    {
        await using var fixture = await Fixture.CreateAsync();
        var obj = await fixture.Library.CreateObjectAsync(fixture.Project.Id, "Design", "Relics", T0);
        await fixture.Library.UpdateOverviewAsync(fixture.Project.Id, obj.Id, "Current", 0, T0.AddMinutes(1));
        await fixture.Library.AddNodeAsync(fixture.Project.Id, obj.Id, Day, "State", [], T0.AddMinutes(2));

        Assert.Equal(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM project_daily_summaries;"));
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM leader_session_epochs WHERE handoff_summary IS NOT NULL;"));
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM project_memory_synthesis_jobs;"));
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM leader_messages;"));
    }

    private static Project Project(string name) =>
        new(Guid.NewGuid(), name, $"C:/{name}/{Guid.NewGuid():N}", ProjectType.Generic, null, T0, T0);

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            TemporaryDatabase temporary,
            WorkbenchDatabase database,
            Project project,
            ProjectRepository projects,
            ProjectLibraryEvolutionRepository library)
        {
            Temporary = temporary;
            Database = database;
            Project = project;
            Projects = projects;
            Library = library;
        }

        public TemporaryDatabase Temporary { get; }
        public WorkbenchDatabase Database { get; }
        public Project Project { get; }
        public ProjectRepository Projects { get; }
        public ProjectLibraryEvolutionRepository Library { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var temporary = new TemporaryDatabase();
            var database = new WorkbenchDatabase(temporary.DatabasePath);
            await database.InitializeAsync();
            var project = ProjectLibraryEvolutionRepositoryTests.Project("Project");
            var projects = new ProjectRepository(database);
            await projects.UpsertAsync(project);
            return new Fixture(temporary, database, project, projects, new ProjectLibraryEvolutionRepository(database));
        }

        public async Task<long> ScalarAsync(string sql)
        {
            await using var connection = Database.CreateConnection();
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }

        public ValueTask DisposeAsync() => Temporary.DisposeAsync();
    }
}
