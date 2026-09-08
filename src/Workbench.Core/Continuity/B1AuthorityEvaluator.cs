using System.Diagnostics.CodeAnalysis;

namespace Workbench.Core.Continuity;

public sealed class ValidatedAuthorityDecision
{
    internal ValidatedAuthorityDecision(
        long expectedProjectCommitSequence,
        AuthorityDecisionRef decisionRef,
        ProjectRef projectRef,
        DecidingAuthorityRef decidingAuthorityRef,
        IReadOnlyList<ConsideredRef> consideredRefs,
        LogicalActorEstablishmentEffect? logicalActorEstablishmentEffect,
        ResponsibilityEstablishmentEffect? responsibilityEstablishmentEffect,
        AssignmentDispositionEffect? assignmentDispositionEffect,
        RevisionActivationEffect? revisionActivationEffect,
        AssignmentDelegationEffect? assignmentDelegationEffect,
        IReadOnlyList<AcceptedStateContribution> acceptedStateContributions,
        DateTimeOffset createdAt)
    {
        ExpectedProjectCommitSequence = expectedProjectCommitSequence;
        DecisionRef = decisionRef;
        ProjectRef = projectRef;
        DecidingAuthorityRef = decidingAuthorityRef;
        ConsideredRefs = Array.AsReadOnly(consideredRefs.ToArray());
        LogicalActorEstablishmentEffect = logicalActorEstablishmentEffect;
        ResponsibilityEstablishmentEffect = responsibilityEstablishmentEffect;
        AssignmentDispositionEffect = assignmentDispositionEffect;
        RevisionActivationEffect = revisionActivationEffect;
        AssignmentDelegationEffect = assignmentDelegationEffect;
        AcceptedStateContributions = Array.AsReadOnly(acceptedStateContributions.ToArray());
        CreatedAt = createdAt;
    }

    public long ExpectedProjectCommitSequence { get; }
    public AuthorityDecisionRef DecisionRef { get; }
    public ProjectRef ProjectRef { get; }
    public DecidingAuthorityRef DecidingAuthorityRef { get; }
    public IReadOnlyList<ConsideredRef> ConsideredRefs { get; }
    public LogicalActorEstablishmentEffect? LogicalActorEstablishmentEffect { get; }
    public ResponsibilityEstablishmentEffect? ResponsibilityEstablishmentEffect { get; }
    public AssignmentDispositionEffect? AssignmentDispositionEffect { get; }
    public RevisionActivationEffect? RevisionActivationEffect { get; }
    public AssignmentDelegationEffect? AssignmentDelegationEffect { get; }
    public IReadOnlyList<AcceptedStateContribution> AcceptedStateContributions { get; }
    public DateTimeOffset CreatedAt { get; }
}

public sealed class B1AuthorityEvaluator
{
    public ValidatedAuthorityDecision Evaluate(
        B1ProjectState state,
        EstablishLogicalActorCommand command,
        AuthorityDecisionRef id,
        DateTimeOffset createdAt)
    {
        RequireCollections(command.ConsideredRefs, command.Contributions);
        RequireDefined(command.RoleKind);
        var context = Begin(state, command.ProjectRef, command.AuthenticatedOperatorRef,
            command.DecidingAuthorityRef, command.ConsideredRefs);
        RequireCapability(context, B1AuthorityCapability.EstablishLogicalActor, null);
        var actor = NewActor(command.ProjectRef, command.RoleKind, id, createdAt);
        var actorEffect = new LogicalActorEstablishmentEffect(actor);
        var contributions = ResolveContributions(context, command.Contributions, id, actorEffect, null, null);
        return Complete(context, id, command.DecidingAuthorityRef, command.ConsideredRefs,
            actorEffect, null, null, null, null, contributions, createdAt);
    }

    public ValidatedAuthorityDecision Evaluate(
        B1ProjectState state,
        EstablishResponsibilityCommand command,
        AuthorityDecisionRef id,
        DateTimeOffset createdAt)
    {
        RequireCollections(command.ConsideredRefs, command.Contributions);
        ArgumentNullException.ThrowIfNull(command.Contract);
        ValidateEstablishmentDelegationShape(command.InitialDelegation, command.EstablishedAssigneeRoleKind);
        var context = Begin(state, command.ProjectRef, command.AuthenticatedOperatorRef,
            command.DecidingAuthorityRef, command.ConsideredRefs);

        RequireCapability(context, B1AuthorityCapability.EstablishResponsibility, null);
        var responsibility = new Responsibility(
            NewResponsibilityRef(), command.ProjectRef, command.Contract, id, createdAt);
        var responsibilityEffect = new ResponsibilityEstablishmentEffect(responsibility);
        LogicalActorEstablishmentEffect? actorEffect = null;
        AssignmentDelegationEffect? delegationEffect = null;
        if (command.InitialDelegation is not null)
        {
            actorEffect = BuildOptionalActor(context, command.InitialDelegation.AssigneeTarget,
                command.EstablishedAssigneeRoleKind, id, createdAt);
            delegationEffect = BuildDelegation(context, command.InitialDelegation,
                responsibility, actorEffect?.LogicalActor, id);
        }

        var contributions = ResolveContributions(
            context, command.Contributions, id, actorEffect, responsibilityEffect, delegationEffect);
        return Complete(context, id, command.DecidingAuthorityRef, command.ConsideredRefs,
            actorEffect, responsibilityEffect, null, null, delegationEffect, contributions, createdAt);
    }

    public ValidatedAuthorityDecision Evaluate(
        B1ProjectState state,
        DelegateAssignmentCommand command,
        AuthorityDecisionRef id,
        DateTimeOffset createdAt)
    {
        RequireCollections(command.ConsideredRefs, command.Contributions);
        ArgumentNullException.ThrowIfNull(command.Delegation);
        if (command.Delegation.ResponsibilityTarget is not ResponsibilityTarget.Existing)
        {
            Fail(B1FailureCode.InvalidDecisionShape, "DelegateAssignment requires an existing Responsibility.");
        }

        ValidateAssigneeShape(command.Delegation.AssigneeTarget, command.EstablishedAssigneeRoleKind);
        var context = Begin(state, command.ProjectRef, command.AuthenticatedOperatorRef,
            command.DecidingAuthorityRef, command.ConsideredRefs);
        var actorEffect = BuildOptionalActor(context, command.Delegation.AssigneeTarget,
            command.EstablishedAssigneeRoleKind, id, createdAt);
        var delegationEffect = BuildDelegation(context, command.Delegation, null, actorEffect?.LogicalActor, id);
        var contributions = ResolveContributions(
            context, command.Contributions, id, actorEffect, null, delegationEffect);
        return Complete(context, id, command.DecidingAuthorityRef, command.ConsideredRefs,
            actorEffect, null, null, null, delegationEffect, contributions, createdAt);
    }

    public ValidatedAuthorityDecision Evaluate(
        B1ProjectState state,
        DecideAssignmentCommand command,
        AuthorityDecisionRef id,
        DateTimeOffset createdAt)
    {
        RequireCollections(command.ConsideredRefs, command.Contributions);
        ArgumentNullException.ThrowIfNull(command.Disposition);
        ValidateDecideShape(command);
        var context = Begin(state, command.ProjectRef, command.AuthenticatedOperatorRef,
            command.DecidingAuthorityRef, command.ConsideredRefs);
        var disposition = BuildDisposition(context, command.Disposition);
        LogicalActorEstablishmentEffect? actorEffect = null;
        RevisionActivationEffect? activationEffect = null;
        AssignmentDelegationEffect? delegationEffect = null;

        if (command.Activation is not null)
        {
            activationEffect = BuildActivation(context, command.Activation, id, disposition);
        }
        else if (command.Replacement is not null)
        {
            var assignment = GetAssignment(context, command.Replacement.ReplacesAssignmentRef);
            actorEffect = BuildOptionalActor(context, command.Replacement.AssigneeTarget,
                command.Replacement.EstablishedAssigneeRoleKind, id, createdAt);
            var instruction = new AssignmentDelegationInstruction(
                new ResponsibilityTarget.Existing(assignment.ResponsibilityRef),
                command.Replacement.AssigneeTarget,
                command.Replacement.InitialRevisionContract,
                command.Replacement.ReplacesAssignmentRef);
            delegationEffect = BuildDelegation(context, instruction, null, actorEffect?.LogicalActor, id);
        }

        var contributions = ResolveContributions(
            context, command.Contributions, id, actorEffect, null, delegationEffect);
        return Complete(context, id, command.DecidingAuthorityRef, command.ConsideredRefs,
            actorEffect, null, disposition, activationEffect, delegationEffect, contributions, createdAt);
    }

    public ValidatedAuthorityDecision Evaluate(
        B1ProjectState state,
        ActivateAssignmentRevisionCommand command,
        AuthorityDecisionRef id,
        DateTimeOffset createdAt)
    {
        RequireCollections(command.ConsideredRefs, command.Contributions);
        ArgumentNullException.ThrowIfNull(command.Activation);
        var context = Begin(state, command.ProjectRef, command.AuthenticatedOperatorRef,
            command.DecidingAuthorityRef, command.ConsideredRefs);
        var activation = BuildActivation(context, command.Activation, id, null);
        var contributions = ResolveContributions(context, command.Contributions, id, null, null, null);
        return Complete(context, id, command.DecidingAuthorityRef, command.ConsideredRefs,
            null, null, null, activation, null, contributions, createdAt);
    }

    public ValidatedAuthorityDecision Evaluate(
        B1ProjectState state,
        AuthorAcceptedStateCommand command,
        AuthorityDecisionRef id,
        DateTimeOffset createdAt)
    {
        RequireCollections(command.ConsideredRefs, command.Contributions);
        if (command.Contributions.Count == 0)
        {
            Fail(B1FailureCode.InvalidDecisionShape, "AuthorAcceptedState requires at least one contribution.");
        }

        var context = Begin(state, command.ProjectRef, command.AuthenticatedOperatorRef,
            command.DecidingAuthorityRef, command.ConsideredRefs);
        var contributions = ResolveContributions(context, command.Contributions, id, null, null, null);
        return Complete(context, id, command.DecidingAuthorityRef, command.ConsideredRefs,
            null, null, null, null, null, contributions, createdAt);
    }

    private static EvaluationContext Begin(
        B1ProjectState state,
        ProjectRef projectRef,
        UserPrincipalRef operatorRef,
        DecidingAuthorityRef authorityRef,
        IReadOnlyList<ConsideredRef> consideredRefs)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(authorityRef);
        if (projectRef != state.Governance.ProjectRef)
        {
            Fail(B1FailureCode.WrongProject, "The command Project does not match the governed Project.");
        }

        if (operatorRef != state.Governance.BootstrapPrincipalRef)
        {
            Fail(B1FailureCode.NotAuthorized, "Only the bootstrap principal may operate B1 authority commands.");
        }

        LogicalActorRef? decidingActor = authorityRef switch
        {
            DecidingAuthorityRef.UserPrincipal user when user.UserPrincipalRef == state.Governance.BootstrapPrincipalRef => null,
            DecidingAuthorityRef.UserPrincipal => throw Failure(
                B1FailureCode.NotAuthorized, "Only the bootstrap principal is a UserPrincipal authority."),
            DecidingAuthorityRef.LogicalActor actor => ResolveDecidingActor(state, actor.LogicalActorRef),
            _ => throw Failure(B1FailureCode.InvalidDecisionShape, "Unknown deciding authority kind.")
        };

        ValidateConsideredRefs(state, consideredRefs);
        var projection = B1Projector.Build(state);
        return new(state, projection, decidingActor);
    }

    private static LogicalActorRef ResolveDecidingActor(B1ProjectState state, LogicalActorRef actorRef)
    {
        var actor = state.LogicalActors.SingleOrDefault(candidate => candidate.LogicalActorRef == actorRef);
        if (actor is null)
        {
            Fail(B1FailureCode.InvalidReference, "The deciding LogicalActor does not exist.");
        }

        if (actor.ProjectRef != state.Governance.ProjectRef)
        {
            Fail(B1FailureCode.WrongProject, "The deciding LogicalActor belongs to another Project.");
        }

        return actorRef;
    }

    private static void ValidateConsideredRefs(B1ProjectState state, IReadOnlyList<ConsideredRef> consideredRefs)
    {
        foreach (var considered in consideredRefs)
        {
            switch (considered)
            {
                case ConsideredRef.Claim claim:
                    GetClaim(state, claim.ClaimRef);
                    break;
                case ConsideredRef.Handoff handoff:
                    var item = state.Handoffs.SingleOrDefault(candidate => candidate.HandoffRef == handoff.HandoffRef);
                    if (item is null)
                    {
                        Fail(B1FailureCode.InvalidReference, "A considered Handoff does not exist.");
                    }
                    GetAttemptAssignment(state, item.AttemptRef);
                    break;
                case ConsideredRef.Evidence:
                case ConsideredRef.EvolutionCandidate:
                    break;
                default:
                    Fail(B1FailureCode.InvalidDecisionShape, "Unknown considered-reference kind.");
                    break;
            }
        }
    }

    private static LogicalActorEstablishmentEffect? BuildOptionalActor(
        EvaluationContext context,
        AssignmentAssigneeTarget assigneeTarget,
        RoleKind? roleKind,
        AuthorityDecisionRef decisionRef,
        DateTimeOffset createdAt)
    {
        if (assigneeTarget is not AssignmentAssigneeTarget.EstablishedByThisDecision)
        {
            return null;
        }

        RequireCapability(context, B1AuthorityCapability.EstablishLogicalActor, null);
        return new(NewActor(context.State.Governance.ProjectRef, roleKind!.Value, decisionRef, createdAt));
    }

    private static AssignmentDispositionEffect BuildDisposition(
        EvaluationContext context,
        AssignmentDispositionInstruction instruction)
    {
        if (!Enum.IsDefined(instruction.Disposition))
        {
            Fail(B1FailureCode.InvalidDecisionShape, "Unknown Assignment disposition.");
        }

        var assignment = GetAssignment(context, instruction.AssignmentRef);
        var currentRevision = GetCurrentRevision(context, assignment.AssignmentRef);
        if (instruction.EffectiveRevisionRef != currentRevision.RevisionRef)
        {
            Fail(B1FailureCode.StaleRevision, "The Assignment disposition targets a stale Revision.");
        }

        if (context.Projection.AcceptedProjectState.RevisionDispositions.ContainsKey(currentRevision.RevisionRef))
        {
            Fail(B1FailureCode.AlreadyDispositioned, "The current Revision already has a disposition.");
        }

        RequireCapability(context, B1AuthorityCapability.DecideAssignmentDisposition, assignment.ResponsibilityRef);
        return new(assignment.AssignmentRef, currentRevision.RevisionRef, instruction.Disposition);
    }

    private static RevisionActivationEffect BuildActivation(
        EvaluationContext context,
        RevisionActivationInstruction instruction,
        AuthorityDecisionRef decisionRef,
        AssignmentDispositionEffect? sameDecisionDisposition)
    {
        ArgumentNullException.ThrowIfNull(instruction.NewRevisionContract);
        var assignment = GetAssignment(context, instruction.AssignmentRef);
        var currentRevision = GetCurrentRevision(context, assignment.AssignmentRef);
        if (instruction.ExpectedCurrentRevisionRef != currentRevision.RevisionRef)
        {
            Fail(B1FailureCode.StaleRevision, "Revision activation targets a stale Revision.");
        }

        var hasPriorDisposition = context.Projection.AcceptedProjectState.RevisionDispositions.TryGetValue(
            currentRevision.RevisionRef, out var priorDisposition);
        if (sameDecisionDisposition is not null)
        {
            if (hasPriorDisposition || sameDecisionDisposition.Disposition != AssignmentDisposition.RevisionRequired)
            {
                Fail(B1FailureCode.InvalidDecisionShape, "Compound activation requires a newly RevisionRequired current Revision.");
            }
        }
        else if (hasPriorDisposition && priorDisposition!.Disposition != AssignmentDisposition.RevisionRequired)
        {
            Fail(B1FailureCode.AlreadyDispositioned, "Accepted or Rejected Revisions cannot be activated.");
        }

        ValidateRevisionSourceClaim(context.State, instruction.SourceClaimRef, assignment.AssignmentRef);
        RequireCapability(context, B1AuthorityCapability.ActivateAssignmentRevision, assignment.ResponsibilityRef);
        ValidateRevisionBoundary(context, assignment.ResponsibilityRef, currentRevision.Contract, instruction.NewRevisionContract);
        var revision = new AssignmentRevision(
            NewRevisionRef(), assignment.AssignmentRef, currentRevision.RevisionRef,
            instruction.NewRevisionContract, decisionRef);
        return new(revision, instruction.SourceClaimRef);
    }

    private static AssignmentDelegationEffect BuildDelegation(
        EvaluationContext context,
        AssignmentDelegationInstruction instruction,
        Responsibility? establishedResponsibility,
        LogicalActor? establishedActor,
        AuthorityDecisionRef decisionRef)
    {
        ArgumentNullException.ThrowIfNull(instruction.InitialRevisionContract);
        var responsibility = instruction.ResponsibilityTarget switch
        {
            ResponsibilityTarget.Existing existing => GetResponsibility(context, existing.ResponsibilityRef),
            ResponsibilityTarget.EstablishedByThisDecision when establishedResponsibility is not null => establishedResponsibility,
            _ => throw Failure(B1FailureCode.InvalidDecisionShape, "The prospective Responsibility target is unavailable.")
        };
        var assignee = instruction.AssigneeTarget switch
        {
            AssignmentAssigneeTarget.Existing existing => GetActor(context, existing.LogicalActorRef),
            AssignmentAssigneeTarget.EstablishedByThisDecision when establishedActor is not null => establishedActor,
            _ => throw Failure(B1FailureCode.InvalidDecisionShape, "The prospective assignee target is unavailable.")
        };

        if (instruction.ReplacesAssignmentRef is { } replacementRef)
        {
            var replacement = GetAssignment(context, replacementRef);
            if (replacement.ResponsibilityRef != responsibility.ResponsibilityRef)
            {
                Fail(B1FailureCode.InvalidDecisionShape, "A replacement must target the same Responsibility.");
            }
            if (!context.Projection.AcceptedProjectState.CurrentDelegationAssignments.Contains(replacementRef))
            {
                Fail(B1FailureCode.StaleReplacement, "The replacement target is no longer current.");
            }
        }

        RequireCapability(context, B1AuthorityCapability.DelegateAssignment, responsibility.ResponsibilityRef);
        ValidateDelegationBoundary(context, responsibility, instruction.InitialRevisionContract);
        var assignmentRef = NewAssignmentRef();
        var revisionRef = NewRevisionRef();
        var assignment = new Assignment(
            assignmentRef, responsibility.ResponsibilityRef, assignee.LogicalActorRef, revisionRef, decisionRef);
        var revision = new AssignmentRevision(
            revisionRef, assignmentRef, null, instruction.InitialRevisionContract, decisionRef);
        return new(assignment, revision, instruction.ReplacesAssignmentRef);
    }

    private static IReadOnlyList<AcceptedStateContribution> ResolveContributions(
        EvaluationContext context,
        IReadOnlyList<AcceptedContributionInstruction> instructions,
        AuthorityDecisionRef decisionRef,
        LogicalActorEstablishmentEffect? actorEffect,
        ResponsibilityEstablishmentEffect? responsibilityEffect,
        AssignmentDelegationEffect? delegationEffect)
    {
        var current = context.Projection.AcceptedProjectState.CurrentContributions
            .ToDictionary(contribution => contribution.ContributionRef);
        var resolved = new List<AcceptedStateContribution>(instructions.Count);
        var supersessionTargets = new HashSet<AcceptedStateContributionRef>();
        foreach (var instruction in instructions)
        {
            if (instruction is null || string.IsNullOrWhiteSpace(instruction.Statement) || instruction.Scope is null)
            {
                Fail(B1FailureCode.InvalidDecisionShape, "A contribution requires a statement and explicit scope.");
            }

            var scope = ResolveContributionScope(
                context, instruction.Scope, responsibilityEffect, delegationEffect);
            RequireContributionCapability(context, scope, delegationEffect);
            ValidateContributionSource(context.State, instruction.SourceClaimRef);
            if (instruction.SupersedesContributionRef is { } supersedes)
            {
                if (!current.TryGetValue(supersedes, out var target) || !ScopesEqual(scope, target.Scope))
                {
                    Fail(B1FailureCode.StaleSupersession, "The supersession target is not current at the exact scope.");
                }
                if (!supersessionTargets.Add(supersedes))
                {
                    Fail(B1FailureCode.InvalidDecisionShape, "A Decision cannot supersede the same current contribution more than once.");
                }
                if ((scope is ContributionScopeRef.Responsibility responsibilityScope &&
                     responsibilityEffect?.Responsibility.ResponsibilityRef == responsibilityScope.ResponsibilityRef) ||
                    (scope is ContributionScopeRef.Assignment assignmentScope &&
                     delegationEffect?.Assignment.AssignmentRef == assignmentScope.AssignmentRef))
                {
                    Fail(B1FailureCode.StaleSupersession, "A prospective scope cannot supersede current state.");
                }
            }

            resolved.Add(new(
                NewContributionRef(), instruction.Statement, scope, instruction.SupersedesContributionRef,
                decisionRef, instruction.SourceClaimRef));
        }

        return Array.AsReadOnly(resolved.ToArray());
    }

    private static ContributionScopeRef ResolveContributionScope(
        EvaluationContext context,
        ContributionScopeTarget target,
        ResponsibilityEstablishmentEffect? responsibilityEffect,
        AssignmentDelegationEffect? delegationEffect) => target switch
        {
            ContributionScopeTarget.Project project when project.ProjectRef == context.State.Governance.ProjectRef =>
                new ContributionScopeRef.Project(project.ProjectRef),
            ContributionScopeTarget.Project => throw Failure(B1FailureCode.WrongProject, "Contribution Project scope is wrong."),
            ContributionScopeTarget.Responsibility.Existing existing =>
                new ContributionScopeRef.Responsibility(GetResponsibility(context, existing.ResponsibilityRef).ResponsibilityRef),
            ContributionScopeTarget.Responsibility.EstablishedByThisDecision when responsibilityEffect is not null =>
                new ContributionScopeRef.Responsibility(responsibilityEffect.Responsibility.ResponsibilityRef),
            ContributionScopeTarget.Assignment.Existing existing =>
                new ContributionScopeRef.Assignment(GetAssignment(context, existing.AssignmentRef).AssignmentRef),
            ContributionScopeTarget.Assignment.DelegatedByThisDecision when delegationEffect is not null =>
                new ContributionScopeRef.Assignment(delegationEffect.Assignment.AssignmentRef),
            _ => throw Failure(B1FailureCode.InvalidDecisionShape, "The prospective contribution scope is unavailable.")
        };

    private static void RequireContributionCapability(
        EvaluationContext context,
        ContributionScopeRef scope,
        AssignmentDelegationEffect? delegationEffect)
    {
        switch (scope)
        {
            case ContributionScopeRef.Project:
                RequireCapability(context, B1AuthorityCapability.AcceptProjectStateContribution, null);
                break;
            case ContributionScopeRef.Responsibility responsibility:
                RequireCapability(context, B1AuthorityCapability.AcceptResponsibilityStateContribution,
                    responsibility.ResponsibilityRef);
                break;
            case ContributionScopeRef.Assignment assignmentScope:
                var assignment = context.Projection.AcceptedProjectState.Assignments.TryGetValue(
                    assignmentScope.AssignmentRef, out var existing)
                    ? existing
                    : delegationEffect?.Assignment.AssignmentRef == assignmentScope.AssignmentRef
                        ? delegationEffect.Assignment
                        : throw Failure(B1FailureCode.InvalidReference, "The Assignment does not exist.");
                RequireCapability(context, B1AuthorityCapability.AcceptAssignmentStateContribution,
                    assignment.ResponsibilityRef);
                break;
            default:
                Fail(B1FailureCode.InvalidDecisionShape, "Unknown contribution scope.");
                break;
        }
    }

    private static void ValidateDelegationBoundary(
        EvaluationContext context,
        Responsibility responsibility,
        AssignmentRevisionContract revisionContract)
    {
        if (!revisionContract.DelegatedAuthorityBoundary.IsSubsetOf(
                responsibility.Contract.MaximumDelegableAuthorityBoundary))
        {
            Fail(B1FailureCode.AuthorityAmplification, "Delegated authority exceeds the Responsibility maximum.");
        }

        if (context.DecidingActorRef is null)
        {
            return;
        }

        foreach (var capability in revisionContract.DelegatedAuthorityBoundary.Capabilities)
        {
            if (!CanExercise(context, capability, responsibility.ResponsibilityRef))
            {
                Fail(B1FailureCode.AuthorityAmplification, "A LogicalActor cannot delegate authority it cannot exercise.");
            }
        }
    }

    private static void ValidateRevisionBoundary(
        EvaluationContext context,
        ResponsibilityRef responsibilityRef,
        AssignmentRevisionContract prior,
        AssignmentRevisionContract next)
    {
        var responsibility = GetResponsibility(context, responsibilityRef);
        if (!next.DelegatedAuthorityBoundary.IsSubsetOf(responsibility.Contract.MaximumDelegableAuthorityBoundary))
        {
            Fail(B1FailureCode.AuthorityAmplification, "Revision authority exceeds the Responsibility maximum.");
        }

        if (context.DecidingActorRef is null)
        {
            return;
        }

        var added = next.DelegatedAuthorityBoundary.Except(prior.DelegatedAuthorityBoundary);
        foreach (var capability in added.Capabilities)
        {
            if (!CanExercise(context, capability, responsibilityRef))
            {
                Fail(B1FailureCode.AuthorityAmplification, "A LogicalActor cannot add authority it cannot exercise.");
            }
        }
    }

    private static void RequireCapability(
        EvaluationContext context,
        B1AuthorityCapability capability,
        ResponsibilityRef? targetResponsibility)
    {
        if (context.DecidingActorRef is null)
        {
            return;
        }

        if (!CanExercise(context, capability, targetResponsibility))
        {
            Fail(B1FailureCode.NotAuthorized, "The deciding LogicalActor lacks capability at the target locality.");
        }
    }

    private static bool CanExercise(
        EvaluationContext context,
        B1AuthorityCapability capability,
        ResponsibilityRef? targetResponsibility)
    {
        if (context.DecidingActorRef is null)
        {
            return true;
        }

        var isProjectCapability = IsProjectCapability(capability);
        foreach (var assignment in context.State.Assignments.Where(candidate =>
                     candidate.AssigneeActorRef == context.DecidingActorRef.Value &&
                     context.Projection.EffectiveFulfillmentAssignments.Contains(candidate.AssignmentRef)))
        {
            GetResponsibility(context, assignment.ResponsibilityRef);
            if (!isProjectCapability && assignment.ResponsibilityRef != targetResponsibility)
            {
                continue;
            }

            var revision = GetCurrentRevision(context, assignment.AssignmentRef);
            if (revision.Contract.DelegatedAuthorityBoundary.Contains(capability))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsProjectCapability(B1AuthorityCapability capability) => capability is
        B1AuthorityCapability.EstablishLogicalActor or
        B1AuthorityCapability.EstablishResponsibility or
        B1AuthorityCapability.AcceptProjectStateContribution;

    private static Assignment GetAssignment(EvaluationContext context, AssignmentRef assignmentRef)
    {
        if (!context.Projection.AcceptedProjectState.Assignments.TryGetValue(assignmentRef, out var assignment))
        {
            Fail(B1FailureCode.InvalidReference, "The Assignment does not exist.");
        }
        GetResponsibility(context, assignment.ResponsibilityRef);
        return assignment;
    }

    private static AssignmentRevision GetCurrentRevision(EvaluationContext context, AssignmentRef assignmentRef)
    {
        if (!context.Projection.AcceptedProjectState.CurrentEffectiveRevisionRefs.TryGetValue(
                assignmentRef, out var revisionRef))
        {
            Fail(B1FailureCode.InvalidReference, "The Assignment has no current Revision.");
        }
        if (!context.Projection.AcceptedProjectState.Revisions.TryGetValue(revisionRef, out var revision))
        {
            Fail(B1FailureCode.InvalidReference, "The current Revision does not exist.");
        }
        return revision;
    }

    private static Responsibility GetResponsibility(EvaluationContext context, ResponsibilityRef responsibilityRef)
    {
        if (!context.Projection.AcceptedProjectState.Responsibilities.TryGetValue(
                responsibilityRef, out var responsibility))
        {
            Fail(B1FailureCode.InvalidReference, "The Responsibility does not exist.");
        }
        if (responsibility.ProjectRef != context.State.Governance.ProjectRef)
        {
            Fail(B1FailureCode.WrongProject, "The Responsibility belongs to another Project.");
        }
        return responsibility;
    }

    private static LogicalActor GetActor(EvaluationContext context, LogicalActorRef actorRef)
    {
        if (!context.Projection.AcceptedProjectState.LogicalActors.TryGetValue(actorRef, out var actor))
        {
            Fail(B1FailureCode.InvalidReference, "The LogicalActor does not exist.");
        }
        if (actor.ProjectRef != context.State.Governance.ProjectRef)
        {
            Fail(B1FailureCode.WrongProject, "The LogicalActor belongs to another Project.");
        }
        return actor;
    }

    private static Assignment GetAttemptAssignment(B1ProjectState state, AttemptRef attemptRef)
    {
        var attempt = state.Attempts.SingleOrDefault(candidate => candidate.AttemptRef == attemptRef);
        if (attempt is null)
        {
            Fail(B1FailureCode.InvalidReference, "A referenced Attempt does not exist.");
        }
        var assignment = state.Assignments.SingleOrDefault(candidate => candidate.AssignmentRef == attempt.AssignmentRef);
        if (assignment is null)
        {
            Fail(B1FailureCode.InvalidReference, "The Attempt Assignment does not exist.");
        }
        var responsibility = state.Responsibilities.SingleOrDefault(
            candidate => candidate.ResponsibilityRef == assignment.ResponsibilityRef);
        if (responsibility is null)
        {
            Fail(B1FailureCode.InvalidReference, "The Attempt Assignment Responsibility does not exist.");
        }
        if (responsibility.ProjectRef != state.Governance.ProjectRef)
        {
            Fail(B1FailureCode.WrongProject, "The considered Handoff belongs to another Project.");
        }
        return assignment;
    }

    private static Claim GetClaim(B1ProjectState state, ClaimRef claimRef)
    {
        var claim = state.Claims.SingleOrDefault(candidate => candidate.ClaimRef == claimRef);
        if (claim is null)
        {
            Fail(B1FailureCode.InvalidReference, "A referenced Claim does not exist.");
        }
        if (claim.ProjectRef != state.Governance.ProjectRef)
        {
            Fail(B1FailureCode.WrongProject, "The Claim belongs to another Project.");
        }
        return claim;
    }

    private static void ValidateRevisionSourceClaim(
        B1ProjectState state,
        ClaimRef? sourceClaimRef,
        AssignmentRef assignmentRef)
    {
        if (sourceClaimRef is not { } claimRef)
        {
            return;
        }
        var claim = GetClaim(state, claimRef);
        if (claim.Payload is not ClaimPayload.ProposedAssignmentRevision proposal ||
            proposal.AssignmentRef != assignmentRef)
        {
            Fail(B1FailureCode.InvalidReference, "Revision source must be a proposal for the same Assignment.");
        }
    }

    private static void ValidateContributionSource(B1ProjectState state, ClaimRef? sourceClaimRef)
    {
        if (sourceClaimRef is not { } claimRef)
        {
            return;
        }
        var claim = GetClaim(state, claimRef);
        if (claim.Payload is not ClaimPayload.ProposedStateContribution)
        {
            Fail(B1FailureCode.InvalidReference, "Contribution source must be a proposed state contribution.");
        }
    }

    private static bool ScopesEqual(ContributionScopeRef left, ContributionScopeRef right) =>
        (left, right) switch
        {
            (ContributionScopeRef.Project a, ContributionScopeRef.Project b) => a.ProjectRef == b.ProjectRef,
            (ContributionScopeRef.Responsibility a, ContributionScopeRef.Responsibility b) =>
                a.ResponsibilityRef == b.ResponsibilityRef,
            (ContributionScopeRef.Assignment a, ContributionScopeRef.Assignment b) =>
                a.AssignmentRef == b.AssignmentRef,
            _ => false
        };

    private static void ValidateEstablishmentDelegationShape(
        AssignmentDelegationInstruction? delegation,
        RoleKind? establishedRoleKind)
    {
        if (delegation is null)
        {
            if (establishedRoleKind is not null)
            {
                Fail(B1FailureCode.InvalidDecisionShape, "Actor establishment requires initial delegation.");
            }
            return;
        }

        if (delegation.ResponsibilityTarget is not ResponsibilityTarget.EstablishedByThisDecision ||
            delegation.ReplacesAssignmentRef is not null)
        {
            Fail(B1FailureCode.InvalidDecisionShape, "Initial delegation must target the new Responsibility and replace nothing.");
        }
        ValidateAssigneeShape(delegation.AssigneeTarget, establishedRoleKind);
    }

    private static void ValidateAssigneeShape(AssignmentAssigneeTarget target, RoleKind? establishedRoleKind)
    {
        ArgumentNullException.ThrowIfNull(target);
        if ((target is AssignmentAssigneeTarget.EstablishedByThisDecision) != establishedRoleKind.HasValue)
        {
            Fail(B1FailureCode.InvalidDecisionShape, "Prospective assignee and Actor role must appear together.");
        }
        if (establishedRoleKind is { } roleKind)
        {
            RequireDefined(roleKind);
        }
    }

    private static void ValidateDecideShape(DecideAssignmentCommand command)
    {
        if (command.Activation is not null && command.Replacement is not null)
        {
            Fail(B1FailureCode.InvalidDecisionShape, "Disposition cannot activate and replace together.");
        }
        if (command.Activation is not null &&
            (command.Disposition.Disposition != AssignmentDisposition.RevisionRequired ||
             command.Activation.AssignmentRef != command.Disposition.AssignmentRef ||
             command.Activation.ExpectedCurrentRevisionRef != command.Disposition.EffectiveRevisionRef))
        {
            Fail(B1FailureCode.InvalidDecisionShape, "Activation requires RevisionRequired for the same Assignment and Revision.");
        }
        if (command.Replacement is not null)
        {
            if (command.Disposition.Disposition is not (AssignmentDisposition.Rejected or AssignmentDisposition.RevisionRequired) ||
                command.Replacement.ReplacesAssignmentRef != command.Disposition.AssignmentRef)
            {
                Fail(B1FailureCode.InvalidDecisionShape, "Replacement requires Rejected or RevisionRequired for the same Assignment.");
            }
            ValidateAssigneeShape(command.Replacement.AssigneeTarget, command.Replacement.EstablishedAssigneeRoleKind);
        }
    }

    private static ValidatedAuthorityDecision Complete(
        EvaluationContext context,
        AuthorityDecisionRef decisionRef,
        DecidingAuthorityRef authorityRef,
        IReadOnlyList<ConsideredRef> consideredRefs,
        LogicalActorEstablishmentEffect? actor,
        ResponsibilityEstablishmentEffect? responsibility,
        AssignmentDispositionEffect? disposition,
        RevisionActivationEffect? activation,
        AssignmentDelegationEffect? delegation,
        IReadOnlyList<AcceptedStateContribution> contributions,
        DateTimeOffset createdAt)
    {
        if (decisionRef.Value == Guid.Empty)
        {
            Fail(B1FailureCode.InvalidReference, "Decision reference is empty.");
        }
        if (actor is null && responsibility is null && disposition is null && activation is null &&
            delegation is null && contributions.Count == 0)
        {
            Fail(B1FailureCode.InvalidDecisionShape, "An AuthorityDecision requires at least one effect.");
        }
        return new(
            context.State.Governance.LastProjectCommitSequence, decisionRef,
            context.State.Governance.ProjectRef, authorityRef, consideredRefs,
            actor, responsibility, disposition, activation, delegation, contributions, createdAt);
    }

    private static void RequireCollections(
        IReadOnlyList<ConsideredRef> consideredRefs,
        IReadOnlyList<AcceptedContributionInstruction> contributions)
    {
        if (consideredRefs is null || contributions is null || consideredRefs.Any(item => item is null))
        {
            Fail(B1FailureCode.InvalidDecisionShape, "Command collections cannot be null.");
        }
    }

    private static void RequireDefined(RoleKind roleKind)
    {
        if (!Enum.IsDefined(roleKind))
        {
            Fail(B1FailureCode.InvalidDecisionShape, "Unknown LogicalActor role.");
        }
    }

    private static LogicalActor NewActor(
        ProjectRef projectRef,
        RoleKind roleKind,
        AuthorityDecisionRef decisionRef,
        DateTimeOffset createdAt) =>
        new(new LogicalActorRef(Guid.NewGuid()), projectRef, roleKind, decisionRef, createdAt);

    private static ResponsibilityRef NewResponsibilityRef() => new(Guid.NewGuid());
    private static AssignmentRef NewAssignmentRef() => new(Guid.NewGuid());
    private static RevisionRef NewRevisionRef() => new(Guid.NewGuid());
    private static AcceptedStateContributionRef NewContributionRef() => new(Guid.NewGuid());

    private static B1CommandException Failure(B1FailureCode code, string message) => new(code, message);
    [DoesNotReturn]
    private static void Fail(B1FailureCode code, string message) => throw Failure(code, message);

    private sealed record EvaluationContext(
        B1ProjectState State,
        B1ProjectProjection Projection,
        LogicalActorRef? DecidingActorRef);
}
