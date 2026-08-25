using Workbench.Core.Continuity;
using Workbench.Storage.Continuity;

namespace Workbench.App.Continuity;

public enum ContributionDecisionMode
{
    Ignore,
    AdoptVerbatim,
    EditAndEstablish
}

public sealed record GuidedDecisionRequest(
    ProjectRef ProjectRef,
    UserPrincipalRef UserPrincipalRef,
    HandoffRef HandoffRef,
    AssignmentDisposition Disposition,
    ContributionDecisionMode ContributionMode,
    string? EditedContributionStatement,
    string? NewRevisionContract);

public sealed record GuidedDecisionPreview(
    AssignmentDisposition Disposition,
    string SubmittedAs,
    string DecidingAs,
    IReadOnlyList<string> Effects,
    string ContributionSummary);

public sealed class GuidedDecisionService(
    B1AuthorityRepository authorityRepository,
    B1AuthorityEvaluator evaluator,
    B1AuthorityCommandService authorityCommands,
    TimeProvider timeProvider)
{
    private readonly B1AuthorityRepository _authorityRepository =
        authorityRepository ?? throw new ArgumentNullException(nameof(authorityRepository));
    private readonly B1AuthorityEvaluator _evaluator =
        evaluator ?? throw new ArgumentNullException(nameof(evaluator));
    private readonly B1AuthorityCommandService _authorityCommands =
        authorityCommands ?? throw new ArgumentNullException(nameof(authorityCommands));
    private readonly TimeProvider _timeProvider =
        timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public async Task<GuidedDecisionPreview> PreviewAsync(
        GuidedDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        var state = await _authorityRepository.LoadProjectStateAsync(request.ProjectRef, cancellationToken);
        var command = BuildCommand(state, request);
        var validated = _evaluator.Evaluate(
            state,
            command,
            new AuthorityDecisionRef(Guid.NewGuid()),
            _timeProvider.GetUtcNow());
        return BuildPreview(state, request, validated);
    }

    public async Task<AuthorityDecision> CommitAsync(
        GuidedDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        var state = await _authorityRepository.LoadProjectStateAsync(request.ProjectRef, cancellationToken);
        var command = BuildCommand(state, request);
        return await _authorityCommands.DecideAssignmentAsync(command, cancellationToken);
    }

    private static DecideAssignmentCommand BuildCommand(
        B1ProjectState state,
        GuidedDecisionRequest request)
    {
        if (request.UserPrincipalRef != state.Governance.BootstrapPrincipalRef)
            throw new B1CommandException(B1FailureCode.NotAuthorized, "Only the Project bootstrap UserPrincipal may decide.");
        var handoff = state.Handoffs.SingleOrDefault(value => value.HandoffRef == request.HandoffRef)
            ?? throw new B1CommandException(B1FailureCode.InvalidReference, "The Handoff does not belong to this Project.");
        var attempt = state.Attempts.SingleOrDefault(value => value.AttemptRef == handoff.AttemptRef)
            ?? throw new B1CommandException(B1FailureCode.InvalidReference, "The Handoff Attempt does not exist.");
        var projection = B1Projector.Build(state);
        var assignment = projection.AcceptedProjectState.Assignments.GetValueOrDefault(attempt.AssignmentRef)
            ?? throw new B1CommandException(B1FailureCode.InvalidReference, "The Assignment is no longer current.");
        var currentRevision = projection.AcceptedProjectState.CurrentEffectiveRevisionRefs[assignment.AssignmentRef];
        if (currentRevision != attempt.EffectiveRevisionRef)
            throw new B1CommandException(B1FailureCode.StaleRevision, "The Handoff targets a stale Assignment Revision.");

        var claims = state.Claims.ToDictionary(value => value.ClaimRef);
        var considered = new List<ConsideredRef> { new ConsideredRef.Handoff(handoff.HandoffRef) };
        foreach (var claimRef in AllClaimRefs(handoff))
            considered.Add(new ConsideredRef.Claim(claimRef));

        var contributions = new List<AcceptedContributionInstruction>();
        var proposedClaims = handoff.ProposedContributionClaimRefs
            .Select(value => claims[value])
            .ToArray();
        if (request.ContributionMode != ContributionDecisionMode.Ignore)
        {
            if (!string.IsNullOrWhiteSpace(request.EditedContributionStatement) && proposedClaims.Length > 1)
                throw new B1CommandException(B1FailureCode.InvalidDecisionShape, "Edit mode currently requires one proposed contribution at a time.");
            foreach (var claim in proposedClaims)
            {
                if (claim.Payload is not ClaimPayload.ProposedStateContribution proposal)
                    throw new B1CommandException(B1FailureCode.InvalidReference, "The Handoff contribution Claim is malformed.");
                var statement = request.ContributionMode == ContributionDecisionMode.EditAndEstablish
                    ? request.EditedContributionStatement
                    : proposal.Statement;
                if (string.IsNullOrWhiteSpace(statement))
                    throw new B1CommandException(B1FailureCode.InvalidDecisionShape, "Edit mode requires an authority-owned contribution statement.");
                contributions.Add(new(
                    statement.Trim(),
                    ToTarget(proposal.Scope),
                    proposal.ProposedSupersedes,
                    claim.ClaimRef));
            }
        }

        RevisionActivationInstruction? activation = null;
        if (request.Disposition == AssignmentDisposition.RevisionRequired &&
            !string.IsNullOrWhiteSpace(request.NewRevisionContract))
        {
            var revisionClaim = handoff.ProposedAssignmentRevisionClaimRefs
                .Select(value => claims[value])
                .FirstOrDefault(value => value.Payload is ClaimPayload.ProposedAssignmentRevision);
            activation = new RevisionActivationInstruction(
                assignment.AssignmentRef,
                currentRevision,
                new AssignmentRevisionContract(request.NewRevisionContract!.Trim()),
                revisionClaim?.ClaimRef);
        }

        return new DecideAssignmentCommand(
            request.ProjectRef,
            request.UserPrincipalRef,
            new DecidingAuthorityRef.UserPrincipal(request.UserPrincipalRef),
            new AssignmentDispositionInstruction(assignment.AssignmentRef, currentRevision, request.Disposition),
            activation,
            null,
            considered,
            contributions);
    }

    private static GuidedDecisionPreview BuildPreview(
        B1ProjectState state,
        GuidedDecisionRequest request,
        ValidatedAuthorityDecision validated)
    {
        var effects = new List<string>();
        if (validated.AssignmentDispositionEffect is { } disposition)
            effects.Add($"Assignment disposition: {disposition.Disposition}");
        if (validated.RevisionActivationEffect is not null)
            effects.Add("Create a successor Assignment Revision");
        if (validated.AcceptedStateContributions.Count > 0)
            effects.Add($"Establish {validated.AcceptedStateContributions.Count} Accepted Project contribution(s)");
        return new(
            request.Disposition,
            $"LogicalActor via Handoff {request.HandoffRef}",
            $"UserPrincipal {request.UserPrincipalRef}",
            effects,
            validated.AcceptedStateContributions.Count == 0
                ? "No Proposed State Contribution will enter Accepted State."
                : string.Join("; ", validated.AcceptedStateContributions.Select(value => value.Statement)));
    }

    private static IEnumerable<ClaimRef> AllClaimRefs(Handoff handoff)
    {
        yield return handoff.ResultClaimRef;
        foreach (var value in handoff.ValidationClaimRefs) yield return value;
        foreach (var value in handoff.UnresolvedIssueClaimRefs) yield return value;
        foreach (var value in handoff.ProposedContributionClaimRefs) yield return value;
        foreach (var value in handoff.ProposedAssignmentRevisionClaimRefs) yield return value;
    }

    private static ContributionScopeTarget ToTarget(ContributionScopeRef scope) => scope switch
    {
        ContributionScopeRef.Project project => new ContributionScopeTarget.Project(project.ProjectRef),
        ContributionScopeRef.Responsibility responsibility => new ContributionScopeTarget.Responsibility.Existing(responsibility.ResponsibilityRef),
        ContributionScopeRef.Assignment assignment => new ContributionScopeTarget.Assignment.Existing(assignment.AssignmentRef),
        _ => throw new B1CommandException(B1FailureCode.InvalidDecisionShape, "Unknown contribution scope.")
    };
}
