using Microsoft.Data.Sqlite;
using Workbench.App.Continuity;
using Workbench.Core.Continuity;
using Workbench.Core.Projects;
using Workbench.Storage.Continuity;
using Workbench.Storage.Database;

namespace Workbench.App.Tests.Continuity;

public sealed class B1NonAuthoritativeCommandServiceTests
{
    private static readonly DateTimeOffset At =
        DateTimeOffset.Parse("2026-08-22T12:00:00.0000000+00:00");

    [Fact]
    public async Task Service_exposes_attempt_and_binding_commands_without_authority_decision()
    {
        await using var fixture = await Fixture.CreateAsync();
        var attemptRef = new AttemptRef(Guid.NewGuid());
        var attempt = await fixture.Service.CreateAttemptAsync(fixture.CreateAttempt(attemptRef));
        await fixture.Service.SelectCurrentAttemptAsync(fixture.SelectAttempt(null, attemptRef));
        var binding = await fixture.Service.CreateSessionBindingAsync(
            fixture.CreateBinding(attemptRef, "external:service"));
        await fixture.Service.SelectCurrentSessionBindingAsync(
            fixture.SelectBinding(attemptRef, null, binding.SessionBindingRef));
        await fixture.Service.ClearCurrentSessionBindingAsync(
            fixture.ClearBinding(attemptRef, binding.SessionBindingRef));

        Assert.Equal(attemptRef, attempt.AttemptRef);
        Assert.Equal(attemptRef.Value.ToString(), await fixture.SelectedAttemptAsync());
        Assert.Null(await fixture.SelectedBindingAsync(attemptRef));
        Assert.Equal(1L, await fixture.CountAsync("b1_authority_decisions"));
    }

    [Fact]
    public async Task CreateAttemptAndSelect_is_one_routing_transaction()
    {
        await using var fixture = await Fixture.CreateAsync();
        var selected = new AttemptRef(Guid.NewGuid());
        await fixture.Service.CreateAttemptAndSelectAsync(fixture.CreateAttempt(selected), expectedStored: null);
        var rejected = new AttemptRef(Guid.NewGuid());

        var exception = await Assert.ThrowsAsync<B1CommandException>(() =>
            fixture.Service.CreateAttemptAndSelectAsync(fixture.CreateAttempt(rejected), expectedStored: null));

        Assert.Equal(B1FailureCode.StaleRoutingSelection, exception.Code);
        Assert.Equal(selected.Value.ToString(), await fixture.SelectedAttemptAsync());
        Assert.Equal(0L, await fixture.CountByIdAsync("b1_attempts", rejected.Value));
        Assert.Equal(0L, await fixture.CountByIdAsync("b1_attempt_routing", rejected.Value, "attempt_id"));
    }

    [Fact]
    public async Task CreateSessionBindingAndSelect_is_one_routing_transaction()
    {
        await using var fixture = await Fixture.CreateAsync();
        var attemptRef = new AttemptRef(Guid.NewGuid());
        await fixture.Service.CreateAttemptAsync(fixture.CreateAttempt(attemptRef));
        var first = await fixture.Service.CreateSessionBindingAndSelectAsync(
            fixture.CreateBinding(attemptRef, "external:first"), expectedStored: null);
        var rejected = fixture.CreateBinding(attemptRef, "external:rejected");

        var exception = await Assert.ThrowsAsync<B1CommandException>(() =>
            fixture.Service.CreateSessionBindingAndSelectAsync(rejected, expectedStored: null));

        Assert.Equal(B1FailureCode.StaleRoutingSelection, exception.Code);
        Assert.Equal(first.SessionBindingRef.Value.ToString(), await fixture.SelectedBindingAsync(attemptRef));
        Assert.Equal(0L, await fixture.CountByIdAsync(
            "b1_session_bindings", rejected.SessionBinding.SessionBindingRef.Value));
    }

    [Fact]
    public async Task Explicit_clear_uses_stored_expected_binding()
    {
        await using var fixture = await Fixture.CreateAsync();
        var attemptRef = new AttemptRef(Guid.NewGuid());
        await fixture.Service.CreateAttemptAsync(fixture.CreateAttempt(attemptRef));
        var binding = await fixture.Service.CreateSessionBindingAndSelectAsync(
            fixture.CreateBinding(attemptRef, "external:stored"), expectedStored: null);

        var stale = await Assert.ThrowsAsync<B1CommandException>(() =>
            fixture.Service.ClearCurrentSessionBindingAsync(fixture.ClearBinding(attemptRef, expected: null)));
        Assert.Equal(B1FailureCode.StaleRoutingSelection, stale.Code);

        await fixture.Service.ClearCurrentSessionBindingAsync(
            fixture.ClearBinding(attemptRef, binding.SessionBindingRef));
        Assert.Null(await fixture.SelectedBindingAsync(attemptRef));
        Assert.Equal(1L, await fixture.CountByIdAsync(
            "b1_session_bindings", binding.SessionBindingRef.Value));
    }

    [Fact]
    public async Task Routing_commands_do_not_call_authority_repository()
    {
        await using var fixture = await Fixture.CreateAsync();
        var decisionsBefore = await fixture.CountAsync("b1_authority_decisions");
        var attemptRef = new AttemptRef(Guid.NewGuid());

        await fixture.Service.CreateAttemptAndSelectAsync(fixture.CreateAttempt(attemptRef), expectedStored: null);
        await fixture.Service.CreateSessionBindingAndSelectAsync(
            fixture.CreateBinding(attemptRef, "external:no-authority"), expectedStored: null);

        Assert.Equal(decisionsBefore, await fixture.CountAsync("b1_authority_decisions"));
        Assert.Equal(0L, await fixture.CountAsync("b1_revision_dispositions"));
        Assert.Equal(0L, await fixture.CountAsync("b1_accepted_state_contributions"));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _directory;

        private Fixture(
            string directory,
            WorkbenchDatabase database,
            B1NonAuthoritativeCommandService service,
            ProjectRef projectRef,
            UserPrincipalRef principalRef,
            LogicalActorRef actorRef,
            AssignmentRef assignmentRef,
            RevisionRef revisionRef)
        {
            _directory = directory;
            Database = database;
            Service = service;
            ProjectRef = projectRef;
            PrincipalRef = principalRef;
            ActorRef = actorRef;
            AssignmentRef = assignmentRef;
            RevisionRef = revisionRef;
        }

        public WorkbenchDatabase Database { get; }
        public B1NonAuthoritativeCommandService Service { get; }
        public ProjectRef ProjectRef { get; }
        public UserPrincipalRef PrincipalRef { get; }
        public LogicalActorRef ActorRef { get; }
        public AssignmentRef AssignmentRef { get; }
        public RevisionRef RevisionRef { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), "AI.Game.Workbench.App.Tests", Guid.NewGuid().ToString("N"));
            var database = new WorkbenchDatabase(Path.Combine(directory, "test.db"));
            await database.InitializeAsync();
            var projectId = Guid.NewGuid();
            var principal = new UserPrincipalRef("user:app-test");
            await new B1ProjectGovernanceRepository(database).CreateGovernedProjectAsync(
                new Workbench.Core.Projects.Project(
                    projectId, "App Project", $"C:/Projects/{projectId:N}", ProjectType.Godot, null, At, At),
                principal);

            var decision = Guid.NewGuid();
            var actor = Guid.NewGuid();
            var responsibility = Guid.NewGuid();
            var assignment = Guid.NewGuid();
            var revision = Guid.NewGuid();
            await using (var connection = database.CreateConnection())
            {
                await connection.OpenAsync();
                await ExecuteAsync(connection, """
                    INSERT INTO b1_authority_decisions(
                        id,project_id,project_commit_sequence,command_kind,deciding_authority_kind,
                        deciding_user_principal,deciding_actor_id,created_at)
                    VALUES($decision,$project,1,'EstablishResponsibility','UserPrincipal',$principal,NULL,$at);
                    INSERT INTO b1_logical_actors(id,project_id,role_kind,authorized_by_decision_id,created_at)
                    VALUES($actor,$project,'Worker',$decision,$at);
                    INSERT INTO b1_responsibilities(
                        id,project_id,obligation,expected_outcome,maximum_authority_json,
                        authorized_by_decision_id,created_at)
                    VALUES($responsibility,$project,'Do work','Return result','[]',$decision,$at);
                    INSERT INTO b1_assignments(
                        id,project_id,responsibility_id,assignee_actor_id,replaces_assignment_id,
                        authorized_by_decision_id)
                    VALUES($assignment,$project,$responsibility,$actor,NULL,$decision);
                    INSERT INTO b1_revisions(
                        id,project_id,assignment_id,prior_revision_id,work_contract,
                        delegated_authority_json,authorized_by_decision_id)
                    VALUES($revision,$project,$assignment,NULL,'App contract','[]',$decision);
                    INSERT INTO b1_assignment_routing(assignment_id,project_id,selected_attempt_id)
                    VALUES($assignment,$project,NULL);
                    UPDATE b1_project_governance SET last_commit_sequence=1 WHERE project_id=$project;
                    """, ("$decision", decision.ToString()), ("$project", projectId.ToString()),
                    ("$principal", principal.Value), ("$actor", actor.ToString()),
                    ("$responsibility", responsibility.ToString()), ("$assignment", assignment.ToString()),
                    ("$revision", revision.ToString()), ("$at", At.ToString("O")));
            }

            var routing = new B1RoutingRepository(database);
            return new Fixture(
                directory,
                database,
                new B1NonAuthoritativeCommandService(routing),
                new ProjectRef(projectId),
                principal,
                new LogicalActorRef(actor),
                new AssignmentRef(assignment),
                new RevisionRef(revision));
        }

        public CreateAttemptCommand CreateAttempt(AttemptRef attemptRef) =>
            new(ProjectRef, PrincipalRef, attemptRef, AssignmentRef, RevisionRef, At);

        public SelectCurrentAttemptCommand SelectAttempt(AttemptRef? expected, AttemptRef? selected) =>
            new(ProjectRef, PrincipalRef, AssignmentRef, expected, selected);

        public CreateSessionBindingCommand CreateBinding(AttemptRef attemptRef, string externalRef) =>
            new(
                ProjectRef,
                PrincipalRef,
                new SessionBinding(
                    new SessionBindingRef(Guid.NewGuid()),
                    attemptRef,
                    ActorRef,
                    new ExternalSessionRef(externalRef),
                    At));

        public SelectCurrentSessionBindingCommand SelectBinding(
            AttemptRef attemptRef,
            SessionBindingRef? expected,
            SessionBindingRef selected) =>
            new(ProjectRef, PrincipalRef, attemptRef, expected, selected);

        public ClearCurrentSessionBindingCommand ClearBinding(
            AttemptRef attemptRef,
            SessionBindingRef? expected) =>
            new(ProjectRef, PrincipalRef, attemptRef, expected);

        public Task<string?> SelectedAttemptAsync() => ScalarAsync(
            "SELECT selected_attempt_id FROM b1_assignment_routing WHERE assignment_id=$assignment;",
            ("$assignment", AssignmentRef.Value.ToString()));

        public Task<string?> SelectedBindingAsync(AttemptRef attemptRef) => ScalarAsync(
            "SELECT selected_session_binding_id FROM b1_attempt_routing WHERE attempt_id=$attempt;",
            ("$attempt", attemptRef.Value.ToString()));

        public async Task<long> CountAsync(string table)
        {
            var value = await ScalarAsync($"SELECT COUNT(*) FROM {table} WHERE project_id=$project;",
                ("$project", ProjectRef.Value.ToString()));
            return Convert.ToInt64(value);
        }

        public async Task<long> CountByIdAsync(string table, Guid id, string idColumn = "id")
        {
            var value = await ScalarAsync($"SELECT COUNT(*) FROM {table} WHERE {idColumn}=$id;",
                ("$id", id.ToString()));
            return Convert.ToInt64(value);
        }

        public async ValueTask DisposeAsync()
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
                    return;
                }
                catch (IOException) when (attempt < 19)
                {
                    await Task.Delay(25);
                }
                catch (UnauthorizedAccessException) when (attempt < 19)
                {
                    await Task.Delay(25);
                }
            }
        }

        private async Task<string?> ScalarAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = Database.CreateConnection();
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
            var result = await command.ExecuteScalarAsync();
            return result is null or DBNull ? null : Convert.ToString(result);
        }

        private static async Task ExecuteAsync(
            SqliteConnection connection,
            string sql,
            params (string Name, object Value)[] parameters)
        {
            var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
            await command.ExecuteNonQueryAsync();
        }
    }
}
