using Microsoft.Data.Sqlite;

namespace Workbench.Storage.Migrations;

internal static class Migration029EvolutionCandidateConsideredRefs
{
    public const long Version = 29;

    public static async Task ApplyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE UNIQUE INDEX ux_project_evolution_candidates_identity
                ON project_evolution_candidates(id, project_id);

            CREATE TABLE b1_decision_considered_refs_v29 (
                decision_id TEXT NOT NULL,
                project_id TEXT NOT NULL REFERENCES b1_project_governance(project_id) ON DELETE CASCADE,
                ordinal INTEGER NOT NULL CHECK(ordinal >= 0),
                ref_kind TEXT NOT NULL CHECK(ref_kind IN ('Claim', 'Handoff', 'Evidence', 'EvolutionCandidate')),
                claim_id TEXT NULL,
                handoff_id TEXT NULL,
                evidence_ref TEXT NULL,
                evolution_candidate_id TEXT NULL,
                PRIMARY KEY(decision_id, ordinal),
                FOREIGN KEY(decision_id, project_id)
                    REFERENCES b1_authority_decisions(id, project_id) ON DELETE CASCADE,
                FOREIGN KEY(claim_id, project_id)
                    REFERENCES b1_claims(id, project_id),
                FOREIGN KEY(handoff_id, project_id)
                    REFERENCES b1_handoffs(id, project_id),
                FOREIGN KEY(evolution_candidate_id, project_id)
                    REFERENCES project_evolution_candidates(id, project_id),
                CHECK(
                    (ref_kind = 'Claim' AND claim_id IS NOT NULL AND handoff_id IS NULL AND evidence_ref IS NULL AND evolution_candidate_id IS NULL) OR
                    (ref_kind = 'Handoff' AND claim_id IS NULL AND handoff_id IS NOT NULL AND evidence_ref IS NULL AND evolution_candidate_id IS NULL) OR
                    (ref_kind = 'Evidence' AND claim_id IS NULL AND handoff_id IS NULL AND evolution_candidate_id IS NULL AND
                     evidence_ref IS NOT NULL AND length(trim(evidence_ref)) > 0) OR
                    (ref_kind = 'EvolutionCandidate' AND claim_id IS NULL AND handoff_id IS NULL AND evidence_ref IS NULL AND evolution_candidate_id IS NOT NULL))
            );

            INSERT INTO b1_decision_considered_refs_v29(
                decision_id,project_id,ordinal,ref_kind,claim_id,handoff_id,evidence_ref,evolution_candidate_id)
            SELECT decision_id,project_id,ordinal,ref_kind,claim_id,handoff_id,evidence_ref,NULL
            FROM b1_decision_considered_refs;

            DROP TABLE b1_decision_considered_refs;
            ALTER TABLE b1_decision_considered_refs_v29 RENAME TO b1_decision_considered_refs;

            UPDATE b1_accepted_state_contributions
            SET statement = rtrim(substr(statement, 1, instr(statement, '该规则拟作为') - 1), '，,;； ')
            WHERE instr(statement, '该规则拟作为') > 1;

            UPDATE b1_accepted_state_contributions
            SET statement = rtrim(substr(statement, 1, instr(statement, '待用户确认') - 1), '，,;； ')
            WHERE instr(statement, '待用户确认') > 1;

            UPDATE b1_accepted_state_contributions
            SET statement = rtrim(substr(statement, 1, instr(statement, '尚未进入 Accepted Project State') - 1), '，,;； ')
            WHERE instr(statement, '尚未进入 Accepted Project State') > 1;

            UPDATE b1_accepted_state_contributions
            SET statement = rtrim(substr(statement, 1, instr(statement, '尚未进入Accepted Project State') - 1), '，,;； ')
            WHERE instr(statement, '尚未进入Accepted Project State') > 1;

            PRAGMA user_version = 29;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
