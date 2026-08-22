using Microsoft.Data.Sqlite;

namespace Workbench.Storage.Migrations;

internal static class Migration020ManualContinuitySpine
{
    public const long Version = 20;

    public static async Task ApplyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE b1_legacy_project_origins (
                project_id TEXT PRIMARY KEY REFERENCES projects(id) ON DELETE CASCADE,
                source_schema_version INTEGER NOT NULL CHECK(source_schema_version = 19)
            );

            INSERT INTO b1_legacy_project_origins(project_id, source_schema_version)
            SELECT id, 19 FROM projects;

            CREATE TABLE b1_project_governance (
                project_id TEXT PRIMARY KEY REFERENCES projects(id) ON DELETE CASCADE,
                bootstrap_user_principal TEXT NOT NULL CHECK(length(trim(bootstrap_user_principal)) > 0),
                origin TEXT NOT NULL CHECK(origin IN ('Created', 'Adopted')),
                adopted_at TEXT NULL,
                last_commit_sequence INTEGER NOT NULL CHECK(last_commit_sequence >= 0),
                CHECK((origin = 'Created' AND adopted_at IS NULL) OR
                      (origin = 'Adopted' AND adopted_at IS NOT NULL))
            );

            CREATE TABLE b1_authority_decisions (
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL REFERENCES b1_project_governance(project_id) ON DELETE CASCADE,
                project_commit_sequence INTEGER NOT NULL CHECK(project_commit_sequence > 0),
                command_kind TEXT NOT NULL CHECK(command_kind IN (
                    'EstablishLogicalActor', 'EstablishResponsibility', 'DelegateAssignment',
                    'DecideAssignment', 'ActivateAssignmentRevision', 'AuthorAcceptedState')),
                deciding_authority_kind TEXT NOT NULL CHECK(deciding_authority_kind IN ('UserPrincipal', 'LogicalActor')),
                deciding_user_principal TEXT NULL,
                deciding_actor_id TEXT NULL,
                created_at TEXT NOT NULL,
                UNIQUE(id, project_id),
                UNIQUE(project_id, project_commit_sequence),
                FOREIGN KEY(deciding_actor_id, project_id)
                    REFERENCES b1_logical_actors(id, project_id),
                CHECK(
                    (deciding_authority_kind = 'UserPrincipal' AND
                     deciding_user_principal IS NOT NULL AND length(trim(deciding_user_principal)) > 0 AND
                     deciding_actor_id IS NULL) OR
                    (deciding_authority_kind = 'LogicalActor' AND
                     deciding_user_principal IS NULL AND deciding_actor_id IS NOT NULL))
            );

            CREATE TABLE b1_logical_actors (
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL REFERENCES b1_project_governance(project_id) ON DELETE CASCADE,
                role_kind TEXT NOT NULL CHECK(role_kind IN ('Leader', 'Worker', 'Reviewer')),
                authorized_by_decision_id TEXT NOT NULL,
                created_at TEXT NOT NULL,
                UNIQUE(id, project_id),
                FOREIGN KEY(authorized_by_decision_id, project_id)
                    REFERENCES b1_authority_decisions(id, project_id)
            );

            CREATE TABLE b1_responsibilities (
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL REFERENCES b1_project_governance(project_id) ON DELETE CASCADE,
                obligation TEXT NOT NULL CHECK(length(trim(obligation)) > 0),
                expected_outcome TEXT NOT NULL CHECK(length(trim(expected_outcome)) > 0),
                maximum_authority_json TEXT NOT NULL
                    CHECK(json_valid(maximum_authority_json) AND json_type(maximum_authority_json) = 'array'),
                authorized_by_decision_id TEXT NOT NULL,
                created_at TEXT NOT NULL,
                UNIQUE(id, project_id),
                FOREIGN KEY(authorized_by_decision_id, project_id)
                    REFERENCES b1_authority_decisions(id, project_id)
            );

            CREATE TABLE b1_assignments (
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL REFERENCES b1_project_governance(project_id) ON DELETE CASCADE,
                responsibility_id TEXT NOT NULL,
                assignee_actor_id TEXT NOT NULL,
                replaces_assignment_id TEXT NULL,
                authorized_by_decision_id TEXT NOT NULL,
                UNIQUE(id, project_id),
                UNIQUE(id, project_id, responsibility_id),
                FOREIGN KEY(responsibility_id, project_id)
                    REFERENCES b1_responsibilities(id, project_id),
                FOREIGN KEY(assignee_actor_id, project_id)
                    REFERENCES b1_logical_actors(id, project_id),
                FOREIGN KEY(replaces_assignment_id, project_id, responsibility_id)
                    REFERENCES b1_assignments(id, project_id, responsibility_id),
                FOREIGN KEY(authorized_by_decision_id, project_id)
                    REFERENCES b1_authority_decisions(id, project_id),
                CHECK(replaces_assignment_id IS NULL OR replaces_assignment_id <> id)
            );

            CREATE UNIQUE INDEX ux_b1_assignments_one_replacement_per_target
            ON b1_assignments(replaces_assignment_id)
            WHERE replaces_assignment_id IS NOT NULL;

            CREATE TABLE b1_revisions (
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL REFERENCES b1_project_governance(project_id) ON DELETE CASCADE,
                assignment_id TEXT NOT NULL,
                prior_revision_id TEXT NULL,
                activation_source_claim_id TEXT NULL,
                work_contract TEXT NOT NULL CHECK(length(trim(work_contract)) > 0),
                delegated_authority_json TEXT NOT NULL
                    CHECK(json_valid(delegated_authority_json) AND json_type(delegated_authority_json) = 'array'),
                authorized_by_decision_id TEXT NOT NULL,
                UNIQUE(id, project_id),
                UNIQUE(id, project_id, assignment_id),
                FOREIGN KEY(assignment_id, project_id)
                    REFERENCES b1_assignments(id, project_id),
                FOREIGN KEY(prior_revision_id, project_id, assignment_id)
                    REFERENCES b1_revisions(id, project_id, assignment_id),
                FOREIGN KEY(activation_source_claim_id, project_id)
                    REFERENCES b1_claims(id, project_id),
                FOREIGN KEY(authorized_by_decision_id, project_id)
                    REFERENCES b1_authority_decisions(id, project_id),
                CHECK(prior_revision_id IS NULL OR prior_revision_id <> id),
                CHECK(prior_revision_id IS NOT NULL OR activation_source_claim_id IS NULL)
            );

            CREATE UNIQUE INDEX ux_b1_revisions_one_initial_per_assignment
            ON b1_revisions(assignment_id)
            WHERE prior_revision_id IS NULL;

            CREATE UNIQUE INDEX ux_b1_revisions_one_successor_per_revision
            ON b1_revisions(prior_revision_id)
            WHERE prior_revision_id IS NOT NULL;

            CREATE TABLE b1_revision_dispositions (
                revision_id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL REFERENCES b1_project_governance(project_id) ON DELETE CASCADE,
                assignment_id TEXT NOT NULL,
                disposition TEXT NOT NULL CHECK(disposition IN ('Accepted', 'Rejected', 'RevisionRequired')),
                authority_decision_id TEXT NOT NULL,
                UNIQUE(revision_id, project_id),
                FOREIGN KEY(revision_id, project_id, assignment_id)
                    REFERENCES b1_revisions(id, project_id, assignment_id),
                FOREIGN KEY(authority_decision_id, project_id)
                    REFERENCES b1_authority_decisions(id, project_id)
            );

            CREATE TABLE b1_attempts (
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL REFERENCES b1_project_governance(project_id) ON DELETE CASCADE,
                assignment_id TEXT NOT NULL,
                effective_revision_id TEXT NOT NULL,
                created_at TEXT NOT NULL,
                UNIQUE(id, project_id),
                UNIQUE(id, project_id, assignment_id),
                FOREIGN KEY(assignment_id, project_id)
                    REFERENCES b1_assignments(id, project_id),
                FOREIGN KEY(effective_revision_id, project_id, assignment_id)
                    REFERENCES b1_revisions(id, project_id, assignment_id)
            );

            CREATE TABLE b1_session_bindings (
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL REFERENCES b1_project_governance(project_id) ON DELETE CASCADE,
                attempt_id TEXT NOT NULL,
                logical_actor_id TEXT NOT NULL,
                external_session_ref TEXT NOT NULL CHECK(length(trim(external_session_ref)) > 0),
                created_at TEXT NOT NULL,
                UNIQUE(id, project_id),
                UNIQUE(id, project_id, attempt_id),
                FOREIGN KEY(attempt_id, project_id)
                    REFERENCES b1_attempts(id, project_id),
                FOREIGN KEY(logical_actor_id, project_id)
                    REFERENCES b1_logical_actors(id, project_id)
            );

            CREATE TABLE b1_claims (
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL REFERENCES b1_project_governance(project_id) ON DELETE CASCADE,
                claimant_kind TEXT NOT NULL CHECK(claimant_kind IN ('UserPrincipal', 'LogicalActor')),
                claimant_user_principal TEXT NULL,
                claimant_actor_id TEXT NULL,
                source_binding_id TEXT NULL,
                kind TEXT NOT NULL CHECK(kind IN (
                    'Result', 'Validation', 'UnresolvedIssue',
                    'ProposedStateContribution', 'ProposedAssignmentRevision')),
                statement TEXT NULL,
                proposed_scope_kind TEXT NULL,
                proposed_scope_project_id TEXT NULL,
                proposed_scope_responsibility_id TEXT NULL,
                proposed_scope_assignment_id TEXT NULL,
                proposed_supersedes_contribution_id TEXT NULL,
                proposed_assignment_id TEXT NULL,
                base_revision_id TEXT NULL,
                proposed_work_contract TEXT NULL,
                proposed_delegated_authority_json TEXT NULL,
                evidence_refs_json TEXT NOT NULL
                    CHECK(json_valid(evidence_refs_json) AND json_type(evidence_refs_json) = 'array'),
                created_at TEXT NOT NULL,
                UNIQUE(id, project_id),
                FOREIGN KEY(claimant_actor_id, project_id)
                    REFERENCES b1_logical_actors(id, project_id),
                FOREIGN KEY(source_binding_id, project_id)
                    REFERENCES b1_session_bindings(id, project_id),
                FOREIGN KEY(proposed_scope_responsibility_id, project_id)
                    REFERENCES b1_responsibilities(id, project_id),
                FOREIGN KEY(proposed_scope_assignment_id, project_id)
                    REFERENCES b1_assignments(id, project_id),
                FOREIGN KEY(proposed_assignment_id, project_id)
                    REFERENCES b1_assignments(id, project_id),
                FOREIGN KEY(base_revision_id, project_id, proposed_assignment_id)
                    REFERENCES b1_revisions(id, project_id, assignment_id),
                FOREIGN KEY(proposed_supersedes_contribution_id, project_id)
                    REFERENCES b1_accepted_state_contributions(id, project_id),
                CHECK(
                    (claimant_kind = 'UserPrincipal' AND
                     claimant_user_principal IS NOT NULL AND length(trim(claimant_user_principal)) > 0 AND
                     claimant_actor_id IS NULL AND source_binding_id IS NULL) OR
                    (claimant_kind = 'LogicalActor' AND claimant_user_principal IS NULL AND claimant_actor_id IS NOT NULL)),
                CHECK(
                    (kind IN ('Result', 'Validation', 'UnresolvedIssue') AND
                     statement IS NOT NULL AND length(trim(statement)) > 0 AND
                     proposed_scope_kind IS NULL AND proposed_scope_project_id IS NULL AND
                     proposed_scope_responsibility_id IS NULL AND proposed_scope_assignment_id IS NULL AND
                     proposed_supersedes_contribution_id IS NULL AND proposed_assignment_id IS NULL AND
                     base_revision_id IS NULL AND proposed_work_contract IS NULL AND
                     proposed_delegated_authority_json IS NULL) OR
                    (kind = 'ProposedStateContribution' AND
                     statement IS NOT NULL AND length(trim(statement)) > 0 AND
                     proposed_scope_kind IN ('Project', 'Responsibility', 'Assignment') AND
                     ((proposed_scope_kind = 'Project' AND proposed_scope_project_id = project_id AND
                       proposed_scope_responsibility_id IS NULL AND proposed_scope_assignment_id IS NULL) OR
                      (proposed_scope_kind = 'Responsibility' AND proposed_scope_project_id IS NULL AND
                       proposed_scope_responsibility_id IS NOT NULL AND proposed_scope_assignment_id IS NULL) OR
                      (proposed_scope_kind = 'Assignment' AND proposed_scope_project_id IS NULL AND
                       proposed_scope_responsibility_id IS NULL AND proposed_scope_assignment_id IS NOT NULL)) AND
                     proposed_assignment_id IS NULL AND base_revision_id IS NULL AND proposed_work_contract IS NULL AND
                     proposed_delegated_authority_json IS NULL) OR
                    (kind = 'ProposedAssignmentRevision' AND
                     statement IS NULL AND proposed_scope_kind IS NULL AND proposed_scope_project_id IS NULL AND
                     proposed_scope_responsibility_id IS NULL AND proposed_scope_assignment_id IS NULL AND
                     proposed_supersedes_contribution_id IS NULL AND proposed_assignment_id IS NOT NULL AND
                     base_revision_id IS NOT NULL AND proposed_work_contract IS NOT NULL AND
                     length(trim(proposed_work_contract)) > 0 AND proposed_delegated_authority_json IS NOT NULL AND
                     json_valid(proposed_delegated_authority_json) AND
                     json_type(proposed_delegated_authority_json) = 'array'))
            );

            CREATE TABLE b1_handoffs (
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL REFERENCES b1_project_governance(project_id) ON DELETE CASCADE,
                attempt_id TEXT NOT NULL,
                result_claim_id TEXT NOT NULL,
                evidence_refs_json TEXT NOT NULL
                    CHECK(json_valid(evidence_refs_json) AND json_type(evidence_refs_json) = 'array'),
                created_at TEXT NOT NULL,
                UNIQUE(id, project_id),
                UNIQUE(id, project_id, attempt_id),
                FOREIGN KEY(attempt_id, project_id)
                    REFERENCES b1_attempts(id, project_id),
                FOREIGN KEY(result_claim_id, project_id)
                    REFERENCES b1_claims(id, project_id)
            );

            CREATE TABLE b1_handoff_claim_refs (
                handoff_id TEXT NOT NULL,
                project_id TEXT NOT NULL REFERENCES b1_project_governance(project_id) ON DELETE CASCADE,
                role_kind TEXT NOT NULL CHECK(role_kind IN (
                    'Validation', 'UnresolvedIssue', 'ProposedStateContribution', 'ProposedAssignmentRevision')),
                ordinal INTEGER NOT NULL CHECK(ordinal >= 0),
                claim_id TEXT NOT NULL,
                PRIMARY KEY(handoff_id, role_kind, ordinal),
                FOREIGN KEY(handoff_id, project_id)
                    REFERENCES b1_handoffs(id, project_id) ON DELETE CASCADE,
                FOREIGN KEY(claim_id, project_id)
                    REFERENCES b1_claims(id, project_id)
            );

            CREATE TABLE b1_decision_considered_refs (
                decision_id TEXT NOT NULL,
                project_id TEXT NOT NULL REFERENCES b1_project_governance(project_id) ON DELETE CASCADE,
                ordinal INTEGER NOT NULL CHECK(ordinal >= 0),
                ref_kind TEXT NOT NULL CHECK(ref_kind IN ('Claim', 'Handoff', 'Evidence')),
                claim_id TEXT NULL,
                handoff_id TEXT NULL,
                evidence_ref TEXT NULL,
                PRIMARY KEY(decision_id, ordinal),
                FOREIGN KEY(decision_id, project_id)
                    REFERENCES b1_authority_decisions(id, project_id) ON DELETE CASCADE,
                FOREIGN KEY(claim_id, project_id)
                    REFERENCES b1_claims(id, project_id),
                FOREIGN KEY(handoff_id, project_id)
                    REFERENCES b1_handoffs(id, project_id),
                CHECK(
                    (ref_kind = 'Claim' AND claim_id IS NOT NULL AND handoff_id IS NULL AND evidence_ref IS NULL) OR
                    (ref_kind = 'Handoff' AND claim_id IS NULL AND handoff_id IS NOT NULL AND evidence_ref IS NULL) OR
                    (ref_kind = 'Evidence' AND claim_id IS NULL AND handoff_id IS NULL AND
                     evidence_ref IS NOT NULL AND length(trim(evidence_ref)) > 0))
            );

            CREATE TABLE b1_accepted_state_contributions (
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL REFERENCES b1_project_governance(project_id) ON DELETE CASCADE,
                statement TEXT NOT NULL CHECK(length(trim(statement)) > 0),
                scope_kind TEXT NOT NULL CHECK(scope_kind IN ('Project', 'Responsibility', 'Assignment')),
                scope_project_id TEXT NULL,
                scope_responsibility_id TEXT NULL,
                scope_assignment_id TEXT NULL,
                supersedes_contribution_id TEXT NULL,
                authority_decision_id TEXT NOT NULL,
                source_claim_id TEXT NULL,
                UNIQUE(id, project_id),
                UNIQUE(supersedes_contribution_id),
                FOREIGN KEY(scope_responsibility_id, project_id)
                    REFERENCES b1_responsibilities(id, project_id),
                FOREIGN KEY(scope_assignment_id, project_id)
                    REFERENCES b1_assignments(id, project_id),
                FOREIGN KEY(supersedes_contribution_id, project_id)
                    REFERENCES b1_accepted_state_contributions(id, project_id),
                FOREIGN KEY(authority_decision_id, project_id)
                    REFERENCES b1_authority_decisions(id, project_id),
                FOREIGN KEY(source_claim_id, project_id)
                    REFERENCES b1_claims(id, project_id),
                CHECK(
                    (scope_kind = 'Project' AND scope_project_id = project_id AND
                     scope_responsibility_id IS NULL AND scope_assignment_id IS NULL) OR
                    (scope_kind = 'Responsibility' AND scope_project_id IS NULL AND
                     scope_responsibility_id IS NOT NULL AND scope_assignment_id IS NULL) OR
                    (scope_kind = 'Assignment' AND scope_project_id IS NULL AND
                     scope_responsibility_id IS NULL AND scope_assignment_id IS NOT NULL)),
                CHECK(supersedes_contribution_id IS NULL OR supersedes_contribution_id <> id)
            );

            CREATE TABLE b1_assignment_routing (
                assignment_id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL REFERENCES b1_project_governance(project_id) ON DELETE CASCADE,
                selected_attempt_id TEXT NULL,
                UNIQUE(assignment_id, project_id),
                FOREIGN KEY(assignment_id, project_id)
                    REFERENCES b1_assignments(id, project_id) ON DELETE CASCADE,
                FOREIGN KEY(selected_attempt_id, project_id, assignment_id)
                    REFERENCES b1_attempts(id, project_id, assignment_id)
            );

            CREATE TABLE b1_attempt_routing (
                attempt_id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL REFERENCES b1_project_governance(project_id) ON DELETE CASCADE,
                selected_handoff_id TEXT NULL,
                selected_session_binding_id TEXT NULL,
                UNIQUE(attempt_id, project_id),
                FOREIGN KEY(attempt_id, project_id)
                    REFERENCES b1_attempts(id, project_id) ON DELETE CASCADE,
                FOREIGN KEY(selected_handoff_id, project_id, attempt_id)
                    REFERENCES b1_handoffs(id, project_id, attempt_id),
                FOREIGN KEY(selected_session_binding_id, project_id, attempt_id)
                    REFERENCES b1_session_bindings(id, project_id, attempt_id)
            );

            CREATE INDEX ix_b1_logical_actors_project_created
            ON b1_logical_actors(project_id, created_at, id);
            CREATE INDEX ix_b1_responsibilities_project_created
            ON b1_responsibilities(project_id, created_at, id);
            CREATE INDEX ix_b1_assignments_responsibility
            ON b1_assignments(project_id, responsibility_id, id);
            CREATE INDEX ix_b1_revisions_assignment
            ON b1_revisions(project_id, assignment_id, id);
            CREATE INDEX ix_b1_attempts_assignment
            ON b1_attempts(project_id, assignment_id, created_at, id);
            CREATE INDEX ix_b1_session_bindings_attempt
            ON b1_session_bindings(project_id, attempt_id, created_at, id);
            CREATE INDEX ix_b1_claims_project_created
            ON b1_claims(project_id, created_at, id);
            CREATE INDEX ix_b1_handoffs_attempt_created
            ON b1_handoffs(project_id, attempt_id, created_at, id);
            CREATE INDEX ix_b1_decisions_project_sequence
            ON b1_authority_decisions(project_id, project_commit_sequence, id);
            CREATE INDEX ix_b1_contributions_project_scope
            ON b1_accepted_state_contributions(
                project_id, scope_kind, scope_project_id, scope_responsibility_id, scope_assignment_id, id);

            PRAGMA user_version = 20;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
