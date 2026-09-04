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
    public async Task Governed_project_without_bootstrap_is_setup_incomplete()
    {
        await using var fixture = await Fixture.CreateAsync();
        var project = fixture.CreateProject("Ready");
        await fixture.GovernedProjects.CreateGovernedProjectAsync(project, fixture.Principal);

        var status = await fixture.Service.GetStatusAsync(project);

        Assert.Equal(ProjectWorldEntryKind.ProjectWorldSetupIncomplete, status.Kind);
        Assert.False(status.HasLegacyContext);
        Assert.Equal("Project setup incomplete · resume setup", status.DisplayLabel);
    }

    [Fact]
    public async Task Fully_initialized_project_is_project_world_ready()
    {
        await using var fixture = await Fixture.CreateAsync();
        var project = fixture.CreateProject("Ready");
        await fixture.GovernedProjects.CreateGovernedProjectAsync(project, fixture.Principal);
        var authority = new B1AuthorityRepository(fixture.Database);
        var initialization = new ProjectWorldInitializationService(
            authority,
            new B1AuthorityCommandService(authority, new B1AuthorityEvaluator(), TimeProvider.System),
            fixture.GovernedProjects);

        await initialization.CommitAsync(new ProjectWorldInitializationRequest(
            new ProjectRef(project.Id), fixture.Principal, RoleKind.Worker,
            "Own the first project responsibility", "A clear first project outcome",
            "Define the first bounded piece of work"));

        var status = await fixture.Service.GetStatusAsync(project);

        Assert.Equal(ProjectWorldEntryKind.ProjectWorldReady, status.Kind);
        Assert.False(status.HasLegacyContext);
        Assert.Equal("Project ready", status.DisplayLabel);
    }

    [Fact]
    public async Task Completed_initial_revision_does_not_require_bootstrap_recovery()
    {
        await using var fixture = await Fixture.CreateAsync();
        var project = fixture.CreateProject("CompletedBootstrap");
        await fixture.GovernedProjects.CreateGovernedProjectAsync(project, fixture.Principal);
        var authority = new B1AuthorityRepository(fixture.Database);
        var commands = new B1AuthorityCommandService(authority, new B1AuthorityEvaluator(), TimeProvider.System);
        var initialization = new ProjectWorldInitializationService(
            authority, commands, fixture.GovernedProjects);

        await initialization.CommitAsync(new ProjectWorldInitializationRequest(
            new ProjectRef(project.Id), fixture.Principal, RoleKind.Worker,
            "Own the first project responsibility", "A clear first project outcome",
            "Define the first bounded piece of work"));

        var projection = B1Projector.Build(await authority.LoadProjectStateAsync(new ProjectRef(project.Id)));
        var assignment = Assert.Single(projection.AcceptedProjectState.Assignments.Values);
        var revision = projection.AcceptedProjectState.CurrentEffectiveRevisionRefs[assignment.AssignmentRef];
        await commands.DecideAssignmentAsync(new DecideAssignmentCommand(
            new ProjectRef(project.Id), fixture.Principal,
            new DecidingAuthorityRef.UserPrincipal(fixture.Principal),
            new AssignmentDispositionInstruction(assignment.AssignmentRef, revision, AssignmentDisposition.Accepted),
            null, null, [], []));

        var status = await fixture.Service.GetStatusAsync(project);

        Assert.Equal(ProjectWorldEntryKind.ProjectWorldReady, status.Kind);
    }

    [Fact]
    public async Task Bootstrap_with_successor_revision_remains_project_world_ready()
    {
        await using var fixture = await Fixture.CreateAsync();
        var project = fixture.CreateProject("SuccessorBootstrap");
        await fixture.GovernedProjects.CreateGovernedProjectAsync(project, fixture.Principal);
        var authority = new B1AuthorityRepository(fixture.Database);
        var commands = new B1AuthorityCommandService(authority, new B1AuthorityEvaluator(), TimeProvider.System);
        var initialization = new ProjectWorldInitializationService(
            authority, commands, fixture.GovernedProjects);

        await initialization.CommitAsync(new ProjectWorldInitializationRequest(
            new ProjectRef(project.Id), fixture.Principal, RoleKind.Worker,
            "Own the first project responsibility", "A clear first project outcome",
            "Define the first bounded piece of work"));

        var projection = B1Projector.Build(await authority.LoadProjectStateAsync(new ProjectRef(project.Id)));
        var assignment = Assert.Single(projection.AcceptedProjectState.Assignments.Values);
        var initialRevision = projection.AcceptedProjectState.CurrentEffectiveRevisionRefs[assignment.AssignmentRef];
        await commands.DecideAssignmentAsync(new DecideAssignmentCommand(
            new ProjectRef(project.Id), fixture.Principal,
            new DecidingAuthorityRef.UserPrincipal(fixture.Principal),
            new AssignmentDispositionInstruction(assignment.AssignmentRef, initialRevision, AssignmentDisposition.RevisionRequired),
            new RevisionActivationInstruction(
                assignment.AssignmentRef, initialRevision,
                new AssignmentRevisionContract("Successor work"), null),
            null, [], []));

        var status = await fixture.Service.GetStatusAsync(project);

        Assert.Equal(ProjectWorldEntryKind.ProjectWorldReady, status.Kind);
    }

    [Fact]
    public async Task Partial_actor_bootstrap_requires_recovery()
    {
        await using var fixture = await Fixture.CreateAsync();
        var project = fixture.CreateProject("ActorOnly");
        await fixture.GovernedProjects.CreateGovernedProjectAsync(project, fixture.Principal);
        await fixture.InsertPartialActorAsync(project.Id);

        var status = await fixture.Service.GetStatusAsync(project);

        Assert.Equal(ProjectWorldEntryKind.BootstrapRecoveryRequired, status.Kind);
        Assert.Contains("recovery", status.DisplayLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Partial_responsibility_bootstrap_requires_recovery()
    {
        await using var fixture = await Fixture.CreateAsync();
        var project = fixture.CreateProject("ResponsibilityOnly");
        await fixture.GovernedProjects.CreateGovernedProjectAsync(project, fixture.Principal);
        await fixture.InsertPartialResponsibilityAsync(project.Id);

        var status = await fixture.Service.GetStatusAsync(project);

        Assert.Equal(ProjectWorldEntryKind.BootstrapRecoveryRequired, status.Kind);
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
        public WorkbenchDatabase Database => _database;
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

        public async Task InsertPartialActorAsync(Guid projectId)
        {
            await using var connection = _database.CreateConnection();
            await connection.OpenAsync();
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
            var decisionId = Guid.NewGuid();
            var decision = connection.CreateCommand();
            decision.Transaction = transaction;
            decision.CommandText = """
                INSERT INTO b1_authority_decisions(
                    id,project_id,project_commit_sequence,command_kind,deciding_authority_kind,
                    deciding_user_principal,deciding_actor_id,created_at)
                VALUES($id,$project,1,'EstablishLogicalActor','UserPrincipal','user:partial',NULL,$created);
                """;
            decision.Parameters.AddWithValue("$id", decisionId.ToString());
            decision.Parameters.AddWithValue("$project", projectId.ToString());
            decision.Parameters.AddWithValue("$created", At.ToString("O"));
            await decision.ExecuteNonQueryAsync();
            var actor = connection.CreateCommand();
            actor.Transaction = transaction;
            actor.CommandText = """
                INSERT INTO b1_logical_actors(id,project_id,role_kind,authorized_by_decision_id,created_at)
                VALUES($id,$project,'Worker',$decision,$created);
                """;
            actor.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            actor.Parameters.AddWithValue("$project", projectId.ToString());
            actor.Parameters.AddWithValue("$decision", decisionId.ToString());
            actor.Parameters.AddWithValue("$created", At.ToString("O"));
            await actor.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }

        public async Task InsertPartialResponsibilityAsync(Guid projectId)
        {
            await using var connection = _database.CreateConnection();
            await connection.OpenAsync();
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
            var decisionId = Guid.NewGuid();
            var decision = connection.CreateCommand();
            decision.Transaction = transaction;
            decision.CommandText = """
                INSERT INTO b1_authority_decisions(
                    id,project_id,project_commit_sequence,command_kind,deciding_authority_kind,
                    deciding_user_principal,deciding_actor_id,created_at)
                VALUES($id,$project,1,'EstablishResponsibility','UserPrincipal','user:partial',NULL,$created);
                """;
            decision.Parameters.AddWithValue("$id", decisionId.ToString());
            decision.Parameters.AddWithValue("$project", projectId.ToString());
            decision.Parameters.AddWithValue("$created", At.ToString("O"));
            await decision.ExecuteNonQueryAsync();
            var responsibility = connection.CreateCommand();
            responsibility.Transaction = transaction;
            responsibility.CommandText = """
                INSERT INTO b1_responsibilities(
                    id,project_id,obligation,expected_outcome,maximum_authority_json,
                    authorized_by_decision_id,created_at)
                VALUES($id,$project,'partial obligation','partial outcome','[]',$decision,$created);
                """;
            responsibility.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            responsibility.Parameters.AddWithValue("$project", projectId.ToString());
            responsibility.Parameters.AddWithValue("$decision", decisionId.ToString());
            responsibility.Parameters.AddWithValue("$created", At.ToString("O"));
            await responsibility.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }

        public ValueTask DisposeAsync()
        {
            try { Directory.Delete(_directory, true); } catch { }
            return ValueTask.CompletedTask;
        }
    }
}
