using Workbench.App.Continuity;
using Workbench.Core.Continuity;
using Workbench.Core.Projects;
using Workbench.Storage.Continuity;
using Workbench.Storage.Database;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.Tests.Continuity;

public sealed class B1AuthorityCommandServiceTests
{
    private static readonly AuthorityBoundary Empty = AuthorityBoundary.Empty;

    [Fact]
    public async Task EstablishLogicalActor_creates_identity_only()
    {
        await using var f = await Fixture.CreateAsync();

        var decision = await f.Service.EstablishLogicalActorAsync(f.EstablishActor());
        var state = await f.LoadAsync();

        var actor = Assert.Single(state.LogicalActors);
        Assert.Equal(RoleKind.Worker, actor.RoleKind);
        Assert.Equal(decision.DecisionRef, actor.AuthorizedByDecisionRef);
        Assert.Empty(state.Responsibilities);
        Assert.Empty(state.Assignments);
        Assert.Empty(state.Revisions);
        Assert.Empty(state.RevisionDispositions);
        Assert.Empty(state.AcceptedProjectState().CurrentContributions);
    }

    [Fact]
    public async Task EstablishResponsibility_can_create_obligation_without_owner_or_assignment()
    {
        await using var f = await Fixture.CreateAsync();

        var decision = await f.Service.EstablishResponsibilityAsync(
            f.EstablishResponsibility(f.Contract("Own continuity", Empty)));
        var state = await f.LoadAsync();

        var responsibility = Assert.Single(state.Responsibilities);
        Assert.Equal("Own continuity", responsibility.Contract.Obligation);
        Assert.Equal(decision.DecisionRef, responsibility.AuthorizedByDecisionRef);
        Assert.Empty(state.LogicalActors);
        Assert.Empty(state.Assignments);
        Assert.Empty(state.Revisions);
    }

    [Fact]
    public async Task Bootstrap_can_atomically_establish_actor_responsibility_assignment_and_initial_revision()
    {
        await using var f = await Fixture.CreateAsync();

        var decision = await f.Service.EstablishResponsibilityAsync(
            f.EstablishResponsibilityWithInitialDelegation(f.Contract("Govern releases", Empty), Empty));
        var state = await f.LoadAsync();

        var actor = Assert.Single(state.LogicalActors);
        var responsibility = Assert.Single(state.Responsibilities);
        var assignment = Assert.Single(state.Assignments);
        var revision = Assert.Single(state.Revisions);
        Assert.Equal(responsibility.ResponsibilityRef, assignment.ResponsibilityRef);
        Assert.Equal(actor.LogicalActorRef, assignment.AssigneeActorRef);
        Assert.Equal(revision.RevisionRef, assignment.InitialRevisionRef);
        Assert.Null(revision.PriorRevisionRef);
        Assert.All(new[]
        {
            actor.AuthorizedByDecisionRef,
            responsibility.AuthorizedByDecisionRef,
            assignment.AuthorizedByDecisionRef,
            revision.AuthorizedByDecisionRef
        }, authorized => Assert.Equal(decision.DecisionRef, authorized));
    }

    [Fact]
    public async Task Actor_establishment_grants_no_authority()
    {
        await using var f = await Fixture.CreateAsync();
        var actorDecision = await f.Service.EstablishLogicalActorAsync(f.EstablishActor(RoleKind.Leader));
        var actorRef = actorDecision.LogicalActorEstablishmentEffect!.LogicalActor.LogicalActorRef;

        var exception = await Assert.ThrowsAsync<B1CommandException>(() =>
            f.Service.EstablishLogicalActorAsync(
                f.EstablishActor(decidingAuthority: new DecidingAuthorityRef.LogicalActor(actorRef))));

        Assert.Equal(B1FailureCode.NotAuthorized, exception.Code);
        Assert.Single((await f.LoadAsync()).LogicalActors);
    }

    [Fact]
    public async Task Prospective_assignee_and_scope_resolve_to_durable_refs()
    {
        await using var f = await Fixture.CreateAsync();
        var command = f.EstablishResponsibilityWithInitialDelegation(
            f.Contract("Own release policy", Empty),
            Empty,
            [
                f.Contribution("Responsibility rule", new ContributionScopeTarget.Responsibility.EstablishedByThisDecision()),
                f.Contribution("Assignment rule", new ContributionScopeTarget.Assignment.DelegatedByThisDecision())
            ]);

        var decision = await f.Service.EstablishResponsibilityAsync(command);
        var reloaded = Assert.Single((await f.LoadAsync()).AuthorityDecisions);
        var responsibilityRef = decision.ResponsibilityEstablishmentEffect!.Responsibility.ResponsibilityRef;
        var actorRef = decision.LogicalActorEstablishmentEffect!.LogicalActor.LogicalActorRef;
        var assignmentRef = decision.AssignmentDelegationEffect!.Assignment.AssignmentRef;

        Assert.Equal(actorRef, decision.AssignmentDelegationEffect.Assignment.AssigneeActorRef);
        Assert.Contains(reloaded.AcceptedStateContributions,
            contribution => contribution.Scope == new ContributionScopeRef.Responsibility(responsibilityRef));
        Assert.Contains(reloaded.AcceptedStateContributions,
            contribution => contribution.Scope == new ContributionScopeRef.Assignment(assignmentRef));
    }

    [Fact]
    public async Task Failure_of_one_contribution_rolls_back_every_established_identity()
    {
        await using var f = await Fixture.CreateAsync();
        var missing = new AssignmentRef(Guid.NewGuid());
        var command = f.EstablishResponsibilityWithInitialDelegation(
            f.Contract("Atomic responsibility", Empty),
            Empty,
            [f.Contribution("Invalid target", new ContributionScopeTarget.Assignment.Existing(missing))]);

        var exception = await Assert.ThrowsAsync<B1CommandException>(() =>
            f.Service.EstablishResponsibilityAsync(command));
        var state = await f.LoadAsync();

        Assert.Equal(B1FailureCode.InvalidReference, exception.Code);
        Assert.Empty(state.AuthorityDecisions);
        Assert.Empty(state.LogicalActors);
        Assert.Empty(state.Responsibilities);
        Assert.Empty(state.Assignments);
        Assert.Empty(state.Revisions);
        Assert.Equal(0, state.Governance.LastProjectCommitSequence);
    }

    [Fact]
    public async Task DelegateAssignment_creates_one_immutable_assignment_and_initial_revision()
    {
        await using var f = await Fixture.CreateAsync();
        var actor = await f.AddActorAsync();
        var responsibility = await f.AddResponsibilityAsync(f.Contract("Delegate work", Empty));

        var decision = await f.Service.DelegateAssignmentAsync(
            f.Delegate(responsibility, actor, Empty));
        var state = await f.LoadAsync();

        var assignment = Assert.Single(state.Assignments);
        var revision = Assert.Single(state.Revisions);
        Assert.Equal(decision.AssignmentDelegationEffect!.Assignment, assignment);
        Assert.Equal(responsibility, assignment.ResponsibilityRef);
        Assert.Equal(actor, assignment.AssigneeActorRef);
        Assert.Equal(revision.RevisionRef, assignment.InitialRevisionRef);
        Assert.Null(revision.PriorRevisionRef);
    }

    [Fact]
    public async Task Parallel_delegation_does_not_replace_existing_assignment()
    {
        await using var f = await Fixture.CreateAsync();
        var actor = await f.AddActorAsync();
        var responsibility = await f.AddResponsibilityAsync(f.Contract("Parallel work", Empty));

        var first = await f.Service.DelegateAssignmentAsync(f.Delegate(responsibility, actor, Empty));
        var second = await f.Service.DelegateAssignmentAsync(f.Delegate(responsibility, actor, Empty));
        var projection = B1Projector.Build(await f.LoadAsync());

        Assert.Null(first.AssignmentDelegationEffect!.ReplacesAssignmentRef);
        Assert.Null(second.AssignmentDelegationEffect!.ReplacesAssignmentRef);
        Assert.Equal(2, projection.AcceptedProjectState.CurrentDelegationAssignments.Count);
    }

    [Fact]
    public async Task Explicit_replacement_preserves_old_assignment_history()
    {
        await using var f = await Fixture.CreateAsync();
        var actor = await f.AddActorAsync();
        var responsibility = await f.AddResponsibilityAsync(f.Contract("Replace work", Empty));
        var first = await f.Service.DelegateAssignmentAsync(f.Delegate(responsibility, actor, Empty));
        var firstAssignment = first.AssignmentDelegationEffect!.Assignment.AssignmentRef;

        var replacement = await f.Service.DelegateAssignmentAsync(
            f.Delegate(responsibility, actor, Empty, replaces: firstAssignment));
        var state = await f.LoadAsync();
        var projection = B1Projector.Build(state);

        Assert.Equal(2, state.Assignments.Count);
        Assert.Contains(firstAssignment, state.Assignments.Select(value => value.AssignmentRef));
        Assert.DoesNotContain(firstAssignment, projection.AcceptedProjectState.CurrentDelegationAssignments);
        Assert.Contains(replacement.AssignmentDelegationEffect!.Assignment.AssignmentRef,
            projection.AcceptedProjectState.CurrentDelegationAssignments);
    }

    [Fact]
    public async Task Replacement_target_must_still_be_current()
    {
        await using var f = await Fixture.CreateAsync();
        var actor = await f.AddActorAsync();
        var responsibility = await f.AddResponsibilityAsync(f.Contract("Stale replacement", Empty));
        var first = await f.Service.DelegateAssignmentAsync(f.Delegate(responsibility, actor, Empty));
        var firstAssignment = first.AssignmentDelegationEffect!.Assignment.AssignmentRef;
        await f.Service.DelegateAssignmentAsync(f.Delegate(responsibility, actor, Empty, replaces: firstAssignment));

        var exception = await Assert.ThrowsAsync<B1CommandException>(() =>
            f.Service.DelegateAssignmentAsync(f.Delegate(responsibility, actor, Empty, replaces: firstAssignment)));

        Assert.Equal(B1FailureCode.StaleReplacement, exception.Code);
        Assert.Equal(2, (await f.LoadAsync()).Assignments.Count);
    }

    [Fact]
    public async Task Accepted_assignment_remains_replaceable_by_a_later_separate_decision()
    {
        await using var f = await Fixture.CreateAsync();
        var actor = await f.AddActorAsync();
        var responsibility = await f.AddResponsibilityAsync(f.Contract("Accepted replacement", Empty));
        var first = await f.Service.DelegateAssignmentAsync(f.Delegate(responsibility, actor, Empty));
        var assignment = first.AssignmentDelegationEffect!.Assignment;
        var revision = first.AssignmentDelegationEffect.InitialRevision;
        await f.CommitDispositionAsync(assignment.AssignmentRef, revision.RevisionRef, AssignmentDisposition.Accepted);

        var replacement = await f.Service.DelegateAssignmentAsync(
            f.Delegate(responsibility, actor, Empty, replaces: assignment.AssignmentRef));

        Assert.Equal(assignment.AssignmentRef, replacement.AssignmentDelegationEffect!.ReplacesAssignmentRef);
        Assert.Equal(5, replacement.ProjectCommitSequence);
    }

    [Fact]
    public async Task LogicalActor_delegate_is_responsibility_local()
    {
        await using var f = await Fixture.CreateAsync();
        var boundary = Boundary(B1AuthorityCapability.DelegateAssignment);
        var seeded = await f.AddSeededResponsibilityAsync(boundary, boundary);
        var other = await f.AddResponsibilityAsync(f.Contract("Other responsibility", boundary));

        var exception = await Assert.ThrowsAsync<B1CommandException>(() =>
            f.Service.DelegateAssignmentAsync(f.Delegate(
                other,
                seeded.Actor,
                Empty,
                decidingAuthority: new DecidingAuthorityRef.LogicalActor(seeded.Actor))));

        Assert.Equal(B1FailureCode.NotAuthorized, exception.Code);
        Assert.Single((await f.LoadAsync()).Assignments);
    }

    [Fact]
    public async Task LogicalActor_cannot_seed_new_responsibility_even_when_it_can_establish_one()
    {
        await using var f = await Fixture.CreateAsync();
        var boundary = Boundary(
            B1AuthorityCapability.EstablishResponsibility,
            B1AuthorityCapability.DelegateAssignment);
        var seeded = await f.AddSeededResponsibilityAsync(boundary, boundary);
        var actorAuthority = new DecidingAuthorityRef.LogicalActor(seeded.Actor);
        var newResponsibilityDecision = await f.Service.EstablishResponsibilityAsync(
            f.EstablishResponsibility(f.Contract("Actor-created responsibility", boundary), actorAuthority));
        var newResponsibility = newResponsibilityDecision.ResponsibilityEstablishmentEffect!.Responsibility.ResponsibilityRef;

        var exception = await Assert.ThrowsAsync<B1CommandException>(() =>
            f.Service.DelegateAssignmentAsync(f.Delegate(
                newResponsibility, seeded.Actor, Empty, decidingAuthority: actorAuthority)));

        Assert.Equal(B1FailureCode.NotAuthorized, exception.Code);
        Assert.Single((await f.LoadAsync()).Assignments);
    }

    [Fact]
    public async Task Bootstrap_seeds_each_responsibility_first_assignment()
    {
        await using var f = await Fixture.CreateAsync();
        var boundary = Boundary(
            B1AuthorityCapability.EstablishResponsibility,
            B1AuthorityCapability.DelegateAssignment);
        var seeded = await f.AddSeededResponsibilityAsync(boundary, boundary);
        var actorAuthority = new DecidingAuthorityRef.LogicalActor(seeded.Actor);
        var created = await f.Service.EstablishResponsibilityAsync(
            f.EstablishResponsibility(f.Contract("Root-seeded next", boundary), actorAuthority));
        var responsibility = created.ResponsibilityEstablishmentEffect!.Responsibility.ResponsibilityRef;

        var delegated = await f.Service.DelegateAssignmentAsync(
            f.Delegate(responsibility, seeded.Actor, Empty));

        Assert.Equal(responsibility, delegated.AssignmentDelegationEffect!.Assignment.ResponsibilityRef);
        Assert.Equal(2, (await f.LoadAsync()).Assignments.Count);
    }

    [Fact]
    public async Task New_boundary_cannot_exceed_maximum_or_decider_possession()
    {
        await using var f = await Fixture.CreateAsync();
        var delegateOnly = Boundary(B1AuthorityCapability.DelegateAssignment);
        var delegateAndDecide = Boundary(
            B1AuthorityCapability.DelegateAssignment,
            B1AuthorityCapability.DecideAssignmentDisposition);
        var maximumLimited = await f.AddResponsibilityAsync(f.Contract("Maximum limited", delegateOnly));
        var actor = await f.AddActorAsync();

        var maximumFailure = await Assert.ThrowsAsync<B1CommandException>(() =>
            f.Service.DelegateAssignmentAsync(f.Delegate(maximumLimited, actor, delegateAndDecide)));
        Assert.Equal(B1FailureCode.AuthorityAmplification, maximumFailure.Code);

        var seeded = await f.AddSeededResponsibilityAsync(delegateAndDecide, delegateOnly);
        var possessionFailure = await Assert.ThrowsAsync<B1CommandException>(() =>
            f.Service.DelegateAssignmentAsync(f.Delegate(
                seeded.Responsibility,
                seeded.Actor,
                delegateAndDecide,
                decidingAuthority: new DecidingAuthorityRef.LogicalActor(seeded.Actor))));
        Assert.Equal(B1FailureCode.AuthorityAmplification, possessionFailure.Code);
    }

    [Fact]
    public async Task Three_sequence_conflicts_reload_and_then_commit_on_the_fourth_attempt()
    {
        await using var f = await Fixture.CreateAsync();
        var time = new ConflictInjectingTimeProvider(
            Fixture.Now,
            conflictsToInject: 3,
            f.CommitUnrelatedActorAsync);

        var decision = await f.CreateService(time).EstablishLogicalActorAsync(f.EstablishActor());
        var state = await f.LoadAsync();

        Assert.Equal(4, time.ReadCount);
        Assert.Equal(4, decision.ProjectCommitSequence);
        Assert.Equal(4, state.Governance.LastProjectCommitSequence);
        Assert.Equal(4, state.AuthorityDecisions.Count);
        Assert.Contains(decision.DecisionRef, state.AuthorityDecisions.Select(value => value.DecisionRef));
    }

    [Fact]
    public async Task Fourth_sequence_conflict_exhausts_three_retry_budget_without_target_effects()
    {
        await using var f = await Fixture.CreateAsync();
        var time = new ConflictInjectingTimeProvider(
            Fixture.Now,
            conflictsToInject: 4,
            f.CommitUnrelatedActorAsync);

        var exception = await Assert.ThrowsAsync<B1CommandException>(() =>
            f.CreateService(time).EstablishLogicalActorAsync(f.EstablishActor()));
        var state = await f.LoadAsync();

        Assert.Equal(B1FailureCode.ConcurrentProjectChange, exception.Code);
        Assert.Equal(4, time.ReadCount);
        Assert.Equal(4, state.Governance.LastProjectCommitSequence);
        Assert.Equal(4, state.AuthorityDecisions.Count);
        Assert.Equal(4, state.LogicalActors.Count);
    }

    [Fact]
    public async Task DecideAssignment_accepts_current_unresolved_revision_once()
    {
        await using var f = await Fixture.CreateAsync();
        var seed = await f.SeedAssignmentAsync(Empty, Empty);

        var decision = await f.Service.DecideAssignmentAsync(
            f.Decide(seed, AssignmentDisposition.Accepted));
        var second = await Assert.ThrowsAsync<B1CommandException>(() =>
            f.Service.DecideAssignmentAsync(f.Decide(seed, AssignmentDisposition.Accepted)));
        var state = await f.LoadAsync();

        Assert.Equal(AssignmentDisposition.Accepted, decision.AssignmentDispositionEffect!.Disposition);
        Assert.Equal(B1FailureCode.AlreadyDispositioned, second.Code);
        Assert.Single(state.RevisionDispositions);
    }

    [Fact]
    public async Task Stale_or_already_dispositioned_revision_rolls_back_whole_decision()
    {
        await using var f = await Fixture.CreateAsync();
        var seed = await f.SeedAssignmentAsync(Empty, Empty);
        var activation = await f.Service.ActivateAssignmentRevisionAsync(
            f.Activate(f.Activation(seed, "R2")));
        var current = seed with { Revision = activation.RevisionActivationEffect!.Revision.RevisionRef };
        var contribution = f.Contribution("must not persist", new ContributionScopeTarget.Project(f.ProjectRef));
        var before = await f.LoadAsync();
        var stale = f.Decide(seed, AssignmentDisposition.Accepted, contributions: [contribution]);

        var staleFailure = await Assert.ThrowsAsync<B1CommandException>(() =>
            f.Service.DecideAssignmentAsync(stale));
        await f.Service.DecideAssignmentAsync(f.Decide(current, AssignmentDisposition.Accepted));
        var afterAccepted = await f.LoadAsync();
        var repeatedFailure = await Assert.ThrowsAsync<B1CommandException>(() =>
            f.Service.DecideAssignmentAsync(f.Decide(
                current, AssignmentDisposition.Accepted, contributions: [contribution])));
        var final = await f.LoadAsync();

        Assert.Equal(B1FailureCode.StaleRevision, staleFailure.Code);
        Assert.Equal(B1FailureCode.AlreadyDispositioned, repeatedFailure.Code);
        Assert.Equal(before.Governance.LastProjectCommitSequence + 1, final.Governance.LastProjectCommitSequence);
        AssertStateUnchanged(afterAccepted, final);
        Assert.Empty(final.AcceptedProjectState().CurrentContributions);
    }

    [Fact]
    public async Task RevisionRequired_can_atomically_activate_replacement_revision()
    {
        await using var f = await Fixture.CreateAsync();
        var boundary = Boundary(
            B1AuthorityCapability.DecideAssignmentDisposition,
            B1AuthorityCapability.ActivateAssignmentRevision);
        var seed = await f.SeedAssignmentAsync(boundary, boundary);
        var activation = f.Activation(seed, "R2");

        var decision = await f.Service.DecideAssignmentAsync(
            f.Decide(seed, AssignmentDisposition.RevisionRequired, activation: activation));
        var state = await f.LoadAsync();

        Assert.Equal(AssignmentDisposition.RevisionRequired, decision.AssignmentDispositionEffect!.Disposition);
        Assert.Equal(decision.DecisionRef, decision.RevisionActivationEffect!.Revision.AuthorizedByDecisionRef);
        Assert.Equal(2, state.Revisions.Count);
        Assert.Equal(decision.RevisionActivationEffect.Revision.RevisionRef,
            state.AcceptedProjectState().CurrentEffectiveRevisionRefs[seed.Assignment]);
    }

    [Fact]
    public async Task RevisionRequired_can_receive_later_activation_only()
    {
        await using var f = await Fixture.CreateAsync();
        var boundary = Boundary(
            B1AuthorityCapability.DecideAssignmentDisposition,
            B1AuthorityCapability.ActivateAssignmentRevision);
        var seed = await f.SeedAssignmentAsync(boundary, boundary);
        var disposition = await f.Service.DecideAssignmentAsync(
            f.Decide(seed, AssignmentDisposition.RevisionRequired));

        var activation = await f.Service.ActivateAssignmentRevisionAsync(
            f.Activate(f.Activation(seed, "R2")));

        Assert.NotEqual(disposition.DecisionRef, activation.DecisionRef);
        Assert.Null(activation.AssignmentDispositionEffect);
        Assert.NotNull(activation.RevisionActivationEffect);
    }

    [Fact]
    public async Task Accepted_or_rejected_cannot_activate_revision()
    {
        foreach (var disposition in new[] { AssignmentDisposition.Accepted, AssignmentDisposition.Rejected })
        {
            await using var f = await Fixture.CreateAsync();
            var boundary = Boundary(
                B1AuthorityCapability.DecideAssignmentDisposition,
                B1AuthorityCapability.ActivateAssignmentRevision);
            var seed = await f.SeedAssignmentAsync(boundary, boundary);
            await f.Service.DecideAssignmentAsync(f.Decide(seed, disposition));
            var before = await f.LoadAsync();

            var exception = await Assert.ThrowsAsync<B1CommandException>(() =>
                f.Service.ActivateAssignmentRevisionAsync(f.Activate(f.Activation(seed, "forbidden"))));

            Assert.Equal(B1FailureCode.AlreadyDispositioned, exception.Code);
            AssertStateUnchanged(before, await f.LoadAsync());
        }
    }

    [Fact]
    public async Task Revision_activation_source_must_be_proposed_revision_for_same_assignment()
    {
        await using var f = await Fixture.CreateAsync();
        var boundary = Boundary(B1AuthorityCapability.ActivateAssignmentRevision);
        var first = await f.SeedAssignmentAsync(boundary, boundary);
        var second = await f.SeedAssignmentAsync(boundary, boundary);
        var wrongKind = await f.AddClaimAsync(new ClaimPayload.Result("not a revision proposal"));
        var wrongAssignment = await f.AddClaimAsync(new ClaimPayload.ProposedAssignmentRevision(
            second.Assignment, second.Revision, new AssignmentRevisionContract("other proposal")));

        var wrongKindFailure = await Assert.ThrowsAsync<B1CommandException>(() =>
            f.Service.ActivateAssignmentRevisionAsync(f.Activate(
                f.Activation(first, "R2", wrongKind.ClaimRef))));
        var wrongAssignmentFailure = await Assert.ThrowsAsync<B1CommandException>(() =>
            f.Service.ActivateAssignmentRevisionAsync(f.Activate(
                f.Activation(first, "R2", wrongAssignment.ClaimRef))));

        Assert.Equal(B1FailureCode.InvalidReference, wrongKindFailure.Code);
        Assert.Equal(B1FailureCode.InvalidReference, wrongAssignmentFailure.Code);
        Assert.Equal(2, (await f.LoadAsync()).Revisions.Count);
    }

    [Fact]
    public async Task Stale_revision_proposal_remains_considered_history_but_new_contract_targets_current_revision()
    {
        await using var f = await Fixture.CreateAsync();
        var boundary = Boundary(B1AuthorityCapability.ActivateAssignmentRevision);
        var seed = await f.SeedAssignmentAsync(boundary, boundary);
        var staleProposal = await f.AddClaimAsync(new ClaimPayload.ProposedAssignmentRevision(
            seed.Assignment, seed.Revision, new AssignmentRevisionContract("proposal against R1")));
        var r2Decision = await f.Service.ActivateAssignmentRevisionAsync(
            f.Activate(f.Activation(seed, "R2")));
        var r2 = r2Decision.RevisionActivationEffect!.Revision.RevisionRef;
        var current = seed with { Revision = r2 };
        var command = f.Activate(f.Activation(current, "authority-authored R3", staleProposal.ClaimRef)) with
        {
            ConsideredRefs = [new ConsideredRef.Claim(staleProposal.ClaimRef)]
        };

        var decision = await f.Service.ActivateAssignmentRevisionAsync(command);

        Assert.Equal(r2, decision.RevisionActivationEffect!.Revision.PriorRevisionRef);
        Assert.Equal("authority-authored R3", decision.RevisionActivationEffect.Revision.Contract.WorkContract);
        Assert.Equal(staleProposal.ClaimRef, decision.RevisionActivationEffect.SourceClaimRef);
        Assert.Contains(new ConsideredRef.Claim(staleProposal.ClaimRef), decision.ConsideredRefs);
        var persistedProposal = Assert.Single(
            (await f.LoadAsync()).Claims, value => value.ClaimRef == staleProposal.ClaimRef);
        Assert.Equal("proposal against R1",
            Assert.IsType<ClaimPayload.ProposedAssignmentRevision>(persistedProposal.Payload)
                .ProposedContract.WorkContract);
    }

    [Fact]
    public async Task RevisionRequired_or_rejected_can_replace_same_assignment()
    {
        foreach (var disposition in new[] { AssignmentDisposition.RevisionRequired, AssignmentDisposition.Rejected })
        {
            await using var f = await Fixture.CreateAsync();
            var boundary = Boundary(
                B1AuthorityCapability.DecideAssignmentDisposition,
                B1AuthorityCapability.DelegateAssignment);
            var seed = await f.SeedAssignmentAsync(boundary, boundary);
            var command = f.Decide(seed, disposition, replacement: f.Replacement(seed, seed.Actor));

            var decision = await f.Service.DecideAssignmentAsync(command);

            Assert.Equal(seed.Assignment, decision.AssignmentDelegationEffect!.ReplacesAssignmentRef);
            Assert.Equal(disposition, decision.AssignmentDispositionEffect!.Disposition);
        }
    }

    [Fact]
    public async Task Replacement_can_atomically_establish_new_assignee_with_delegate_and_actor_capabilities()
    {
        await using var f = await Fixture.CreateAsync();
        var boundary = Boundary(
            B1AuthorityCapability.DecideAssignmentDisposition,
            B1AuthorityCapability.DelegateAssignment,
            B1AuthorityCapability.EstablishLogicalActor);
        var seed = await f.SeedAssignmentAsync(boundary, boundary);
        var replacement = f.Replacement(seed, establishedRole: RoleKind.Worker);

        var decision = await f.Service.DecideAssignmentAsync(
            f.Decide(
                seed,
                AssignmentDisposition.Rejected,
                replacement: replacement,
                decidingAuthority: new DecidingAuthorityRef.LogicalActor(seed.Actor)));
        var state = await f.LoadAsync();

        Assert.NotNull(decision.LogicalActorEstablishmentEffect);
        Assert.Equal(decision.LogicalActorEstablishmentEffect!.LogicalActor.LogicalActorRef,
            decision.AssignmentDelegationEffect!.Assignment.AssigneeActorRef);
        Assert.Equal(decision.DecisionRef, decision.AssignmentDelegationEffect.InitialRevision.AuthorizedByDecisionRef);
        Assert.Equal(2, state.LogicalActors.Count);
        Assert.Equal(2, state.Assignments.Count);
    }

    [Fact]
    public async Task Replacement_actor_establishment_without_project_capability_rolls_back_whole_decision()
    {
        await using var f = await Fixture.CreateAsync();
        var boundary = Boundary(
            B1AuthorityCapability.DecideAssignmentDisposition,
            B1AuthorityCapability.DelegateAssignment);
        var seed = await f.SeedAssignmentAsync(boundary, boundary);
        var before = await f.LoadAsync();
        var replacement = f.Replacement(seed, establishedRole: RoleKind.Worker);

        var exception = await Assert.ThrowsAsync<B1CommandException>(() =>
            f.Service.DecideAssignmentAsync(
                f.Decide(
                    seed,
                    AssignmentDisposition.Rejected,
                    replacement: replacement,
                    decidingAuthority: new DecidingAuthorityRef.LogicalActor(seed.Actor))));
        var after = await f.LoadAsync();

        Assert.Equal(B1FailureCode.NotAuthorized, exception.Code);
        AssertStateUnchanged(before, after);
        Assert.Single(after.LogicalActors);
        Assert.Single(after.Assignments);
        Assert.Single(after.Revisions);
        Assert.Empty(after.RevisionDispositions);
        Assert.Empty(after.AcceptedProjectState().CurrentContributions);
    }

    [Fact]
    public async Task Accepted_plus_replacement_is_invalid()
    {
        await using var f = await Fixture.CreateAsync();
        var seed = await f.SeedAssignmentAsync(Empty, Empty);
        var before = await f.LoadAsync();

        var exception = await Assert.ThrowsAsync<B1CommandException>(() =>
            f.Service.DecideAssignmentAsync(f.Decide(
                seed, AssignmentDisposition.Accepted, replacement: f.Replacement(seed, seed.Actor))));

        Assert.Equal(B1FailureCode.InvalidDecisionShape, exception.Code);
        AssertStateUnchanged(before, await f.LoadAsync());
    }

    [Fact]
    public async Task Disposition_parallel_delegation_or_other_assignment_replacement_is_invalid()
    {
        await using var f = await Fixture.CreateAsync();
        var first = await f.SeedAssignmentAsync(Empty, Empty);
        var second = await f.SeedAssignmentAsync(Empty, Empty);
        var before = await f.LoadAsync();
        var otherReplacement = f.Replacement(second, second.Actor);

        var exception = await Assert.ThrowsAsync<B1CommandException>(() =>
            f.Service.DecideAssignmentAsync(f.Decide(
                first, AssignmentDisposition.Rejected, replacement: otherReplacement)));

        Assert.Equal(B1FailureCode.InvalidDecisionShape, exception.Code);
        AssertStateUnchanged(before, await f.LoadAsync());
        Assert.Null(typeof(DecideAssignmentCommand).GetProperty("Delegation"));
        Assert.Equal(
            typeof(AssignmentRef),
            typeof(AssignmentReplacementInstruction).GetProperty("ReplacesAssignmentRef")!.PropertyType);
    }

    [Fact]
    public async Task Historical_old_revision_claim_can_support_project_contribution_without_old_disposition()
    {
        await using var f = await Fixture.CreateAsync();
        var boundary = Boundary(B1AuthorityCapability.ActivateAssignmentRevision);
        var seed = await f.SeedAssignmentAsync(boundary, boundary);
        var historical = await f.AddClaimAsync(new ClaimPayload.ProposedAssignmentRevision(
            seed.Assignment, seed.Revision, new AssignmentRevisionContract("historical proposal")));
        await f.Service.ActivateAssignmentRevisionAsync(f.Activate(f.Activation(seed, "R2")));
        var command = f.Author(f.Contribution("Dependency must be replaced", new ContributionScopeTarget.Project(f.ProjectRef))) with
        {
            ConsideredRefs = [new ConsideredRef.Claim(historical.ClaimRef)]
        };

        var decision = await f.Service.AuthorAcceptedStateAsync(command);
        var state = await f.LoadAsync();

        Assert.Null(decision.AssignmentDispositionEffect);
        Assert.Contains(new ConsideredRef.Claim(historical.ClaimRef), decision.ConsideredRefs);
        Assert.Single(decision.AcceptedStateContributions);
        Assert.DoesNotContain(state.RevisionDispositions, value => value.RevisionRef == seed.Revision);
        var persistedClaim = Assert.Single(state.Claims, value => value.ClaimRef == historical.ClaimRef);
        Assert.Equal(historical.Payload, persistedClaim.Payload);
    }

    [Fact]
    public async Task Activation_creates_no_attempt_or_execution_state()
    {
        await using var f = await Fixture.CreateAsync();
        var boundary = Boundary(B1AuthorityCapability.ActivateAssignmentRevision);
        var seed = await f.SeedAssignmentAsync(boundary, boundary);

        var decision = await f.Service.ActivateAssignmentRevisionAsync(
            f.Activate(f.Activation(seed, "R2")));
        var state = await f.LoadAsync();

        Assert.NotNull(decision.RevisionActivationEffect);
        Assert.Null(decision.AssignmentDispositionEffect);
        Assert.Empty(state.Attempts);
        Assert.Empty(state.SessionBindings);
        Assert.Empty(state.Handoffs);
        Assert.All(state.AssignmentRoutingSelections, routing => Assert.Null(routing.SelectedAttemptRef));
    }

    [Fact]
    public async Task AuthorAcceptedState_requires_one_or_more_contributions()
    {
        await using var f = await Fixture.CreateAsync();

        var exception = await Assert.ThrowsAsync<B1CommandException>(() =>
            f.Service.AuthorAcceptedStateAsync(f.Author()));

        Assert.Equal(B1FailureCode.InvalidDecisionShape, exception.Code);
        Assert.Empty((await f.LoadAsync()).AuthorityDecisions);
    }

    [Fact]
    public async Task Authority_can_adopt_modify_or_author_without_claim()
    {
        await using var f = await Fixture.CreateAsync();
        var scope = new ContributionScopeRef.Project(f.ProjectRef);
        var proposal = await f.AddClaimAsync(new ClaimPayload.ProposedStateContribution("proposal X", scope, null));

        var adopted = await f.Service.AuthorAcceptedStateAsync(f.Author(
            f.Contribution("proposal X", new ContributionScopeTarget.Project(f.ProjectRef), source: proposal.ClaimRef)));
        var modified = await f.Service.AuthorAcceptedStateAsync(f.Author(
            f.Contribution("authority revision Y", new ContributionScopeTarget.Project(f.ProjectRef), source: proposal.ClaimRef)));
        var authored = await f.Service.AuthorAcceptedStateAsync(f.Author(
            f.Contribution("authority-only Z", new ContributionScopeTarget.Project(f.ProjectRef))));

        Assert.Equal(proposal.ClaimRef, Assert.Single(adopted.AcceptedStateContributions).SourceClaimRef);
        Assert.Equal("authority revision Y", Assert.Single(modified.AcceptedStateContributions).Statement);
        Assert.Null(Assert.Single(authored.AcceptedStateContributions).SourceClaimRef);
        Assert.Equal("proposal X", ((ClaimPayload.ProposedStateContribution)proposal.Payload).Statement);
    }

    [Fact]
    public async Task Source_claim_must_be_same_project_proposed_state_claim()
    {
        await using var f = await Fixture.CreateAsync();
        var result = await f.AddClaimAsync(new ClaimPayload.Result("wrong kind"));
        var external = await f.AddOtherProjectProposedContributionClaimAsync("other project");
        var target = new ContributionScopeTarget.Project(f.ProjectRef);

        var wrongKind = await Assert.ThrowsAsync<B1CommandException>(() =>
            f.Service.AuthorAcceptedStateAsync(f.Author(f.Contribution("x", target, source: result.ClaimRef))));
        var wrongProject = await Assert.ThrowsAsync<B1CommandException>(() =>
            f.Service.AuthorAcceptedStateAsync(f.Author(f.Contribution("y", target, source: external.ClaimRef))));

        Assert.Equal(B1FailureCode.InvalidReference, wrongKind.Code);
        Assert.Equal(B1FailureCode.InvalidReference, wrongProject.Code);
        Assert.Empty((await f.LoadAsync()).AcceptedProjectState().CurrentContributions);
    }

    [Fact]
    public async Task Proposal_scope_or_proposed_supersession_never_applies_automatically()
    {
        await using var f = await Fixture.CreateAsync();
        var initial = await f.Service.AuthorAcceptedStateAsync(f.Author(
            f.Contribution("A", new ContributionScopeTarget.Project(f.ProjectRef))));
        var current = Assert.Single(initial.AcceptedStateContributions);
        var proposal = await f.AddClaimAsync(new ClaimPayload.ProposedStateContribution(
            "proposal B",
            new ContributionScopeRef.Project(f.ProjectRef),
            current.ContributionRef));
        var responsibility = await f.AddResponsibilityAsync(f.Contract("Scoped authority", Empty));

        var decision = await f.Service.AuthorAcceptedStateAsync(f.Author(f.Contribution(
            "authority C",
            new ContributionScopeTarget.Responsibility.Existing(responsibility),
            source: proposal.ClaimRef)));
        var projected = (await f.LoadAsync()).AcceptedProjectState().CurrentContributions;

        Assert.Null(Assert.Single(decision.AcceptedStateContributions).SupersedesContributionRef);
        Assert.Contains(current, projected);
        Assert.Contains(projected, value => value.Statement == "authority C" &&
            value.Scope == new ContributionScopeRef.Responsibility(responsibility));
    }

    [Fact]
    public async Task Rejected_or_revision_required_decision_may_still_author_explicit_contributions()
    {
        await using var f = await Fixture.CreateAsync();
        var first = await f.SeedAssignmentAsync(Empty, Empty);
        var second = await f.SeedAssignmentAsync(Empty, Empty);
        var contribution = f.Contribution("compatibility remains required", new ContributionScopeTarget.Project(f.ProjectRef));

        var rejected = await f.Service.DecideAssignmentAsync(
            f.Decide(first, AssignmentDisposition.Rejected, contributions: [contribution]));
        var revisionRequired = await f.Service.DecideAssignmentAsync(
            f.Decide(second, AssignmentDisposition.RevisionRequired, contributions: [contribution]));

        Assert.Single(rejected.AcceptedStateContributions);
        Assert.Single(revisionRequired.AcceptedStateContributions);
        Assert.Equal(2, (await f.LoadAsync()).AcceptedProjectState().CurrentContributions.Count);
    }

    [Fact]
    public async Task Contributions_default_to_coexistence()
    {
        await using var f = await Fixture.CreateAsync();
        var scope = new ContributionScopeTarget.Project(f.ProjectRef);
        await f.Service.AuthorAcceptedStateAsync(f.Author(f.Contribution("A", scope)));
        await f.Service.AuthorAcceptedStateAsync(f.Author(f.Contribution("X", scope)));

        var current = (await f.LoadAsync()).AcceptedProjectState().CurrentContributions;

        Assert.Equal(2, current.Count);
        Assert.Contains(current, value => value.Statement == "A");
        Assert.Contains(current, value => value.Statement == "X");
    }

    [Fact]
    public async Task Supersession_requires_current_exact_scope_target()
    {
        await using var f = await Fixture.CreateAsync();
        var scope = new ContributionScopeTarget.Project(f.ProjectRef);
        var aDecision = await f.Service.AuthorAcceptedStateAsync(f.Author(f.Contribution("A", scope)));
        var a = Assert.Single(aDecision.AcceptedStateContributions);
        var bDecision = await f.Service.AuthorAcceptedStateAsync(f.Author(
            f.Contribution("B", scope, supersedes: a.ContributionRef)));
        var b = Assert.Single(bDecision.AcceptedStateContributions);
        var responsibility = await f.AddResponsibilityAsync(f.Contract("Exact scope", Empty));

        var stale = await Assert.ThrowsAsync<B1CommandException>(() =>
            f.Service.AuthorAcceptedStateAsync(f.Author(f.Contribution("C", scope, supersedes: a.ContributionRef))));
        var wrongScope = await Assert.ThrowsAsync<B1CommandException>(() =>
            f.Service.AuthorAcceptedStateAsync(f.Author(f.Contribution(
                "D", new ContributionScopeTarget.Responsibility.Existing(responsibility), supersedes: b.ContributionRef))));
        var current = (await f.LoadAsync()).AcceptedProjectState().CurrentContributions;

        Assert.Equal(B1FailureCode.StaleSupersession, stale.Code);
        Assert.Equal(B1FailureCode.StaleSupersession, wrongScope.Code);
        Assert.Equal(b, Assert.Single(current));
    }

    [Fact]
    public async Task Contributions_in_same_decision_cannot_supersede_each_other()
    {
        await using var f = await Fixture.CreateAsync();
        var scope = new ContributionScopeTarget.Project(f.ProjectRef);
        var currentDecision = await f.Service.AuthorAcceptedStateAsync(f.Author(f.Contribution("A", scope)));
        var current = Assert.Single(currentDecision.AcceptedStateContributions);
        var before = await f.LoadAsync();
        var command = f.Author(
            f.Contribution("B", scope, supersedes: current.ContributionRef),
            f.Contribution("C", scope, supersedes: current.ContributionRef));

        var failure = await Assert.ThrowsAsync<B1CommandException>(() =>
            f.Service.AuthorAcceptedStateAsync(command));

        Assert.Equal(B1FailureCode.InvalidDecisionShape, failure.Code);
        AssertStateUnchanged(before, await f.LoadAsync());
    }

    [Fact]
    public async Task Concurrent_supersession_has_one_complete_winner()
    {
        await using var f = await Fixture.CreateAsync();
        var scope = new ContributionScopeTarget.Project(f.ProjectRef);
        var initial = await f.Service.AuthorAcceptedStateAsync(f.Author(f.Contribution("A", scope)));
        var target = Assert.Single(initial.AcceptedStateContributions).ContributionRef;
        using var barrier = new Barrier(2);
        var firstService = f.CreateService(new FirstReadBarrierTimeProvider(Fixture.Now, barrier));
        var secondService = f.CreateService(new FirstReadBarrierTimeProvider(Fixture.Now.AddSeconds(1), barrier));

        var results = await Task.WhenAll(
            Task.Run(() => CaptureAsync(firstService.AuthorAcceptedStateAsync(
                f.Author(f.Contribution("B", scope, supersedes: target))))),
            Task.Run(() => CaptureAsync(secondService.AuthorAcceptedStateAsync(
                f.Author(f.Contribution("C", scope, supersedes: target))))));
        var current = (await f.LoadAsync()).AcceptedProjectState().CurrentContributions;

        Assert.NotNull(Assert.Single(results, result => result.Decision is not null).Decision);
        var failure = Assert.Single(results, result => result.Exception is not null).Exception!;
        Assert.Equal(B1FailureCode.StaleSupersession, failure.Code);
        Assert.Single(current);
        Assert.DoesNotContain(current, value => value.ContributionRef == target);
    }

    [Fact]
    public async Task Prospective_scope_resolves_only_for_identity_created_by_same_command()
    {
        await using var f = await Fixture.CreateAsync();
        var invalid = await Assert.ThrowsAsync<B1CommandException>(() =>
            f.Service.AuthorAcceptedStateAsync(f.Author(f.Contribution(
                "no prospective identity", new ContributionScopeTarget.Assignment.DelegatedByThisDecision()))));
        var seed = await f.SeedAssignmentAsync(Empty, Empty);
        var replacement = f.Replacement(seed, seed.Actor);
        var contribution = f.Contribution(
            "new assignment state", new ContributionScopeTarget.Assignment.DelegatedByThisDecision());

        var decision = await f.Service.DecideAssignmentAsync(
            f.Decide(seed, AssignmentDisposition.Rejected, replacement: replacement, contributions: [contribution]));

        Assert.Equal(B1FailureCode.InvalidDecisionShape, invalid.Code);
        Assert.Equal(
            new ContributionScopeRef.Assignment(decision.AssignmentDelegationEffect!.Assignment.AssignmentRef),
            Assert.Single(decision.AcceptedStateContributions).Scope);
    }

    [Fact]
    public async Task Contribution_failure_rolls_back_disposition_activation_and_replacement()
    {
        await using var activationFixture = await Fixture.CreateAsync();
        var activationBoundary = Boundary(
            B1AuthorityCapability.DecideAssignmentDisposition,
            B1AuthorityCapability.ActivateAssignmentRevision);
        var activationSeed = await activationFixture.SeedAssignmentAsync(activationBoundary, activationBoundary);
        var activationBefore = await activationFixture.LoadAsync();
        var invalidContribution = activationFixture.Contribution(
            "invalid", new ContributionScopeTarget.Assignment.Existing(new AssignmentRef(Guid.NewGuid())));

        await Assert.ThrowsAsync<B1CommandException>(() => activationFixture.Service.DecideAssignmentAsync(
            activationFixture.Decide(
                activationSeed,
                AssignmentDisposition.RevisionRequired,
                activation: activationFixture.Activation(activationSeed, "R2"),
                contributions: [invalidContribution])));
        AssertStateUnchanged(activationBefore, await activationFixture.LoadAsync());

        await using var replacementFixture = await Fixture.CreateAsync();
        var replacementBoundary = Boundary(
            B1AuthorityCapability.DecideAssignmentDisposition,
            B1AuthorityCapability.DelegateAssignment,
            B1AuthorityCapability.EstablishLogicalActor);
        var replacementSeed = await replacementFixture.SeedAssignmentAsync(replacementBoundary, replacementBoundary);
        var replacementBefore = await replacementFixture.LoadAsync();
        var badReplacementContribution = replacementFixture.Contribution(
            "invalid", new ContributionScopeTarget.Responsibility.Existing(new ResponsibilityRef(Guid.NewGuid())));

        await Assert.ThrowsAsync<B1CommandException>(() => replacementFixture.Service.DecideAssignmentAsync(
            replacementFixture.Decide(
                replacementSeed,
                AssignmentDisposition.Rejected,
                replacement: replacementFixture.Replacement(replacementSeed, establishedRole: RoleKind.Worker),
                contributions: [badReplacementContribution])));
        AssertStateUnchanged(replacementBefore, await replacementFixture.LoadAsync());
    }

    [Fact]
    public void Public_authority_surface_contains_exactly_six_named_commands()
    {
        var methods = typeof(B1AuthorityCommandService)
            .GetMethods()
            .Where(method => method.DeclaringType == typeof(B1AuthorityCommandService))
            .Select(method => method.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[]
        {
            "ActivateAssignmentRevisionAsync",
            "AuthorAcceptedStateAsync",
            "DecideAssignmentAsync",
            "DelegateAssignmentAsync",
            "EstablishLogicalActorAsync",
            "EstablishResponsibilityAsync"
        }, methods);
    }

    private static async Task<(AuthorityDecision? Decision, B1CommandException? Exception)> CaptureAsync(
        Task<AuthorityDecision> operation)
    {
        try
        {
            return (await operation, null);
        }
        catch (B1CommandException exception)
        {
            return (null, exception);
        }
    }

    private static void AssertStateUnchanged(B1ProjectState before, B1ProjectState after)
    {
        Assert.Equal(before.Governance, after.Governance);
        Assert.Equal(
            before.LogicalActors.Select(value => value.LogicalActorRef),
            after.LogicalActors.Select(value => value.LogicalActorRef));
        Assert.Equal(
            before.Responsibilities.Select(value => value.ResponsibilityRef),
            after.Responsibilities.Select(value => value.ResponsibilityRef));
        Assert.Equal(
            before.Assignments.Select(value => value.AssignmentRef),
            after.Assignments.Select(value => value.AssignmentRef));
        Assert.Equal(
            before.Revisions.Select(value => value.RevisionRef),
            after.Revisions.Select(value => value.RevisionRef));
        Assert.Equal(before.RevisionDispositions, after.RevisionDispositions);
        Assert.Equal(
            before.Attempts.Select(value => value.AttemptRef),
            after.Attempts.Select(value => value.AttemptRef));
        Assert.Equal(
            before.SessionBindings.Select(value => value.SessionBindingRef),
            after.SessionBindings.Select(value => value.SessionBindingRef));
        Assert.Equal(
            before.Claims.Select(value => value.ClaimRef),
            after.Claims.Select(value => value.ClaimRef));
        Assert.Equal(
            before.Handoffs.Select(value => value.HandoffRef),
            after.Handoffs.Select(value => value.HandoffRef));
        Assert.Equal(
            before.AuthorityDecisions.Select(value => (value.ProjectCommitSequence, value.DecisionRef)),
            after.AuthorityDecisions.Select(value => (value.ProjectCommitSequence, value.DecisionRef)));
        Assert.Equal(
            before.AuthorityDecisions.SelectMany(value => value.AcceptedStateContributions)
                .Select(value => value.ContributionRef),
            after.AuthorityDecisions.SelectMany(value => value.AcceptedStateContributions)
                .Select(value => value.ContributionRef));
        Assert.Equal(before.AssignmentRoutingSelections, after.AssignmentRoutingSelections);
        Assert.Equal(before.AttemptRoutingSelections, after.AttemptRoutingSelections);
    }

    private static AuthorityBoundary Boundary(params B1AuthorityCapability[] capabilities) =>
        AuthorityBoundary.Create(capabilities);

    private sealed record SeededAssignment(
        LogicalActorRef Actor,
        ResponsibilityRef Responsibility,
        AssignmentRef Assignment,
        RevisionRef Revision);

    private sealed class Fixture : IAsyncDisposable
    {
        public static readonly DateTimeOffset Now =
            new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);
        private readonly string _directory;
        private readonly B1AuthorityRepository _repository;
        private readonly B1AuthorityEvaluator _evaluator = new();
        private readonly B1ClaimHandoffRepository _claims;

        private Fixture(
            string directory,
            WorkbenchDatabase database,
            ProjectRef projectRef,
            UserPrincipalRef principal,
            B1AuthorityRepository repository,
            B1ClaimHandoffRepository claims,
            B1AuthorityCommandService service)
        {
            _directory = directory;
            Database = database;
            ProjectRef = projectRef;
            Principal = principal;
            _repository = repository;
            _claims = claims;
            Service = service;
        }

        public WorkbenchDatabase Database { get; }
        public ProjectRef ProjectRef { get; }
        public UserPrincipalRef Principal { get; }
        public DecidingAuthorityRef BootstrapAuthority => new DecidingAuthorityRef.UserPrincipal(Principal);
        public B1AuthorityCommandService Service { get; }

        public B1AuthorityCommandService CreateService(TimeProvider timeProvider) =>
            new(_repository, _evaluator, timeProvider);

        public static async Task<Fixture> CreateAsync()
        {
            var directory = Path.Combine(
                Path.GetTempPath(), "AI.Game.Workbench.App.Tests", Guid.NewGuid().ToString("N"));
            var database = new WorkbenchDatabase(Path.Combine(directory, "test.db"));
            await database.InitializeAsync();
            var projectId = Guid.NewGuid();
            var principal = new UserPrincipalRef("user:b1-authority-app-test");
            await new B1ProjectGovernanceRepository(database).CreateGovernedProjectAsync(
                new CoreProject(
                    projectId,
                    "B1 Authority App Project",
                    $"C:/Projects/{projectId:N}",
                    ProjectType.Godot,
                    null,
                    Now,
                    Now),
                principal);
            var repository = new B1AuthorityRepository(database);
            var claims = new B1ClaimHandoffRepository(database);
            var time = new MutableTimeProvider(Now);
            return new Fixture(
                directory,
                database,
                new ProjectRef(projectId),
                principal,
                repository,
                claims,
                new B1AuthorityCommandService(repository, new B1AuthorityEvaluator(), time));
        }

        public EstablishLogicalActorCommand EstablishActor(
            RoleKind roleKind = RoleKind.Worker,
            DecidingAuthorityRef? decidingAuthority = null) =>
            new(ProjectRef, Principal, decidingAuthority ?? BootstrapAuthority, roleKind, [], []);

        public EstablishResponsibilityCommand EstablishResponsibility(
            ResponsibilityContract contract,
            DecidingAuthorityRef? decidingAuthority = null) =>
            new(ProjectRef, Principal, decidingAuthority ?? BootstrapAuthority,
                contract, null, null, [], []);

        public EstablishResponsibilityCommand EstablishResponsibilityWithInitialDelegation(
            ResponsibilityContract contract,
            AuthorityBoundary delegatedBoundary,
            IReadOnlyList<AcceptedContributionInstruction>? contributions = null) =>
            new(
                ProjectRef,
                Principal,
                BootstrapAuthority,
                contract,
                new AssignmentDelegationInstruction(
                    new ResponsibilityTarget.EstablishedByThisDecision(),
                    new AssignmentAssigneeTarget.EstablishedByThisDecision(),
                    new AssignmentRevisionContract("Initial work", delegatedBoundary),
                    null),
                RoleKind.Worker,
                [],
                contributions ?? []);

        public DelegateAssignmentCommand Delegate(
            ResponsibilityRef responsibility,
            LogicalActorRef actor,
            AuthorityBoundary delegatedBoundary,
            AssignmentRef? replaces = null,
            DecidingAuthorityRef? decidingAuthority = null) =>
            new(
                ProjectRef,
                Principal,
                decidingAuthority ?? BootstrapAuthority,
                new AssignmentDelegationInstruction(
                    new ResponsibilityTarget.Existing(responsibility),
                    new AssignmentAssigneeTarget.Existing(actor),
                    new AssignmentRevisionContract("Delegated work", delegatedBoundary),
                    replaces),
                null,
                [],
                []);

        public DecideAssignmentCommand Decide(
            SeededAssignment seed,
            AssignmentDisposition disposition,
            RevisionActivationInstruction? activation = null,
            AssignmentReplacementInstruction? replacement = null,
            IReadOnlyList<AcceptedContributionInstruction>? contributions = null,
            DecidingAuthorityRef? decidingAuthority = null) =>
            new(
                ProjectRef,
                Principal,
                decidingAuthority ?? BootstrapAuthority,
                new AssignmentDispositionInstruction(seed.Assignment, seed.Revision, disposition),
                activation,
                replacement,
                [],
                contributions ?? []);

        public RevisionActivationInstruction Activation(
            SeededAssignment seed,
            string workContract,
            ClaimRef? sourceClaimRef = null) =>
            new(
                seed.Assignment,
                seed.Revision,
                new AssignmentRevisionContract(workContract),
                sourceClaimRef);

        public ActivateAssignmentRevisionCommand Activate(
            RevisionActivationInstruction activation,
            IReadOnlyList<AcceptedContributionInstruction>? contributions = null,
            DecidingAuthorityRef? decidingAuthority = null) =>
            new(
                ProjectRef,
                Principal,
                decidingAuthority ?? BootstrapAuthority,
                activation,
                [],
                contributions ?? []);

        public AssignmentReplacementInstruction Replacement(
            SeededAssignment seed,
            LogicalActorRef? existingActor = null,
            RoleKind? establishedRole = null) =>
            new(
                seed.Assignment,
                establishedRole is null
                    ? new AssignmentAssigneeTarget.Existing(existingActor ?? seed.Actor)
                    : new AssignmentAssigneeTarget.EstablishedByThisDecision(),
                establishedRole,
                new AssignmentRevisionContract("Replacement work"));

        public AuthorAcceptedStateCommand Author(params AcceptedContributionInstruction[] contributions) =>
            new(ProjectRef, Principal, BootstrapAuthority, [], contributions);

        public ResponsibilityContract Contract(string obligation, AuthorityBoundary maximum) =>
            new(obligation, $"{obligation} outcome", maximum);

        public AcceptedContributionInstruction Contribution(
            string statement,
            ContributionScopeTarget scope,
            AcceptedStateContributionRef? supersedes = null,
            ClaimRef? source = null) =>
            new(statement, scope, supersedes, source);

        public async Task<LogicalActorRef> AddActorAsync()
        {
            var decision = await Service.EstablishLogicalActorAsync(EstablishActor());
            return decision.LogicalActorEstablishmentEffect!.LogicalActor.LogicalActorRef;
        }

        public async Task<ResponsibilityRef> AddResponsibilityAsync(ResponsibilityContract contract)
        {
            var decision = await Service.EstablishResponsibilityAsync(EstablishResponsibility(contract));
            return decision.ResponsibilityEstablishmentEffect!.Responsibility.ResponsibilityRef;
        }

        public async Task<(LogicalActorRef Actor, ResponsibilityRef Responsibility, AssignmentRef Assignment)>
            AddSeededResponsibilityAsync(AuthorityBoundary maximum, AuthorityBoundary delegated)
        {
            var decision = await Service.EstablishResponsibilityAsync(
                EstablishResponsibilityWithInitialDelegation(Contract("Seeded responsibility", maximum), delegated));
            return (
                decision.LogicalActorEstablishmentEffect!.LogicalActor.LogicalActorRef,
                decision.ResponsibilityEstablishmentEffect!.Responsibility.ResponsibilityRef,
                decision.AssignmentDelegationEffect!.Assignment.AssignmentRef);
        }

        public async Task<SeededAssignment> SeedAssignmentAsync(
            AuthorityBoundary maximum,
            AuthorityBoundary delegated)
        {
            var decision = await Service.EstablishResponsibilityAsync(
                EstablishResponsibilityWithInitialDelegation(
                    Contract($"Seeded {Guid.NewGuid():N}", maximum), delegated));
            return new(
                decision.LogicalActorEstablishmentEffect!.LogicalActor.LogicalActorRef,
                decision.ResponsibilityEstablishmentEffect!.Responsibility.ResponsibilityRef,
                decision.AssignmentDelegationEffect!.Assignment.AssignmentRef,
                decision.AssignmentDelegationEffect.InitialRevision.RevisionRef);
        }

        public Task<Claim> AddClaimAsync(ClaimPayload payload) =>
            _claims.RecordClaimAsync(new RecordClaimCommand(
                ProjectRef,
                Principal,
                new ClaimRef(Guid.NewGuid()),
                new ClaimantRef.UserPrincipal(Principal),
                null,
                payload,
                [],
                Now));

        public async Task<Claim> AddOtherProjectProposedContributionClaimAsync(string statement)
        {
            var projectId = Guid.NewGuid();
            var projectRef = new ProjectRef(projectId);
            var principal = new UserPrincipalRef($"user:other:{projectId:N}");
            await new B1ProjectGovernanceRepository(Database).CreateGovernedProjectAsync(
                new CoreProject(
                    projectId,
                    "Other B1 Project",
                    $"C:/Projects/{projectId:N}",
                    ProjectType.Godot,
                    null,
                    Now,
                    Now),
                principal);
            return await _claims.RecordClaimAsync(new RecordClaimCommand(
                projectRef,
                principal,
                new ClaimRef(Guid.NewGuid()),
                new ClaimantRef.UserPrincipal(principal),
                null,
                new ClaimPayload.ProposedStateContribution(
                    statement, new ContributionScopeRef.Project(projectRef), null),
                [],
                Now));
        }

        public async Task CommitDispositionAsync(
            AssignmentRef assignmentRef,
            RevisionRef revisionRef,
            AssignmentDisposition disposition)
        {
            var state = await _repository.LoadProjectStateAsync(ProjectRef);
            var command = new DecideAssignmentCommand(
                ProjectRef,
                Principal,
                BootstrapAuthority,
                new AssignmentDispositionInstruction(assignmentRef, revisionRef, disposition),
                null,
                null,
                [],
                []);
            var validated = _evaluator.Evaluate(
                state, command, new AuthorityDecisionRef(Guid.NewGuid()), Now);
            Assert.IsType<AuthorityCommitResult.Committed>(await _repository.TryCommitAsync(validated));
        }

        public async Task CommitUnrelatedActorAsync()
        {
            var state = await _repository.LoadProjectStateAsync(ProjectRef);
            var command = EstablishActor(RoleKind.Reviewer);
            var validated = _evaluator.Evaluate(
                state, command, new AuthorityDecisionRef(Guid.NewGuid()), Now);
            Assert.IsType<AuthorityCommitResult.Committed>(await _repository.TryCommitAsync(validated));
        }

        public Task<B1ProjectState> LoadAsync() => _repository.LoadProjectStateAsync(ProjectRef);

        public async ValueTask DisposeAsync()
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
                    return;
                }
                catch (IOException) when (attempt < 19)
                {
                    await Task.Delay(25);
                }
                catch (UnauthorizedAccessException) when (attempt < 19)
                {
                    await Task.Delay(25);
                }
            }
        }
    }


    private sealed class ConflictInjectingTimeProvider(
        DateTimeOffset utcNow,
        int conflictsToInject,
        Func<Task> injectConflict) : TimeProvider
    {
        private int _readCount;

        public int ReadCount => Volatile.Read(ref _readCount);

        public override DateTimeOffset GetUtcNow()
        {
            var read = Interlocked.Increment(ref _readCount);
            if (read <= conflictsToInject)
            {
                Task.Run(injectConflict).GetAwaiter().GetResult();
            }

            return utcNow.AddTicks(read);
        }
    }

    private sealed class FirstReadBarrierTimeProvider(
        DateTimeOffset utcNow,
        Barrier barrier) : TimeProvider
    {
        private int _readCount;

        public override DateTimeOffset GetUtcNow()
        {
            if (Interlocked.Increment(ref _readCount) == 1)
            {
                barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            }

            return utcNow;
        }
    }
}

internal static class B1AuthorityCommandServiceTestExtensions
{
    public static AcceptedProjectState AcceptedProjectState(this B1ProjectState state) =>
        B1Projector.Build(state).AcceptedProjectState;
}
