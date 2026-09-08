using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Workbench.Core.Continuity;
using Workbench.Storage.Database;

namespace Workbench.Storage.Continuity;

public abstract record AuthorityCommitResult
{
    private AuthorityCommitResult() { }
    public sealed record Committed(AuthorityDecision Decision) : AuthorityCommitResult;
    public sealed record ProjectSequenceConflict : AuthorityCommitResult;
}

public sealed class B1AuthorityRepository(WorkbenchDatabase database)
{
    private readonly WorkbenchDatabase _database =
        database ?? throw new ArgumentNullException(nameof(database));

    public async Task<B1ProjectState> LoadProjectStateAsync(
        ProjectRef projectRef,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: true);
        var state = await LoadProjectStateAsync(connection, transaction, projectRef, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return state;
    }

    public async Task<AuthorityCommitResult> TryCommitAsync(
        ValidatedAuthorityDecision decision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decision);
        RequireAtLeastOneEffect(decision);
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var sequence = decision.ExpectedProjectCommitSequence + 1;
            var advance = Command(connection, transaction, """
                UPDATE b1_project_governance
                SET last_commit_sequence=last_commit_sequence + 1
                WHERE project_id=$project AND last_commit_sequence=$expected;
                """, ("$project", Id(decision.ProjectRef.Value)),
                ("$expected", decision.ExpectedProjectCommitSequence));
            if (await advance.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return new AuthorityCommitResult.ProjectSequenceConflict();
            }

            var persisted = new AuthorityDecision(
                decision.DecisionRef, decision.ProjectRef, sequence, decision.DecidingAuthorityRef,
                decision.ConsideredRefs, decision.LogicalActorEstablishmentEffect,
                decision.ResponsibilityEstablishmentEffect, decision.AssignmentDispositionEffect,
                decision.RevisionActivationEffect, decision.AssignmentDelegationEffect,
                decision.AcceptedStateContributions, decision.CreatedAt);

            await InsertDecisionAsync(connection, transaction, persisted, CommandKind(decision), cancellationToken);
            await InsertConsideredRefsAsync(connection, transaction, persisted, cancellationToken);
            await InsertStructuralEffectsAsync(connection, transaction, persisted, cancellationToken);
            await InsertContributionsAsync(connection, transaction, persisted, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new AuthorityCommitResult.Committed(persisted);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task InsertDecisionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AuthorityDecision decision,
        string commandKind,
        CancellationToken cancellationToken)
    {
        var authorityKind = decision.DecidingAuthorityRef switch
        {
            DecidingAuthorityRef.UserPrincipal => "UserPrincipal",
            DecidingAuthorityRef.LogicalActor => "LogicalActor",
            _ => throw Corrupt("Unknown deciding authority kind.")
        };
        var command = Command(connection, transaction, """
            INSERT INTO b1_authority_decisions(
                id,project_id,project_commit_sequence,command_kind,deciding_authority_kind,
                deciding_user_principal,deciding_actor_id,created_at)
            VALUES($id,$project,$sequence,$command,$authority_kind,$user,$actor,$created);
            """, ("$id", Id(decision.DecisionRef.Value)), ("$project", Id(decision.ProjectRef.Value)),
            ("$sequence", decision.ProjectCommitSequence), ("$command", commandKind),
            ("$authority_kind", authorityKind),
            ("$user", decision.DecidingAuthorityRef is DecidingAuthorityRef.UserPrincipal user
                ? user.UserPrincipalRef.Value : null),
            ("$actor", decision.DecidingAuthorityRef is DecidingAuthorityRef.LogicalActor actor
                ? Id(actor.LogicalActorRef.Value) : null),
            ("$created", Timestamp(decision.CreatedAt)));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertConsideredRefsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AuthorityDecision decision,
        CancellationToken cancellationToken)
    {
        for (var ordinal = 0; ordinal < decision.ConsideredRefs.Count; ordinal++)
        {
            var item = decision.ConsideredRefs[ordinal];
            (string Kind, string? Claim, string? Handoff, string? Evidence, string? EvolutionCandidate) columns = item switch
            {
                ConsideredRef.Claim value => ("Claim", Id(value.ClaimRef.Value), null, null, null),
                ConsideredRef.Handoff value => ("Handoff", null, Id(value.HandoffRef.Value), null, null),
                ConsideredRef.Evidence value => ("Evidence", null, null, value.EvidenceRef.Value, null),
                ConsideredRef.EvolutionCandidate value => ("EvolutionCandidate", null, null, null, Id(value.CandidateId)),
                _ => throw Corrupt("Unknown considered reference kind.")
            };
            var command = Command(connection, transaction, """
                INSERT INTO b1_decision_considered_refs(
                    decision_id,project_id,ordinal,ref_kind,claim_id,handoff_id,evidence_ref,evolution_candidate_id)
                VALUES($decision,$project,$ordinal,$kind,$claim,$handoff,$evidence,$candidate);
                """, ("$decision", Id(decision.DecisionRef.Value)),
                ("$project", Id(decision.ProjectRef.Value)), ("$ordinal", ordinal),
                ("$kind", columns.Kind), ("$claim", columns.Claim),
                ("$handoff", columns.Handoff), ("$evidence", columns.Evidence),
                ("$candidate", columns.EvolutionCandidate));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task InsertStructuralEffectsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AuthorityDecision decision,
        CancellationToken cancellationToken)
    {
        if (decision.LogicalActorEstablishmentEffect is { } actorEffect)
        {
            var actor = actorEffect.LogicalActor;
            await Command(connection, transaction, """
                INSERT INTO b1_logical_actors(id,project_id,role_kind,authorized_by_decision_id,created_at)
                VALUES($id,$project,$role,$decision,$created);
                """, ("$id", Id(actor.LogicalActorRef.Value)), ("$project", Id(actor.ProjectRef.Value)),
                ("$role", actor.RoleKind.ToString()), ("$decision", Id(decision.DecisionRef.Value)),
                ("$created", Timestamp(actor.CreatedAt))).ExecuteNonQueryAsync(cancellationToken);
        }

        if (decision.ResponsibilityEstablishmentEffect is { } responsibilityEffect)
        {
            var responsibility = responsibilityEffect.Responsibility;
            await Command(connection, transaction, """
                INSERT INTO b1_responsibilities(
                    id,project_id,obligation,expected_outcome,maximum_authority_json,
                    authorized_by_decision_id,created_at)
                VALUES($id,$project,$obligation,$outcome,$authority,$decision,$created);
                """, ("$id", Id(responsibility.ResponsibilityRef.Value)),
                ("$project", Id(responsibility.ProjectRef.Value)),
                ("$obligation", responsibility.Contract.Obligation),
                ("$outcome", responsibility.Contract.ExpectedOutcome),
                ("$authority", SerializeBoundary(responsibility.Contract.MaximumDelegableAuthorityBoundary)),
                ("$decision", Id(decision.DecisionRef.Value)),
                ("$created", Timestamp(responsibility.CreatedAt))).ExecuteNonQueryAsync(cancellationToken);
        }

        if (decision.AssignmentDispositionEffect is { } disposition)
        {
            await Command(connection, transaction, """
                INSERT INTO b1_revision_dispositions(
                    revision_id,project_id,assignment_id,disposition,authority_decision_id)
                VALUES($revision,$project,$assignment,$disposition,$decision);
                """, ("$revision", Id(disposition.EffectiveRevisionRef.Value)),
                ("$project", Id(decision.ProjectRef.Value)),
                ("$assignment", Id(disposition.AssignmentRef.Value)),
                ("$disposition", disposition.Disposition.ToString()),
                ("$decision", Id(decision.DecisionRef.Value))).ExecuteNonQueryAsync(cancellationToken);
        }

        if (decision.RevisionActivationEffect is { } activation)
        {
            await InsertRevisionAsync(connection, transaction, decision.ProjectRef,
                activation.Revision, activation.SourceClaimRef, decision.DecisionRef, cancellationToken);
        }

        if (decision.AssignmentDelegationEffect is { } delegation)
        {
            var assignment = delegation.Assignment;
            await Command(connection, transaction, """
                INSERT INTO b1_assignments(
                    id,project_id,responsibility_id,assignee_actor_id,replaces_assignment_id,
                    authorized_by_decision_id)
                VALUES($id,$project,$responsibility,$actor,$replaces,$decision);
                """, ("$id", Id(assignment.AssignmentRef.Value)),
                ("$project", Id(decision.ProjectRef.Value)),
                ("$responsibility", Id(assignment.ResponsibilityRef.Value)),
                ("$actor", Id(assignment.AssigneeActorRef.Value)),
                ("$replaces", delegation.ReplacesAssignmentRef is { } replaced ? Id(replaced.Value) : null),
                ("$decision", Id(decision.DecisionRef.Value))).ExecuteNonQueryAsync(cancellationToken);
            await InsertRevisionAsync(connection, transaction, decision.ProjectRef,
                delegation.InitialRevision, null, decision.DecisionRef, cancellationToken);
            await Command(connection, transaction, """
                INSERT INTO b1_assignment_routing(assignment_id,project_id,selected_attempt_id)
                VALUES($assignment,$project,NULL);
                """, ("$assignment", Id(assignment.AssignmentRef.Value)),
                ("$project", Id(decision.ProjectRef.Value))).ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task InsertRevisionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ProjectRef projectRef,
        AssignmentRevision revision,
        ClaimRef? activationSourceClaimRef,
        AuthorityDecisionRef decisionRef,
        CancellationToken cancellationToken)
    {
        await Command(connection, transaction, """
            INSERT INTO b1_revisions(
                id,project_id,assignment_id,prior_revision_id,activation_source_claim_id,work_contract,
                delegated_authority_json,authorized_by_decision_id)
            VALUES($id,$project,$assignment,$prior,$source,$contract,$authority,$decision);
            """, ("$id", Id(revision.RevisionRef.Value)), ("$project", Id(projectRef.Value)),
            ("$assignment", Id(revision.AssignmentRef.Value)),
            ("$prior", revision.PriorRevisionRef is { } prior ? Id(prior.Value) : null),
            ("$source", activationSourceClaimRef is { } source ? Id(source.Value) : null),
            ("$contract", revision.Contract.WorkContract),
            ("$authority", SerializeBoundary(revision.Contract.DelegatedAuthorityBoundary)),
            ("$decision", Id(decisionRef.Value))).ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertContributionsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AuthorityDecision decision,
        CancellationToken cancellationToken)
    {
        foreach (var contribution in decision.AcceptedStateContributions)
        {
            var (kind, project, responsibility, assignment) = ScopeColumns(contribution.Scope);
            await Command(connection, transaction, """
                INSERT INTO b1_accepted_state_contributions(
                    id,project_id,statement,scope_kind,scope_project_id,scope_responsibility_id,
                    scope_assignment_id,supersedes_contribution_id,authority_decision_id,source_claim_id)
                VALUES($id,$project,$statement,$kind,$scope_project,$responsibility,$assignment,
                    $supersedes,$decision,$source);
                """, ("$id", Id(contribution.ContributionRef.Value)),
                ("$project", Id(decision.ProjectRef.Value)), ("$statement", contribution.Statement),
                ("$kind", kind), ("$scope_project", project), ("$responsibility", responsibility),
                ("$assignment", assignment),
                ("$supersedes", contribution.SupersedesContributionRef is { } supersedes ? Id(supersedes.Value) : null),
                ("$decision", Id(decision.DecisionRef.Value)),
                ("$source", contribution.SourceClaimRef is { } source ? Id(source.Value) : null))
                .ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<B1ProjectState> LoadProjectStateAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        ProjectRef projectRef,
        CancellationToken cancellationToken)
    {
        var governance = await LoadGovernanceAsync(connection, transaction, projectRef, cancellationToken)
            ?? throw new InvalidDataException("The B1 governance root does not exist.");
        var actors = await LoadActorsAsync(connection, transaction, projectRef, cancellationToken);
        var responsibilities = await LoadResponsibilitiesAsync(connection, transaction, projectRef, cancellationToken);
        var revisionRows = await LoadRevisionsAsync(connection, transaction, projectRef, cancellationToken);
        var assignmentRows = await LoadAssignmentsAsync(connection, transaction, projectRef, revisionRows, cancellationToken);
        var assignments = assignmentRows.Select(item => item.Assignment).ToArray();
        var dispositions = await LoadDispositionsAsync(connection, transaction, projectRef, cancellationToken);
        var attempts = await LoadAttemptsAsync(connection, transaction, projectRef, cancellationToken);
        var bindings = await LoadBindingsAsync(connection, transaction, projectRef, cancellationToken);
        var claims = await LoadClaimsAsync(connection, transaction, projectRef, cancellationToken);
        var handoffs = await LoadHandoffsAsync(connection, transaction, projectRef, cancellationToken);
        var contributions = await LoadContributionsAsync(connection, transaction, projectRef, cancellationToken);
        var considered = await LoadConsideredRefsAsync(connection, transaction, projectRef, cancellationToken);
        var decisions = await LoadDecisionsAsync(connection, transaction, projectRef, actors, responsibilities,
            assignmentRows, revisionRows, dispositions, contributions, considered, cancellationToken);
        var assignmentRouting = await LoadAssignmentRoutingAsync(connection, transaction, projectRef, cancellationToken);
        var attemptRouting = await LoadAttemptRoutingAsync(connection, transaction, projectRef, cancellationToken);
        return new(governance, actors, responsibilities, assignments, revisionRows.Select(item => item.Revision).ToArray(),
            dispositions, attempts, bindings, claims, handoffs, decisions, assignmentRouting, attemptRouting);
    }

    private static async Task<ProjectGovernance?> LoadGovernanceAsync(
        SqliteConnection connection, SqliteTransaction? transaction, ProjectRef projectRef,
        CancellationToken cancellationToken)
    {
        var command = Command(connection, transaction, """
            SELECT bootstrap_user_principal,origin,adopted_at,last_commit_sequence
            FROM b1_project_governance WHERE project_id=$project;
            """, ("$project", Id(projectRef.Value)));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var origin = ParseEnum<B1GovernanceOrigin>(reader.GetString(1), "governance origin");
        DateTimeOffset? adopted = reader.IsDBNull(2) ? null : ParseTimestamp(reader.GetString(2), "adopted_at");
        return new(projectRef, new(reader.GetString(0)), origin, adopted, reader.GetInt64(3));
    }

    private static async Task<IReadOnlyList<LogicalActor>> LoadActorsAsync(
        SqliteConnection c, SqliteTransaction? tx, ProjectRef project, CancellationToken ct)
    {
        var result = new List<LogicalActor>();
        var command = Command(c, tx, """
            SELECT id,role_kind,authorized_by_decision_id,created_at FROM b1_logical_actors
            WHERE project_id=$project ORDER BY created_at,id;
            """, ("$project", Id(project.Value)));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(new(new(GuidValue(reader, 0)), project,
            ParseEnum<RoleKind>(reader.GetString(1), "actor role"), new(GuidValue(reader, 2)),
            ParseTimestamp(reader.GetString(3), "actor created_at")));
        return result;
    }

    private static async Task<IReadOnlyList<Responsibility>> LoadResponsibilitiesAsync(
        SqliteConnection c, SqliteTransaction? tx, ProjectRef project, CancellationToken ct)
    {
        var result = new List<Responsibility>();
        var command = Command(c, tx, """
            SELECT id,obligation,expected_outcome,maximum_authority_json,authorized_by_decision_id,created_at
            FROM b1_responsibilities WHERE project_id=$project ORDER BY created_at,id;
            """, ("$project", Id(project.Value)));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(new(new(GuidValue(reader, 0)), project,
            new(reader.GetString(1), reader.GetString(2), ParseBoundary(reader.GetString(3))),
            new(GuidValue(reader, 4)), ParseTimestamp(reader.GetString(5), "responsibility created_at")));
        return result;
    }

    private sealed record RevisionRow(
        AssignmentRevision Revision,
        ProjectRef ProjectRef,
        ClaimRef? ActivationSourceClaimRef);
    private sealed record AssignmentRow(Assignment Assignment, AssignmentRef? ReplacesAssignmentRef, ProjectRef ProjectRef);

    private static async Task<IReadOnlyList<RevisionRow>> LoadRevisionsAsync(
        SqliteConnection c, SqliteTransaction? tx, ProjectRef project, CancellationToken ct)
    {
        var result = new List<RevisionRow>();
        var command = Command(c, tx, """
            SELECT id,assignment_id,prior_revision_id,activation_source_claim_id,work_contract,
                delegated_authority_json,authorized_by_decision_id
            FROM b1_revisions WHERE project_id=$project ORDER BY rowid;
            """, ("$project", Id(project.Value)));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(new(new(new(GuidValue(reader, 0)), new(GuidValue(reader, 1)),
            reader.IsDBNull(2) ? null : new RevisionRef(GuidValue(reader, 2)),
            new(reader.GetString(4), ParseBoundary(reader.GetString(5))), new(GuidValue(reader, 6))), project,
            reader.IsDBNull(3) ? null : new ClaimRef(GuidValue(reader, 3))));
        return result;
    }

    private static async Task<IReadOnlyList<AssignmentRow>> LoadAssignmentsAsync(
        SqliteConnection c, SqliteTransaction? tx, ProjectRef project,
        IReadOnlyList<RevisionRow> revisions, CancellationToken ct)
    {
        var result = new List<AssignmentRow>();
        var command = Command(c, tx, """
            SELECT id,responsibility_id,assignee_actor_id,replaces_assignment_id,authorized_by_decision_id
            FROM b1_assignments WHERE project_id=$project ORDER BY rowid;
            """, ("$project", Id(project.Value)));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var assignmentRef = new AssignmentRef(GuidValue(reader, 0));
            var roots = revisions.Where(item => item.Revision.AssignmentRef == assignmentRef &&
                item.Revision.PriorRevisionRef is null).Select(item => item.Revision).ToArray();
            if (roots.Length != 1) throw Corrupt("Each Assignment must have exactly one initial Revision.");
            var assignment = new Assignment(assignmentRef, new(GuidValue(reader, 1)), new(GuidValue(reader, 2)),
                roots[0].RevisionRef, new(GuidValue(reader, 4)));
            result.Add(new(assignment, reader.IsDBNull(3) ? null : new AssignmentRef(GuidValue(reader, 3)), project));
        }
        return result;
    }

    private static async Task<IReadOnlyList<RevisionDispositionRecord>> LoadDispositionsAsync(
        SqliteConnection c, SqliteTransaction? tx, ProjectRef project, CancellationToken ct)
    {
        var result = new List<RevisionDispositionRecord>();
        var command = Command(c, tx, """
            SELECT assignment_id,revision_id,disposition,authority_decision_id FROM b1_revision_dispositions
            WHERE project_id=$project ORDER BY rowid;
            """, ("$project", Id(project.Value)));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(new(new(GuidValue(reader, 0)), new(GuidValue(reader, 1)),
            ParseEnum<AssignmentDisposition>(reader.GetString(2), "revision disposition"), new(GuidValue(reader, 3))));
        return result;
    }

    private static async Task<IReadOnlyList<Attempt>> LoadAttemptsAsync(
        SqliteConnection c, SqliteTransaction? tx, ProjectRef project, CancellationToken ct)
    {
        var result = new List<Attempt>();
        var command = Command(c, tx, """
            SELECT id,assignment_id,effective_revision_id,created_at FROM b1_attempts
            WHERE project_id=$project ORDER BY created_at,id;
            """, ("$project", Id(project.Value)));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(new(new(GuidValue(reader, 0)), new(GuidValue(reader, 1)),
            new(GuidValue(reader, 2)), ParseTimestamp(reader.GetString(3), "attempt created_at")));
        return result;
    }

    private static async Task<IReadOnlyList<SessionBinding>> LoadBindingsAsync(
        SqliteConnection c, SqliteTransaction? tx, ProjectRef project, CancellationToken ct)
    {
        var result = new List<SessionBinding>();
        var command = Command(c, tx, """
            SELECT id,attempt_id,logical_actor_id,external_session_ref,created_at FROM b1_session_bindings
            WHERE project_id=$project ORDER BY created_at,id;
            """, ("$project", Id(project.Value)));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(new(new(GuidValue(reader, 0)), new(GuidValue(reader, 1)),
            new(GuidValue(reader, 2)), new(reader.GetString(3)), ParseTimestamp(reader.GetString(4), "binding created_at")));
        return result;
    }

    private static async Task<IReadOnlyList<Claim>> LoadClaimsAsync(
        SqliteConnection c, SqliteTransaction? tx, ProjectRef project, CancellationToken ct)
    {
        var result = new List<Claim>();
        var command = Command(c, tx, """
            SELECT id,claimant_kind,claimant_user_principal,claimant_actor_id,source_binding_id,kind,statement,
                proposed_scope_kind,proposed_scope_project_id,proposed_scope_responsibility_id,
                proposed_scope_assignment_id,proposed_supersedes_contribution_id,proposed_assignment_id,
                base_revision_id,proposed_work_contract,proposed_delegated_authority_json,evidence_refs_json,created_at
            FROM b1_claims WHERE project_id=$project ORDER BY created_at,id;
            """, ("$project", Id(project.Value)));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            ClaimantRef claimant = reader.GetString(1) switch
            {
                "UserPrincipal" when !reader.IsDBNull(2) => new ClaimantRef.UserPrincipal(new(reader.GetString(2))),
                "LogicalActor" when !reader.IsDBNull(3) => new ClaimantRef.LogicalActor(new(GuidValue(reader, 3))),
                _ => throw Corrupt("Invalid Claim claimant columns.")
            };
            var payload = ParseClaimPayload(reader, project);
            result.Add(new(new(GuidValue(reader, 0)), project, claimant,
                reader.IsDBNull(4) ? null : new SessionBindingRef(GuidValue(reader, 4)), payload,
                ParseEvidence(reader.GetString(16)), ParseTimestamp(reader.GetString(17), "claim created_at")));
        }
        return result;
    }

    private static ClaimPayload ParseClaimPayload(SqliteDataReader reader, ProjectRef project) => reader.GetString(5) switch
    {
        "Result" => new ClaimPayload.Result(reader.GetString(6)),
        "Validation" => new ClaimPayload.Validation(reader.GetString(6)),
        "UnresolvedIssue" => new ClaimPayload.UnresolvedIssue(reader.GetString(6)),
        "ProposedStateContribution" => new ClaimPayload.ProposedStateContribution(reader.GetString(6),
            ParseScope(reader.GetString(7), reader, 8, 9, 10, project),
            reader.IsDBNull(11) ? null : new AcceptedStateContributionRef(GuidValue(reader, 11))),
        "ProposedAssignmentRevision" => new ClaimPayload.ProposedAssignmentRevision(
            new(GuidValue(reader, 12)), new(GuidValue(reader, 13)),
            new(reader.GetString(14), ParseBoundary(reader.GetString(15)))),
        _ => throw Corrupt("Unknown Claim kind.")
    };

    private static async Task<IReadOnlyList<Handoff>> LoadHandoffsAsync(
        SqliteConnection c, SqliteTransaction? tx, ProjectRef project, CancellationToken ct)
    {
        var refs = new Dictionary<(Guid, string), List<ClaimRef>>();
        var refCommand = Command(c, tx, """
            SELECT handoff_id,role_kind,claim_id FROM b1_handoff_claim_refs
            WHERE project_id=$project ORDER BY handoff_id,role_kind,ordinal;
            """, ("$project", Id(project.Value)));
        await using (var reader = await refCommand.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var key = (GuidValue(reader, 0), reader.GetString(1));
                if (!refs.TryGetValue(key, out var values)) refs[key] = values = [];
                values.Add(new(GuidValue(reader, 2)));
            }
        }
        IReadOnlyList<ClaimRef> Get(Guid id, string role) => refs.TryGetValue((id, role), out var values) ? values : [];
        var result = new List<Handoff>();
        var command = Command(c, tx, """
            SELECT id,attempt_id,result_claim_id,evidence_refs_json,created_at FROM b1_handoffs
            WHERE project_id=$project ORDER BY created_at,id;
            """, ("$project", Id(project.Value)));
        await using var handoffReader = await command.ExecuteReaderAsync(ct);
        while (await handoffReader.ReadAsync(ct))
        {
            var id = GuidValue(handoffReader, 0);
            result.Add(new(new(id), new(GuidValue(handoffReader, 1)), new(GuidValue(handoffReader, 2)),
                Get(id, "Validation"), Get(id, "UnresolvedIssue"), Get(id, "ProposedStateContribution"),
                Get(id, "ProposedAssignmentRevision"), ParseEvidence(handoffReader.GetString(3)),
                ParseTimestamp(handoffReader.GetString(4), "handoff created_at")));
        }
        return result;
    }

    private static async Task<IReadOnlyList<AcceptedStateContribution>> LoadContributionsAsync(
        SqliteConnection c, SqliteTransaction? tx, ProjectRef project, CancellationToken ct)
    {
        var result = new List<AcceptedStateContribution>();
        var command = Command(c, tx, """
            SELECT id,statement,scope_kind,scope_project_id,scope_responsibility_id,scope_assignment_id,
                supersedes_contribution_id,authority_decision_id,source_claim_id
            FROM b1_accepted_state_contributions WHERE project_id=$project ORDER BY rowid;
            """, ("$project", Id(project.Value)));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(new(new(GuidValue(reader, 0)), reader.GetString(1),
            ParseScope(reader.GetString(2), reader, 3, 4, 5, project),
            reader.IsDBNull(6) ? null : new AcceptedStateContributionRef(GuidValue(reader, 6)),
            new(GuidValue(reader, 7)), reader.IsDBNull(8) ? null : new ClaimRef(GuidValue(reader, 8))));
        return result;
    }

    private static async Task<IReadOnlyDictionary<AuthorityDecisionRef, IReadOnlyList<ConsideredRef>>> LoadConsideredRefsAsync(
        SqliteConnection c, SqliteTransaction? tx, ProjectRef project, CancellationToken ct)
    {
        var result = new Dictionary<AuthorityDecisionRef, List<ConsideredRef>>();
        var command = Command(c, tx, """
            SELECT decision_id,ref_kind,claim_id,handoff_id,evidence_ref,evolution_candidate_id FROM b1_decision_considered_refs
            WHERE project_id=$project ORDER BY decision_id,ordinal;
            """, ("$project", Id(project.Value)));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var decision = new AuthorityDecisionRef(GuidValue(reader, 0));
            if (!result.TryGetValue(decision, out var values)) result[decision] = values = [];
            values.Add(reader.GetString(1) switch
            {
                "Claim" => new ConsideredRef.Claim(new(GuidValue(reader, 2))),
                "Handoff" => new ConsideredRef.Handoff(new(GuidValue(reader, 3))),
                "Evidence" => new ConsideredRef.Evidence(new(reader.GetString(4))),
                "EvolutionCandidate" => new ConsideredRef.EvolutionCandidate(GuidValue(reader, 5)),
                _ => throw Corrupt("Unknown considered reference kind.")
            });
        }
        return result.ToDictionary(item => item.Key, item => (IReadOnlyList<ConsideredRef>)item.Value);
    }

    private static async Task<IReadOnlyList<AuthorityDecision>> LoadDecisionsAsync(
        SqliteConnection c, SqliteTransaction? tx, ProjectRef project,
        IReadOnlyList<LogicalActor> actors, IReadOnlyList<Responsibility> responsibilities,
        IReadOnlyList<AssignmentRow> assignments, IReadOnlyList<RevisionRow> revisions,
        IReadOnlyList<RevisionDispositionRecord> dispositions,
        IReadOnlyList<AcceptedStateContribution> contributions,
        IReadOnlyDictionary<AuthorityDecisionRef, IReadOnlyList<ConsideredRef>> considered,
        CancellationToken ct)
    {
        var result = new List<AuthorityDecision>();
        var command = Command(c, tx, """
            SELECT id,project_commit_sequence,command_kind,deciding_authority_kind,
                deciding_user_principal,deciding_actor_id,created_at
            FROM b1_authority_decisions WHERE project_id=$project ORDER BY project_commit_sequence;
            """, ("$project", Id(project.Value)));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var id = new AuthorityDecisionRef(GuidValue(reader, 0));
            var kind = reader.GetString(2);
            if (!CommandKinds.Contains(kind)) throw Corrupt("Unknown authority command kind.");
            DecidingAuthorityRef authority = reader.GetString(3) switch
            {
                "UserPrincipal" when !reader.IsDBNull(4) => new DecidingAuthorityRef.UserPrincipal(new(reader.GetString(4))),
                "LogicalActor" when !reader.IsDBNull(5) => new DecidingAuthorityRef.LogicalActor(new(GuidValue(reader, 5))),
                _ => throw Corrupt("Invalid deciding authority columns.")
            };
            var actorEffects = actors.Where(item => item.AuthorizedByDecisionRef == id).ToArray();
            var responsibilityEffects = responsibilities.Where(item => item.AuthorizedByDecisionRef == id).ToArray();
            var delegationEffects = assignments.Where(item => item.Assignment.AuthorizedByDecisionRef == id).ToArray();
            var activationEffects = revisions.Where(item => item.Revision.AuthorizedByDecisionRef == id &&
                item.Revision.PriorRevisionRef is not null).ToArray();
            var dispositionEffects = dispositions.Where(item => item.AuthorityDecisionRef == id).ToArray();
            if (actorEffects.Length > 1 || responsibilityEffects.Length > 1 || delegationEffects.Length > 1 ||
                activationEffects.Length > 1 || dispositionEffects.Length > 1)
                throw Corrupt("A bounded AuthorityDecision has multiple singleton effects.");
            var delegation = delegationEffects.SingleOrDefault();
            var initial = delegation is null ? null : revisions.SingleOrDefault(item =>
                item.Revision.RevisionRef == delegation.Assignment.InitialRevisionRef)?.Revision
                ?? throw Corrupt("An Assignment delegation lacks its initial Revision.");
            var disposition = dispositionEffects.SingleOrDefault();
            var actorEffect = actorEffects.Length == 0 ? null : new LogicalActorEstablishmentEffect(actorEffects[0]);
            var responsibilityEffect = responsibilityEffects.Length == 0
                ? null
                : new ResponsibilityEstablishmentEffect(responsibilityEffects[0]);
            var dispositionEffect = disposition is null
                ? null
                : new AssignmentDispositionEffect(
                    disposition.AssignmentRef, disposition.RevisionRef, disposition.Disposition);
            var activationEffect = activationEffects.Length == 0
                ? null
                : new RevisionActivationEffect(
                    activationEffects[0].Revision, activationEffects[0].ActivationSourceClaimRef);
            var delegationEffect = delegation is null
                ? null
                : new AssignmentDelegationEffect(
                    delegation.Assignment, initial!, delegation.ReplacesAssignmentRef);
            var decisionContributions = contributions.Where(item => item.AuthorityDecisionRef == id).ToArray();
            ValidatePersistedDecisionShape(kind, id, actorEffect, responsibilityEffect,
                dispositionEffect, activationEffect, delegationEffect, decisionContributions);
            result.Add(new(id, project, reader.GetInt64(1), authority,
                considered.TryGetValue(id, out var refs) ? refs : [],
                actorEffect, responsibilityEffect, dispositionEffect, activationEffect, delegationEffect,
                decisionContributions,
                ParseTimestamp(reader.GetString(6), "decision created_at")));
        }
        return result;
    }

    private static void ValidatePersistedDecisionShape(
        string commandKind,
        AuthorityDecisionRef decisionRef,
        LogicalActorEstablishmentEffect? actor,
        ResponsibilityEstablishmentEffect? responsibility,
        AssignmentDispositionEffect? disposition,
        RevisionActivationEffect? activation,
        AssignmentDelegationEffect? delegation,
        IReadOnlyList<AcceptedStateContribution> contributions)
    {
        if (delegation is not null)
        {
            if (delegation.Assignment.AuthorizedByDecisionRef != decisionRef ||
                delegation.InitialRevision.AuthorizedByDecisionRef != decisionRef ||
                delegation.InitialRevision.PriorRevisionRef is not null ||
                delegation.Assignment.InitialRevisionRef != delegation.InitialRevision.RevisionRef ||
                delegation.Assignment.AssignmentRef != delegation.InitialRevision.AssignmentRef)
            {
                throw Corrupt("An Assignment delegation and its initial Revision must share one authority Decision.");
            }

            if (actor is not null &&
                actor.LogicalActor.LogicalActorRef != delegation.Assignment.AssigneeActorRef)
            {
                throw Corrupt("A Decision-established assignee does not match its Assignment delegation.");
            }
        }
        else if (actor is not null && commandKind is not "EstablishLogicalActor")
        {
            throw Corrupt("A compound Actor establishment requires an Assignment delegation.");
        }

        var valid = commandKind switch
        {
            "EstablishLogicalActor" => actor is not null && responsibility is null && disposition is null &&
                activation is null && delegation is null,
            "EstablishResponsibility" => responsibility is not null && disposition is null && activation is null &&
                (delegation is null
                    ? actor is null
                    : delegation.ReplacesAssignmentRef is null &&
                      delegation.Assignment.ResponsibilityRef == responsibility.Responsibility.ResponsibilityRef),
            "DelegateAssignment" => responsibility is null && disposition is null && activation is null &&
                delegation is not null,
            "DecideAssignment" => responsibility is null && disposition is not null &&
                !(activation is not null && delegation is not null) &&
                (activation is null ||
                    (actor is null &&
                     disposition.Disposition == AssignmentDisposition.RevisionRequired &&
                     activation.Revision.AssignmentRef == disposition.AssignmentRef &&
                     activation.Revision.PriorRevisionRef == disposition.EffectiveRevisionRef)) &&
                (delegation is null ||
                    (delegation.ReplacesAssignmentRef == disposition.AssignmentRef &&
                     disposition.Disposition is AssignmentDisposition.Rejected or AssignmentDisposition.RevisionRequired)),
            "ActivateAssignmentRevision" => actor is null && responsibility is null && disposition is null &&
                activation is not null && delegation is null,
            "AuthorAcceptedState" => actor is null && responsibility is null && disposition is null &&
                activation is null && delegation is null && contributions.Count > 0,
            _ => false
        };

        if (!valid)
        {
            throw Corrupt("The persisted command kind does not match its bounded AuthorityDecision effects.");
        }
    }

    private static async Task<IReadOnlyList<AssignmentRoutingSelection>> LoadAssignmentRoutingAsync(
        SqliteConnection c, SqliteTransaction? tx, ProjectRef project, CancellationToken ct)
    {
        var result = new List<AssignmentRoutingSelection>();
        var command = Command(c, tx, "SELECT assignment_id,selected_attempt_id FROM b1_assignment_routing WHERE project_id=$project ORDER BY rowid;",
            ("$project", Id(project.Value)));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(new(new(GuidValue(reader, 0)),
            reader.IsDBNull(1) ? null : new AttemptRef(GuidValue(reader, 1))));
        return result;
    }

    private static async Task<IReadOnlyList<AttemptRoutingSelection>> LoadAttemptRoutingAsync(
        SqliteConnection c, SqliteTransaction? tx, ProjectRef project, CancellationToken ct)
    {
        var result = new List<AttemptRoutingSelection>();
        var command = Command(c, tx, """
            SELECT attempt_id,selected_handoff_id,selected_session_binding_id FROM b1_attempt_routing
            WHERE project_id=$project ORDER BY rowid;
            """, ("$project", Id(project.Value)));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(new(new(GuidValue(reader, 0)),
            reader.IsDBNull(1) ? null : new HandoffRef(GuidValue(reader, 1)),
            reader.IsDBNull(2) ? null : new SessionBindingRef(GuidValue(reader, 2))));
        return result;
    }

    private static string CommandKind(ValidatedAuthorityDecision decision)
    {
        if (decision.ResponsibilityEstablishmentEffect is not null) return "EstablishResponsibility";
        if (decision.AssignmentDispositionEffect is not null) return "DecideAssignment";
        if (decision.RevisionActivationEffect is not null) return "ActivateAssignmentRevision";
        if (decision.AssignmentDelegationEffect is not null) return "DelegateAssignment";
        if (decision.LogicalActorEstablishmentEffect is not null) return "EstablishLogicalActor";
        return "AuthorAcceptedState";
    }

    private static void RequireAtLeastOneEffect(ValidatedAuthorityDecision decision)
    {
        if (decision.LogicalActorEstablishmentEffect is null && decision.ResponsibilityEstablishmentEffect is null &&
            decision.AssignmentDispositionEffect is null && decision.RevisionActivationEffect is null &&
            decision.AssignmentDelegationEffect is null && decision.AcceptedStateContributions.Count == 0)
            throw new InvalidDataException("A validated AuthorityDecision must contain at least one effect.");
    }

    private static readonly HashSet<string> CommandKinds =
    [
        "EstablishLogicalActor", "EstablishResponsibility", "DelegateAssignment", "DecideAssignment",
        "ActivateAssignmentRevision", "AuthorAcceptedState"
    ];

    private static (string Kind, string? Project, string? Responsibility, string? Assignment) ScopeColumns(
        ContributionScopeRef scope) => scope switch
    {
        ContributionScopeRef.Project value => ("Project", Id(value.ProjectRef.Value), null, null),
        ContributionScopeRef.Responsibility value => ("Responsibility", null, Id(value.ResponsibilityRef.Value), null),
        ContributionScopeRef.Assignment value => ("Assignment", null, null, Id(value.AssignmentRef.Value)),
        _ => throw Corrupt("Unknown contribution scope.")
    };

    private static ContributionScopeRef ParseScope(
        string kind, SqliteDataReader reader, int projectOrdinal, int responsibilityOrdinal,
        int assignmentOrdinal, ProjectRef expectedProject) => kind switch
    {
        "Project" when !reader.IsDBNull(projectOrdinal) && GuidValue(reader, projectOrdinal) == expectedProject.Value =>
            new ContributionScopeRef.Project(expectedProject),
        "Responsibility" when !reader.IsDBNull(responsibilityOrdinal) =>
            new ContributionScopeRef.Responsibility(new(GuidValue(reader, responsibilityOrdinal))),
        "Assignment" when !reader.IsDBNull(assignmentOrdinal) =>
            new ContributionScopeRef.Assignment(new(GuidValue(reader, assignmentOrdinal))),
        _ => throw Corrupt("Invalid contribution scope columns.")
    };

    private static string SerializeBoundary(AuthorityBoundary boundary) =>
        JsonSerializer.Serialize(boundary.Capabilities.OrderBy(item => item).Select(item => item.ToString()));

    private static AuthorityBoundary ParseBoundary(string json)
    {
        try
        {
            var values = JsonSerializer.Deserialize<string[]>(json) ?? throw Corrupt("Authority JSON is null.");
            return AuthorityBoundary.Create(values.Select(value => ParseEnum<B1AuthorityCapability>(value, "authority capability")));
        }
        catch (JsonException exception)
        {
            throw Corrupt("Authority JSON is invalid.", exception);
        }
    }

    private static IReadOnlyList<EvidenceRef> ParseEvidence(string json)
    {
        try
        {
            return (JsonSerializer.Deserialize<string[]>(json) ?? throw Corrupt("Evidence JSON is null."))
                .Select(value => new EvidenceRef(value)).ToArray();
        }
        catch (JsonException exception)
        {
            throw Corrupt("Evidence JSON is invalid.", exception);
        }
    }

    private static T ParseEnum<T>(string value, string label) where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw Corrupt($"Unknown {label}.");

    private static Guid GuidValue(SqliteDataReader reader, int ordinal) =>
        Guid.TryParse(reader.GetString(ordinal), out var value) && value != Guid.Empty
            ? value
            : throw Corrupt("A persisted B1 identifier is invalid.");

    private static DateTimeOffset ParseTimestamp(string value, string label) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : throw Corrupt($"Invalid {label}.");

    private static SqliteCommand Command(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        params (string Name, object? Value)[] values)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    private static string Id(Guid value) => value.ToString();
    private static string Timestamp(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static InvalidDataException Corrupt(string message, Exception? inner = null) =>
        new(message, inner);
}
