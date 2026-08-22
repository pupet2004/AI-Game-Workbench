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

        var projectRef = new ProjectRef(projectId);
        var authority = new DecidingAuthorityRef.UserPrincipal(principal);
        var evaluator = new B1AuthorityEvaluator();
        var repository = new B1AuthorityRepository(Database);
        var state = await repository.LoadProjectStateAsync(projectRef);
        var validated = evaluator.Evaluate(state,
            new EstablishResponsibilityCommand(
                projectRef,
                principal,
                authority,
                new ResponsibilityContract(
                    "Complete assigned work",
                    "Bounded result",
                    AuthorityBoundary.Empty),
                new AssignmentDelegationInstruction(
                    new ResponsibilityTarget.EstablishedByThisDecision(),
                    new AssignmentAssigneeTarget.EstablishedByThisDecision(),
                    new AssignmentRevisionContract("Initial contract"),
                    null),
                RoleKind.Worker,
                [],
                []),
            new AuthorityDecisionRef(Guid.NewGuid()),
            At);
        var committed = Assert.IsType<AuthorityCommitResult.Committed>(
            await repository.TryCommitAsync(validated)).Decision;
        var actor = committed.LogicalActorEstablishmentEffect!.LogicalActor;
        var responsibility = committed.ResponsibilityEstablishmentEffect!.Responsibility;
        var delegation = committed.AssignmentDelegationEffect!;
        return new(projectRef, principal, actor.LogicalActorRef, responsibility.ResponsibilityRef,
            delegation.Assignment.AssignmentRef, delegation.InitialRevision.RevisionRef);
    }

    public async Task<RevisionRef> AddSuccessorRevisionAsync(B1ContinuitySeed seed)
    {
        var repository = new B1AuthorityRepository(Database);
        var state = await repository.LoadProjectStateAsync(seed.ProjectRef);
        var validated = new B1AuthorityEvaluator().Evaluate(state,
            new ActivateAssignmentRevisionCommand(
                seed.ProjectRef,
                seed.BootstrapPrincipalRef,
                new DecidingAuthorityRef.UserPrincipal(seed.BootstrapPrincipalRef),
                new RevisionActivationInstruction(
                    seed.AssignmentRef,
                    seed.RevisionRef,
                    new AssignmentRevisionContract("Successor contract"),
                    null),
                [],
                []),
            new AuthorityDecisionRef(Guid.NewGuid()),
            At);
        var committed = Assert.IsType<AuthorityCommitResult.Committed>(
            await repository.TryCommitAsync(validated)).Decision;
        return committed.RevisionActivationEffect!.Revision.RevisionRef;
    }

    public async Task AddDispositionAsync(B1ContinuitySeed seed, RevisionRef revisionRef)
    {
        var repository = new B1AuthorityRepository(Database);
        var state = await repository.LoadProjectStateAsync(seed.ProjectRef);
        var validated = new B1AuthorityEvaluator().Evaluate(state,
            new DecideAssignmentCommand(
                seed.ProjectRef,
                seed.BootstrapPrincipalRef,
                new DecidingAuthorityRef.UserPrincipal(seed.BootstrapPrincipalRef),
                new AssignmentDispositionInstruction(
                    seed.AssignmentRef,
                    revisionRef,
                    AssignmentDisposition.Accepted),
                null,
                null,
                [],
                []),
            new AuthorityDecisionRef(Guid.NewGuid()),
            At);
        Assert.IsType<AuthorityCommitResult.Committed>(await repository.TryCommitAsync(validated));
    }

    public async Task MarkAssignmentReplacedAsync(B1ContinuitySeed seed)
    {
        var repository = new B1AuthorityRepository(Database);
        var state = await repository.LoadProjectStateAsync(seed.ProjectRef);
        var validated = new B1AuthorityEvaluator().Evaluate(state,
            new DelegateAssignmentCommand(
                seed.ProjectRef,
                seed.BootstrapPrincipalRef,
                new DecidingAuthorityRef.UserPrincipal(seed.BootstrapPrincipalRef),
                new AssignmentDelegationInstruction(
                    new ResponsibilityTarget.Existing(seed.ResponsibilityRef),
                    new AssignmentAssigneeTarget.Existing(seed.AssigneeActorRef),
                    new AssignmentRevisionContract("Replacement contract"),
                    seed.AssignmentRef),
                null,
                [],
                []),
            new AuthorityDecisionRef(Guid.NewGuid()),
            At);
        Assert.IsType<AuthorityCommitResult.Committed>(await repository.TryCommitAsync(validated));
    }

    public async Task<LogicalActorRef> AddActorAsync(B1ContinuitySeed seed)
    {
        var repository = new B1AuthorityRepository(Database);
        var state = await repository.LoadProjectStateAsync(seed.ProjectRef);
        var validated = new B1AuthorityEvaluator().Evaluate(state,
            new EstablishLogicalActorCommand(
                seed.ProjectRef,
                seed.BootstrapPrincipalRef,
                new DecidingAuthorityRef.UserPrincipal(seed.BootstrapPrincipalRef),
                RoleKind.Reviewer,
                [],
                []),
            new AuthorityDecisionRef(Guid.NewGuid()),
            At);
        var committed = Assert.IsType<AuthorityCommitResult.Committed>(
            await repository.TryCommitAsync(validated)).Decision;
        return committed.LogicalActorEstablishmentEffect!.LogicalActor.LogicalActorRef;
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

}
