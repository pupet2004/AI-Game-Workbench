using Workbench.Core.Continuity;

namespace Workbench.Core.Tests.Continuity;

public sealed class B1ClaimHandoffContractTests
{
    [Fact]
    public void Claimant_is_user_or_actor_only()
    {
        ClaimantRef user = new ClaimantRef.UserPrincipal(new UserPrincipalRef("user:1"));
        ClaimantRef actor = new ClaimantRef.LogicalActor(ActorRef());

        Assert.IsType<ClaimantRef.UserPrincipal>(user);
        Assert.IsType<ClaimantRef.LogicalActor>(actor);
        Assert.Equal(["LogicalActor", "UserPrincipal"], CaseNames<ClaimantRef>());
    }

    [Fact]
    public void Claim_payload_has_exactly_five_closed_cases()
    {
        ClaimPayload payload = new ClaimPayload.ProposedStateContribution(
            "Session is replaceable",
            new ContributionScopeRef.Project(ProjectRef()),
            ProposedSupersedes: null);

        Assert.IsType<ClaimPayload.ProposedStateContribution>(payload);
        Assert.Equal(
            ["ProposedAssignmentRevision", "ProposedStateContribution", "Result", "UnresolvedIssue", "Validation"],
            CaseNames<ClaimPayload>());
    }

    [Fact]
    public void User_claim_cannot_carry_session_binding()
    {
        Assert.Throws<ArgumentException>(() => new Claim(
            ClaimRef(),
            ProjectRef(),
            new ClaimantRef.UserPrincipal(new UserPrincipalRef("user:1")),
            BindingRef(),
            new ClaimPayload.Result("Done"),
            [],
            DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Contribution_scope_is_explicit_and_closed()
    {
        Assert.Equal(["Assignment", "Project", "Responsibility"], CaseNames<ContributionScopeRef>());
        Assert.IsType<ContributionScopeRef.Project>(new ContributionScopeRef.Project(ProjectRef()));
        Assert.IsType<ContributionScopeRef.Responsibility>(new ContributionScopeRef.Responsibility(ResponsibilityRef()));
        Assert.IsType<ContributionScopeRef.Assignment>(new ContributionScopeRef.Assignment(AssignmentRef()));
    }

    [Fact]
    public void Handoff_has_one_primary_result_and_no_status()
    {
        var result = ClaimRef();
        var handoff = new Handoff(HandoffRef(), AttemptRef(), result, [], [], [], [], [], DateTimeOffset.UtcNow);

        Assert.Equal(result, handoff.ResultClaimRef);
        Assert.DoesNotContain(typeof(Handoff).GetProperties(), property =>
            new[] { "Status", "Current", "Final", "Accepted", "Rejected", "Failed", "Completed" }
                .Any(fragment => property.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Primary_result_contract_targets_assignee()
    {
        var result = ClaimRef();
        var handoff = new Handoff(HandoffRef(), AttemptRef(), result, [], [], [], [], [], DateTimeOffset.UtcNow);

        Assert.Equal(result, handoff.ResultClaimRef);
        Assert.Null(typeof(Handoff).GetProperty("SubmittedByPrincipalRef"));
        Assert.Null(typeof(Handoff).GetProperty("AssignmentRef"));
        Assert.Null(typeof(Handoff).GetProperty("EffectiveRevisionRef"));
    }

    [Fact]
    public void Considered_ref_distinguishes_claim_handoff_evidence_and_evolution_candidate()
    {
        Assert.IsType<ConsideredRef.Claim>(new ConsideredRef.Claim(ClaimRef()));
        Assert.IsType<ConsideredRef.Handoff>(new ConsideredRef.Handoff(HandoffRef()));
        Assert.IsType<ConsideredRef.Evidence>(new ConsideredRef.Evidence(new EvidenceRef("ci:run/1")));
        Assert.IsType<ConsideredRef.EvolutionCandidate>(new ConsideredRef.EvolutionCandidate(Guid.NewGuid()));
        Assert.Equal(["Claim", "Evidence", "EvolutionCandidate", "Handoff"], CaseNames<ConsideredRef>());
    }

    [Fact]
    public void Prospective_targets_exist_only_in_commands()
    {
        Assert.IsType<ResponsibilityTarget.EstablishedByThisDecision>(new ResponsibilityTarget.EstablishedByThisDecision());
        Assert.IsType<AssignmentAssigneeTarget.EstablishedByThisDecision>(new AssignmentAssigneeTarget.EstablishedByThisDecision());
        Assert.IsType<ContributionScopeTarget.Responsibility.EstablishedByThisDecision>(
            new ContributionScopeTarget.Responsibility.EstablishedByThisDecision());
        Assert.IsType<ContributionScopeTarget.Assignment.DelegatedByThisDecision>(
            new ContributionScopeTarget.Assignment.DelegatedByThisDecision());
        Assert.DoesNotContain(typeof(AcceptedStateContribution).GetProperties(), property =>
            property.PropertyType == typeof(ResponsibilityTarget)
            || property.PropertyType == typeof(AssignmentAssigneeTarget)
            || property.PropertyType == typeof(ContributionScopeTarget));
    }

    [Fact]
    public void Eight_non_authoritative_commands_are_named()
    {
        Assert.Equal(
            [
                "ClearCurrentSessionBindingCommand",
                "CreateAttemptCommand",
                "CreateHandoffCommand",
                "CreateSessionBindingCommand",
                "RecordClaimCommand",
                "SelectContinuationHandoffCommand",
                "SelectCurrentAttemptCommand",
                "SelectCurrentSessionBindingCommand"
            ],
            new[]
            {
                typeof(CreateAttemptCommand), typeof(SelectCurrentAttemptCommand), typeof(RecordClaimCommand),
                typeof(CreateHandoffCommand), typeof(SelectContinuationHandoffCommand), typeof(CreateSessionBindingCommand),
                typeof(SelectCurrentSessionBindingCommand), typeof(ClearCurrentSessionBindingCommand)
            }.Select(type => type.Name).Order().ToArray());
    }

    [Fact]
    public void Six_authority_commands_are_named_without_effect_array()
    {
        var commands = new[]
        {
            typeof(EstablishLogicalActorCommand), typeof(EstablishResponsibilityCommand),
            typeof(DelegateAssignmentCommand), typeof(DecideAssignmentCommand),
            typeof(ActivateAssignmentRevisionCommand), typeof(AuthorAcceptedStateCommand)
        };

        Assert.Equal(
            ["ActivateAssignmentRevisionCommand", "AuthorAcceptedStateCommand", "DecideAssignmentCommand", "DelegateAssignmentCommand", "EstablishLogicalActorCommand", "EstablishResponsibilityCommand"],
            commands.Select(type => type.Name).Order().ToArray());
        Assert.All(commands, command => Assert.DoesNotContain(command.GetProperties(), property =>
            property.Name == "Effects" || property.PropertyType == typeof(IReadOnlyList<object>)));
        Assert.All(commands, command => Assert.Contains(command.GetProperties(), property =>
            property.Name == "AuthenticatedOperatorRef" && property.PropertyType == typeof(UserPrincipalRef)));
    }

    [Fact]
    public void Authority_decision_owns_immutable_considered_and_contribution_lists()
    {
        var considered = new List<ConsideredRef> { new ConsideredRef.Evidence(new EvidenceRef("review:1")) };
        var contributions = new List<AcceptedStateContribution>
        {
            new(new AcceptedStateContributionRef(Guid.NewGuid()), "State", new ContributionScopeRef.Project(ProjectRef()),
                null, new AuthorityDecisionRef(Guid.NewGuid()), null)
        };
        var decision = new AuthorityDecision(
            new AuthorityDecisionRef(Guid.NewGuid()), ProjectRef(), 1,
            new DecidingAuthorityRef.UserPrincipal(new UserPrincipalRef("user:1")), considered,
            null, null, null, null, null, contributions, DateTimeOffset.UtcNow);

        considered.Add(new ConsideredRef.Evidence(new EvidenceRef("review:2")));
        contributions.Add(contributions[0] with { Statement = "Changed" });

        Assert.Single(decision.ConsideredRefs);
        Assert.Single(decision.AcceptedStateContributions);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<ConsideredRef>)decision.ConsideredRefs).Add(new ConsideredRef.Evidence(new EvidenceRef("review:3"))));
    }

    private static string[] CaseNames<T>() => typeof(T).GetNestedTypes().Select(type => type.Name).Order().ToArray();
    private static ProjectRef ProjectRef() => new(Guid.NewGuid());
    private static LogicalActorRef ActorRef() => new(Guid.NewGuid());
    private static ResponsibilityRef ResponsibilityRef() => new(Guid.NewGuid());
    private static AssignmentRef AssignmentRef() => new(Guid.NewGuid());
    private static AttemptRef AttemptRef() => new(Guid.NewGuid());
    private static SessionBindingRef BindingRef() => new(Guid.NewGuid());
    private static ClaimRef ClaimRef() => new(Guid.NewGuid());
    private static HandoffRef HandoffRef() => new(Guid.NewGuid());
}
