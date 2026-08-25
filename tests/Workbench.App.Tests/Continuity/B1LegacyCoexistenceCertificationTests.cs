using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.Data.Sqlite;
using Workbench.App.Services;
using Workbench.App.Tests.Support;
using Workbench.Core.Continuity;
using Workbench.Storage.Database;

namespace Workbench.App.Tests.Continuity;

public sealed class B1LegacyCoexistenceCertificationTests
{
    private static readonly DateTimeOffset LegacyAt =
        DateTimeOffset.Parse("2026-08-22T10:00:00.0000000+00:00", CultureInfo.InvariantCulture);
    private static readonly DateTimeOffset CurrentAt =
        DateTimeOffset.Parse("2026-08-23T10:00:00.0000000+00:00", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Migration_creates_only_legacy_origin_and_no_b1_identity_or_authority()
    {
        await using var fixture = await LegacyFixture.CreateAsync();

        Assert.Equal(22L, await fixture.ScalarAsync<long>("PRAGMA user_version;"));
        Assert.Equal("ok", await fixture.ScalarAsync<string>("PRAGMA quick_check;"));
        Assert.Equal(0L, await fixture.ScalarAsync<long>("SELECT COUNT(*) FROM pragma_foreign_key_check;"));
        Assert.Equal(1L, await fixture.ProjectCountAsync("b1_legacy_project_origins"));
        foreach (var table in B1HistoryTables)
        {
            Assert.Equal(0L, await fixture.ProjectCountAsync(table));
        }
    }

    [Fact]
    public async Task Legacy_leader_epoch_never_becomes_actor_or_session_binding()
    {
        await using var fixture = await LegacyFixture.CreateAsync();

        var epoch = await fixture.Services.LeaderSessionEpochRepository.GetCurrentForProjectAsync(fixture.ProjectId);
        var messages = await fixture.Services.LeaderMessageRepository.GetAllAsync(fixture.EpochId);

        Assert.NotNull(epoch);
        Assert.Equal(fixture.EpochId, epoch.Id);
        Assert.Equal("legacy-external-session", epoch.ExternalSessionId);
        Assert.Equal("LEGACY_LEADER_MESSAGE", Assert.Single(messages).Text);
        Assert.Equal(0L, await fixture.ProjectCountAsync("b1_logical_actors"));
        Assert.Equal(0L, await fixture.ProjectCountAsync("b1_session_bindings"));
    }

    [Fact]
    public async Task Legacy_task_revision_never_becomes_responsibility_assignment_or_b1_revision()
    {
        await using var fixture = await LegacyFixture.CreateAsync();

        var task = await fixture.Services.TaskRepository.GetAsync(fixture.ProjectId, fixture.TaskId);
        var revisions = await fixture.Services.TaskRevisionRepository.ListAsync(fixture.ProjectId, fixture.TaskId);

        Assert.NotNull(task);
        Assert.Equal("LEGACY_TASK", task.Title);
        Assert.Equal("LEGACY_REVISION_GOAL", Assert.Single(revisions).Goal);
        Assert.Equal(0L, await fixture.ProjectCountAsync("b1_responsibilities"));
        Assert.Equal(0L, await fixture.ProjectCountAsync("b1_assignments"));
        Assert.Equal(0L, await fixture.ProjectCountAsync("b1_revisions"));
    }

    [Fact]
    public async Task Legacy_review_or_autoproceed_never_becomes_authority_decision()
    {
        await using var fixture = await LegacyFixture.CreateAsync();

        var review = await fixture.ReviewRepository.GetDecisionAsync(fixture.ProjectId, fixture.TaskId, fixture.ReviewDecisionId);

        Assert.NotNull(review);
        Assert.Equal("Pass", review.Outcome);
        Assert.Equal("NotifyAndProceed", review.AuthorityResolution);
        Assert.Equal(0L, await fixture.ProjectCountAsync("b1_authority_decisions"));
        Assert.Equal(0L, await fixture.ProjectCountAsync("b1_revision_dispositions"));
    }

    [Fact]
    public async Task Legacy_completion_event_never_becomes_claim_or_handoff()
    {
        await using var fixture = await LegacyFixture.CreateAsync();

        Assert.Equal("{\"result\":\"LEGACY_COMPLETION\"}", await fixture.ScalarAsync<string>(
            "SELECT payload_json FROM task_events WHERE id=$id AND event_type='WorkerCompleted';",
            ("$id", fixture.CompletionEventId.ToString())));
        Assert.Equal(0L, await fixture.ProjectCountAsync("b1_claims"));
        Assert.Equal(0L, await fixture.ProjectCountAsync("b1_handoffs"));
    }

    [Fact]
    public async Task Memory_daily_library_and_summary_never_become_accepted_state()
    {
        await using var fixture = await LegacyFixture.CreateAsync();

        var memory = await fixture.Services.ProjectMemoryService.GetMemoryItemAsync(fixture.MemoryId);
        var daily = await fixture.Services.DailySummaryRepository.GetAsync(fixture.ProjectId, new DateOnly(2026, 8, 22));
        var library = await fixture.Services.ProjectLibraryRepository.BrowseAsync(fixture.ProjectId);
        var summaries = await fixture.Services.ProjectSummaryRepository.QueryAsync(
            new Workbench.Storage.Memory.SummaryQuery(fixture.ProjectId, 10));
        var synthesis = await fixture.Services.ProjectMemorySynthesisRepository.GetAsync(fixture.SynthesisEpochId);

        Assert.NotNull(memory);
        Assert.NotNull(daily);
        Assert.Equal("LEGACY_MEMORY", memory.Content);
        Assert.Equal("LEGACY_DAILY", daily.Content);
        Assert.Equal("LEGACY_LIBRARY", Assert.Single(library).Summary);
        Assert.Equal("LEGACY_SUMMARY", Assert.Single(summaries).Text);
        Assert.NotNull(synthesis);
        Assert.Equal(Workbench.Storage.Memory.ProjectMemorySynthesisJobStatus.Completed, synthesis.Status);
        Assert.Equal(2, synthesis.AttemptCount);
        Assert.Equal(LegacyAt, synthesis.LastAttemptedAt);
        Assert.Equal(LegacyAt, synthesis.CompletedAt);
        Assert.Null(synthesis.LastError);
        await fixture.AdoptAsync();
        Assert.Empty((await fixture.Services.B1Projections.GetAcceptedProjectStateAsync(fixture.ProjectRef)).CurrentContributions);
    }

    [Fact]
    public async Task Explicit_adoption_sets_only_bootstrap_root()
    {
        await using var fixture = await LegacyFixture.CreateAsync();

        var adopted = await fixture.AdoptAsync();

        Assert.Equal(B1GovernanceOrigin.Adopted, adopted.Origin);
        Assert.Equal(fixture.OperatorRef, adopted.BootstrapPrincipalRef);
        Assert.Equal(CurrentAt, adopted.AdoptedAt);
        foreach (var table in B1HistoryTables.Where(table => table != "b1_project_governance"))
        {
            Assert.Equal(0L, await fixture.ProjectCountAsync(table));
        }
    }

    [Fact]
    public async Task Explicit_current_time_claim_can_reference_legacy_evidence_locator()
    {
        await using var fixture = await LegacyFixture.CreateAsync();
        var seed = await fixture.EstablishCurrentB1RootAsync();
        var evidence = new EvidenceRef($"legacy:task-event:{fixture.CompletionEventId}");

        var claim = await fixture.Services.B1NonAuthoritativeCommands.RecordClaimAsync(
            new RecordClaimCommand(
                fixture.ProjectRef,
                fixture.OperatorRef,
                new ClaimRef(Guid.NewGuid()),
                new ClaimantRef.LogicalActor(seed.ActorRef),
                null,
                new ClaimPayload.Result("CURRENT_TIME_RESULT"),
                [evidence],
                CurrentAt));

        Assert.Equal([evidence], claim.EvidenceRefs);
        Assert.Equal(CurrentAt, claim.CreatedAt);
        Assert.Equal(1L, await fixture.ProjectCountAsync("b1_claims"));
        Assert.Equal(0L, await fixture.ProjectCountAsync("b1_handoffs"));
    }

    [Fact]
    public async Task Named_authority_command_is_required_before_any_accepted_contribution()
    {
        await using var fixture = await LegacyFixture.CreateAsync();
        var seed = await fixture.EstablishCurrentB1RootAsync();
        var contributionClaim = await fixture.RecordContributionClaimAsync(seed, "CURRENT_TIME_CONTRIBUTION");

        Assert.Empty((await fixture.Services.B1Projections.GetAcceptedProjectStateAsync(fixture.ProjectRef)).CurrentContributions);
        var decision = await fixture.Services.B1AuthorityCommands.AuthorAcceptedStateAsync(
            new AuthorAcceptedStateCommand(
                fixture.ProjectRef,
                fixture.OperatorRef,
                new DecidingAuthorityRef.UserPrincipal(fixture.OperatorRef),
                [new ConsideredRef.Claim(contributionClaim.ClaimRef)],
                [new AcceptedContributionInstruction(
                    "CURRENT_TIME_CONTRIBUTION",
                    new ContributionScopeTarget.Project(fixture.ProjectRef),
                    null,
                    contributionClaim.ClaimRef)]));

        var accepted = Assert.Single((await fixture.Services.B1Projections.GetAcceptedProjectStateAsync(fixture.ProjectRef)).CurrentContributions);
        Assert.Equal(decision.DecisionRef, accepted.AuthorityDecisionRef);
        Assert.Equal(contributionClaim.ClaimRef, accepted.SourceClaimRef);
    }

    [Fact]
    public async Task Legacy_rows_remain_byte_for_byte_equivalent_in_bounded_columns_after_b1_actions()
    {
        await using var fixture = await LegacyFixture.CreateAsync();
        var before = await fixture.ReadLegacySnapshotAsync();
        var seed = await fixture.EstablishCurrentB1RootAsync();
        var contributionClaim = await fixture.RecordContributionClaimAsync(seed, "CURRENT_TIME_CONTRIBUTION");
        await fixture.Services.B1AuthorityCommands.AuthorAcceptedStateAsync(
            new AuthorAcceptedStateCommand(
                fixture.ProjectRef,
                fixture.OperatorRef,
                new DecidingAuthorityRef.UserPrincipal(fixture.OperatorRef),
                [new ConsideredRef.Claim(contributionClaim.ClaimRef)],
                [new AcceptedContributionInstruction(
                    "CURRENT_TIME_CONTRIBUTION",
                    new ContributionScopeTarget.Project(fixture.ProjectRef),
                    null,
                    contributionClaim.ClaimRef)]));

        Assert.Equal(before, await fixture.ReadLegacySnapshotAsync());
    }

    private static readonly string[] B1HistoryTables =
    [
        "b1_accepted_state_contributions", "b1_assignment_routing", "b1_assignments", "b1_attempt_routing",
        "b1_attempts", "b1_authority_decisions", "b1_claims", "b1_decision_considered_refs",
        "b1_handoff_claim_refs", "b1_handoffs", "b1_logical_actors", "b1_project_governance",
        "b1_responsibilities", "b1_revision_dispositions", "b1_revisions", "b1_session_bindings"
    ];

    private sealed record B1Seed(LogicalActorRef ActorRef, AssignmentRef AssignmentRef, RevisionRef RevisionRef);
    private sealed record LegacySnapshot(string Rows);

    private sealed class LegacyFixture : IAsyncDisposable
    {
        private readonly TemporaryDirectory _directory;
        private readonly AppServices _services;

        private LegacyFixture(TemporaryDirectory directory, AppServices services)
        {
            _directory = directory;
            _services = services;
        }

        public Guid ProjectId { get; } = Guid.Parse("00000000-0000-0000-0000-000000000101");
        public Guid EpochId { get; } = Guid.Parse("00000000-0000-0000-0000-000000000104");
        public Guid TaskId { get; } = Guid.Parse("00000000-0000-0000-0000-000000000102");
        public Guid CompletionEventId { get; } = Guid.Parse("00000000-0000-0000-0000-000000000107");
        public Guid SynthesisEpochId { get; } = Guid.Parse("00000000-0000-0000-0000-000000000109");
        public Guid MemoryId { get; } = Guid.Parse("00000000-0000-0000-0000-000000000111");
        public Guid ReviewDecisionId { get; } = Guid.Parse("00000000-0000-0000-0000-000000000112");
        public ProjectRef ProjectRef => new(ProjectId);
        public UserPrincipalRef OperatorRef { get; } = new("user:explicit-adopter");
        public AppServices Services => _services;
        public Workbench.Storage.Reviews.LeaderReviewStateRepository ReviewRepository =>
            new(_services.Database);

        public static async Task<LegacyFixture> CreateAsync()
        {
            var directory = new TemporaryDirectory("b1-legacy-coexistence");
            var database = new WorkbenchDatabase(Path.Combine(directory.Path, "workbench.db"));
            await InitializeThroughAsync(database, 19);
            await SeedV19Async(database);
            await database.InitializeAsync();
            return new LegacyFixture(directory, AppServices.CreateForDatabasePath(database.DatabasePath));
        }

        public Task<ProjectGovernance> AdoptAsync() =>
            _services.B1ProjectGovernance.AdoptLegacyProjectAsync(ProjectRef, OperatorRef, CurrentAt);

        public async Task<B1Seed> EstablishCurrentB1RootAsync()
        {
            await AdoptAsync();
            var decision = await _services.B1AuthorityCommands.EstablishResponsibilityAsync(
                new EstablishResponsibilityCommand(
                    ProjectRef,
                    OperatorRef,
                    new DecidingAuthorityRef.UserPrincipal(OperatorRef),
                    new ResponsibilityContract("CURRENT_TIME_RESPONSIBILITY", "CURRENT_TIME_RESULT", AuthorityBoundary.Empty),
                    new AssignmentDelegationInstruction(
                        new ResponsibilityTarget.EstablishedByThisDecision(),
                        new AssignmentAssigneeTarget.EstablishedByThisDecision(),
                        new AssignmentRevisionContract("CURRENT_TIME_REVISION"),
                        null),
                    RoleKind.Worker,
                    [],
                    []));
            return new(
                decision.LogicalActorEstablishmentEffect!.LogicalActor.LogicalActorRef,
                decision.AssignmentDelegationEffect!.Assignment.AssignmentRef,
                decision.AssignmentDelegationEffect.InitialRevision.RevisionRef);
        }

        public Task<Claim> RecordContributionClaimAsync(B1Seed seed, string statement) =>
            _services.B1NonAuthoritativeCommands.RecordClaimAsync(
                new RecordClaimCommand(
                    ProjectRef,
                    OperatorRef,
                    new ClaimRef(Guid.NewGuid()),
                    new ClaimantRef.LogicalActor(seed.ActorRef),
                    null,
                    new ClaimPayload.ProposedStateContribution(
                        statement,
                        new ContributionScopeRef.Project(ProjectRef),
                        null),
                    [new EvidenceRef($"legacy:task-event:{CompletionEventId}")],
                    CurrentAt));

        public async Task<long> ProjectCountAsync(string table) => await ScalarAsync<long>(
            $"SELECT COUNT(*) FROM {table} WHERE project_id=$project;", ("$project", ProjectId.ToString()));

        public async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = _services.Database.CreateConnection();
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
            return (T)(await command.ExecuteScalarAsync())!;
        }

        public async Task<LegacySnapshot> ReadLegacySnapshotAsync()
        {
            await using var connection = _services.Database.CreateConnection();
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = """
                SELECT 'project',id,name,root_path FROM projects
                UNION ALL SELECT 'epoch',id,project_id,external_session_id FROM leader_session_epochs
                UNION ALL SELECT 'message',CAST(sequence AS TEXT),epoch_id,text FROM leader_messages
                UNION ALL SELECT 'task',id,project_id,title FROM tasks
                UNION ALL SELECT 'revision',id,task_id,goal FROM task_revisions
                UNION ALL SELECT 'event',id,task_id,payload_json FROM task_events
                UNION ALL SELECT 'review',review_decision_id,project_id,outcome FROM task_review_decisions
                UNION ALL SELECT 'memory',id,project_id,content FROM project_memory_items
                UNION ALL SELECT 'synthesis',epoch_id,project_id,status || ':' || attempt_count || ':' ||
                    COALESCE(last_attempted_at,'<NULL>') || ':' || COALESCE(completed_at,'<NULL>') || ':' ||
                    COALESCE(last_error,'<NULL>') FROM project_memory_synthesis_jobs
                UNION ALL SELECT 'daily',local_date,project_id,content FROM project_daily_summaries
                UNION ALL SELECT 'library',id,project_id,summary FROM project_library_entries
                UNION ALL SELECT 'summary',entry_id,project_id,text FROM project_summary_entries
                ORDER BY 1,2;
                """;
            await using var reader = await command.ExecuteReaderAsync();
            var rows = new List<string>();
            while (await reader.ReadAsync())
            {
                rows.Add(string.Join('|', Enumerable.Range(0, reader.FieldCount).Select(index =>
                    reader.IsDBNull(index) ? "<NULL>" : Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture))));
            }
            return new(string.Join('\n', rows));
        }

        public async ValueTask DisposeAsync()
        {
            await _services.DisposeAsync();
            _directory.Dispose();
        }

        private static async Task InitializeThroughAsync(WorkbenchDatabase database, long targetVersion)
        {
            var directory = Path.GetDirectoryName(database.DatabasePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            await using var connection = database.CreateConnection();
            await connection.OpenAsync();
            foreach (var migration in typeof(WorkbenchDatabase).Assembly.GetTypes()
                         .Where(type => type.Namespace == "Workbench.Storage.Migrations" && type.Name.StartsWith("Migration", StringComparison.Ordinal))
                         .Select(type => (Version: Convert.ToInt64(type.GetField("Version", BindingFlags.Public | BindingFlags.Static)!.GetRawConstantValue()),
                             Apply: type.GetMethod("ApplyAsync", BindingFlags.Public | BindingFlags.Static)!))
                         .Where(item => item.Version <= targetVersion)
                         .OrderBy(item => item.Version))
            {
                if (migration.Version is 12 or 15 or 16 or 18) await ExecuteAsync(connection, "PRAGMA foreign_keys = OFF;");
                await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
                try
                {
                    await ((Task)migration.Apply.Invoke(null, [connection, transaction, CancellationToken.None])!)!;
                    await transaction.CommitAsync();
                }
                catch (TargetInvocationException exception) when (exception.InnerException is not null)
                {
                    await transaction.RollbackAsync();
                    ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                    throw;
                }
                finally
                {
                    if (migration.Version is 12 or 15 or 16 or 18) await ExecuteAsync(connection, "PRAGMA foreign_keys = ON;");
                }
            }
        }

        private static async Task SeedV19Async(WorkbenchDatabase database)
        {
            await using var connection = database.CreateConnection();
            await connection.OpenAsync();
            await ExecuteAsync(connection, """
                INSERT INTO projects(id,name,root_path,project_type,git_root,created_at,last_opened_at)
                VALUES('00000000-0000-0000-0000-000000000101','LEGACY_PROJECT','C:/Legacy',0,NULL,$at,$at);
                INSERT INTO project_leaders(project_id,current_epoch_id,created_at,updated_at)
                VALUES('00000000-0000-0000-0000-000000000101',NULL,$at,$at);
                INSERT INTO leader_session_epochs(id,project_id,provider_id,provider_account_id,model_id,agent_session_id,external_session_id,working_directory,started_at,last_active_at,ended_at,rollover_reason,handoff_summary,boot_context_delivered_at)
                VALUES('00000000-0000-0000-0000-000000000104','00000000-0000-0000-0000-000000000101','legacy-provider','00000000-0000-0000-0000-000000000105','legacy-model','00000000-0000-0000-0000-000000000106','legacy-external-session','C:/Legacy',$at,$at,NULL,NULL,NULL,$at);
                UPDATE project_leaders SET current_epoch_id='00000000-0000-0000-0000-000000000104'
                WHERE project_id='00000000-0000-0000-0000-000000000101';
                INSERT INTO leader_session_epochs(id,project_id,provider_id,provider_account_id,model_id,agent_session_id,external_session_id,working_directory,started_at,last_active_at,ended_at,rollover_reason,handoff_summary,boot_context_delivered_at)
                VALUES('00000000-0000-0000-0000-000000000109','00000000-0000-0000-0000-000000000101','legacy-provider','00000000-0000-0000-0000-000000000115','legacy-model','00000000-0000-0000-0000-000000000116','legacy-synthesis-session','C:/Legacy',$at,$at,$at,'Manual','LEGACY_SYNTHESIS_HANDOFF',$at);
                INSERT INTO leader_messages(epoch_id,sequence,role,text,created_at)
                VALUES('00000000-0000-0000-0000-000000000104',1,'user','LEGACY_LEADER_MESSAGE',$at);
                INSERT INTO tasks(id,project_id,title,status,current_revision_id,created_at,updated_at,cancelled_at)
                VALUES('00000000-0000-0000-0000-000000000102','00000000-0000-0000-0000-000000000101','LEGACY_TASK','ReadyToStart','00000000-0000-0000-0000-000000000103',$at,$at,NULL);
                INSERT INTO task_revisions(id,task_id,revision_number,goal,scope,out_of_scope,acceptance_json,risk_level,recommended_provider_id,recommended_provider_account_id,recommended_model_profile_id,recommended_agent_runtime_id,change_reason,approved_by,created_at,previous_revision_id)
                VALUES('00000000-0000-0000-0000-000000000103','00000000-0000-0000-0000-000000000102',1,'LEGACY_REVISION_GOAL','Legacy','None','["LEGACY_ACCEPTANCE"]','Low','legacy-provider','legacy-account','legacy-model','legacy-runtime','Initial','User',$at,NULL);
                INSERT INTO task_events(id,project_id,task_id,execution_id,event_type,status,payload_json,created_at)
                VALUES('00000000-0000-0000-0000-000000000107','00000000-0000-0000-0000-000000000101','00000000-0000-0000-0000-000000000102',NULL,'WorkerCompleted','Completed','{"result":"LEGACY_COMPLETION"}',$at);
                INSERT INTO project_memory_items(id,project_id,layer,topic,content,status,created_at,updated_at,certified_at)
                VALUES('00000000-0000-0000-0000-000000000111','00000000-0000-0000-0000-000000000101','Formal','legacy','LEGACY_MEMORY','Active',$at,$at,NULL);
                INSERT INTO project_memory_sources(memory_id,source_type,source_ref)
                VALUES('00000000-0000-0000-0000-000000000111','Task','00000000-0000-0000-0000-000000000102');
                INSERT INTO project_memory_synthesis_jobs(epoch_id,project_id,status,attempt_count,last_attempted_at,completed_at,last_error,created_at,updated_at)
                VALUES('00000000-0000-0000-0000-000000000109','00000000-0000-0000-0000-000000000101','Completed',2,$at,$at,NULL,$at,$at);
                INSERT INTO project_daily_summaries(project_id,local_date,content,revision,created_at,updated_at)
                VALUES('00000000-0000-0000-0000-000000000101','2026-08-22','LEGACY_DAILY',1,$at,$at);
                INSERT INTO project_daily_summary_sources(project_id,local_date,source_type,source_ref)
                VALUES('00000000-0000-0000-0000-000000000101','2026-08-22','Task','00000000-0000-0000-0000-000000000102');
                INSERT INTO project_library_entries(id,project_id,source_session_id,task_id,category,topic,summary,source_reference,created_at)
                VALUES('00000000-0000-0000-0000-000000000108','00000000-0000-0000-0000-000000000101','00000000-0000-0000-0000-000000000104','00000000-0000-0000-0000-000000000102','Legacy','Continuity','LEGACY_LIBRARY','legacy://library',$at);
                INSERT INTO task_review_decisions(review_decision_id,project_id,task_id,revision_id,source_event_id,final_report_event_id,outcome,action_level,authority_mode,authority_resolution,authority_mode_recording,created_at)
                VALUES('00000000-0000-0000-0000-000000000112','00000000-0000-0000-0000-000000000101','00000000-0000-0000-0000-000000000102','00000000-0000-0000-0000-000000000103',NULL,'00000000-0000-0000-0000-000000000107','Pass','L1LocalFix','Balanced','NotifyAndProceed','Recorded',$at);
                INSERT INTO project_summary_entries(entry_id,project_id,occurred_at,created_at,kind,text,result_id,delta_ordinal)
                VALUES('00000000-0000-0000-0000-000000000113','00000000-0000-0000-0000-000000000101',$at,$at,'Decision','LEGACY_SUMMARY','00000000-0000-0000-0000-000000000114',0);
                INSERT INTO project_summary_source_refs(entry_id,ordinal,source_kind,source_locator)
                VALUES('00000000-0000-0000-0000-000000000113',0,'Task','00000000-0000-0000-0000-000000000102');
                """, ("$at", LegacyAt.ToString("O", CultureInfo.InvariantCulture)));
        }

        private static async Task ExecuteAsync(SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
        {
            var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
            await command.ExecuteNonQueryAsync();
        }
    }
}
