using Microsoft.Data.Sqlite;
using Workbench.Core.Continuity;
using Workbench.Core.Projects;
using Workbench.Storage.Continuity;
using Workbench.Storage.Database;
using Workbench.Storage.Memory;
using Workbench.Storage.Tests.Database;

namespace Workbench.Storage.Tests.Continuity;

public sealed class B1AuthorityRepositoryTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-08-22T14:00:00Z");

    [Fact]
    public async Task Decision_commit_returns_persisted_decision_with_assigned_sequence()
    {
        await using var f = await Fixture.CreateAsync();
        var validated = f.EvaluateActor(At);

        var committed = Assert.IsType<AuthorityCommitResult.Committed>(await f.Repository.TryCommitAsync(validated));

        Assert.Equal(1, committed.Decision.ProjectCommitSequence);
        Assert.Equal(validated.DecisionRef, committed.Decision.DecisionRef);
        Assert.Equal(validated.LogicalActorEstablishmentEffect, committed.Decision.LogicalActorEstablishmentEffect);
    }

    [Fact]
    public async Task Successful_decisions_reload_in_sequence_order()
    {
        await using var f = await Fixture.CreateAsync();
        await f.CommitAsync(f.EvaluateActor(At.AddHours(2)));
        await f.CommitAsync(f.EvaluateActor(At.AddHours(-2)));

        var state = await f.Repository.LoadProjectStateAsync(f.ProjectRef);

        Assert.Equal([1L, 2L], state.AuthorityDecisions.Select(item => item.ProjectCommitSequence));
    }

    [Fact]
    public async Task CreatedAt_does_not_override_commit_sequence()
    {
        await using var f = await Fixture.CreateAsync();
        var later = await f.CommitAsync(f.EvaluateActor(At.AddDays(1)));
        var earlier = await f.CommitAsync(f.EvaluateActor(At.AddDays(-1)));

        var state = await f.Repository.LoadProjectStateAsync(f.ProjectRef);

        Assert.Equal([later.DecisionRef, earlier.DecisionRef], state.AuthorityDecisions.Select(item => item.DecisionRef));
    }

    [Fact]
    public async Task Structural_effects_and_initial_revision_commit_together()
    {
        await using var f = await Fixture.CreateAsync();
        var decision = await f.CommitInitialSpineAsync();
        var state = await f.Repository.LoadProjectStateAsync(f.ProjectRef);

        Assert.NotNull(decision.LogicalActorEstablishmentEffect);
        Assert.NotNull(decision.ResponsibilityEstablishmentEffect);
        Assert.NotNull(decision.AssignmentDelegationEffect);
        Assert.Single(state.LogicalActors);
        Assert.Single(state.Responsibilities);
        Assert.Single(state.Assignments);
        Assert.Single(state.Revisions);
        Assert.Single(state.AssignmentRoutingSelections);
    }

    [Fact]
    public async Task Assignment_initial_revision_round_trips_from_unique_root_revision()
    {
        await using var f = await Fixture.CreateAsync();
        var decision = await f.CommitInitialSpineAsync();
        var expected = decision.AssignmentDelegationEffect!;

        var state = await f.Repository.LoadProjectStateAsync(f.ProjectRef);

        var assignment = Assert.Single(state.Assignments);
        Assert.Equal(expected.InitialRevision.RevisionRef, assignment.InitialRevisionRef);
        Assert.Equal(expected.InitialRevision, Assert.Single(state.Revisions));
    }

    [Fact]
    public async Task Missing_or_ambiguous_initial_revision_is_rejected_as_corrupt()
    {
        await using (var missing = await Fixture.CreateAsync())
        {
            var decision = await missing.CommitInitialSpineAsync();
            await missing.ExecuteAsync("PRAGMA foreign_keys=OFF; DELETE FROM b1_revisions WHERE id=$id;",
                ("$id", decision.AssignmentDelegationEffect!.InitialRevision.RevisionRef.Value.ToString()));
            await Assert.ThrowsAsync<InvalidDataException>(() => missing.Repository.LoadProjectStateAsync(missing.ProjectRef));
        }

        await using (var ambiguous = await Fixture.CreateAsync())
        {
            var decision = await ambiguous.CommitInitialSpineAsync();
            var effect = decision.AssignmentDelegationEffect!;
            await ambiguous.ExecuteAsync("DROP INDEX ux_b1_revisions_one_initial_per_assignment;");
            await ambiguous.ExecuteAsync("""
                INSERT INTO b1_revisions(id,project_id,assignment_id,prior_revision_id,work_contract,
                    delegated_authority_json,authorized_by_decision_id)
                VALUES($id,$project,$assignment,NULL,'ambiguous root','[]',$decision);
                """, ("$id", Guid.NewGuid().ToString()), ("$project", ambiguous.ProjectRef.Value.ToString()),
                ("$assignment", effect.Assignment.AssignmentRef.Value.ToString()),
                ("$decision", decision.DecisionRef.Value.ToString()));
            await Assert.ThrowsAsync<InvalidDataException>(() => ambiguous.Repository.LoadProjectStateAsync(ambiguous.ProjectRef));
        }
    }

    [Fact]
    public async Task Disposition_activation_replacement_and_contributions_round_trip()
    {
        await using var f = await Fixture.CreateAsync();
        var first = await f.CommitInitialSpineAsync();
        var assignment = first.AssignmentDelegationEffect!.Assignment;
        var revision = first.AssignmentDelegationEffect.InitialRevision;
        var state = await f.Repository.LoadProjectStateAsync(f.ProjectRef);
        var activation = f.Evaluator.Evaluate(state,
            new ActivateAssignmentRevisionCommand(f.ProjectRef, f.Principal, f.Authority,
                new(assignment.AssignmentRef, revision.RevisionRef, new("R2"), null), [], []),
            new(Guid.NewGuid()), At.AddMinutes(1));
        var activated = await f.CommitAsync(activation);
        var r2 = activated.RevisionActivationEffect!.Revision;
        state = await f.Repository.LoadProjectStateAsync(f.ProjectRef);
        var decide = f.Evaluator.Evaluate(state,
            new DecideAssignmentCommand(f.ProjectRef, f.Principal, f.Authority,
                new(assignment.AssignmentRef, r2.RevisionRef, AssignmentDisposition.Rejected), null,
                new(assignment.AssignmentRef, new AssignmentAssigneeTarget.Existing(assignment.AssigneeActorRef),
                    null, new("replacement")), [],
                [new("replacement retained", new ContributionScopeTarget.Assignment.DelegatedByThisDecision(), null, null)]),
            new(Guid.NewGuid()), At.AddMinutes(2));
        var committed = await f.CommitAsync(decide);

        state = await f.Repository.LoadProjectStateAsync(f.ProjectRef);
        Assert.Equal(3, state.AuthorityDecisions.Count);
        Assert.Contains(state.RevisionDispositions, item => item.RevisionRef == r2.RevisionRef);
        Assert.Contains(state.Assignments, item => item.AssignmentRef == committed.AssignmentDelegationEffect!.Assignment.AssignmentRef);
        Assert.Contains(state.AuthorityDecisions, item => item.AcceptedStateContributions.Count == 1);
    }

    [Fact]
    public async Task Considered_refs_and_proposal_source_round_trip_without_becoming_evidence_or_authority()
    {
        await using var f = await Fixture.CreateAsync();
        var initial = await f.CommitInitialSpineAsync();
        var candidateId = Guid.NewGuid();
        await new ProjectEvolutionCandidateRepository(f.Database).SaveAsync(new(
            candidateId,
            f.ProjectRef.Value,
            null,
            null,
            "current_user_message",
            "因果编号",
            "WorldRule",
            "ConstraintRevision",
            null,
            "因果编号只负责标识和追踪因果链。",
            "WorldRule",
            "AuthorityConfirmation",
            "用户明确了一条正式规则。",
            ProjectEvolutionCandidateStatus.Observed,
            At));
        var assignment = initial.AssignmentDelegationEffect!.Assignment;
        var revision = initial.AssignmentDelegationEffect.InitialRevision;
        var claimRepository = new B1ClaimHandoffRepository(f.Database);
        var revisionProposal = await claimRepository.RecordClaimAsync(new(
            f.ProjectRef, f.Principal, new(Guid.NewGuid()), new ClaimantRef.UserPrincipal(f.Principal), null,
            new ClaimPayload.ProposedAssignmentRevision(
                assignment.AssignmentRef, revision.RevisionRef, new("proposed R2")),
            [], At));
        var claim = await claimRepository.RecordClaimAsync(new(
            f.ProjectRef, f.Principal, new(Guid.NewGuid()), new ClaimantRef.UserPrincipal(f.Principal), null,
            new ClaimPayload.ProposedStateContribution("proposal", new ContributionScopeRef.Project(f.ProjectRef), null),
            [new EvidenceRef("evidence:claim")], At));
        var state = await f.Repository.LoadProjectStateAsync(f.ProjectRef);
        var activation = f.Evaluator.Evaluate(state,
            new ActivateAssignmentRevisionCommand(f.ProjectRef, f.Principal, f.Authority,
                new(assignment.AssignmentRef, revision.RevisionRef, new("authority-authored R2"), revisionProposal.ClaimRef),
                [new ConsideredRef.Claim(revisionProposal.ClaimRef)], []),
            new(Guid.NewGuid()), At.AddSeconds(30));
        await f.CommitAsync(activation);
        state = await f.Repository.LoadProjectStateAsync(f.ProjectRef);
        Assert.Equal(revisionProposal.ClaimRef,
            state.AuthorityDecisions[^1].RevisionActivationEffect!.SourceClaimRef);

        var validated = f.Evaluator.Evaluate(state,
            new AuthorAcceptedStateCommand(f.ProjectRef, f.Principal, f.Authority,
                [new ConsideredRef.Claim(claim.ClaimRef), new ConsideredRef.Evidence(new("evidence:decision")), new ConsideredRef.EvolutionCandidate(candidateId)],
                [new("accepted", new ContributionScopeTarget.Project(f.ProjectRef), null, claim.ClaimRef)]),
            new(Guid.NewGuid()), At.AddMinutes(1));
        await f.CommitAsync(validated);

        state = await f.Repository.LoadProjectStateAsync(f.ProjectRef);
        var decision = state.AuthorityDecisions[^1];
        Assert.Equal(3, decision.ConsideredRefs.Count);
        Assert.Contains(new ConsideredRef.EvolutionCandidate(candidateId), decision.ConsideredRefs);
        Assert.Equal(claim.ClaimRef, Assert.Single(decision.AcceptedStateContributions).SourceClaimRef);
        Assert.Equal([new EvidenceRef("evidence:claim")],
            state.Claims.Single(item => item.ClaimRef == claim.ClaimRef).EvidenceRefs);
    }

    [Fact]
    public async Task Wrong_expected_sequence_commits_zero_rows()
    {
        await using var f = await Fixture.CreateAsync();
        var first = f.EvaluateActor(At);
        var stale = f.EvaluateActor(At.AddMinutes(1));
        await f.CommitAsync(first);

        Assert.IsType<AuthorityCommitResult.ProjectSequenceConflict>(await f.Repository.TryCommitAsync(stale));
        Assert.Equal(1, await f.CountAsync("b1_authority_decisions"));
        Assert.Equal(1, await f.CountAsync("b1_logical_actors"));
    }

    [Fact]
    public async Task Constraint_failure_rolls_back_decision_and_every_effect()
    {
        await using var f = await Fixture.CreateAsync();
        var validated = await f.EvaluateInitialSpineAsync();
        await f.ExecuteAsync(
            "CREATE TRIGGER reject_b1_revision BEFORE INSERT ON b1_revisions " +
            "BEGIN SELECT RAISE(ABORT, 'reject revision'); END;");

        await Assert.ThrowsAsync<SqliteException>(() => f.Repository.TryCommitAsync(validated));
        Assert.Equal(0, await f.CountAsync("b1_authority_decisions"));
        Assert.Equal(0, await f.CountAsync("b1_logical_actors"));
        Assert.Equal(0, await f.CountAsync("b1_responsibilities"));
        Assert.Equal(0, await f.CountAsync("b1_assignments"));
        Assert.Equal(0, await f.CountAsync("b1_revisions"));
        Assert.Equal(0, (await f.Repository.LoadProjectStateAsync(f.ProjectRef)).Governance.LastProjectCommitSequence);
    }

    [Fact]
    public async Task Concurrent_same_sequence_commits_one_winner()
    {
        await using var f = await Fixture.CreateAsync();
        var first = f.EvaluateActor(At);
        var second = f.EvaluateActor(At.AddMinutes(1));
        var concurrent = await RunConcurrentlyAsync(
            () => f.Repository.TryCommitAsync(first),
            () => f.Repository.TryCommitAsync(second));
        var results = new[] { concurrent.First, concurrent.Second };

        Assert.Single(results.OfType<AuthorityCommitResult.Committed>());
        Assert.Single(results.OfType<AuthorityCommitResult.ProjectSequenceConflict>());
    }

    [Fact]
    public async Task Failed_decision_consumes_no_successful_sequence()
    {
        await using var f = await Fixture.CreateAsync();
        var first = f.EvaluateActor(At);
        var stale = f.EvaluateActor(At.AddMinutes(1));
        await f.CommitAsync(first);
        Assert.IsType<AuthorityCommitResult.ProjectSequenceConflict>(await f.Repository.TryCommitAsync(stale));

        var next = await f.CommitAsync(f.EvaluateActor(At.AddMinutes(2)));
        Assert.Equal(2, next.ProjectCommitSequence);
    }

    [Fact]
    public async Task Supersession_unique_target_has_one_winner()
    {
        await using var f = await Fixture.CreateAsync();
        var initial = f.Evaluator.Evaluate(await f.Repository.LoadProjectStateAsync(f.ProjectRef),
            new AuthorAcceptedStateCommand(f.ProjectRef, f.Principal, f.Authority, [],
                [new("A", new ContributionScopeTarget.Project(f.ProjectRef), null, null)]),
            new(Guid.NewGuid()), At);
        var prior = Assert.Single((await f.CommitAsync(initial)).AcceptedStateContributions);
        var state = await f.Repository.LoadProjectStateAsync(f.ProjectRef);
        ValidatedAuthorityDecision Candidate(string text) => f.Evaluator.Evaluate(state,
            new AuthorAcceptedStateCommand(f.ProjectRef, f.Principal, f.Authority, [],
                [new(text, new ContributionScopeTarget.Project(f.ProjectRef), prior.ContributionRef, null)]),
            new(Guid.NewGuid()), At.AddMinutes(1));

        var first = Candidate("B");
        var second = Candidate("C");
        var concurrent = await RunConcurrentlyAsync(
            () => f.Repository.TryCommitAsync(first),
            () => f.Repository.TryCommitAsync(second));
        var results = new[] { concurrent.First, concurrent.Second };
        Assert.Single(results.OfType<AuthorityCommitResult.Committed>());
        Assert.Single(results.OfType<AuthorityCommitResult.ProjectSequenceConflict>());
    }

    [Fact]
    public async Task Replacement_unique_target_has_one_winner()
    {
        await using var f = await Fixture.CreateAsync();
        var initial = await f.CommitInitialSpineAsync();
        var old = initial.AssignmentDelegationEffect!.Assignment;
        var state = await f.Repository.LoadProjectStateAsync(f.ProjectRef);
        ValidatedAuthorityDecision Candidate() => f.Evaluator.Evaluate(state,
            new DelegateAssignmentCommand(f.ProjectRef, f.Principal, f.Authority,
                new(new ResponsibilityTarget.Existing(old.ResponsibilityRef),
                    new AssignmentAssigneeTarget.Existing(old.AssigneeActorRef), new("replacement"), old.AssignmentRef),
                null, [], []), new(Guid.NewGuid()), At.AddMinutes(1));

        var first = Candidate();
        var second = Candidate();
        var concurrent = await RunConcurrentlyAsync(
            () => f.Repository.TryCommitAsync(first),
            () => f.Repository.TryCommitAsync(second));
        var results = new[] { concurrent.First, concurrent.Second };
        Assert.Single(results.OfType<AuthorityCommitResult.Committed>());
        Assert.Single(results.OfType<AuthorityCommitResult.ProjectSequenceConflict>());
    }

    [Fact]
    public async Task Decision_with_zero_effects_cannot_reach_repository()
    {
        await using var f = await Fixture.CreateAsync();
        Assert.Empty(typeof(ValidatedAuthorityDecision).GetConstructors());
        var error = Assert.Throws<B1CommandException>(() => f.Evaluator.Evaluate(
            f.Repository.LoadProjectStateAsync(f.ProjectRef).GetAwaiter().GetResult(),
            new AuthorAcceptedStateCommand(f.ProjectRef, f.Principal, f.Authority, [], []),
            new(Guid.NewGuid()), At));
        Assert.Equal(B1FailureCode.InvalidDecisionShape, error.Code);
    }

    [Fact]
    public async Task Concurrent_load_returns_one_complete_authority_snapshot()
    {
        await using var f = await Fixture.CreateAsync();
        for (var index = 0; index < 12; index++)
        {
            var validated = f.EvaluateActor(At.AddMinutes(index));
            var concurrent = await RunConcurrentlyAsync(
                () => f.Repository.LoadProjectStateAsync(f.ProjectRef),
                () => f.Repository.TryCommitAsync(validated));
            Assert.IsType<AuthorityCommitResult.Committed>(concurrent.Second);
            var snapshot = concurrent.First;
            Assert.Equal(snapshot.Governance.LastProjectCommitSequence, snapshot.AuthorityDecisions.Count);
            Assert.Equal(snapshot.AuthorityDecisions.Count, snapshot.LogicalActors.Count);
        }
    }

    [Fact]
    public async Task Mismatched_or_empty_authority_history_is_rejected_as_corrupt()
    {
        await using (var empty = await Fixture.CreateAsync())
        {
            await empty.ExecuteAsync("""
                INSERT INTO b1_authority_decisions(
                    id,project_id,project_commit_sequence,command_kind,deciding_authority_kind,
                    deciding_user_principal,deciding_actor_id,created_at)
                VALUES($id,$project,1,'AuthorAcceptedState','UserPrincipal',$principal,NULL,$at);
                UPDATE b1_project_governance SET last_commit_sequence=1 WHERE project_id=$project;
                """, ("$id", Guid.NewGuid().ToString()), ("$project", empty.ProjectRef.Value.ToString()),
                ("$principal", empty.Principal.Value), ("$at", At.ToString("O")));
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                empty.Repository.LoadProjectStateAsync(empty.ProjectRef));
        }

        await using (var mismatched = await Fixture.CreateAsync())
        {
            var decision = await mismatched.CommitAsync(mismatched.EvaluateActor(At));
            await mismatched.ExecuteAsync("""
                UPDATE b1_authority_decisions SET command_kind='AuthorAcceptedState' WHERE id=$id;
                """, ("$id", decision.DecisionRef.Value.ToString()));
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                mismatched.Repository.LoadProjectStateAsync(mismatched.ProjectRef));
        }
    }

    [Fact]
    public async Task Delegation_root_revision_authority_mismatch_is_rejected_as_corrupt()
    {
        await using var f = await Fixture.CreateAsync();
        var initial = await f.CommitInitialSpineAsync();
        var unrelated = await f.CommitAsync(f.EvaluateActor(At.AddMinutes(1)));
        await f.ExecuteAsync("""
            UPDATE b1_revisions SET authorized_by_decision_id=$other WHERE id=$revision;
            """, ("$other", unrelated.DecisionRef.Value.ToString()),
            ("$revision", initial.AssignmentDelegationEffect!.InitialRevision.RevisionRef.Value.ToString()));

        await Assert.ThrowsAsync<InvalidDataException>(() => f.Repository.LoadProjectStateAsync(f.ProjectRef));
    }

    private static async Task<(TFirst First, TSecond Second)> RunConcurrentlyAsync<TFirst, TSecond>(
        Func<Task<TFirst>> first,
        Func<Task<TSecond>> second)
    {
        using var barrier = new Barrier(participantCount: 3);
        var firstTask = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            return await first();
        });
        var secondTask = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            return await second();
        });
        barrier.SignalAndWait();
        return (await firstTask, await secondTask);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly TemporaryDatabase _temporary;
        private Fixture(TemporaryDatabase temporary, WorkbenchDatabase database, ProjectRef projectRef,
            UserPrincipalRef principal)
        {
            _temporary = temporary;
            Database = database;
            ProjectRef = projectRef;
            Principal = principal;
            Repository = new(database);
        }

        public WorkbenchDatabase Database { get; }
        public ProjectRef ProjectRef { get; }
        public UserPrincipalRef Principal { get; }
        public DecidingAuthorityRef Authority => new DecidingAuthorityRef.UserPrincipal(Principal);
        public B1AuthorityRepository Repository { get; }
        public B1AuthorityEvaluator Evaluator { get; } = new();

        public static async Task<Fixture> CreateAsync()
        {
            var temporary = new TemporaryDatabase();
            var database = new WorkbenchDatabase(temporary.DatabasePath);
            await database.InitializeAsync();
            var projectRef = new ProjectRef(Guid.NewGuid());
            var principal = new UserPrincipalRef("user:authority-repository");
            var project = new Project(projectRef.Value, "B1 Authority", $"C:/Projects/{projectRef.Value:N}",
                ProjectType.Godot, null, At, At);
            await new B1ProjectGovernanceRepository(database).CreateGovernedProjectAsync(project, principal);
            return new Fixture(temporary, database, projectRef, principal);
        }

        public ValidatedAuthorityDecision EvaluateActor(DateTimeOffset createdAt) => Evaluator.Evaluate(
            Repository.LoadProjectStateAsync(ProjectRef).GetAwaiter().GetResult(),
            new EstablishLogicalActorCommand(ProjectRef, Principal, Authority, RoleKind.Worker, [], []),
            new(Guid.NewGuid()), createdAt);

        public async Task<ValidatedAuthorityDecision> EvaluateInitialSpineAsync()
        {
            var state = await Repository.LoadProjectStateAsync(ProjectRef);
            var maximum = AuthorityBoundary.Create(Enum.GetValues<B1AuthorityCapability>());
            return Evaluator.Evaluate(state,
                new EstablishResponsibilityCommand(ProjectRef, Principal, Authority,
                    new("Own continuity", "Continuity preserved", maximum),
                    new(new ResponsibilityTarget.EstablishedByThisDecision(),
                        new AssignmentAssigneeTarget.EstablishedByThisDecision(),
                        new("Initial contract", maximum), null), RoleKind.Worker, [], []),
                new(Guid.NewGuid()), At);
        }

        public async Task<AuthorityDecision> CommitInitialSpineAsync() =>
            await CommitAsync(await EvaluateInitialSpineAsync());

        public async Task<AuthorityDecision> CommitAsync(ValidatedAuthorityDecision validated) =>
            Assert.IsType<AuthorityCommitResult.Committed>(await Repository.TryCommitAsync(validated)).Decision;

        public async Task ExecuteAsync(string sql, params (string Name, object Value)[] values)
        {
            await using var connection = Database.CreateConnection();
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<long> CountAsync(string table)
        {
            await using var connection = Database.CreateConnection();
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE project_id=$project;";
            command.Parameters.AddWithValue("$project", ProjectRef.Value.ToString());
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }

        public ValueTask DisposeAsync() => _temporary.DisposeAsync();
    }
}
