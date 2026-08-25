using Microsoft.Data.Sqlite;
using Workbench.App.Continuity;
using Workbench.App.ProjectWorld;
using Workbench.Core.Continuity;
using Workbench.Core.Projects;
using Workbench.Storage.Continuity;
using Workbench.Storage.Database;
using Workbench.Storage.Projects;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.Tests;

public sealed class ProjectWorldEntryStatusServiceTests
{
    private static readonly DateTimeOffset At =
        DateTimeOffset.Parse("2026-08-25T15:00:00.0000000+00:00");

    [Fact]
    public async Task Governed_project_is_project_world_ready()
    {
        await using var fixture = await Fixture.CreateAsync();
        var project = fixture.CreateProject("Ready");
        await fixture.GovernedProjects.CreateGovernedProjectAsync(project, fixture.Principal);

        var status = await fixture.Service.GetStatusAsync(project);

        Assert.Equal(ProjectWorldEntryKind.ProjectWorldReady, status.Kind);
        Assert.False(status.HasLegacyContext);
        Assert.Equal("Project World ready", status.DisplayLabel);
    }

    [Fact]
    public async Task Legacy_origin_without_governance_requires_setup()
    {
        await using var fixture = await Fixture.CreateAsync();
        var project = fixture.CreateProject("Legacy");
        await fixture.Projects.UpsertAsync(project);
        await fixture.InsertLegacyOriginAsync(project.Id);

        var status = await fixture.Service.GetStatusAsync(project);

        Assert.Equal(ProjectWorldEntryKind.LegacySetupRequired, status.Kind);
        Assert.True(status.HasLegacyContext);
        Assert.Equal("Setup required · Legacy data available", status.DisplayLabel);
    }

    [Fact]
    public async Task Registered_project_without_governance_or_legacy_origin_is_unmanaged()
    {
        await using var fixture = await Fixture.CreateAsync();
        var project = fixture.CreateProject("Unmanaged");
        await fixture.Projects.UpsertAsync(project);

        var status = await fixture.Service.GetStatusAsync(project);

        Assert.Equal(ProjectWorldEntryKind.UnmanagedProjectUnavailable, status.Kind);
        Assert.False(status.HasLegacyContext);
        Assert.Equal("Project setup required", status.DisplayLabel);
    }

    [Fact]
    public async Task Existing_path_missing_is_unavailable_without_reading_governance()
    {
        await using var fixture = await Fixture.CreateAsync();
        var project = fixture.CreateProject("Missing");
        Directory.Delete(project.RootPath, recursive: true);
        await fixture.Projects.UpsertAsync(project);

        var status = await fixture.Service.GetStatusAsync(project);

        Assert.Equal(ProjectWorldEntryKind.PathUnavailable, status.Kind);
        Assert.Equal("Unavailable", status.DisplayLabel);
    }

    [Fact]
    public async Task Orphan_b1_history_is_corrupt_and_not_legacy_adoptable()
    {
        await using var fixture = await Fixture.CreateAsync();
        var project = fixture.CreateProject("Corrupt");
        await fixture.Projects.UpsertAsync(project);
        await fixture.InsertOrphanDecisionAsync(project.Id);

        var status = await fixture.Service.GetStatusAsync(project);

        Assert.Equal(ProjectWorldEntryKind.CorruptProjectUnavailable, status.Kind);
        Assert.False(status.HasLegacyContext);
        Assert.Contains("inconsistent", status.DisplayLabel, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _directory;
        private readonly WorkbenchDatabase _database;

        private Fixture(
            string directory,
            WorkbenchDatabase database,
            ProjectRepository projects,
            B1ProjectGovernanceRepository governedProjects,
            ProjectWorldEntryStatusService service)
        {
            _directory = directory;
            _database = database;
            Projects = projects;
            GovernedProjects = governedProjects;
            Service = service;
        }

        public ProjectRepository Projects { get; }
        public B1ProjectGovernanceRepository GovernedProjects { get; }
        public ProjectWorldEntryStatusService Service { get; }
        public UserPrincipalRef Principal { get; } = new("user:entry-status-tests");

        public static async Task<Fixture> CreateAsync()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "AI.Game.Workbench.App.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var database = new WorkbenchDatabase(Path.Combine(directory, "entry-status.db"));
            await database.InitializeAsync();
            var governance = new B1ProjectGovernanceRepository(database);
            return new(
                directory,
                database,
                new ProjectRepository(database),
                governance,
                new ProjectWorldEntryStatusService(
                    governance,
                    new B1ProjectionService(new B1AuthorityRepository(database))));
        }

        public CoreProject CreateProject(string name)
        {
            var path = Path.Combine(_directory, name);
            Directory.CreateDirectory(path);
            return new CoreProject(
                Guid.NewGuid(),
                name,
                path,
                ProjectType.Godot,
                null,
                At,
                At);
        }

        public async Task InsertLegacyOriginAsync(Guid projectId)
        {
            await using var connection = _database.CreateConnection();
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO b1_legacy_project_origins(project_id,source_schema_version) VALUES($project,19);";
            command.Parameters.AddWithValue("$project", projectId.ToString());
            await command.ExecuteNonQueryAsync();
        }

        public async Task InsertOrphanDecisionAsync(Guid projectId)
        {
            await using var connection = _database.CreateConnection();
            await connection.OpenAsync();
            var disable = connection.CreateCommand();
            disable.CommandText = "PRAGMA foreign_keys=OFF;";
            await disable.ExecuteNonQueryAsync();
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO b1_authority_decisions(
                    id,project_id,project_commit_sequence,command_kind,deciding_authority_kind,
                    deciding_user_principal,deciding_actor_id,created_at)
                VALUES($id,$project,1,'AuthorAcceptedState','UserPrincipal','user:orphan',NULL,$created);
                """;
            command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            command.Parameters.AddWithValue("$project", projectId.ToString());
            command.Parameters.AddWithValue("$created", At.ToString("O"));
            await command.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
            var enable = connection.CreateCommand();
            enable.CommandText = "PRAGMA foreign_keys=ON;";
            await enable.ExecuteNonQueryAsync();
        }

        public ValueTask DisposeAsync()
        {
            try { Directory.Delete(_directory, true); } catch { }
            return ValueTask.CompletedTask;
        }
    }
}
