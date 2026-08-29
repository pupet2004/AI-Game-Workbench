using System.Globalization;
using Microsoft.Data.Sqlite;
using Workbench.Storage.Database;

namespace Workbench.Storage.Tests.Database;

public sealed class ManualContinuityMigrationTests
{
    private static readonly string[] B1Tables =
    [
        "b1_accepted_state_contributions",
        "b1_assignment_routing",
        "b1_assignments",
        "b1_attempt_routing",
        "b1_attempts",
        "b1_authority_decisions",
        "b1_claims",
        "b1_decision_considered_refs",
        "b1_evidence_records",
        "b1_handoff_claim_refs",
        "b1_handoffs",
        "b1_legacy_project_origins",
        "b1_logical_actors",
        "b1_project_governance",
        "b1_responsibilities",
        "b1_revision_dispositions",
        "b1_revisions",
        "b1_session_bindings"
    ];

    private static readonly string[] B1HistoryTables =
        B1Tables.Where(table => table != "b1_legacy_project_origins").ToArray();

    [Fact]
    public async Task Migration020_sets_user_version_and_creates_exact_b1_tables()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);

        await database.InitializeAsync();

        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        Assert.Equal(25L, await ScalarAsync<long>(connection, "PRAGMA user_version;"));
        Assert.Equal(B1Tables, await StringsAsync(connection,
            "SELECT name FROM sqlite_master WHERE type='table' AND name LIKE 'b1_%' ORDER BY name;"));
        Assert.Contains("activation_source_claim_id", await StringsAsync(connection,
            "SELECT name FROM pragma_table_info('b1_revisions') ORDER BY cid;"));
    }

    [Fact]
    public async Task Migration020_marks_only_projects_existing_at_upgrade_as_pre_b1()
    {
        await using var fixture = await V19Fixture.CreateAsync(projectCount: 2);

        await fixture.Database.InitializeAsync();

        await using var connection = fixture.Database.CreateConnection();
        await connection.OpenAsync();
        Assert.Equal(fixture.ProjectIds.Order(), await StringsAsync(connection,
            "SELECT project_id FROM b1_legacy_project_origins ORDER BY project_id;"));
        Assert.Equal(["19", "19"], await StringsAsync(connection,
            "SELECT CAST(source_schema_version AS TEXT) FROM b1_legacy_project_origins ORDER BY project_id;"));
    }

    [Fact]
    public async Task Post_migration_project_insert_is_not_legacy_eligible()
    {
        await using var fixture = await V19Fixture.CreateAsync(projectCount: 1);
        await fixture.Database.InitializeAsync();
        await using var connection = fixture.Database.CreateConnection();
        await connection.OpenAsync();
        const string postB1Project = "00000000-0000-0000-0000-000000009999";

        await ExecuteAsync(connection, """
            INSERT INTO projects(id,name,root_path,project_type,git_root,created_at,last_opened_at)
            VALUES($id,'Post B1','C:/PostB1',0,NULL,$at,$at);
            """, ("$id", postB1Project), ("$at", V19Fixture.At));

        Assert.Equal(0L, await ScalarAsync<long>(connection,
            "SELECT COUNT(*) FROM b1_legacy_project_origins WHERE project_id=$id;", ("$id", postB1Project)));
    }

    [Fact]
    public async Task Migration020_does_not_synthesize_any_b1_domain_or_authority_rows()
    {
        await using var fixture = await V19Fixture.CreateAsync(projectCount: 2);

        await fixture.Database.InitializeAsync();

        await using var connection = fixture.Database.CreateConnection();
        await connection.OpenAsync();
        Assert.Equal(2L, await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM b1_legacy_project_origins;"));
        foreach (var table in B1HistoryTables)
        {
            Assert.Equal(0L, await ScalarAsync<long>(connection, $"SELECT COUNT(*) FROM {table};"));
        }
    }

    [Fact]
    public async Task Migration020_preserves_v19_schema_and_rows()
    {
        await using var fixture = await V19Fixture.CreateAsync(projectCount: 2);
        LegacySnapshot beforeRows;
        IReadOnlyDictionary<string, string> beforeSchema;
        await using (var before = fixture.Database.CreateConnection())
        {
            await before.OpenAsync();
            Assert.Equal(19L, await ScalarAsync<long>(before, "PRAGMA user_version;"));
            beforeRows = await ReadLegacySnapshotAsync(before);
            beforeSchema = await ReadLegacySchemaAsync(before);
        }

        await fixture.Database.InitializeAsync();

        await using var after = fixture.Database.CreateConnection();
        await after.OpenAsync();
        Assert.Equal(25L, await ScalarAsync<long>(after, "PRAGMA user_version;"));
        Assert.Equal(beforeRows, await ReadLegacySnapshotAsync(after));
        var schemaDeltaKeys = new[] { "index|ix_library_nodes_object_occurred", "table|project_library_timeline_nodes" };
        var expectedSchema = beforeSchema
            .Where(entry => !schemaDeltaKeys.Contains(entry.Key, StringComparer.Ordinal))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        var afterSchema = (await ReadLegacySchemaAsync(after))
            .Where(entry => !schemaDeltaKeys.Contains(entry.Key, StringComparer.Ordinal))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        Assert.Equal(expectedSchema, afterSchema);
        Assert.Equal("ok", await ScalarAsync<string>(after, "PRAGMA quick_check;"));
        Assert.Equal(0L, await ScalarAsync<long>(after, "SELECT COUNT(*) FROM pragma_foreign_key_check;"));
    }

    [Fact]
    public async Task Historical_v11_chain_migrates_to_v20_without_legacy_or_b1_semantic_rewrite()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await HistoricalMigrationTestDatabase.InitializeThroughAsync(database, 11);
        await SeedV11Async(database, projectCount: 1);
        LegacySnapshot before;
        await using (var connection = database.CreateConnection())
        {
            await connection.OpenAsync();
            before = await ReadV11BoundedSnapshotAsync(connection);
            Assert.Equal(11L, await ScalarAsync<long>(connection, "PRAGMA user_version;"));
        }

        await database.InitializeAsync();

        await using var after = database.CreateConnection();
        await after.OpenAsync();
        Assert.Equal(25L, await ScalarAsync<long>(after, "PRAGMA user_version;"));
        Assert.Equal(before, await ReadV11BoundedSnapshotAsync(after));
        Assert.Equal("ok", await ScalarAsync<string>(after, "PRAGMA quick_check;"));
        Assert.Equal(0L, await ScalarAsync<long>(after, "SELECT COUNT(*) FROM pragma_foreign_key_check;"));
        Assert.Equal(1L, await ScalarAsync<long>(after, "SELECT COUNT(*) FROM b1_legacy_project_origins;"));
        foreach (var table in B1HistoryTables)
        {
            Assert.Equal(0L, await ScalarAsync<long>(after, $"SELECT COUNT(*) FROM {table};"));
        }
    }

    [Fact]
    public async Task B1_owned_foreign_keys_reject_cross_project_identity()
    {
        await using var fixture = await V19Fixture.CreateAsync(projectCount: 2);
        await fixture.Database.InitializeAsync();
        await using var connection = fixture.Database.CreateConnection();
        await connection.OpenAsync();
        var first = fixture.ProjectIds[0];
        var second = fixture.ProjectIds[1];
        var seed = await SeedB1GovernanceAsync(connection, first, "01");
        var other = await SeedB1GovernanceAsync(connection, second, "02");

        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, """
            INSERT INTO b1_assignments(
                id,project_id,responsibility_id,assignee_actor_id,replaces_assignment_id,authorized_by_decision_id)
            VALUES($id,$project,$responsibility,$foreign_actor,NULL,$decision);
            """,
            ("$id", Id("cross-assignment")), ("$project", second),
            ("$responsibility", other.ResponsibilityId), ("$foreign_actor", seed.ActorId),
            ("$decision", other.DecisionId)));

        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, """
            INSERT INTO b1_authority_decisions(
                id,project_id,project_commit_sequence,command_kind,deciding_authority_kind,
                deciding_user_principal,deciding_actor_id,created_at)
            VALUES($id,$project,2,'AuthorAcceptedState','LogicalActor',NULL,$foreign_actor,$at);
            """, ("$id", Id("cross-authority")), ("$project", second),
            ("$foreign_actor", seed.ActorId), ("$at", V19Fixture.At)));

        var existingContribution = Id("cross-project-existing-contribution");
        await InsertProjectContributionAsync(connection, existingContribution, first, seed.DecisionId, null);
        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, """
            INSERT INTO b1_claims(
                id,project_id,claimant_kind,claimant_user_principal,claimant_actor_id,source_binding_id,
                kind,statement,proposed_scope_kind,proposed_scope_project_id,
                proposed_scope_responsibility_id,proposed_scope_assignment_id,
                proposed_supersedes_contribution_id,proposed_assignment_id,base_revision_id,
                proposed_work_contract,proposed_delegated_authority_json,evidence_refs_json,created_at)
            VALUES($id,$project,'UserPrincipal',$user,NULL,NULL,'ProposedStateContribution','state',
                'Project',$project,NULL,NULL,$foreign_contribution,NULL,NULL,NULL,NULL,'[]',$at);
            """, ("$id", Id("cross-proposed-supersession")), ("$project", second),
            ("$user", "user:02"), ("$foreign_contribution", existingContribution), ("$at", V19Fixture.At)));
    }

    [Fact]
    public async Task B1_tables_enforce_single_revision_disposition_and_single_superseder()
    {
        await using var fixture = await V19Fixture.CreateAsync(projectCount: 1);
        await fixture.Database.InitializeAsync();
        await using var connection = fixture.Database.CreateConnection();
        await connection.OpenAsync();
        var project = fixture.ProjectIds[0];
        var seed = await SeedB1GovernanceAsync(connection, project, "11");

        await ExecuteAsync(connection, """
            INSERT INTO b1_revision_dispositions(
                revision_id,project_id,assignment_id,disposition,authority_decision_id)
            VALUES($revision,$project,$assignment,'Accepted',$decision);
            """, ("$revision", seed.RevisionId), ("$project", project),
            ("$assignment", seed.AssignmentId), ("$decision", seed.DecisionId));
        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, """
            INSERT INTO b1_revision_dispositions(
                revision_id,project_id,assignment_id,disposition,authority_decision_id)
            VALUES($revision,$project,$assignment,'Rejected',$decision);
            """, ("$revision", seed.RevisionId), ("$project", project),
            ("$assignment", seed.AssignmentId), ("$decision", seed.DecisionId)));

        var original = Id("contribution-original");
        await InsertProjectContributionAsync(connection, original, project, seed.DecisionId, null);
        await InsertProjectContributionAsync(connection, Id("contribution-next"), project, seed.DecisionId, original);
        await Assert.ThrowsAsync<SqliteException>(() => InsertProjectContributionAsync(
            connection, Id("contribution-conflict"), project, seed.DecisionId, original));
    }

    [Fact]
    public async Task B1_tables_enforce_single_assignment_replacement_target()
    {
        await using var fixture = await V19Fixture.CreateAsync(projectCount: 1);
        await fixture.Database.InitializeAsync();
        await using var connection = fixture.Database.CreateConnection();
        await connection.OpenAsync();
        var project = fixture.ProjectIds[0];
        var seed = await SeedB1GovernanceAsync(connection, project, "21");

        await InsertReplacementAsync(connection, Id("replacement-one"), project, seed, seed.AssignmentId);
        await Assert.ThrowsAsync<SqliteException>(() => InsertReplacementAsync(
            connection, Id("replacement-two"), project, seed, seed.AssignmentId));
    }

    [Fact]
    public async Task B1_revision_activation_source_is_same_project_and_not_initial()
    {
        await using var fixture = await V19Fixture.CreateAsync(projectCount: 2);
        await fixture.Database.InitializeAsync();
        await using var connection = fixture.Database.CreateConnection();
        await connection.OpenAsync();
        var first = fixture.ProjectIds[0];
        var second = fixture.ProjectIds[1];
        var firstSeed = await SeedB1GovernanceAsync(connection, first, "activation-01");
        var secondSeed = await SeedB1GovernanceAsync(connection, second, "activation-02");
        var firstClaim = Id("activation-claim-01");
        var secondClaim = Id("activation-claim-02");
        await InsertResultClaimAsync(connection, firstClaim, first, "user:activation-01");
        await InsertResultClaimAsync(connection, secondClaim, second, "user:activation-02");

        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, """
            UPDATE b1_revisions
            SET activation_source_claim_id=$claim
            WHERE id=$revision;
            """, ("$claim", firstClaim), ("$revision", firstSeed.RevisionId)));

        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, """
            INSERT INTO b1_revisions(
                id,project_id,assignment_id,prior_revision_id,work_contract,
                delegated_authority_json,activation_source_claim_id,authorized_by_decision_id)
            VALUES($id,$project,$assignment,$prior,'R2','[]',$claim,$decision);
            """, ("$id", Id("cross-project-activation-revision")), ("$project", second),
            ("$assignment", secondSeed.AssignmentId), ("$prior", secondSeed.RevisionId),
            ("$claim", firstClaim), ("$decision", secondSeed.DecisionId)));

        await ExecuteAsync(connection, """
            INSERT INTO b1_revisions(
                id,project_id,assignment_id,prior_revision_id,work_contract,
                delegated_authority_json,activation_source_claim_id,authorized_by_decision_id)
            VALUES($id,$project,$assignment,$prior,'R2','[]',$claim,$decision);
            """, ("$id", Id("same-project-activation-revision")), ("$project", second),
            ("$assignment", secondSeed.AssignmentId), ("$prior", secondSeed.RevisionId),
            ("$claim", secondClaim), ("$decision", secondSeed.DecisionId));
    }

    private static async Task<B1Seed> SeedB1GovernanceAsync(SqliteConnection connection, string projectId, string suffix)
    {
        var decision = Id($"decision-{suffix}");
        var actor = Id($"actor-{suffix}");
        var responsibility = Id($"responsibility-{suffix}");
        var assignment = Id($"assignment-{suffix}");
        var revision = Id($"revision-{suffix}");
        await ExecuteAsync(connection, """
            INSERT INTO b1_project_governance(
                project_id,bootstrap_user_principal,origin,adopted_at,last_commit_sequence)
            VALUES($project,$user,'Created',NULL,1);
            INSERT INTO b1_authority_decisions(
                id,project_id,project_commit_sequence,command_kind,deciding_authority_kind,
                deciding_user_principal,deciding_actor_id,created_at)
            VALUES($decision,$project,1,'EstablishResponsibility','UserPrincipal',$user,NULL,$at);
            INSERT INTO b1_logical_actors(id,project_id,role_kind,authorized_by_decision_id,created_at)
            VALUES($actor,$project,'Worker',$decision,$at);
            INSERT INTO b1_responsibilities(
                id,project_id,obligation,expected_outcome,maximum_authority_json,authorized_by_decision_id,created_at)
            VALUES($responsibility,$project,'Own work','Work complete','[]',$decision,$at);
            INSERT INTO b1_assignments(
                id,project_id,responsibility_id,assignee_actor_id,replaces_assignment_id,authorized_by_decision_id)
            VALUES($assignment,$project,$responsibility,$actor,NULL,$decision);
            INSERT INTO b1_revisions(
                id,project_id,assignment_id,prior_revision_id,work_contract,delegated_authority_json,authorized_by_decision_id)
            VALUES($revision,$project,$assignment,NULL,'Initial work','[]',$decision);
            """, ("$project", projectId), ("$user", $"user:{suffix}"), ("$decision", decision),
            ("$actor", actor), ("$responsibility", responsibility), ("$assignment", assignment),
            ("$revision", revision), ("$at", V19Fixture.At));
        return new(decision, actor, responsibility, assignment, revision);
    }

    private static Task InsertProjectContributionAsync(
        SqliteConnection connection, string id, string project, string decision, string? supersedes) =>
        ExecuteAsync(connection, """
            INSERT INTO b1_accepted_state_contributions(
                id,project_id,statement,scope_kind,scope_project_id,scope_responsibility_id,
                scope_assignment_id,supersedes_contribution_id,authority_decision_id,source_claim_id)
            VALUES($id,$project,'state','Project',$project,NULL,NULL,$supersedes,$decision,NULL);
            """, ("$id", id), ("$project", project), ("$supersedes", supersedes), ("$decision", decision));

    private static Task InsertReplacementAsync(
        SqliteConnection connection, string id, string project, B1Seed seed, string replaces) =>
        ExecuteAsync(connection, """
            INSERT INTO b1_assignments(
                id,project_id,responsibility_id,assignee_actor_id,replaces_assignment_id,authorized_by_decision_id)
            VALUES($id,$project,$responsibility,$actor,$replaces,$decision);
            """, ("$id", id), ("$project", project), ("$responsibility", seed.ResponsibilityId),
            ("$actor", seed.ActorId), ("$replaces", replaces), ("$decision", seed.DecisionId));

    private static Task InsertResultClaimAsync(
        SqliteConnection connection, string id, string project, string user) =>
        ExecuteAsync(connection, """
            INSERT INTO b1_claims(
                id,project_id,claimant_kind,claimant_user_principal,claimant_actor_id,source_binding_id,
                kind,statement,proposed_scope_kind,proposed_scope_project_id,
                proposed_scope_responsibility_id,proposed_scope_assignment_id,
                proposed_supersedes_contribution_id,proposed_assignment_id,base_revision_id,
                proposed_work_contract,proposed_delegated_authority_json,evidence_refs_json,created_at)
            VALUES($id,$project,'UserPrincipal',$user,NULL,NULL,'Result','source',NULL,NULL,NULL,NULL,
                NULL,NULL,NULL,NULL,NULL,'[]',$at);
            """, ("$id", id), ("$project", project), ("$user", user), ("$at", V19Fixture.At));

    private static async Task SeedV11Async(WorkbenchDatabase database, int projectCount)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        await ExecuteAsync(connection, """
            INSERT INTO projects(id,name,root_path,project_type,git_root,created_at,last_opened_at)
            VALUES('00000000-0000-0000-0000-000000000101','Migration Project','C:/Migration',0,NULL,$at,$at);
            INSERT INTO project_leaders(project_id,current_epoch_id,created_at,updated_at)
            VALUES('00000000-0000-0000-0000-000000000101',NULL,$at,$at);
            INSERT INTO leader_session_epochs(
                id,project_id,provider_id,provider_account_id,model_id,agent_session_id,
                external_session_id,working_directory,started_at,last_active_at,ended_at,
                rollover_reason,handoff_summary,boot_context_delivered_at)
            VALUES('00000000-0000-0000-0000-000000000104',
                '00000000-0000-0000-0000-000000000101','codex','account','model','agent',
                'external','C:/Migration',$at,$at,NULL,NULL,NULL,$at);
            UPDATE project_leaders SET current_epoch_id='00000000-0000-0000-0000-000000000104'
            WHERE project_id='00000000-0000-0000-0000-000000000101';
            INSERT INTO leader_messages(epoch_id,sequence,role,text,created_at)
            VALUES('00000000-0000-0000-0000-000000000104',1,'user','leader message',$at);
            INSERT INTO tasks(id,project_id,title,status,current_revision_id,created_at,updated_at,cancelled_at)
            VALUES('00000000-0000-0000-0000-000000000102',
                '00000000-0000-0000-0000-000000000101','Migration Task','ReadyToStart',
                '00000000-0000-0000-0000-000000000103',$at,$at,NULL);
            INSERT INTO task_revisions(
                id,task_id,revision_number,goal,scope,out_of_scope,acceptance_json,risk_level,
                recommended_provider_id,recommended_provider_account_id,recommended_model_profile_id,
                recommended_agent_runtime_id,change_reason,approved_by,created_at,previous_revision_id)
            VALUES('00000000-0000-0000-0000-000000000103',
                '00000000-0000-0000-0000-000000000102',1,'Preserve migration data','Storage','None',
                '[]','Low','codex','account','profile','runtime','Initial','Leader',$at,NULL);
            INSERT INTO task_events(id,project_id,task_id,execution_id,event_type,status,payload_json,created_at)
            VALUES('00000000-0000-0000-0000-000000000107',
                '00000000-0000-0000-0000-000000000101',
                '00000000-0000-0000-0000-000000000102',NULL,'FixtureCreated','ReadyToStart',
                '{"kind":"fixture"}',$at);
            INSERT INTO project_memory_items(
                id,project_id,layer,topic,content,status,created_at,updated_at,certified_at)
            VALUES('00000000-0000-0000-0000-000000000111',
                '00000000-0000-0000-0000-000000000101','Daily','topic','legacy memory','Active',$at,$at,NULL);
            INSERT INTO project_memory_sources(memory_id,source_type,source_ref)
            VALUES('00000000-0000-0000-0000-000000000111','Task','00000000-0000-0000-0000-000000000102');
            INSERT INTO project_daily_summaries(project_id,local_date,content,revision,created_at,updated_at)
            VALUES('00000000-0000-0000-0000-000000000101','2026-08-22','daily summary',1,$at,$at);
            INSERT INTO project_daily_summary_sources(project_id,local_date,source_type,source_ref)
            VALUES('00000000-0000-0000-0000-000000000101','2026-08-22','Task',
                '00000000-0000-0000-0000-000000000102');
            INSERT INTO project_library_entries(
                id,project_id,source_session_id,task_id,category,topic,summary,source_reference,created_at)
            VALUES('00000000-0000-0000-0000-000000000108',
                '00000000-0000-0000-0000-000000000101','external',
                '00000000-0000-0000-0000-000000000102','Architecture','Persistence',
                'legacy library','docs/legacy.md',$at);
            """, ("$at", V19Fixture.At));

        if (projectCount == 2)
        {
            await ExecuteAsync(connection, """
                INSERT INTO projects(id,name,root_path,project_type,git_root,created_at,last_opened_at)
                VALUES('00000000-0000-0000-0000-000000000201','Second Project','C:/Second',0,NULL,$at,$at);
                """, ("$at", V19Fixture.At));
        }
    }

    private static async Task SeedV19RowsAsync(WorkbenchDatabase database)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        await ExecuteAsync(connection, """
            INSERT INTO task_review_decisions(
                review_decision_id,project_id,task_id,revision_id,source_event_id,final_report_event_id,
                outcome,action_level,authority_mode,authority_resolution,authority_mode_recording,created_at)
            VALUES('00000000-0000-0000-0000-000000000112',
                '00000000-0000-0000-0000-000000000101',
                '00000000-0000-0000-0000-000000000102',
                '00000000-0000-0000-0000-000000000103',NULL,
                '00000000-0000-0000-0000-000000000107','Pass','L1LocalFix','Balanced',
                'NotifyAndProceed','Recorded',$at);
            INSERT INTO project_summary_entries(
                entry_id,project_id,occurred_at,created_at,kind,text,result_id,delta_ordinal)
            VALUES('00000000-0000-0000-0000-000000000113',
                '00000000-0000-0000-0000-000000000101',$at,$at,'Decision','summary entry',
                '00000000-0000-0000-0000-000000000114',0);
            INSERT INTO project_summary_source_refs(entry_id,ordinal,source_kind,source_locator)
            VALUES('00000000-0000-0000-0000-000000000113',0,'Task',
                '00000000-0000-0000-0000-000000000102');
            """, ("$at", V19Fixture.At));
    }

    private static Task<LegacySnapshot> ReadV11BoundedSnapshotAsync(SqliteConnection connection) =>
        ReadSnapshotAsync(connection, includeV19Rows: false);

    private static Task<LegacySnapshot> ReadLegacySnapshotAsync(SqliteConnection connection) =>
        ReadSnapshotAsync(connection, includeV19Rows: true);

    private static async Task<LegacySnapshot> ReadSnapshotAsync(SqliteConnection connection, bool includeV19Rows)
    {
        var common = string.Join('\n', await RowsAsync(connection, """
            SELECT id,name,root_path FROM projects ORDER BY id;
            """));
        var continuity = string.Join('\n', await RowsAsync(connection, """
            SELECT 'epoch',id,project_id,external_session_id FROM leader_session_epochs
            UNION ALL SELECT 'task',id,project_id,status FROM tasks
            UNION ALL SELECT 'revision',id,task_id,goal FROM task_revisions
            UNION ALL SELECT 'event',id,task_id,payload_json FROM task_events
            UNION ALL SELECT 'memory',id,project_id,content FROM project_memory_items
            UNION ALL SELECT 'library',id,project_id,summary FROM project_library_entries
            ORDER BY 1,2;
            """));
        var later = includeV19Rows
            ? string.Join('\n', await RowsAsync(connection, """
                SELECT 'review',review_decision_id,project_id,outcome FROM task_review_decisions
                UNION ALL SELECT 'summary',entry_id,project_id,text FROM project_summary_entries
                ORDER BY 1,2;
                """))
            : string.Empty;
        return new(common, continuity, later);
    }

    private static async Task<IReadOnlyDictionary<string, string>> ReadLegacySchemaAsync(SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT type || '|' || name, sql
            FROM sqlite_master
            WHERE type IN ('table','index','trigger','view')
              AND name NOT LIKE 'sqlite_%'
              AND name NOT IN ('project_library_proposals', 'ix_library_proposals_project_status')
              AND name <> 'ux_worker_active_project'
              AND tbl_name NOT LIKE 'b1\_%' ESCAPE '\'
              AND sql IS NOT NULL
            ORDER BY type,name;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        while (await reader.ReadAsync()) result.Add(reader.GetString(0), NormalizeSql(reader.GetString(1)));
        return result;
    }

    private static string NormalizeSql(string sql) =>
        string.Join(' ', sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static async Task<IReadOnlyList<string>> RowsAsync(SqliteConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
        {
            var values = new string[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
            {
                values[i] = reader.IsDBNull(i)
                    ? "<NULL>"
                    : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture)!;
            }
            rows.Add(string.Join('|', values));
        }
        return rows;
    }

    private static async Task<IReadOnlyList<string>> StringsAsync(SqliteConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync()) values.Add(reader.GetString(0));
        return values;
    }

    private static async Task<T> ScalarAsync<T>(
        SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameters(command, parameters);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameters(command, parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static void AddParameters(SqliteCommand command, IEnumerable<(string Name, object? Value)> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
    }

    private static string Id(string seed)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed));
        return new Guid(bytes[..16]).ToString();
    }

    private sealed record LegacySnapshot(string Projects, string Continuity, string V19Rows);
    private sealed record B1Seed(
        string DecisionId, string ActorId, string ResponsibilityId, string AssignmentId, string RevisionId);

    private sealed class V19Fixture : IAsyncDisposable
    {
        private readonly TemporaryDatabase _temporary;

        private V19Fixture(TemporaryDatabase temporary, WorkbenchDatabase database, string[] projectIds)
        {
            _temporary = temporary;
            Database = database;
            ProjectIds = projectIds;
        }

        public const string At = "2026-08-22T10:00:00.0000000+00:00";
        public WorkbenchDatabase Database { get; }
        public string[] ProjectIds { get; }

        public static async Task<V19Fixture> CreateAsync(int projectCount)
        {
            var temporary = new TemporaryDatabase();
            var database = new WorkbenchDatabase(temporary.DatabasePath);
            await HistoricalMigrationTestDatabase.InitializeThroughAsync(database, 11);
            await SeedV11Async(database, projectCount);
            await HistoricalMigrationTestDatabase.InitializeThroughAsync(database, 19);
            await SeedV19RowsAsync(database);
            return new(temporary, database,
                projectCount == 2
                    ? ["00000000-0000-0000-0000-000000000101", "00000000-0000-0000-0000-000000000201"]
                    : ["00000000-0000-0000-0000-000000000101"]);
        }

        public ValueTask DisposeAsync() => _temporary.DisposeAsync();
    }
}
