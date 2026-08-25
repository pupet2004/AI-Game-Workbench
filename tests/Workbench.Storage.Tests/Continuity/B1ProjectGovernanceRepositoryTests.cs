using Microsoft.Data.Sqlite;
using Workbench.Core.Continuity;
using Workbench.Core.Projects;
using Workbench.Storage.Continuity;
using Workbench.Storage.Database;
using Workbench.Storage.Projects;
using Workbench.Storage.Tests.Database;

namespace Workbench.Storage.Tests.Continuity;

public sealed class B1ProjectGovernanceRepositoryTests
{
    private static readonly DateTimeOffset CreatedAt =
        DateTimeOffset.Parse("2026-08-22T08:00:00.0000000+00:00");

    private static readonly DateTimeOffset AdoptedAt =
        DateTimeOffset.Parse("2026-08-22T09:00:00.0000000+00:00");

    [Fact]
    public async Task CreateGovernedProject_inserts_project_and_root_atomically()
    {
        await using var fixture = await Fixture.CreateAsync();
        var project = NewProject();
        var principal = new UserPrincipalRef("user:bootstrap");

        var governance = await fixture.Repository.CreateGovernedProjectAsync(project, principal);

        Assert.Equal(project, await fixture.Projects.GetByIdAsync(project.Id));
        Assert.Equal(governance, await fixture.Repository.GetAsync(new ProjectRef(project.Id)));
        Assert.Equal(1L, await fixture.CountAsync("projects", project.Id));
        Assert.Equal(1L, await fixture.CountAsync("b1_project_governance", project.Id));

        var duplicate = await Assert.ThrowsAsync<B1CommandException>(() =>
            fixture.Repository.CreateGovernedProjectAsync(
                project with { Name = "Must not replace the existing Project" },
                new UserPrincipalRef("user:other")));
        Assert.Equal(B1FailureCode.GovernanceAlreadyExists, duplicate.Code);
        Assert.Equal(project, await fixture.Projects.GetByIdAsync(project.Id));
        Assert.Equal(governance, await fixture.Repository.GetAsync(new ProjectRef(project.Id)));
    }

    [Fact]
    public async Task New_project_requires_nonblank_user_principal()
    {
        await using var fixture = await Fixture.CreateAsync();
        var project = NewProject();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Repository.CreateGovernedProjectAsync(project, default));

        Assert.Null(await fixture.Projects.GetByIdAsync(project.Id));
        Assert.Null(await fixture.Repository.GetAsync(new ProjectRef(project.Id)));
    }

    [Fact]
    public async Task New_project_has_created_origin_and_zero_commit_sequence()
    {
        await using var fixture = await Fixture.CreateAsync();
        var project = NewProject();
        var principal = new UserPrincipalRef("user:creator");

        var governance = await fixture.Repository.CreateGovernedProjectAsync(project, principal);

        Assert.Equal(new ProjectRef(project.Id), governance.ProjectRef);
        Assert.Equal(principal, governance.BootstrapPrincipalRef);
        Assert.Equal(B1GovernanceOrigin.Created, governance.Origin);
        Assert.Null(governance.AdoptedAt);
        Assert.Equal(0, governance.LastProjectCommitSequence);
        Assert.Equal(0L, await fixture.CountAsync("b1_legacy_project_origins", project.Id));
    }

    [Fact]
    public async Task Eligible_pre_b1_project_can_be_adopted_once()
    {
        await using var fixture = await Fixture.CreateLegacyAsync();
        var principal = new UserPrincipalRef("user:authenticated-adopter");

        var governance = await fixture.Repository.AdoptLegacyProjectAsync(
            new ProjectRef(fixture.Project.Id), principal, AdoptedAt);

        Assert.Equal(new ProjectRef(fixture.Project.Id), governance.ProjectRef);
        Assert.Equal(principal, governance.BootstrapPrincipalRef);
        Assert.Equal(B1GovernanceOrigin.Adopted, governance.Origin);
        Assert.Equal(AdoptedAt, governance.AdoptedAt);
        Assert.Equal(0, governance.LastProjectCommitSequence);
        Assert.Equal(governance, await fixture.Repository.GetAsync(new ProjectRef(fixture.Project.Id)));

        var duplicate = await Assert.ThrowsAsync<B1CommandException>(() =>
            fixture.Repository.AdoptLegacyProjectAsync(
                new ProjectRef(fixture.Project.Id), new UserPrincipalRef("user:replacement"), AdoptedAt.AddHours(1)));
        Assert.Equal(B1FailureCode.GovernanceAlreadyExists, duplicate.Code);
    }

    [Fact]
    public async Task Null_governance_without_origin_is_not_adoptable()
    {
        await using var fixture = await Fixture.CreateAsync();
        var project = NewProject();
        await fixture.Projects.UpsertAsync(project);

        var exception = await Assert.ThrowsAsync<B1CommandException>(() =>
            fixture.Repository.AdoptLegacyProjectAsync(
                new ProjectRef(project.Id), new UserPrincipalRef("user:adopter"), AdoptedAt));

        Assert.Equal(B1FailureCode.LegacyProjectNotEligible, exception.Code);
        Assert.Null(await fixture.Repository.GetAsync(new ProjectRef(project.Id)));
        Assert.Equal(0L, await fixture.CountAsync("b1_legacy_project_origins", project.Id));
    }

    [Theory]
    [InlineData("Actor")]
    [InlineData("Responsibility")]
    [InlineData("Assignment")]
    [InlineData("Decision")]
    public async Task Adoption_fails_when_any_b1_governance_history_exists(string historyKind)
    {
        await using var fixture = await Fixture.CreateLegacyAsync();
        await fixture.SeedOrphanHistoryAsync(historyKind);

        var exception = await Assert.ThrowsAsync<B1CommandException>(() =>
            fixture.Repository.AdoptLegacyProjectAsync(
                new ProjectRef(fixture.Project.Id), new UserPrincipalRef("user:adopter"), AdoptedAt));

        Assert.Equal(B1FailureCode.LegacyProjectNotEligible, exception.Code);
        Assert.Null(await fixture.Repository.GetAsync(new ProjectRef(fixture.Project.Id)));
    }

    [Fact]
    public async Task Adoption_creates_no_decision_claim_or_accepted_state()
    {
        await using var fixture = await Fixture.CreateLegacyAsync();

        await fixture.Repository.AdoptLegacyProjectAsync(
            new ProjectRef(fixture.Project.Id), new UserPrincipalRef("user:adopter"), AdoptedAt);

        Assert.Equal(1L, await fixture.CountAsync("b1_legacy_project_origins", fixture.Project.Id));
        Assert.Equal(1L, await fixture.CountAsync("b1_project_governance", fixture.Project.Id));
        foreach (var table in new[]
                 {
                     "b1_authority_decisions", "b1_claims", "b1_accepted_state_contributions",
                     "b1_logical_actors", "b1_responsibilities", "b1_assignments", "b1_revisions",
                     "b1_revision_dispositions", "b1_attempts", "b1_session_bindings", "b1_handoffs"
                 })
        {
            Assert.Equal(0L, await fixture.CountAsync(table, fixture.Project.Id));
        }
    }

    [Fact]
    public async Task Adoption_failure_writes_nothing()
    {
        await using var fixture = await Fixture.CreateAsync();
        var project = NewProject();
        await fixture.Projects.UpsertAsync(project);
        var countsBefore = await fixture.B1CountsAsync(project.Id);

        var exception = await Assert.ThrowsAsync<B1CommandException>(() =>
            fixture.Repository.AdoptLegacyProjectAsync(
                new ProjectRef(project.Id), new UserPrincipalRef("user:adopter"), AdoptedAt));

        Assert.Equal(B1FailureCode.LegacyProjectNotEligible, exception.Code);
        Assert.Equal(countsBefore, await fixture.B1CountsAsync(project.Id));
        Assert.Equal(project, await fixture.Projects.GetByIdAsync(project.Id));
    }

    [Fact]
    public async Task Existing_empty_project_can_establish_governance_without_creating_b1_history()
    {
        await using var fixture = await Fixture.CreateAsync();
        var project = NewProject();
        await fixture.Projects.UpsertAsync(project);
        var principal = new UserPrincipalRef("user:existing-project");

        var governance = await fixture.Repository.CreateGovernedProjectForExistingProjectAsync(
            new ProjectRef(project.Id), principal);

        Assert.Equal(B1GovernanceOrigin.Created, governance.Origin);
        Assert.Equal(governance, await fixture.Repository.GetAsync(new ProjectRef(project.Id)));
        Assert.Equal(0L, await fixture.CountAsync("b1_authority_decisions", project.Id));
        Assert.Equal(0L, await fixture.CountAsync("b1_logical_actors", project.Id));
        Assert.Equal(0L, await fixture.CountAsync("b1_responsibilities", project.Id));
        Assert.Equal(0L, await fixture.CountAsync("b1_assignments", project.Id));
    }

    [Fact]
    public async Task Existing_legacy_or_history_project_cannot_be_reclassified_as_new()
    {
        await using var fixture = await Fixture.CreateLegacyAsync();

        var exception = await Assert.ThrowsAsync<B1CommandException>(() =>
            fixture.Repository.CreateGovernedProjectForExistingProjectAsync(
                new ProjectRef(fixture.Project.Id), new UserPrincipalRef("user:wrong-route")));

        Assert.Equal(B1FailureCode.LegacyProjectNotEligible, exception.Code);
        Assert.Null(await fixture.Repository.GetAsync(new ProjectRef(fixture.Project.Id)));
    }

    [Fact]
    public async Task Entry_facts_distinguish_governed_legacy_and_unmanaged_projects()
    {
        await using var governed = await Fixture.CreateAsync();
        var governedProject = NewProject();
        await governed.Repository.CreateGovernedProjectAsync(governedProject, new UserPrincipalRef("user:governed"));
        var governedFacts = await governed.Repository.GetEntryFactsAsync(new ProjectRef(governedProject.Id));
        Assert.Equal(new B1GovernanceEntryFacts(true, true, false, false), governedFacts);

        await using var unmanaged = await Fixture.CreateAsync();
        var unmanagedProject = NewProject();
        await unmanaged.Projects.UpsertAsync(unmanagedProject);
        var unmanagedFacts = await unmanaged.Repository.GetEntryFactsAsync(new ProjectRef(unmanagedProject.Id));
        Assert.Equal(new B1GovernanceEntryFacts(true, false, false, false), unmanagedFacts);

        await using var legacy = await Fixture.CreateLegacyAsync();
        var legacyFacts = await legacy.Repository.GetEntryFactsAsync(new ProjectRef(legacy.Project.Id));
        Assert.Equal(new B1GovernanceEntryFacts(true, false, true, false), legacyFacts);
    }

    [Fact]
    public async Task Entry_facts_report_orphan_b1_history_without_governance()
    {
        await using var fixture = await Fixture.CreateAsync();
        var project = fixture.Project;
        await fixture.Projects.UpsertAsync(project);
        await fixture.SeedOrphanHistoryAsync("Decision");

        var facts = await fixture.Repository.GetEntryFactsAsync(new ProjectRef(project.Id));

        Assert.Equal(new B1GovernanceEntryFacts(true, false, false, true), facts);
    }

    private static Project NewProject() =>
        new(
            Guid.NewGuid(),
            "Governed Project",
            $"C:/Projects/{Guid.NewGuid():N}",
            ProjectType.Godot,
            "C:/Projects",
            CreatedAt,
            CreatedAt.AddMinutes(5));

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly TemporaryDatabase _temporary;

        private Fixture(TemporaryDatabase temporary, WorkbenchDatabase database, Project project)
        {
            _temporary = temporary;
            Database = database;
            Project = project;
            Repository = new B1ProjectGovernanceRepository(database);
            Projects = new ProjectRepository(database);
        }

        public WorkbenchDatabase Database { get; }
        public Project Project { get; }
        public B1ProjectGovernanceRepository Repository { get; }
        public ProjectRepository Projects { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var temporary = new TemporaryDatabase();
            var database = new WorkbenchDatabase(temporary.DatabasePath);
            await database.InitializeAsync();
            return new Fixture(temporary, database, NewProject());
        }

        public static async Task<Fixture> CreateLegacyAsync()
        {
            var temporary = new TemporaryDatabase();
            var database = new WorkbenchDatabase(temporary.DatabasePath);
            await HistoricalMigrationTestDatabase.InitializeThroughAsync(database, 19);
            var project = NewProject();
            await new ProjectRepository(database).UpsertAsync(project);
            await using (var connection = database.CreateConnection())
            {
                await connection.OpenAsync();
                await ExecuteAsync(connection, """
                    INSERT INTO project_leaders(project_id,current_epoch_id,created_at,updated_at)
                    VALUES($project,NULL,$at,$at);
                    INSERT INTO leader_session_epochs(
                        id,project_id,provider_id,provider_account_id,model_id,agent_session_id,
                        external_session_id,working_directory,started_at,last_active_at,ended_at,
                        rollover_reason,handoff_summary,boot_context_delivered_at)
                    VALUES($epoch,$project,'legacy-provider','legacy-account','legacy-model','legacy-agent',
                        'legacy-external','C:/Legacy',$at,$at,NULL,NULL,NULL,$at);
                    UPDATE project_leaders SET current_epoch_id=$epoch WHERE project_id=$project;
                    INSERT INTO project_settings(project_id,leader_session_rotation_policy,leader_authority_mode)
                    VALUES($project,'Auto','Autonomous');
                    """, ("$project", project.Id.ToString()), ("$epoch", Guid.NewGuid().ToString()),
                    ("$at", CreatedAt.ToString("O")));
            }

            await database.InitializeAsync();
            return new Fixture(temporary, database, project);
        }

        public async Task<long> CountAsync(string table, Guid projectId)
        {
            await using var connection = Database.CreateConnection();
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            var projectColumn = table == "projects" ? "id" : "project_id";
            command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE {projectColumn}=$project;";
            command.Parameters.AddWithValue("$project", projectId.ToString());
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }

        public async Task<string> B1CountsAsync(Guid projectId)
        {
            var counts = new List<string>();
            foreach (var table in new[]
                     {
                         "b1_legacy_project_origins", "b1_project_governance", "b1_logical_actors",
                         "b1_responsibilities", "b1_assignments", "b1_authority_decisions", "b1_claims",
                         "b1_accepted_state_contributions"
                     })
            {
                counts.Add($"{table}:{await CountAsync(table, projectId)}");
            }

            return string.Join('|', counts);
        }

        public async Task SeedOrphanHistoryAsync(string historyKind)
        {
            await using var connection = Database.CreateConnection();
            await connection.OpenAsync();
            await ExecuteAsync(connection, "PRAGMA foreign_keys=OFF;");
            try
            {
                var project = Project.Id.ToString();
                var id = Guid.NewGuid().ToString();
                var decision = Guid.NewGuid().ToString();
                var sql = historyKind switch
                {
                    "Actor" => """
                        INSERT INTO b1_logical_actors(id,project_id,role_kind,authorized_by_decision_id,created_at)
                        VALUES($id,$project,'Worker',$decision,$at);
                        """,
                    "Responsibility" => """
                        INSERT INTO b1_responsibilities(
                            id,project_id,obligation,expected_outcome,maximum_authority_json,
                            authorized_by_decision_id,created_at)
                        VALUES($id,$project,'legacy obligation','legacy outcome','[]',$decision,$at);
                        """,
                    "Assignment" => """
                        INSERT INTO b1_assignments(
                            id,project_id,responsibility_id,assignee_actor_id,replaces_assignment_id,
                            authorized_by_decision_id)
                        VALUES($id,$project,$responsibility,$actor,NULL,$decision);
                        """,
                    "Decision" => """
                        INSERT INTO b1_authority_decisions(
                            id,project_id,project_commit_sequence,command_kind,deciding_authority_kind,
                            deciding_user_principal,deciding_actor_id,created_at)
                        VALUES($id,$project,1,'AuthorAcceptedState','UserPrincipal','user:legacy',NULL,$at);
                        """,
                    _ => throw new ArgumentOutOfRangeException(nameof(historyKind))
                };
                await ExecuteAsync(connection, sql,
                    ("$id", id), ("$project", project), ("$decision", decision),
                    ("$responsibility", Guid.NewGuid().ToString()), ("$actor", Guid.NewGuid().ToString()),
                    ("$at", CreatedAt.ToString("O")));
            }
            finally
            {
                await ExecuteAsync(connection, "PRAGMA foreign_keys=ON;");
            }
        }

        public ValueTask DisposeAsync() => _temporary.DisposeAsync();

        private static async Task ExecuteAsync(
            SqliteConnection connection,
            string sql,
            params (string Name, object Value)[] parameters)
        {
            var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            await command.ExecuteNonQueryAsync();
        }
    }
}
