using System.Globalization;
using Microsoft.Data.Sqlite;
using Workbench.Core.Continuity;
using Workbench.Storage.Database;

namespace Workbench.Storage.Continuity;

public sealed class B1RoutingRepository(WorkbenchDatabase database)
{
    private readonly WorkbenchDatabase _database =
        database ?? throw new ArgumentNullException(nameof(database));

    public Task<Attempt> CreateAttemptAsync(
        CreateAttemptCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return InTransactionAsync(
            (connection, transaction) => CreateAttemptCoreAsync(
                connection, transaction, command, cancellationToken),
            cancellationToken);
    }

    public Task SelectCurrentAttemptAsync(
        SelectCurrentAttemptCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return InTransactionAsync(
            async (connection, transaction) =>
            {
                await SelectAttemptCoreAsync(connection, transaction, command, cancellationToken);
                return true;
            },
            cancellationToken);
    }

    public Task<SessionBinding> CreateSessionBindingAsync(
        CreateSessionBindingCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.SessionBinding);
        return InTransactionAsync(
            (connection, transaction) => CreateSessionBindingCoreAsync(
                connection, transaction, command, cancellationToken),
            cancellationToken);
    }

    public Task SelectCurrentSessionBindingAsync(
        SelectCurrentSessionBindingCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return InTransactionAsync(
            async (connection, transaction) =>
            {
                await SelectBindingCoreAsync(
                    connection,
                    transaction,
                    command.ProjectRef,
                    command.AuthenticatedOperatorRef,
                    command.AttemptRef,
                    command.ExpectedStoredSessionBindingRef,
                    command.SelectedSessionBindingRef,
                    cancellationToken);
                return true;
            },
            cancellationToken);
    }

    public Task ClearCurrentSessionBindingAsync(
        ClearCurrentSessionBindingCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return InTransactionAsync(
            async (connection, transaction) =>
            {
                await SelectBindingCoreAsync(
                    connection,
                    transaction,
                    command.ProjectRef,
                    command.AuthenticatedOperatorRef,
                    command.AttemptRef,
                    command.ExpectedStoredSessionBindingRef,
                    selected: null,
                    cancellationToken);
                return true;
            },
            cancellationToken);
    }

    public Task<Attempt> CreateAttemptAndSelectAsync(
        CreateAttemptCommand create,
        AttemptRef? expectedStored,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(create);
        return InTransactionAsync(
            async (connection, transaction) =>
            {
                var attempt = await CreateAttemptCoreAsync(
                    connection, transaction, create, cancellationToken);
                await SelectAttemptCoreAsync(
                    connection,
                    transaction,
                    new SelectCurrentAttemptCommand(
                        create.ProjectRef,
                        create.AuthenticatedOperatorRef,
                        create.AssignmentRef,
                        expectedStored,
                        create.AttemptRef),
                    cancellationToken);
                return attempt;
            },
            cancellationToken);
    }

    public Task<SessionBinding> CreateSessionBindingAndSelectAsync(
        CreateSessionBindingCommand create,
        SessionBindingRef? expectedStored,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(create.SessionBinding);
        return InTransactionAsync(
            async (connection, transaction) =>
            {
                var binding = await CreateSessionBindingCoreAsync(
                    connection, transaction, create, cancellationToken);
                await SelectBindingCoreAsync(
                    connection,
                    transaction,
                    create.ProjectRef,
                    create.AuthenticatedOperatorRef,
                    binding.AttemptRef,
                    expectedStored,
                    binding.SessionBindingRef,
                    cancellationToken);
                return binding;
            },
            cancellationToken);
    }

    private async Task<T> InTransactionAsync<T>(
        Func<SqliteConnection, SqliteTransaction, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await operation(connection, transaction);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<Attempt> CreateAttemptCoreAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CreateAttemptCommand command,
        CancellationToken cancellationToken)
    {
        Validate(command.ProjectRef, nameof(command.ProjectRef));
        Validate(command.AuthenticatedOperatorRef, nameof(command.AuthenticatedOperatorRef));
        Validate(command.AttemptRef, nameof(command.AttemptRef));
        Validate(command.AssignmentRef, nameof(command.AssignmentRef));
        Validate(command.EffectiveRevisionRef, nameof(command.EffectiveRevisionRef));
        var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO b1_attempts(id,project_id,assignment_id,effective_revision_id,created_at)
            SELECT $attempt,$project,a.id,r.id,$created
            FROM b1_project_governance g
            JOIN b1_assignments a ON a.project_id=g.project_id AND a.id=$assignment
            JOIN b1_revisions r
              ON r.project_id=a.project_id AND r.assignment_id=a.id AND r.id=$revision
            WHERE g.project_id=$project
              AND g.bootstrap_user_principal=$operator
              AND NOT EXISTS(
                  SELECT 1 FROM b1_assignments replacement
                  WHERE replacement.project_id=a.project_id AND replacement.replaces_assignment_id=a.id)
              AND NOT EXISTS(
                  SELECT 1 FROM b1_revisions successor
                  WHERE successor.project_id=r.project_id AND successor.prior_revision_id=r.id)
              AND NOT EXISTS(
                  SELECT 1 FROM b1_revision_dispositions disposition
                  WHERE disposition.project_id=r.project_id AND disposition.revision_id=r.id);
            """;
        Add(insert,
            ("$attempt", command.AttemptRef.Value.ToString()),
            ("$project", command.ProjectRef.Value.ToString()),
            ("$assignment", command.AssignmentRef.Value.ToString()),
            ("$revision", command.EffectiveRevisionRef.Value.ToString()),
            ("$operator", command.AuthenticatedOperatorRef.Value),
            ("$created", command.CreatedAt.ToString("O", CultureInfo.InvariantCulture)));
        if (await insert.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            await RequireBootstrapOperatorAsync(
                connection, transaction, command.ProjectRef, command.AuthenticatedOperatorRef, cancellationToken);
            await ThrowAttemptCreationFailureAsync(connection, transaction, command, cancellationToken);
        }

        var routing = connection.CreateCommand();
        routing.Transaction = transaction;
        routing.CommandText = """
            INSERT INTO b1_attempt_routing(
                attempt_id,project_id,selected_handoff_id,selected_session_binding_id)
            VALUES($attempt,$project,NULL,NULL);
            """;
        Add(routing,
            ("$attempt", command.AttemptRef.Value.ToString()),
            ("$project", command.ProjectRef.Value.ToString()));
        await routing.ExecuteNonQueryAsync(cancellationToken);

        return new Attempt(
            command.AttemptRef,
            command.AssignmentRef,
            command.EffectiveRevisionRef,
            command.CreatedAt);
    }

    private static async Task<SessionBinding> CreateSessionBindingCoreAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CreateSessionBindingCommand command,
        CancellationToken cancellationToken)
    {
        var binding = command.SessionBinding;
        Validate(command.ProjectRef, nameof(command.ProjectRef));
        Validate(command.AuthenticatedOperatorRef, nameof(command.AuthenticatedOperatorRef));
        Validate(binding.SessionBindingRef, nameof(binding.SessionBindingRef));
        Validate(binding.AttemptRef, nameof(binding.AttemptRef));
        Validate(binding.LogicalActorRef, nameof(binding.LogicalActorRef));
        Validate(binding.ExternalSessionRef, nameof(binding.ExternalSessionRef));
        var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO b1_session_bindings(
                id,project_id,attempt_id,logical_actor_id,external_session_ref,created_at)
            SELECT $binding,$project,attempt.id,assignment.assignee_actor_id,$external,$created
            FROM b1_project_governance governance
            JOIN b1_attempts attempt
              ON attempt.project_id=governance.project_id AND attempt.id=$attempt
            JOIN b1_assignments assignment
              ON assignment.project_id=attempt.project_id AND assignment.id=attempt.assignment_id
            WHERE governance.project_id=$project
              AND governance.bootstrap_user_principal=$operator
              AND assignment.assignee_actor_id=$actor;
            """;
        Add(insert,
            ("$binding", binding.SessionBindingRef.Value.ToString()),
            ("$project", command.ProjectRef.Value.ToString()),
            ("$attempt", binding.AttemptRef.Value.ToString()),
            ("$actor", binding.LogicalActorRef.Value.ToString()),
            ("$operator", command.AuthenticatedOperatorRef.Value),
            ("$external", binding.ExternalSessionRef.Value),
            ("$created", binding.CreatedAt.ToString("O", CultureInfo.InvariantCulture)));
        if (await insert.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            await RequireBootstrapOperatorAsync(
                connection, transaction, command.ProjectRef, command.AuthenticatedOperatorRef, cancellationToken);
            throw Failure(
                B1FailureCode.InvalidReference,
                "The SessionBinding must belong to the Attempt and its immutable Assignment assignee.");
        }

        return binding;
    }

    private static async Task SelectAttemptCoreAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SelectCurrentAttemptCommand command,
        CancellationToken cancellationToken)
    {
        Validate(command.ProjectRef, nameof(command.ProjectRef));
        Validate(command.AuthenticatedOperatorRef, nameof(command.AuthenticatedOperatorRef));
        Validate(command.AssignmentRef, nameof(command.AssignmentRef));
        Validate(command.ExpectedStoredAttemptRef, nameof(command.ExpectedStoredAttemptRef));
        Validate(command.SelectedAttemptRef, nameof(command.SelectedAttemptRef));
        var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE b1_assignment_routing
            SET selected_attempt_id=$selected
            WHERE assignment_id=$assignment
              AND project_id=$project
              AND EXISTS(
                  SELECT 1 FROM b1_project_governance governance
                  WHERE governance.project_id=$project
                    AND governance.bootstrap_user_principal=$operator)
              AND (
                  ($expected IS NULL AND selected_attempt_id IS NULL) OR
                  ($expected IS NOT NULL AND selected_attempt_id=$expected))
              AND (
                  $selected IS NULL OR EXISTS(
                      SELECT 1 FROM b1_attempts attempt
                      WHERE attempt.id=$selected
                        AND attempt.project_id=$project
                        AND attempt.assignment_id=$assignment));
            """;
        Add(update,
            ("$selected", command.SelectedAttemptRef?.Value.ToString()),
            ("$assignment", command.AssignmentRef.Value.ToString()),
            ("$project", command.ProjectRef.Value.ToString()),
            ("$operator", command.AuthenticatedOperatorRef.Value),
            ("$expected", command.ExpectedStoredAttemptRef?.Value.ToString()));
        if (await update.ExecuteNonQueryAsync(cancellationToken) == 1)
        {
            return;
        }

        await RequireBootstrapOperatorAsync(
            connection, transaction, command.ProjectRef, command.AuthenticatedOperatorRef, cancellationToken);

        if (command.SelectedAttemptRef is { } selected && !await ExistsAsync(
                connection,
                transaction,
                """
                SELECT 1 FROM b1_attempts
                WHERE id=$selected AND project_id=$project AND assignment_id=$assignment;
                """,
                cancellationToken,
                ("$selected", selected.Value.ToString()),
                ("$project", command.ProjectRef.Value.ToString()),
                ("$assignment", command.AssignmentRef.Value.ToString())))
        {
            throw Failure(B1FailureCode.InvalidReference, "The selected Attempt is not owned by the Assignment.");
        }

        if (!await ExistsAsync(
                connection,
                transaction,
                "SELECT 1 FROM b1_assignment_routing WHERE assignment_id=$assignment AND project_id=$project;",
                cancellationToken,
                ("$assignment", command.AssignmentRef.Value.ToString()),
                ("$project", command.ProjectRef.Value.ToString())))
        {
            throw Failure(B1FailureCode.InvalidReference, "The Assignment routing identity does not exist.");
        }

        throw Failure(B1FailureCode.StaleRoutingSelection, "The stored Attempt selection changed.");
    }

    private static async Task SelectBindingCoreAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ProjectRef projectRef,
        UserPrincipalRef operatorRef,
        AttemptRef attemptRef,
        SessionBindingRef? expected,
        SessionBindingRef? selected,
        CancellationToken cancellationToken)
    {
        Validate(projectRef, nameof(projectRef));
        Validate(operatorRef, nameof(operatorRef));
        Validate(attemptRef, nameof(attemptRef));
        Validate(expected, nameof(expected));
        Validate(selected, nameof(selected));
        var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE b1_attempt_routing
            SET selected_session_binding_id=$selected
            WHERE attempt_id=$attempt
              AND project_id=$project
              AND EXISTS(
                  SELECT 1 FROM b1_project_governance governance
                  WHERE governance.project_id=$project
                    AND governance.bootstrap_user_principal=$operator)
              AND (
                  ($expected IS NULL AND selected_session_binding_id IS NULL) OR
                  ($expected IS NOT NULL AND selected_session_binding_id=$expected))
              AND (
                  $selected IS NULL OR EXISTS(
                      SELECT 1 FROM b1_session_bindings binding
                      WHERE binding.id=$selected
                        AND binding.project_id=$project
                        AND binding.attempt_id=$attempt));
            """;
        Add(update,
            ("$selected", selected?.Value.ToString()),
            ("$attempt", attemptRef.Value.ToString()),
            ("$project", projectRef.Value.ToString()),
            ("$operator", operatorRef.Value),
            ("$expected", expected?.Value.ToString()));
        if (await update.ExecuteNonQueryAsync(cancellationToken) == 1)
        {
            return;
        }

        await RequireBootstrapOperatorAsync(
            connection, transaction, projectRef, operatorRef, cancellationToken);

        if (selected is { } selectedRef && !await ExistsAsync(
                connection,
                transaction,
                """
                SELECT 1 FROM b1_session_bindings
                WHERE id=$selected AND project_id=$project AND attempt_id=$attempt;
                """,
                cancellationToken,
                ("$selected", selectedRef.Value.ToString()),
                ("$project", projectRef.Value.ToString()),
                ("$attempt", attemptRef.Value.ToString())))
        {
            throw Failure(B1FailureCode.InvalidReference, "The selected SessionBinding is not owned by the Attempt.");
        }

        if (!await ExistsAsync(
                connection,
                transaction,
                "SELECT 1 FROM b1_attempt_routing WHERE attempt_id=$attempt AND project_id=$project;",
                cancellationToken,
                ("$attempt", attemptRef.Value.ToString()),
                ("$project", projectRef.Value.ToString())))
        {
            throw Failure(B1FailureCode.InvalidReference, "The Attempt routing identity does not exist.");
        }

        throw Failure(B1FailureCode.StaleRoutingSelection, "The stored SessionBinding selection changed.");
    }

    private static async Task ThrowAttemptCreationFailureAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CreateAttemptCommand command,
        CancellationToken cancellationToken)
    {
        var parameters = new[]
        {
            ("$project", (object?)command.ProjectRef.Value.ToString()),
            ("$assignment", (object?)command.AssignmentRef.Value.ToString()),
            ("$revision", (object?)command.EffectiveRevisionRef.Value.ToString())
        };
        if (!await ExistsAsync(
                connection,
                transaction,
                """
                SELECT 1 FROM b1_assignments assignment
                JOIN b1_revisions revision
                  ON revision.project_id=assignment.project_id
                 AND revision.assignment_id=assignment.id
                WHERE assignment.project_id=$project
                  AND assignment.id=$assignment
                  AND revision.id=$revision;
                """,
                cancellationToken,
                parameters))
        {
            throw Failure(B1FailureCode.InvalidReference, "The Assignment or Revision does not belong to the Project.");
        }

        if (await ExistsAsync(
                connection,
                transaction,
                """
                SELECT 1 FROM b1_revision_dispositions
                WHERE project_id=$project AND assignment_id=$assignment AND revision_id=$revision;
                """,
                cancellationToken,
                parameters))
        {
            throw Failure(B1FailureCode.AlreadyDispositioned, "The effective Revision already has a disposition.");
        }

        if (await ExistsAsync(
                connection,
                transaction,
                """
                SELECT 1
                FROM b1_revisions current
                WHERE current.project_id=$project
                  AND current.assignment_id=$assignment
                  AND NOT EXISTS(
                      SELECT 1 FROM b1_revisions successor
                      WHERE successor.project_id=current.project_id
                        AND successor.prior_revision_id=current.id)
                  AND current.id<>$revision;
                """,
                cancellationToken,
                parameters))
        {
            throw Failure(B1FailureCode.StaleRevision, "The requested Revision is not current.");
        }

        throw Failure(B1FailureCode.InvalidReference, "The Assignment is no longer a current delegation.");
    }

    private static async Task RequireBootstrapOperatorAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ProjectRef projectRef,
        UserPrincipalRef operatorRef,
        CancellationToken cancellationToken)
    {
        if (!await ExistsAsync(
                connection,
                transaction,
                """
                SELECT 1 FROM b1_project_governance
                WHERE project_id=$project AND bootstrap_user_principal=$operator;
                """,
                cancellationToken,
                ("$project", projectRef.Value.ToString()),
                ("$operator", operatorRef.Value)))
        {
            throw Failure(B1FailureCode.NotAuthorized, "Only the Project bootstrap principal may operate B1 routing.");
        }
    }

    private static async Task<bool> ExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        Add(command, parameters);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static void Add(SqliteCommand command, params (string Name, object? Value)[] parameters)
    {
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
    }

    private static void Validate(ProjectRef value, string name)
    {
        if (value.Value == Guid.Empty) throw new ArgumentException("ProjectRef is required.", name);
    }

    private static void Validate(AssignmentRef value, string name)
    {
        if (value.Value == Guid.Empty) throw new ArgumentException("AssignmentRef is required.", name);
    }

    private static void Validate(RevisionRef value, string name)
    {
        if (value.Value == Guid.Empty) throw new ArgumentException("RevisionRef is required.", name);
    }

    private static void Validate(AttemptRef value, string name)
    {
        if (value.Value == Guid.Empty) throw new ArgumentException("AttemptRef is required.", name);
    }

    private static void Validate(AttemptRef? value, string name)
    {
        if (value is { Value: var id } && id == Guid.Empty) throw new ArgumentException("AttemptRef is required.", name);
    }

    private static void Validate(SessionBindingRef value, string name)
    {
        if (value.Value == Guid.Empty) throw new ArgumentException("SessionBindingRef is required.", name);
    }

    private static void Validate(SessionBindingRef? value, string name)
    {
        if (value is { Value: var id } && id == Guid.Empty) throw new ArgumentException("SessionBindingRef is required.", name);
    }

    private static void Validate(LogicalActorRef value, string name)
    {
        if (value.Value == Guid.Empty) throw new ArgumentException("LogicalActorRef is required.", name);
    }

    private static void Validate(UserPrincipalRef value, string name)
    {
        if (string.IsNullOrWhiteSpace(value.Value)) throw new ArgumentException("UserPrincipalRef is required.", name);
    }

    private static void Validate(ExternalSessionRef value, string name)
    {
        if (string.IsNullOrWhiteSpace(value.Value)) throw new ArgumentException("ExternalSessionRef is required.", name);
    }

    private static B1CommandException Failure(B1FailureCode code, string message) => new(code, message);
}
