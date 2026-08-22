using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Workbench.Core.Continuity;
using Workbench.Storage.Database;

namespace Workbench.Storage.Continuity;

public sealed class B1ClaimHandoffRepository(WorkbenchDatabase database)
{
    private readonly WorkbenchDatabase _database =
        database ?? throw new ArgumentNullException(nameof(database));

    public Task<Claim> RecordClaimAsync(
        RecordClaimCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.ClaimantRef);
        ArgumentNullException.ThrowIfNull(command.Payload);
        ArgumentNullException.ThrowIfNull(command.EvidenceRefs);
        return InTransactionAsync(
            (connection, transaction) => RecordClaimCoreAsync(
                connection, transaction, command, cancellationToken),
            cancellationToken);
    }

    public Task<Handoff> CreateHandoffAsync(
        CreateHandoffCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Handoff);
        return InTransactionAsync(
            (connection, transaction) => CreateHandoffCoreAsync(
                connection, transaction, command, cancellationToken),
            cancellationToken);
    }

    public Task SelectContinuationHandoffAsync(
        SelectContinuationHandoffCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return InTransactionAsync(
            async (connection, transaction) =>
            {
                await SelectHandoffCoreAsync(connection, transaction, command, cancellationToken);
                return true;
            },
            cancellationToken);
    }

    public Task<Handoff> CreateHandoffAndSelectAsync(
        CreateHandoffCommand create,
        HandoffRef? expectedStored,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(create.Handoff);
        return InTransactionAsync(
            async (connection, transaction) =>
            {
                var handoff = await CreateHandoffCoreAsync(
                    connection, transaction, create, cancellationToken);
                await SelectHandoffCoreAsync(
                    connection,
                    transaction,
                    new SelectContinuationHandoffCommand(
                        create.ProjectRef,
                        create.AuthenticatedOperatorRef,
                        create.Handoff.AttemptRef,
                        expectedStored,
                        create.Handoff.HandoffRef),
                    cancellationToken);
                return handoff;
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

    private static async Task<Claim> RecordClaimCoreAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        RecordClaimCommand command,
        CancellationToken cancellationToken)
    {
        await RequireBootstrapOperatorAsync(
            connection, transaction, command.ProjectRef, command.AuthenticatedOperatorRef, cancellationToken);
        await ValidateClaimOwnershipAsync(connection, transaction, command, cancellationToken);
        var columns = await CreateClaimColumnsAsync(connection, transaction, command, cancellationToken);

        var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO b1_claims(
                id,project_id,claimant_kind,claimant_user_principal,claimant_actor_id,
                source_binding_id,kind,statement,proposed_scope_kind,proposed_scope_project_id,
                proposed_scope_responsibility_id,proposed_scope_assignment_id,
                proposed_supersedes_contribution_id,proposed_assignment_id,base_revision_id,
                proposed_work_contract,proposed_delegated_authority_json,evidence_refs_json,created_at)
            VALUES(
                $id,$project,$claimant_kind,$claimant_user,$claimant_actor,$source_binding,
                $kind,$statement,$scope_kind,$scope_project,$scope_responsibility,$scope_assignment,
                $supersedes,$proposed_assignment,$base_revision,$work_contract,$authority,
                $evidence,$created);
            """;
        Add(insert,
            ("$id", command.ClaimRef.Value.ToString()),
            ("$project", command.ProjectRef.Value.ToString()),
            ("$claimant_kind", columns.ClaimantKind),
            ("$claimant_user", columns.ClaimantUser),
            ("$claimant_actor", columns.ClaimantActor),
            ("$source_binding", command.SourceSessionBindingRef?.Value.ToString()),
            ("$kind", columns.Kind),
            ("$statement", columns.Statement),
            ("$scope_kind", columns.ScopeKind),
            ("$scope_project", columns.ScopeProject),
            ("$scope_responsibility", columns.ScopeResponsibility),
            ("$scope_assignment", columns.ScopeAssignment),
            ("$supersedes", columns.Supersedes),
            ("$proposed_assignment", columns.ProposedAssignment),
            ("$base_revision", columns.BaseRevision),
            ("$work_contract", columns.WorkContract),
            ("$authority", columns.AuthorityJson),
            ("$evidence", SerializeEvidence(command.EvidenceRefs)),
            ("$created", command.CreatedAt.ToString("O", CultureInfo.InvariantCulture)));
        await insert.ExecuteNonQueryAsync(cancellationToken);

        return await LoadClaimAsync(
                   connection, transaction, command.ProjectRef, command.ClaimRef, cancellationToken)
               ?? throw new InvalidDataException("The inserted Claim could not be reloaded.");
    }

    private static async Task<Handoff> CreateHandoffCoreAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CreateHandoffCommand command,
        CancellationToken cancellationToken)
    {
        await RequireBootstrapOperatorAsync(
            connection, transaction, command.ProjectRef, command.AuthenticatedOperatorRef, cancellationToken);
        var handoff = command.Handoff;
        var attempt = await LoadAttemptOwnershipAsync(
            connection, transaction, command.ProjectRef, handoff.AttemptRef, cancellationToken);

        var result = await LoadClaimFactsAsync(
            connection, transaction, command.ProjectRef, handoff.ResultClaimRef, cancellationToken);
        if (result.Kind != "Result" || result.ClaimantKind != "LogicalActor" ||
            !string.Equals(result.ClaimantActor, attempt.AssigneeActor, StringComparison.Ordinal))
        {
            throw Failure(
                B1FailureCode.InvalidReference,
                "The primary Result Claim must belong to the immutable Assignment assignee.");
        }

        if (result.SourceBinding is not null && !await ExistsAsync(
                connection,
                transaction,
                """
                SELECT 1 FROM b1_session_bindings
                WHERE id=$binding AND project_id=$project AND attempt_id=$attempt;
                """,
                cancellationToken,
                ("$binding", result.SourceBinding),
                ("$project", command.ProjectRef.Value.ToString()),
                ("$attempt", handoff.AttemptRef.Value.ToString())))
        {
            throw Failure(
                B1FailureCode.InvalidReference,
                "The primary Result Claim SessionBinding must belong to the Handoff Attempt.");
        }

        await ValidateTypedClaimsAsync(
            connection, transaction, command.ProjectRef, handoff.ValidationClaimRefs,
            "Validation", attempt, packagedRevision: false, cancellationToken);
        await ValidateTypedClaimsAsync(
            connection, transaction, command.ProjectRef, handoff.UnresolvedIssueClaimRefs,
            "UnresolvedIssue", attempt, packagedRevision: false, cancellationToken);
        await ValidateTypedClaimsAsync(
            connection, transaction, command.ProjectRef, handoff.ProposedContributionClaimRefs,
            "ProposedStateContribution", attempt, packagedRevision: false, cancellationToken);
        await ValidateTypedClaimsAsync(
            connection, transaction, command.ProjectRef, handoff.ProposedAssignmentRevisionClaimRefs,
            "ProposedAssignmentRevision", attempt, packagedRevision: true, cancellationToken);

        var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO b1_handoffs(
                id,project_id,attempt_id,result_claim_id,evidence_refs_json,created_at)
            VALUES($id,$project,$attempt,$result,$evidence,$created);
            """;
        Add(insert,
            ("$id", handoff.HandoffRef.Value.ToString()),
            ("$project", command.ProjectRef.Value.ToString()),
            ("$attempt", handoff.AttemptRef.Value.ToString()),
            ("$result", handoff.ResultClaimRef.Value.ToString()),
            ("$evidence", SerializeEvidence(handoff.EvidenceRefs)),
            ("$created", handoff.CreatedAt.ToString("O", CultureInfo.InvariantCulture)));
        await insert.ExecuteNonQueryAsync(cancellationToken);

        await InsertClaimRefsAsync(
            connection, transaction, command.ProjectRef, handoff.HandoffRef,
            "Validation", handoff.ValidationClaimRefs, cancellationToken);
        await InsertClaimRefsAsync(
            connection, transaction, command.ProjectRef, handoff.HandoffRef,
            "UnresolvedIssue", handoff.UnresolvedIssueClaimRefs, cancellationToken);
        await InsertClaimRefsAsync(
            connection, transaction, command.ProjectRef, handoff.HandoffRef,
            "ProposedStateContribution", handoff.ProposedContributionClaimRefs, cancellationToken);
        await InsertClaimRefsAsync(
            connection, transaction, command.ProjectRef, handoff.HandoffRef,
            "ProposedAssignmentRevision", handoff.ProposedAssignmentRevisionClaimRefs, cancellationToken);

        return await LoadHandoffAsync(
                   connection, transaction, command.ProjectRef, handoff.HandoffRef, cancellationToken)
               ?? throw new InvalidDataException("The inserted Handoff could not be reloaded.");
    }

    private static async Task SelectHandoffCoreAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SelectContinuationHandoffCommand command,
        CancellationToken cancellationToken)
    {
        var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE b1_attempt_routing
            SET selected_handoff_id=$selected
            WHERE attempt_id=$attempt
              AND project_id=$project
              AND EXISTS(
                  SELECT 1 FROM b1_project_governance governance
                  WHERE governance.project_id=$project
                    AND governance.bootstrap_user_principal=$operator)
              AND (
                  ($expected IS NULL AND selected_handoff_id IS NULL) OR
                  ($expected IS NOT NULL AND selected_handoff_id=$expected))
              AND (
                  $selected IS NULL OR EXISTS(
                      SELECT 1 FROM b1_handoffs handoff
                      WHERE handoff.id=$selected
                        AND handoff.project_id=$project
                        AND handoff.attempt_id=$attempt));
            """;
        Add(update,
            ("$selected", command.SelectedHandoffRef?.Value.ToString()),
            ("$attempt", command.AttemptRef.Value.ToString()),
            ("$project", command.ProjectRef.Value.ToString()),
            ("$operator", command.AuthenticatedOperatorRef.Value),
            ("$expected", command.ExpectedStoredHandoffRef?.Value.ToString()));
        if (await update.ExecuteNonQueryAsync(cancellationToken) == 1)
        {
            return;
        }

        await RequireBootstrapOperatorAsync(
            connection, transaction, command.ProjectRef, command.AuthenticatedOperatorRef, cancellationToken);
        if (command.SelectedHandoffRef is { } selected && !await ExistsAsync(
                connection,
                transaction,
                """
                SELECT 1 FROM b1_handoffs
                WHERE id=$selected AND project_id=$project AND attempt_id=$attempt;
                """,
                cancellationToken,
                ("$selected", selected.Value.ToString()),
                ("$project", command.ProjectRef.Value.ToString()),
                ("$attempt", command.AttemptRef.Value.ToString())))
        {
            throw Failure(B1FailureCode.InvalidReference, "The selected Handoff is not owned by the Attempt.");
        }

        if (!await ExistsAsync(
                connection,
                transaction,
                "SELECT 1 FROM b1_attempt_routing WHERE attempt_id=$attempt AND project_id=$project;",
                cancellationToken,
                ("$attempt", command.AttemptRef.Value.ToString()),
                ("$project", command.ProjectRef.Value.ToString())))
        {
            throw Failure(B1FailureCode.InvalidReference, "The Attempt routing identity does not exist.");
        }

        throw Failure(B1FailureCode.StaleRoutingSelection, "The stored Handoff selection changed.");
    }

    private static async Task ValidateClaimOwnershipAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        RecordClaimCommand command,
        CancellationToken cancellationToken)
    {
        switch (command.ClaimantRef)
        {
            case ClaimantRef.UserPrincipal user:
                if (command.SourceSessionBindingRef is not null ||
                    !string.Equals(
                        user.UserPrincipalRef.Value,
                        command.AuthenticatedOperatorRef.Value,
                        StringComparison.Ordinal))
                {
                    throw Failure(
                        B1FailureCode.InvalidReference,
                        "A UserPrincipal Claim must belong to the bootstrap principal and cannot carry SessionBinding provenance.");
                }
                break;

            case ClaimantRef.LogicalActor actor:
                if (!await ExistsAsync(
                        connection,
                        transaction,
                        "SELECT 1 FROM b1_logical_actors WHERE id=$actor AND project_id=$project;",
                        cancellationToken,
                        ("$actor", actor.LogicalActorRef.Value.ToString()),
                        ("$project", command.ProjectRef.Value.ToString())))
                {
                    throw Failure(B1FailureCode.InvalidReference, "The Claimant LogicalActor does not belong to the Project.");
                }

                if (command.SourceSessionBindingRef is { } binding && !await ExistsAsync(
                        connection,
                        transaction,
                        """
                        SELECT 1 FROM b1_session_bindings
                        WHERE id=$binding AND project_id=$project AND logical_actor_id=$actor;
                        """,
                        cancellationToken,
                        ("$binding", binding.Value.ToString()),
                        ("$project", command.ProjectRef.Value.ToString()),
                        ("$actor", actor.LogicalActorRef.Value.ToString())))
                {
                    throw Failure(
                        B1FailureCode.InvalidReference,
                        "The Claim SessionBinding provenance does not belong to its LogicalActor Claimant.");
                }
                break;

            default:
                throw Failure(B1FailureCode.InvalidReference, "Unknown Claimant kind.");
        }
    }

    private static async Task<ClaimColumns> CreateClaimColumnsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        RecordClaimCommand command,
        CancellationToken cancellationToken)
    {
        var claimantKind = command.ClaimantRef is ClaimantRef.UserPrincipal
            ? "UserPrincipal"
            : "LogicalActor";
        var claimantUser = (command.ClaimantRef as ClaimantRef.UserPrincipal)?.UserPrincipalRef.Value;
        var claimantActor = (command.ClaimantRef as ClaimantRef.LogicalActor)?.LogicalActorRef.Value.ToString();

        return command.Payload switch
        {
            ClaimPayload.Result result => ClaimColumns.StatementOnly(
                claimantKind, claimantUser, claimantActor, "Result", result.Statement),
            ClaimPayload.Validation validation => ClaimColumns.StatementOnly(
                claimantKind, claimantUser, claimantActor, "Validation", validation.Statement),
            ClaimPayload.UnresolvedIssue issue => ClaimColumns.StatementOnly(
                claimantKind, claimantUser, claimantActor, "UnresolvedIssue", issue.Statement),
            ClaimPayload.ProposedStateContribution contribution =>
                await CreateContributionColumnsAsync(
                    connection,
                    transaction,
                    command.ProjectRef,
                    claimantKind,
                    claimantUser,
                    claimantActor,
                    contribution,
                    cancellationToken),
            ClaimPayload.ProposedAssignmentRevision revision =>
                await CreateRevisionColumnsAsync(
                    connection,
                    transaction,
                    command.ProjectRef,
                    claimantKind,
                    claimantUser,
                    claimantActor,
                    revision,
                    cancellationToken),
            _ => throw Failure(B1FailureCode.InvalidReference, "Unknown Claim payload kind.")
        };
    }

    private static async Task<ClaimColumns> CreateContributionColumnsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ProjectRef projectRef,
        string claimantKind,
        string? claimantUser,
        string? claimantActor,
        ClaimPayload.ProposedStateContribution contribution,
        CancellationToken cancellationToken)
    {
        string scopeKind;
        string? scopeProject = null;
        string? scopeResponsibility = null;
        string? scopeAssignment = null;
        switch (contribution.Scope)
        {
            case ContributionScopeRef.Project project when project.ProjectRef == projectRef:
                scopeKind = "Project";
                scopeProject = projectRef.Value.ToString();
                break;
            case ContributionScopeRef.Responsibility responsibility when await ExistsAsync(
                connection,
                transaction,
                "SELECT 1 FROM b1_responsibilities WHERE id=$id AND project_id=$project;",
                cancellationToken,
                ("$id", responsibility.ResponsibilityRef.Value.ToString()),
                ("$project", projectRef.Value.ToString())):
                scopeKind = "Responsibility";
                scopeResponsibility = responsibility.ResponsibilityRef.Value.ToString();
                break;
            case ContributionScopeRef.Assignment assignment when await ExistsAsync(
                connection,
                transaction,
                "SELECT 1 FROM b1_assignments WHERE id=$id AND project_id=$project;",
                cancellationToken,
                ("$id", assignment.AssignmentRef.Value.ToString()),
                ("$project", projectRef.Value.ToString())):
                scopeKind = "Assignment";
                scopeAssignment = assignment.AssignmentRef.Value.ToString();
                break;
            default:
                throw Failure(B1FailureCode.InvalidReference, "The proposed contribution scope does not belong to the Project.");
        }

        var supersedes = contribution.ProposedSupersedes?.Value.ToString();
        if (supersedes is not null && !await ExistsAsync(
                connection,
                transaction,
                "SELECT 1 FROM b1_accepted_state_contributions WHERE id=$id AND project_id=$project;",
                cancellationToken,
                ("$id", supersedes),
                ("$project", projectRef.Value.ToString())))
        {
            throw Failure(B1FailureCode.InvalidReference, "The proposed supersession target does not belong to the Project.");
        }

        return new ClaimColumns(
            claimantKind, claimantUser, claimantActor, "ProposedStateContribution",
            contribution.Statement, scopeKind, scopeProject, scopeResponsibility, scopeAssignment,
            supersedes, null, null, null, null);
    }

    private static async Task<ClaimColumns> CreateRevisionColumnsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ProjectRef projectRef,
        string claimantKind,
        string? claimantUser,
        string? claimantActor,
        ClaimPayload.ProposedAssignmentRevision revision,
        CancellationToken cancellationToken)
    {
        if (!await ExistsAsync(
                connection,
                transaction,
                """
                SELECT 1 FROM b1_revisions
                WHERE id=$revision AND project_id=$project AND assignment_id=$assignment;
                """,
                cancellationToken,
                ("$revision", revision.BaseEffectiveRevisionRef.Value.ToString()),
                ("$project", projectRef.Value.ToString()),
                ("$assignment", revision.AssignmentRef.Value.ToString())))
        {
            throw Failure(
                B1FailureCode.InvalidReference,
                "The proposed Assignment Revision target does not belong to the Project.");
        }

        return new ClaimColumns(
            claimantKind, claimantUser, claimantActor, "ProposedAssignmentRevision",
            null, null, null, null, null, null,
            revision.AssignmentRef.Value.ToString(),
            revision.BaseEffectiveRevisionRef.Value.ToString(),
            revision.ProposedContract.WorkContract,
            SerializeAuthority(revision.ProposedContract.DelegatedAuthorityBoundary));
    }

    private static async Task<Claim?> LoadClaimAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        ProjectRef projectRef,
        ClaimRef claimRef,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id,project_id,claimant_kind,claimant_user_principal,claimant_actor_id,
                   source_binding_id,kind,statement,proposed_scope_kind,proposed_scope_project_id,
                   proposed_scope_responsibility_id,proposed_scope_assignment_id,
                   proposed_supersedes_contribution_id,proposed_assignment_id,base_revision_id,
                   proposed_work_contract,proposed_delegated_authority_json,evidence_refs_json,created_at
            FROM b1_claims
            WHERE id=$id AND project_id=$project;
            """;
        Add(command,
            ("$id", claimRef.Value.ToString()),
            ("$project", projectRef.Value.ToString()));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        try
        {
            var project = new ProjectRef(Guid.Parse(reader.GetString(1)));
            var claimant = ReadClaimant(reader.GetString(2), GetNullableString(reader, 3), GetNullableString(reader, 4));
            var sourceBinding = ParseOptional<SessionBindingRef>(GetNullableString(reader, 5), value =>
                new SessionBindingRef(Guid.Parse(value)));
            var payload = ReadPayload(reader, project);
            var evidence = ReadEvidence(reader.GetString(17));
            var createdAt = DateTimeOffset.Parse(reader.GetString(18), CultureInfo.InvariantCulture);
            return new Claim(
                new ClaimRef(Guid.Parse(reader.GetString(0))),
                project,
                claimant,
                sourceBinding,
                payload,
                evidence,
                createdAt);
        }
        catch (Exception exception) when (
            exception is ArgumentException or FormatException or JsonException or InvalidOperationException)
        {
            throw new InvalidDataException("Stored B1 Claim data is malformed or uses an unknown extension.", exception);
        }
    }

    private static ClaimantRef ReadClaimant(string kind, string? user, string? actor) => kind switch
    {
        "UserPrincipal" when user is not null && actor is null =>
            new ClaimantRef.UserPrincipal(new UserPrincipalRef(user)),
        "LogicalActor" when user is null && actor is not null =>
            new ClaimantRef.LogicalActor(new LogicalActorRef(Guid.Parse(actor))),
        _ => throw new InvalidDataException("Unknown or inconsistent Claimant columns.")
    };

    private static ClaimPayload ReadPayload(SqliteDataReader reader, ProjectRef projectRef)
    {
        var kind = reader.GetString(6);
        var statement = GetNullableString(reader, 7);
        var scopeKind = GetNullableString(reader, 8);
        var scopeProject = GetNullableString(reader, 9);
        var scopeResponsibility = GetNullableString(reader, 10);
        var scopeAssignment = GetNullableString(reader, 11);
        var supersedes = GetNullableString(reader, 12);
        var proposedAssignment = GetNullableString(reader, 13);
        var baseRevision = GetNullableString(reader, 14);
        var workContract = GetNullableString(reader, 15);
        var authorityJson = GetNullableString(reader, 16);

        if (kind is "Result" or "Validation" or "UnresolvedIssue")
        {
            Require(statement is not null &&
                    scopeKind is null && scopeProject is null && scopeResponsibility is null &&
                    scopeAssignment is null && supersedes is null && proposedAssignment is null &&
                    baseRevision is null && workContract is null && authorityJson is null);
            return kind switch
            {
                "Result" => new ClaimPayload.Result(statement!),
                "Validation" => new ClaimPayload.Validation(statement!),
                _ => new ClaimPayload.UnresolvedIssue(statement!)
            };
        }

        if (kind == "ProposedStateContribution")
        {
            Require(statement is not null && proposedAssignment is null && baseRevision is null &&
                    workContract is null && authorityJson is null);
            ContributionScopeRef scope = scopeKind switch
            {
                "Project" when scopeProject == projectRef.Value.ToString() &&
                               scopeResponsibility is null && scopeAssignment is null =>
                    new ContributionScopeRef.Project(projectRef),
                "Responsibility" when scopeProject is null && scopeResponsibility is not null &&
                                      scopeAssignment is null =>
                    new ContributionScopeRef.Responsibility(
                        new ResponsibilityRef(Guid.Parse(scopeResponsibility))),
                "Assignment" when scopeProject is null && scopeResponsibility is null &&
                                  scopeAssignment is not null =>
                    new ContributionScopeRef.Assignment(new AssignmentRef(Guid.Parse(scopeAssignment))),
                _ => throw new InvalidDataException("Unknown or inconsistent proposed contribution scope columns.")
            };
            return new ClaimPayload.ProposedStateContribution(
                statement!,
                scope,
                ParseOptional<AcceptedStateContributionRef>(supersedes, value =>
                    new AcceptedStateContributionRef(Guid.Parse(value))));
        }

        if (kind == "ProposedAssignmentRevision")
        {
            Require(statement is null && scopeKind is null && scopeProject is null &&
                    scopeResponsibility is null && scopeAssignment is null && supersedes is null &&
                    proposedAssignment is not null && baseRevision is not null &&
                    workContract is not null && authorityJson is not null);
            return new ClaimPayload.ProposedAssignmentRevision(
                new AssignmentRef(Guid.Parse(proposedAssignment!)),
                new RevisionRef(Guid.Parse(baseRevision!)),
                new AssignmentRevisionContract(workContract!, ReadAuthority(authorityJson!)));
        }

        throw new InvalidDataException("Unknown Claim kind.");
    }

    private static async Task<Handoff?> LoadHandoffAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        ProjectRef projectRef,
        HandoffRef handoffRef,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT attempt_id,result_claim_id,evidence_refs_json,created_at
            FROM b1_handoffs
            WHERE id=$id AND project_id=$project;
            """;
        Add(command,
            ("$id", handoffRef.Value.ToString()),
            ("$project", projectRef.Value.ToString()));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        AttemptRef attemptRef;
        ClaimRef resultRef;
        IReadOnlyList<EvidenceRef> evidence;
        DateTimeOffset createdAt;
        try
        {
            attemptRef = new AttemptRef(Guid.Parse(reader.GetString(0)));
            resultRef = new ClaimRef(Guid.Parse(reader.GetString(1)));
            evidence = ReadEvidence(reader.GetString(2));
            createdAt = DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or JsonException)
        {
            throw new InvalidDataException("Stored B1 Handoff data is malformed.", exception);
        }

        await reader.DisposeAsync();
        var refs = await LoadHandoffClaimRefsAsync(
            connection, transaction, projectRef, handoffRef, cancellationToken);
        return new Handoff(
            handoffRef,
            attemptRef,
            resultRef,
            refs.Validation,
            refs.Unresolved,
            refs.Contribution,
            refs.Revision,
            evidence,
            createdAt);
    }

    private static async Task<HandoffClaimRefs> LoadHandoffClaimRefsAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        ProjectRef projectRef,
        HandoffRef handoffRef,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT role_kind,ordinal,claim_id
            FROM b1_handoff_claim_refs
            WHERE handoff_id=$handoff AND project_id=$project
            ORDER BY role_kind,ordinal;
            """;
        Add(command,
            ("$handoff", handoffRef.Value.ToString()),
            ("$project", projectRef.Value.ToString()));
        var validation = new List<ClaimRef>();
        var unresolved = new List<ClaimRef>();
        var contribution = new List<ClaimRef>();
        var revision = new List<ClaimRef>();
        var expectedOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var role = reader.GetString(0);
            var ordinal = reader.GetInt32(1);
            if (!expectedOrdinals.TryGetValue(role, out var expected)) expected = 0;
            if (ordinal != expected)
            {
                throw new InvalidDataException("Stored Handoff Claim ordinals are not contiguous.");
            }
            expectedOrdinals[role] = expected + 1;
            var claimRef = new ClaimRef(Guid.Parse(reader.GetString(2)));
            switch (role)
            {
                case "Validation": validation.Add(claimRef); break;
                case "UnresolvedIssue": unresolved.Add(claimRef); break;
                case "ProposedStateContribution": contribution.Add(claimRef); break;
                case "ProposedAssignmentRevision": revision.Add(claimRef); break;
                default: throw new InvalidDataException("Unknown Handoff Claim role.");
            }
        }
        return new(validation, unresolved, contribution, revision);
    }

    private static async Task ValidateTypedClaimsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ProjectRef projectRef,
        IReadOnlyList<ClaimRef> claimRefs,
        string expectedKind,
        AttemptOwnership attempt,
        bool packagedRevision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claimRefs);
        foreach (var claimRef in claimRefs)
        {
            var claim = await LoadClaimFactsAsync(
                connection, transaction, projectRef, claimRef, cancellationToken);
            if (claim.Kind != expectedKind)
            {
                throw Failure(B1FailureCode.InvalidReference, $"A Handoff {expectedKind} reference has the wrong Claim kind.");
            }

            if (packagedRevision &&
                (!string.Equals(claim.ProposedAssignment, attempt.Assignment, StringComparison.Ordinal) ||
                 !string.Equals(claim.BaseRevision, attempt.Revision, StringComparison.Ordinal)))
            {
                throw Failure(
                    B1FailureCode.InvalidReference,
                    "A packaged Assignment Revision proposal must match the Handoff Attempt contract.");
            }
        }
    }

    private static async Task<AttemptOwnership> LoadAttemptOwnershipAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ProjectRef projectRef,
        AttemptRef attemptRef,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT attempt.assignment_id,attempt.effective_revision_id,assignment.assignee_actor_id
            FROM b1_attempts attempt
            JOIN b1_assignments assignment
              ON assignment.id=attempt.assignment_id AND assignment.project_id=attempt.project_id
            WHERE attempt.id=$attempt AND attempt.project_id=$project;
            """;
        Add(command,
            ("$attempt", attemptRef.Value.ToString()),
            ("$project", projectRef.Value.ToString()));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw Failure(B1FailureCode.InvalidReference, "The Handoff Attempt does not belong to the Project.");
        }
        return new(reader.GetString(0), reader.GetString(1), reader.GetString(2));
    }

    private static async Task<ClaimFacts> LoadClaimFactsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ProjectRef projectRef,
        ClaimRef claimRef,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT kind,claimant_kind,claimant_actor_id,source_binding_id,
                   proposed_assignment_id,base_revision_id
            FROM b1_claims
            WHERE id=$claim AND project_id=$project;
            """;
        Add(command,
            ("$claim", claimRef.Value.ToString()),
            ("$project", projectRef.Value.ToString()));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw Failure(B1FailureCode.InvalidReference, "A referenced Claim does not belong to the Project.");
        }
        return new(
            reader.GetString(0),
            reader.GetString(1),
            GetNullableString(reader, 2),
            GetNullableString(reader, 3),
            GetNullableString(reader, 4),
            GetNullableString(reader, 5));
    }

    private static async Task InsertClaimRefsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ProjectRef projectRef,
        HandoffRef handoffRef,
        string role,
        IReadOnlyList<ClaimRef> claimRefs,
        CancellationToken cancellationToken)
    {
        for (var ordinal = 0; ordinal < claimRefs.Count; ordinal++)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO b1_handoff_claim_refs(
                    handoff_id,project_id,role_kind,ordinal,claim_id)
                VALUES($handoff,$project,$role,$ordinal,$claim);
                """;
            Add(command,
                ("$handoff", handoffRef.Value.ToString()),
                ("$project", projectRef.Value.ToString()),
                ("$role", role),
                ("$ordinal", ordinal),
                ("$claim", claimRefs[ordinal].Value.ToString()));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
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
            throw Failure(B1FailureCode.NotAuthorized, "Only the Project bootstrap principal may record B1 continuity input.");
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

    private static string SerializeEvidence(IReadOnlyList<EvidenceRef> evidenceRefs)
    {
        ArgumentNullException.ThrowIfNull(evidenceRefs);
        return JsonSerializer.Serialize(evidenceRefs.Select(reference => reference.Value).ToArray());
    }

    private static IReadOnlyList<EvidenceRef> ReadEvidence(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Evidence provenance must be a JSON array.");
        }
        var values = new List<EvidenceRef>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException("Evidence provenance contains an unknown extension.");
            }
            values.Add(new EvidenceRef(element.GetString()!));
        }
        return values.AsReadOnly();
    }

    private static string SerializeAuthority(AuthorityBoundary boundary) =>
        JsonSerializer.Serialize(boundary.Capabilities.OrderBy(value => value).Select(value => value.ToString()));

    private static AuthorityBoundary ReadAuthority(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Delegated authority must be a JSON array.");
        }
        var values = new List<B1AuthorityCapability>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String ||
                !Enum.TryParse<B1AuthorityCapability>(element.GetString(), ignoreCase: false, out var value) ||
                !Enum.IsDefined(value))
            {
                throw new InvalidDataException("Unknown delegated authority capability.");
            }
            values.Add(value);
        }
        return AuthorityBoundary.Create(values);
    }

    private static string? GetNullableString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static T? ParseOptional<T>(string? value, Func<string, T> parse) where T : struct =>
        value is null ? null : parse(value);

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidDataException("Stored Claim columns do not match the closed payload kind.");
    }

    private static void Add(SqliteCommand command, params (string Name, object? Value)[] parameters)
    {
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
    }

    private static B1CommandException Failure(B1FailureCode code, string message) => new(code, message);

    private sealed record ClaimColumns(
        string ClaimantKind,
        string? ClaimantUser,
        string? ClaimantActor,
        string Kind,
        string? Statement,
        string? ScopeKind,
        string? ScopeProject,
        string? ScopeResponsibility,
        string? ScopeAssignment,
        string? Supersedes,
        string? ProposedAssignment,
        string? BaseRevision,
        string? WorkContract,
        string? AuthorityJson)
    {
        public static ClaimColumns StatementOnly(
            string claimantKind,
            string? claimantUser,
            string? claimantActor,
            string kind,
            string statement) =>
            new(
                claimantKind, claimantUser, claimantActor, kind, statement,
                null, null, null, null, null, null, null, null, null);
    }

    private sealed record AttemptOwnership(string Assignment, string Revision, string AssigneeActor);

    private sealed record ClaimFacts(
        string Kind,
        string ClaimantKind,
        string? ClaimantActor,
        string? SourceBinding,
        string? ProposedAssignment,
        string? BaseRevision);

    private sealed record HandoffClaimRefs(
        IReadOnlyList<ClaimRef> Validation,
        IReadOnlyList<ClaimRef> Unresolved,
        IReadOnlyList<ClaimRef> Contribution,
        IReadOnlyList<ClaimRef> Revision);
}
