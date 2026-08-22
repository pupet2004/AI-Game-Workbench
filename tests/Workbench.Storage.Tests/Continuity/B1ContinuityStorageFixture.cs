using Microsoft.Data.Sqlite;
using Workbench.Core.Continuity;
using Workbench.Core.Projects;
using Workbench.Storage.Continuity;
using Workbench.Storage.Database;
using Workbench.Storage.Tests.Database;

namespace Workbench.Storage.Tests.Continuity;

internal sealed record B1ContinuitySeed(
    ProjectRef ProjectRef,
    UserPrincipalRef BootstrapPrincipalRef,
    LogicalActorRef AssigneeActorRef,
    ResponsibilityRef ResponsibilityRef,
    AssignmentRef AssignmentRef,
    RevisionRef RevisionRef);

internal sealed class B1ContinuityStorageFixture : IAsyncDisposable
{
    private static readonly DateTimeOffset At =
        DateTimeOffset.Parse("2026-08-22T10:00:00.0000000+00:00");

    private readonly TemporaryDatabase _temporary;

    private B1ContinuityStorageFixture(TemporaryDatabase temporary, WorkbenchDatabase database)
    {
        _temporary = temporary;
        Database = database;
    }

    public WorkbenchDatabase Database { get; }
    public B1ContinuitySeed Primary { get; private set; } = null!;

    public static async Task<B1ContinuityStorageFixture> CreateAsync()
    {
        var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        var fixture = new B1ContinuityStorageFixture(temporary, database);
        fixture.Primary = await fixture.AddProjectAsync("primary");
        return fixture;
    }

    public async Task<B1ContinuitySeed> AddProjectAsync(string suffix)
    {
        var projectId = Guid.NewGuid();
        var principal = new UserPrincipalRef($"user:{suffix}");
        var project = new Project(
            projectId,
            $"Project {suffix}",
            $"C:/Projects/{projectId:N}",
            ProjectType.Godot,
            null,
            At,
            At);
        await new B1ProjectGovernanceRepository(Database).CreateGovernedProjectAsync(project, principal);

        var decision = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var responsibility = Guid.NewGuid();
        var assignment = Guid.NewGuid();
        var revision = Guid.NewGuid();
        await using var connection = Database.CreateConnection();
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
            VALUES($responsibility,$project,'Complete assigned work','Bounded result','[]',$decision,$at);
            INSERT INTO b1_assignments(
                id,project_id,responsibility_id,assignee_actor_id,replaces_assignment_id,
                authorized_by_decision_id)
            VALUES($assignment,$project,$responsibility,$actor,NULL,$decision);
            INSERT INTO b1_revisions(
                id,project_id,assignment_id,prior_revision_id,work_contract,
                delegated_authority_json,authorized_by_decision_id)
            VALUES($revision,$project,$assignment,NULL,'Initial contract','[]',$decision);
            INSERT INTO b1_assignment_routing(assignment_id,project_id,selected_attempt_id)
            VALUES($assignment,$project,NULL);
            UPDATE b1_project_governance SET last_commit_sequence=1 WHERE project_id=$project;
            """,
            ("$decision", decision.ToString()), ("$project", projectId.ToString()),
            ("$principal", principal.Value), ("$actor", actor.ToString()),
            ("$responsibility", responsibility.ToString()), ("$assignment", assignment.ToString()),
            ("$revision", revision.ToString()), ("$at", At.ToString("O")));

        return new(
            new ProjectRef(projectId),
            principal,
            new LogicalActorRef(actor),
            new ResponsibilityRef(responsibility),
            new AssignmentRef(assignment),
            new RevisionRef(revision));
    }

    public async Task<RevisionRef> AddSuccessorRevisionAsync(B1ContinuitySeed seed)
    {
        var decision = await AddDecisionAsync(seed, "ActivateAssignmentRevision");
        var revision = Guid.NewGuid();
        await using var connection = Database.CreateConnection();
        await connection.OpenAsync();
        await ExecuteAsync(connection, """
            INSERT INTO b1_revisions(
                id,project_id,assignment_id,prior_revision_id,work_contract,
                delegated_authority_json,authorized_by_decision_id)
            VALUES($revision,$project,$assignment,$prior,'Successor contract','[]',$decision);
            """, ("$revision", revision.ToString()), ("$project", seed.ProjectRef.Value.ToString()),
            ("$assignment", seed.AssignmentRef.Value.ToString()),
            ("$prior", seed.RevisionRef.Value.ToString()), ("$decision", decision.ToString()));
        return new RevisionRef(revision);
    }

    public async Task AddDispositionAsync(B1ContinuitySeed seed, RevisionRef revisionRef)
    {
        var decision = await AddDecisionAsync(seed, "DecideAssignment");
        await using var connection = Database.CreateConnection();
        await connection.OpenAsync();
        await ExecuteAsync(connection, """
            INSERT INTO b1_revision_dispositions(
                revision_id,project_id,assignment_id,disposition,authority_decision_id)
            VALUES($revision,$project,$assignment,'Accepted',$decision);
            """, ("$revision", revisionRef.Value.ToString()),
            ("$project", seed.ProjectRef.Value.ToString()),
            ("$assignment", seed.AssignmentRef.Value.ToString()),
            ("$decision", decision.ToString()));
    }

    public async Task MarkAssignmentReplacedAsync(B1ContinuitySeed seed)
    {
        var decision = await AddDecisionAsync(seed, "DelegateAssignment");
        var replacement = Guid.NewGuid();
        var revision = Guid.NewGuid();
        await using var connection = Database.CreateConnection();
        await connection.OpenAsync();
        await ExecuteAsync(connection, """
            INSERT INTO b1_assignments(
                id,project_id,responsibility_id,assignee_actor_id,replaces_assignment_id,
                authorized_by_decision_id)
            VALUES($replacement,$project,$responsibility,$actor,$replaced,$decision);
            INSERT INTO b1_revisions(
                id,project_id,assignment_id,prior_revision_id,work_contract,
                delegated_authority_json,authorized_by_decision_id)
            VALUES($revision,$project,$replacement,NULL,'Replacement contract','[]',$decision);
            INSERT INTO b1_assignment_routing(assignment_id,project_id,selected_attempt_id)
            VALUES($replacement,$project,NULL);
            """, ("$replacement", replacement.ToString()), ("$project", seed.ProjectRef.Value.ToString()),
            ("$responsibility", seed.ResponsibilityRef.Value.ToString()),
            ("$actor", seed.AssigneeActorRef.Value.ToString()),
            ("$replaced", seed.AssignmentRef.Value.ToString()), ("$decision", decision.ToString()),
            ("$revision", revision.ToString()));
    }

    public async Task<LogicalActorRef> AddActorAsync(B1ContinuitySeed seed)
    {
        var decision = await AddDecisionAsync(seed, "EstablishLogicalActor");
        var actor = Guid.NewGuid();
        await using var connection = Database.CreateConnection();
        await connection.OpenAsync();
        await ExecuteAsync(connection, """
            INSERT INTO b1_logical_actors(id,project_id,role_kind,authorized_by_decision_id,created_at)
            VALUES($actor,$project,'Reviewer',$decision,$at);
            """, ("$actor", actor.ToString()), ("$project", seed.ProjectRef.Value.ToString()),
            ("$decision", decision.ToString()), ("$at", At.ToString("O")));
        return new LogicalActorRef(actor);
    }

    public async Task<long> CountAsync(string table, Guid id, string idColumn = "id")
    {
        await using var connection = Database.CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE {idColumn}=$id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    public async Task<string?> ScalarStringAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = Database.CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? null : Convert.ToString(result);
    }

    public ValueTask DisposeAsync() => _temporary.DisposeAsync();

    private async Task<Guid> AddDecisionAsync(B1ContinuitySeed seed, string kind)
    {
        await using var connection = Database.CreateConnection();
        await connection.OpenAsync();
        var sequenceCommand = connection.CreateCommand();
        sequenceCommand.CommandText =
            "SELECT last_commit_sequence + 1 FROM b1_project_governance WHERE project_id=$project;";
        sequenceCommand.Parameters.AddWithValue("$project", seed.ProjectRef.Value.ToString());
        var sequence = Convert.ToInt64(await sequenceCommand.ExecuteScalarAsync());
        var decision = Guid.NewGuid();
        await ExecuteAsync(connection, """
            INSERT INTO b1_authority_decisions(
                id,project_id,project_commit_sequence,command_kind,deciding_authority_kind,
                deciding_user_principal,deciding_actor_id,created_at)
            VALUES($decision,$project,$sequence,$kind,'UserPrincipal',$principal,NULL,$at);
            UPDATE b1_project_governance
            SET last_commit_sequence=$sequence
            WHERE project_id=$project;
            """, ("$decision", decision.ToString()), ("$project", seed.ProjectRef.Value.ToString()),
            ("$sequence", sequence), ("$kind", kind),
            ("$principal", seed.BootstrapPrincipalRef.Value), ("$at", At.ToString("O")));
        return decision;
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
