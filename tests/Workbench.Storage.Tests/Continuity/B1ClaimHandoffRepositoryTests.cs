using Microsoft.Data.Sqlite;
using Workbench.Core.Continuity;
using Workbench.Storage.Continuity;

namespace Workbench.Storage.Tests.Continuity;

public sealed class B1ClaimHandoffRepositoryTests
{
    private static readonly DateTimeOffset At =
        DateTimeOffset.Parse("2026-08-22T13:00:00.0000000+00:00");

    [Fact]
    public async Task Claim_round_trips_each_closed_payload_without_status()
    {
        await using var fixture = await B1ContinuityStorageFixture.CreateAsync();
        var repository = new B1ClaimHandoffRepository(fixture.Database);
        ClaimPayload[] payloads =
        [
            new ClaimPayload.Result("work completed"),
            new ClaimPayload.Validation("tests passed"),
            new ClaimPayload.UnresolvedIssue("follow-up remains"),
            new ClaimPayload.ProposedStateContribution(
                "retain compatibility",
                new ContributionScopeRef.Project(fixture.Primary.ProjectRef),
                ProposedSupersedes: null),
            new ClaimPayload.ProposedAssignmentRevision(
                fixture.Primary.AssignmentRef,
                fixture.Primary.RevisionRef,
                new AssignmentRevisionContract(
                    "revised contract",
                    AuthorityBoundary.Create([B1AuthorityCapability.DecideAssignmentDisposition])))
        ];

        foreach (var payload in payloads)
        {
            var claimRef = new ClaimRef(Guid.NewGuid());
            var recorded = await repository.RecordClaimAsync(Command(
                fixture.Primary,
                claimRef,
                new ClaimantRef.LogicalActor(fixture.Primary.AssigneeActorRef),
                payload,
                evidenceRefs: [new EvidenceRef($"evidence:{claimRef.Value:N}")]));

            Assert.Equal(claimRef, recorded.ClaimRef);
            Assert.Equal(new ClaimantRef.LogicalActor(fixture.Primary.AssigneeActorRef), recorded.ClaimantRef);
            Assert.Equal(payload, recorded.Payload);
            Assert.Equal([new EvidenceRef($"evidence:{claimRef.Value:N}")], recorded.EvidenceRefs);
            Assert.DoesNotContain(recorded.GetType().GetProperties(), property =>
                property.Name.Contains("Status", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task User_claim_requires_bootstrap_principal_and_null_session()
    {
        await using var fixture = await B1ContinuityStorageFixture.CreateAsync();
        var repository = new B1ClaimHandoffRepository(fixture.Database);
        var wrongOperator = new RecordClaimCommand(
            fixture.Primary.ProjectRef,
            new UserPrincipalRef("user:not-bootstrap"),
            new ClaimRef(Guid.NewGuid()),
            new ClaimantRef.UserPrincipal(fixture.Primary.BootstrapPrincipalRef),
            null,
            new ClaimPayload.Result("manual result"),
            [],
            At);

        var unauthorized = await Assert.ThrowsAsync<B1CommandException>(() =>
            repository.RecordClaimAsync(wrongOperator));
        Assert.Equal(B1FailureCode.NotAuthorized, unauthorized.Code);

        var attemptRef = await AddAttemptAsync(fixture);
        var binding = await AddBindingAsync(fixture, attemptRef, "external:user-provenance");
        var invalidSession = Command(
            fixture.Primary,
            new ClaimRef(Guid.NewGuid()),
            new ClaimantRef.UserPrincipal(fixture.Primary.BootstrapPrincipalRef),
            new ClaimPayload.Result("manual result"),
            binding.SessionBindingRef);
        await Assert.ThrowsAsync<B1CommandException>(() => repository.RecordClaimAsync(invalidSession));
    }

    [Fact]
    public async Task LogicalActor_claim_is_project_local()
    {
        await using var fixture = await B1ContinuityStorageFixture.CreateAsync();
        var other = await fixture.AddProjectAsync("other-claim-project");
        var repository = new B1ClaimHandoffRepository(fixture.Database);

        var exception = await Assert.ThrowsAsync<B1CommandException>(() => repository.RecordClaimAsync(
            Command(
                fixture.Primary,
                new ClaimRef(Guid.NewGuid()),
                new ClaimantRef.LogicalActor(other.AssigneeActorRef),
                new ClaimPayload.Result("cross-project actor"))));

        Assert.Equal(B1FailureCode.InvalidReference, exception.Code);
    }

    [Fact]
    public async Task LogicalActor_session_provenance_must_belong_to_same_actor()
    {
        await using var fixture = await B1ContinuityStorageFixture.CreateAsync();
        var repository = new B1ClaimHandoffRepository(fixture.Database);
        var attemptRef = await AddAttemptAsync(fixture);
        var binding = await AddBindingAsync(fixture, attemptRef, "external:assignee");
        var reviewer = await fixture.AddActorAsync(fixture.Primary);

        var exception = await Assert.ThrowsAsync<B1CommandException>(() => repository.RecordClaimAsync(
            Command(
                fixture.Primary,
                new ClaimRef(Guid.NewGuid()),
                new ClaimantRef.LogicalActor(reviewer),
                new ClaimPayload.Validation("reviewed"),
                binding.SessionBindingRef)));

        Assert.Equal(B1FailureCode.InvalidReference, exception.Code);
    }

    [Fact]
    public async Task Manual_actor_claim_allows_null_session()
    {
        await using var fixture = await B1ContinuityStorageFixture.CreateAsync();
        var repository = new B1ClaimHandoffRepository(fixture.Database);

        var claim = await repository.RecordClaimAsync(Command(
            fixture.Primary,
            new ClaimRef(Guid.NewGuid()),
            new ClaimantRef.LogicalActor(fixture.Primary.AssigneeActorRef),
            new ClaimPayload.Result("manual actor result")));

        Assert.Equal(new ClaimantRef.LogicalActor(fixture.Primary.AssigneeActorRef), claim.ClaimantRef);
        Assert.Null(claim.SourceSessionBindingRef);
    }

    [Fact]
    public async Task Evidence_ref_is_provenance_not_claimant_or_authority()
    {
        await using var fixture = await B1ContinuityStorageFixture.CreateAsync();
        var repository = new B1ClaimHandoffRepository(fixture.Database);
        var decisionsBefore = await CountProjectRowsAsync(fixture, "b1_authority_decisions");
        var evidence = new EvidenceRef("ci:run/47");

        var claim = await repository.RecordClaimAsync(Command(
            fixture.Primary,
            new ClaimRef(Guid.NewGuid()),
            new ClaimantRef.LogicalActor(fixture.Primary.AssigneeActorRef),
            new ClaimPayload.Validation("47 of 47 passed"),
            evidenceRefs: [evidence]));

        Assert.Equal([evidence], claim.EvidenceRefs);
        Assert.IsType<ClaimantRef.LogicalActor>(claim.ClaimantRef);
        Assert.Equal(decisionsBefore, await CountProjectRowsAsync(fixture, "b1_authority_decisions"));
        Assert.Equal(0, await CountProjectRowsAsync(fixture, "b1_accepted_state_contributions"));
    }

    [Fact]
    public async Task Cross_project_b1_refs_fail_while_external_evidence_locator_is_allowed()
    {
        await using var fixture = await B1ContinuityStorageFixture.CreateAsync();
        var other = await fixture.AddProjectAsync("other-ref-project");
        var repository = new B1ClaimHandoffRepository(fixture.Database);

        await Assert.ThrowsAsync<B1CommandException>(() => repository.RecordClaimAsync(Command(
            fixture.Primary,
            new ClaimRef(Guid.NewGuid()),
            new ClaimantRef.LogicalActor(fixture.Primary.AssigneeActorRef),
            new ClaimPayload.ProposedStateContribution(
                "foreign scope",
                new ContributionScopeRef.Responsibility(other.ResponsibilityRef),
                ProposedSupersedes: null))));

        await Assert.ThrowsAsync<B1CommandException>(() => repository.RecordClaimAsync(Command(
            fixture.Primary,
            new ClaimRef(Guid.NewGuid()),
            new ClaimantRef.LogicalActor(fixture.Primary.AssigneeActorRef),
            new ClaimPayload.ProposedAssignmentRevision(
                other.AssignmentRef,
                other.RevisionRef,
                new AssignmentRevisionContract("foreign revision")))));

        var external = new EvidenceRef("legacy://project-other/task/9");
        var allowed = await repository.RecordClaimAsync(Command(
            fixture.Primary,
            new ClaimRef(Guid.NewGuid()),
            new ClaimantRef.LogicalActor(fixture.Primary.AssigneeActorRef),
            new ClaimPayload.Result("bounded external reference"),
            evidenceRefs: [external]));
        Assert.Equal([external], allowed.EvidenceRefs);
    }

    [Fact]
    public async Task Handoff_requires_typed_same_project_claim_refs()
    {
        await using var fixture = await B1ContinuityStorageFixture.CreateAsync();
        var repository = new B1ClaimHandoffRepository(fixture.Database);
        var attemptRef = await AddAttemptAsync(fixture);
        var result = await RecordAsync(repository, fixture.Primary, new ClaimPayload.Result("result"));

        var wrongKind = await Assert.ThrowsAsync<B1CommandException>(() => repository.CreateHandoffAsync(
            HandoffCommand(fixture.Primary, attemptRef, result.ClaimRef, validationRefs: [result.ClaimRef])));
        Assert.Equal(B1FailureCode.InvalidReference, wrongKind.Code);

        var other = await fixture.AddProjectAsync("other-handoff-project");
        var foreign = await RecordAsync(repository, other, new ClaimPayload.Validation("foreign validation"));
        await Assert.ThrowsAsync<B1CommandException>(() => repository.CreateHandoffAsync(
            HandoffCommand(fixture.Primary, attemptRef, result.ClaimRef, validationRefs: [foreign.ClaimRef])));
    }

    [Fact]
    public async Task Primary_result_must_be_assignee_claim()
    {
        await using var fixture = await B1ContinuityStorageFixture.CreateAsync();
        var repository = new B1ClaimHandoffRepository(fixture.Database);
        var attemptRef = await AddAttemptAsync(fixture);
        var reviewer = await fixture.AddActorAsync(fixture.Primary);
        var result = await RecordAsync(
            repository,
            fixture.Primary,
            new ClaimPayload.Result("reviewer cannot own primary result"),
            new ClaimantRef.LogicalActor(reviewer));

        var exception = await Assert.ThrowsAsync<B1CommandException>(() => repository.CreateHandoffAsync(
            HandoffCommand(fixture.Primary, attemptRef, result.ClaimRef)));
        Assert.Equal(B1FailureCode.InvalidReference, exception.Code);
    }

    [Fact]
    public async Task Primary_result_session_binding_must_belong_to_attempt()
    {
        await using var fixture = await B1ContinuityStorageFixture.CreateAsync();
        var repository = new B1ClaimHandoffRepository(fixture.Database);
        var firstAttempt = await AddAttemptAsync(fixture);
        var secondAttempt = await AddAttemptAsync(fixture);
        var binding = await AddBindingAsync(fixture, secondAttempt, "external:other-attempt");
        var result = await RecordAsync(
            repository,
            fixture.Primary,
            new ClaimPayload.Result("result through another attempt"),
            sourceBinding: binding.SessionBindingRef);

        var exception = await Assert.ThrowsAsync<B1CommandException>(() => repository.CreateHandoffAsync(
            HandoffCommand(fixture.Primary, firstAttempt, result.ClaimRef)));
        Assert.Equal(B1FailureCode.InvalidReference, exception.Code);
    }

    [Fact]
    public async Task Non_primary_claim_may_have_another_valid_project_claimant()
    {
        await using var fixture = await B1ContinuityStorageFixture.CreateAsync();
        var repository = new B1ClaimHandoffRepository(fixture.Database);
        var attemptRef = await AddAttemptAsync(fixture);
        var reviewer = await fixture.AddActorAsync(fixture.Primary);
        var result = await RecordAsync(repository, fixture.Primary, new ClaimPayload.Result("result"));
        var validation = await RecordAsync(
            repository,
            fixture.Primary,
            new ClaimPayload.Validation("reviewer validation"),
            new ClaimantRef.LogicalActor(reviewer));

        var handoff = await repository.CreateHandoffAsync(HandoffCommand(
            fixture.Primary,
            attemptRef,
            result.ClaimRef,
            validationRefs: [validation.ClaimRef]));

        Assert.Equal([validation.ClaimRef], handoff.ValidationClaimRefs);
    }

    [Fact]
    public async Task Packaged_revision_proposal_matches_attempt_assignment_and_revision()
    {
        await using var fixture = await B1ContinuityStorageFixture.CreateAsync();
        var repository = new B1ClaimHandoffRepository(fixture.Database);
        var attemptRef = await AddAttemptAsync(fixture);
        var successor = await fixture.AddSuccessorRevisionAsync(fixture.Primary);
        var result = await RecordAsync(repository, fixture.Primary, new ClaimPayload.Result("result"));
        var proposal = await RecordAsync(
            repository,
            fixture.Primary,
            new ClaimPayload.ProposedAssignmentRevision(
                fixture.Primary.AssignmentRef,
                successor,
                new AssignmentRevisionContract("proposal against successor")));

        var exception = await Assert.ThrowsAsync<B1CommandException>(() => repository.CreateHandoffAsync(
            HandoffCommand(
                fixture.Primary,
                attemptRef,
                result.ClaimRef,
                proposalRefs: [proposal.ClaimRef])));
        Assert.Equal(B1FailureCode.InvalidReference, exception.Code);
    }

    [Fact]
    public async Task Independent_revision_proposal_can_target_other_same_project_assignment()
    {
        await using var fixture = await B1ContinuityStorageFixture.CreateAsync();
        var repository = new B1ClaimHandoffRepository(fixture.Database);
        var (assignment, revision) = await AddParallelAssignmentAsync(fixture);

        var claim = await repository.RecordClaimAsync(Command(
            fixture.Primary,
            new ClaimRef(Guid.NewGuid()),
            new ClaimantRef.LogicalActor(fixture.Primary.AssigneeActorRef),
            new ClaimPayload.ProposedAssignmentRevision(
                assignment,
                revision,
                new AssignmentRevisionContract("parallel assignment proposal"))));

        Assert.Equal(assignment, Assert.IsType<ClaimPayload.ProposedAssignmentRevision>(claim.Payload).AssignmentRef);
    }

    [Fact]
    public async Task Handoff_has_no_completion_or_acceptance_state()
    {
        await using var fixture = await B1ContinuityStorageFixture.CreateAsync();
        var repository = new B1ClaimHandoffRepository(fixture.Database);
        var attemptRef = await AddAttemptAsync(fixture);
        var result = await RecordAsync(repository, fixture.Primary, new ClaimPayload.Result("result"));
        var decisionsBefore = await CountProjectRowsAsync(fixture, "b1_authority_decisions");

        var handoff = await repository.CreateHandoffAsync(
            HandoffCommand(fixture.Primary, attemptRef, result.ClaimRef));

        Assert.Equal(decisionsBefore, await CountProjectRowsAsync(fixture, "b1_authority_decisions"));
        Assert.Equal(0, await CountProjectRowsAsync(fixture, "b1_revision_dispositions"));
        Assert.Equal(0, await CountProjectRowsAsync(fixture, "b1_accepted_state_contributions"));
        Assert.DoesNotContain(handoff.GetType().GetProperties(), property =>
            property.Name.Contains("Status", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("Accepted", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("Completed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Continuation_selection_uses_stored_CAS()
    {
        await using var fixture = await B1ContinuityStorageFixture.CreateAsync();
        var repository = new B1ClaimHandoffRepository(fixture.Database);
        var attemptRef = await AddAttemptAsync(fixture);
        var first = await AddHandoffAsync(repository, fixture.Primary, attemptRef, "first");
        var second = await AddHandoffAsync(repository, fixture.Primary, attemptRef, "second");
        await repository.SelectContinuationHandoffAsync(new SelectContinuationHandoffCommand(
            fixture.Primary.ProjectRef,
            fixture.Primary.BootstrapPrincipalRef,
            attemptRef,
            ExpectedStoredHandoffRef: null,
            first.HandoffRef));
        await fixture.AddDispositionAsync(fixture.Primary, fixture.Primary.RevisionRef);

        var stale = await Assert.ThrowsAsync<B1CommandException>(() =>
            repository.SelectContinuationHandoffAsync(new SelectContinuationHandoffCommand(
                fixture.Primary.ProjectRef,
                fixture.Primary.BootstrapPrincipalRef,
                attemptRef,
                ExpectedStoredHandoffRef: null,
                second.HandoffRef)));
        Assert.Equal(B1FailureCode.StaleRoutingSelection, stale.Code);

        await repository.SelectContinuationHandoffAsync(new SelectContinuationHandoffCommand(
            fixture.Primary.ProjectRef,
            fixture.Primary.BootstrapPrincipalRef,
            attemptRef,
            first.HandoffRef,
            second.HandoffRef));
        Assert.Equal(second.HandoffRef.Value.ToString(), await SelectedHandoffAsync(fixture, attemptRef));
    }

    [Fact]
    public async Task Create_handoff_and_select_is_atomic()
    {
        await using var fixture = await B1ContinuityStorageFixture.CreateAsync();
        var repository = new B1ClaimHandoffRepository(fixture.Database);
        var attemptRef = await AddAttemptAsync(fixture);
        var selected = await AddHandoffAsync(repository, fixture.Primary, attemptRef, "selected");
        await repository.SelectContinuationHandoffAsync(new SelectContinuationHandoffCommand(
            fixture.Primary.ProjectRef,
            fixture.Primary.BootstrapPrincipalRef,
            attemptRef,
            ExpectedStoredHandoffRef: null,
            selected.HandoffRef));
        var result = await RecordAsync(repository, fixture.Primary, new ClaimPayload.Result("rejected"));
        var rejected = HandoffCommand(fixture.Primary, attemptRef, result.ClaimRef);

        var exception = await Assert.ThrowsAsync<B1CommandException>(() =>
            repository.CreateHandoffAndSelectAsync(rejected, expectedStored: null));

        Assert.Equal(B1FailureCode.StaleRoutingSelection, exception.Code);
        Assert.Equal(0, await fixture.CountAsync("b1_handoffs", rejected.Handoff.HandoffRef.Value));
        Assert.Equal(selected.HandoffRef.Value.ToString(), await SelectedHandoffAsync(fixture, attemptRef));
    }

    private static RecordClaimCommand Command(
        B1ContinuitySeed seed,
        ClaimRef claimRef,
        ClaimantRef claimant,
        ClaimPayload payload,
        SessionBindingRef? sourceBinding = null,
        IReadOnlyList<EvidenceRef>? evidenceRefs = null) =>
        new(
            seed.ProjectRef,
            seed.BootstrapPrincipalRef,
            claimRef,
            claimant,
            sourceBinding,
            payload,
            evidenceRefs ?? [],
            At);

    private static async Task<Claim> RecordAsync(
        B1ClaimHandoffRepository repository,
        B1ContinuitySeed seed,
        ClaimPayload payload,
        ClaimantRef? claimant = null,
        SessionBindingRef? sourceBinding = null) =>
        await repository.RecordClaimAsync(Command(
            seed,
            new ClaimRef(Guid.NewGuid()),
            claimant ?? new ClaimantRef.LogicalActor(seed.AssigneeActorRef),
            payload,
            sourceBinding));

    private static CreateHandoffCommand HandoffCommand(
        B1ContinuitySeed seed,
        AttemptRef attemptRef,
        ClaimRef resultRef,
        IReadOnlyList<ClaimRef>? validationRefs = null,
        IReadOnlyList<ClaimRef>? issueRefs = null,
        IReadOnlyList<ClaimRef>? contributionRefs = null,
        IReadOnlyList<ClaimRef>? proposalRefs = null) =>
        new(
            seed.ProjectRef,
            seed.BootstrapPrincipalRef,
            new Handoff(
                new HandoffRef(Guid.NewGuid()),
                attemptRef,
                resultRef,
                validationRefs ?? [],
                issueRefs ?? [],
                contributionRefs ?? [],
                proposalRefs ?? [],
                [new EvidenceRef("handoff:bounded")],
                At));

    private static async Task<AttemptRef> AddAttemptAsync(B1ContinuityStorageFixture fixture)
    {
        var attemptRef = new AttemptRef(Guid.NewGuid());
        await new B1RoutingRepository(fixture.Database).CreateAttemptAsync(new CreateAttemptCommand(
            fixture.Primary.ProjectRef,
            fixture.Primary.BootstrapPrincipalRef,
            attemptRef,
            fixture.Primary.AssignmentRef,
            fixture.Primary.RevisionRef,
            At));
        return attemptRef;
    }

    private static async Task<SessionBinding> AddBindingAsync(
        B1ContinuityStorageFixture fixture,
        AttemptRef attemptRef,
        string external)
    {
        var binding = new SessionBinding(
            new SessionBindingRef(Guid.NewGuid()),
            attemptRef,
            fixture.Primary.AssigneeActorRef,
            new ExternalSessionRef(external),
            At);
        return await new B1RoutingRepository(fixture.Database).CreateSessionBindingAsync(
            new CreateSessionBindingCommand(
                fixture.Primary.ProjectRef,
                fixture.Primary.BootstrapPrincipalRef,
                binding));
    }

    private static async Task<Handoff> AddHandoffAsync(
        B1ClaimHandoffRepository repository,
        B1ContinuitySeed seed,
        AttemptRef attemptRef,
        string statement)
    {
        var result = await RecordAsync(repository, seed, new ClaimPayload.Result(statement));
        return await repository.CreateHandoffAsync(HandoffCommand(seed, attemptRef, result.ClaimRef));
    }

    private static Task<string?> SelectedHandoffAsync(
        B1ContinuityStorageFixture fixture,
        AttemptRef attemptRef) =>
        fixture.ScalarStringAsync(
            "SELECT selected_handoff_id FROM b1_attempt_routing WHERE attempt_id=$attempt;",
            ("$attempt", attemptRef.Value.ToString()));

    private static async Task<long> CountProjectRowsAsync(
        B1ContinuityStorageFixture fixture,
        string table)
    {
        var value = await fixture.ScalarStringAsync(
            $"SELECT COUNT(*) FROM {table} WHERE project_id=$project;",
            ("$project", fixture.Primary.ProjectRef.Value.ToString()));
        return Convert.ToInt64(value);
    }

    private static async Task<(AssignmentRef AssignmentRef, RevisionRef RevisionRef)> AddParallelAssignmentAsync(
        B1ContinuityStorageFixture fixture)
    {
        var assignment = new AssignmentRef(Guid.NewGuid());
        var revision = new RevisionRef(Guid.NewGuid());
        var decision = await fixture.ScalarStringAsync(
            "SELECT id FROM b1_authority_decisions WHERE project_id=$project ORDER BY project_commit_sequence LIMIT 1;",
            ("$project", fixture.Primary.ProjectRef.Value.ToString()));
        await using var connection = fixture.Database.CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO b1_assignments(
                id,project_id,responsibility_id,assignee_actor_id,replaces_assignment_id,
                authorized_by_decision_id)
            VALUES($assignment,$project,$responsibility,$actor,NULL,$decision);
            INSERT INTO b1_revisions(
                id,project_id,assignment_id,prior_revision_id,work_contract,
                delegated_authority_json,authorized_by_decision_id)
            VALUES($revision,$project,$assignment,NULL,'Parallel contract','[]',$decision);
            INSERT INTO b1_assignment_routing(assignment_id,project_id,selected_attempt_id)
            VALUES($assignment,$project,NULL);
            """;
        command.Parameters.AddWithValue("$assignment", assignment.Value.ToString());
        command.Parameters.AddWithValue("$project", fixture.Primary.ProjectRef.Value.ToString());
        command.Parameters.AddWithValue("$responsibility", fixture.Primary.ResponsibilityRef.Value.ToString());
        command.Parameters.AddWithValue("$actor", fixture.Primary.AssigneeActorRef.Value.ToString());
        command.Parameters.AddWithValue("$decision", decision!);
        command.Parameters.AddWithValue("$revision", revision.Value.ToString());
        await command.ExecuteNonQueryAsync();
        return (assignment, revision);
    }
}
