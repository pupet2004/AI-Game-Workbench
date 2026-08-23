using System.Globalization;
using Microsoft.Data.Sqlite;
using Workbench.Core.Continuity;
using Workbench.Core.Projects;
using Workbench.Storage.Database;

namespace Workbench.Storage.Continuity;

public sealed class B1ProjectGovernanceRepository(WorkbenchDatabase database)
{
    private readonly WorkbenchDatabase _database =
        database ?? throw new ArgumentNullException(nameof(database));

    public async Task<ProjectGovernance> CreateGovernedProjectAsync(
        Project project,
        UserPrincipalRef bootstrapPrincipal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        Require(bootstrapPrincipal, nameof(bootstrapPrincipal));
        var governance = new ProjectGovernance(
            new ProjectRef(project.Id),
            bootstrapPrincipal,
            B1GovernanceOrigin.Created,
            adoptedAt: null,
            lastProjectCommitSequence: 0);

        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var exists = connection.CreateCommand();
            exists.Transaction = transaction;
            exists.CommandText = "SELECT EXISTS(SELECT 1 FROM projects WHERE id=$project);";
            exists.Parameters.AddWithValue("$project", project.Id.ToString());
            if (Convert.ToInt64(await exists.ExecuteScalarAsync(cancellationToken)) != 0)
            {
                throw Failure(
                    B1FailureCode.GovernanceAlreadyExists,
                    "The Project already exists and cannot be recreated as a governed Project.");
            }

            var insertProject = connection.CreateCommand();
            insertProject.Transaction = transaction;
            insertProject.CommandText = """
                INSERT INTO projects(id,name,root_path,project_type,git_root,created_at,last_opened_at)
                VALUES($id,$name,$root,$type,$git,$created,$opened);
                """;
            insertProject.Parameters.AddWithValue("$id", project.Id.ToString());
            insertProject.Parameters.AddWithValue("$name", project.Name);
            insertProject.Parameters.AddWithValue("$root", project.RootPath);
            insertProject.Parameters.AddWithValue("$type", (int)project.Type);
            insertProject.Parameters.AddWithValue("$git", (object?)project.GitRoot ?? DBNull.Value);
            insertProject.Parameters.AddWithValue(
                "$created", project.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
            insertProject.Parameters.AddWithValue(
                "$opened", project.LastOpenedAt.ToString("O", CultureInfo.InvariantCulture));
            await insertProject.ExecuteNonQueryAsync(cancellationToken);

            await InsertGovernanceAsync(connection, transaction, governance, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return governance;
        }
        catch (B1CommandException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw Failure(
                B1FailureCode.GovernanceAlreadyExists,
                "The governed Project conflicts with an existing Project or governance root.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<ProjectGovernance> AdoptLegacyProjectAsync(
        ProjectRef projectRef,
        UserPrincipalRef authenticatedPrincipal,
        DateTimeOffset adoptedAt,
        CancellationToken cancellationToken = default)
    {
        Require(projectRef, nameof(projectRef));
        Require(authenticatedPrincipal, nameof(authenticatedPrincipal));
        var governance = new ProjectGovernance(
            projectRef,
            authenticatedPrincipal,
            B1GovernanceOrigin.Adopted,
            adoptedAt,
            lastProjectCommitSequence: 0);

        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            if (await ExistsAsync(
                    connection,
                    transaction,
                    "b1_project_governance",
                    projectRef,
                    cancellationToken))
            {
                throw Failure(
                    B1FailureCode.GovernanceAlreadyExists,
                    "The Project already has a B1 governance root.");
            }

            var eligibility = connection.CreateCommand();
            eligibility.Transaction = transaction;
            eligibility.CommandText = """
                SELECT
                    EXISTS(
                        SELECT 1
                        FROM b1_legacy_project_origins
                        WHERE project_id=$project AND source_schema_version=19),
                    EXISTS(
                        SELECT 1 FROM b1_logical_actors WHERE project_id=$project
                        UNION ALL SELECT 1 FROM b1_responsibilities WHERE project_id=$project
                        UNION ALL SELECT 1 FROM b1_assignments WHERE project_id=$project
                        UNION ALL SELECT 1 FROM b1_authority_decisions WHERE project_id=$project);
                """;
            eligibility.Parameters.AddWithValue("$project", projectRef.Value.ToString());
            await using (var reader = await eligibility.ExecuteReaderAsync(cancellationToken))
            {
                if (!await reader.ReadAsync(cancellationToken) || reader.GetInt64(0) != 1 || reader.GetInt64(1) != 0)
                {
                    throw Failure(
                        B1FailureCode.LegacyProjectNotEligible,
                        "The Project lacks mechanical pre-B1 origin or already has B1 governance history.");
                }
            }

            await InsertGovernanceAsync(connection, transaction, governance, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return governance;
        }
        catch (B1CommandException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw Failure(
                B1FailureCode.GovernanceAlreadyExists,
                "The Project acquired a B1 governance root concurrently.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<ProjectGovernance?> GetAsync(
        ProjectRef projectRef,
        CancellationToken cancellationToken = default)
    {
        Require(projectRef, nameof(projectRef));
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT bootstrap_user_principal,origin,adopted_at,last_commit_sequence
            FROM b1_project_governance
            WHERE project_id=$project;
            """;
        command.Parameters.AddWithValue("$project", projectRef.Value.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ProjectGovernance(
            projectRef,
            new UserPrincipalRef(reader.GetString(0)),
            Enum.Parse<B1GovernanceOrigin>(reader.GetString(1), ignoreCase: false),
            reader.IsDBNull(2)
                ? null
                : DateTimeOffset.Parse(
                    reader.GetString(2),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind),
            reader.GetInt64(3));
    }

    private static async Task InsertGovernanceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ProjectGovernance governance,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO b1_project_governance(
                project_id,bootstrap_user_principal,origin,adopted_at,last_commit_sequence)
            VALUES($project,$principal,$origin,$adopted,0);
            """;
        command.Parameters.AddWithValue("$project", governance.ProjectRef.Value.ToString());
        command.Parameters.AddWithValue("$principal", governance.BootstrapPrincipalRef.Value);
        command.Parameters.AddWithValue("$origin", governance.Origin.ToString());
        command.Parameters.AddWithValue(
            "$adopted",
            governance.AdoptedAt is null
                ? DBNull.Value
                : governance.AdoptedAt.Value.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> ExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        ProjectRef projectRef,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT EXISTS(SELECT 1 FROM {table} WHERE project_id=$project);";
        command.Parameters.AddWithValue("$project", projectRef.Value.ToString());
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) != 0;
    }

    private static void Require(ProjectRef projectRef, string parameterName)
    {
        if (projectRef.Value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty Project reference is required.", parameterName);
        }
    }

    private static void Require(UserPrincipalRef principalRef, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(principalRef.Value))
        {
            throw new ArgumentException("A nonblank UserPrincipal reference is required.", parameterName);
        }
    }

    private static B1CommandException Failure(B1FailureCode code, string message) => new(code, message);
}
