using Workbench.App.Continuity;
using Workbench.Core.Continuity;
using Workbench.Project.Opening;
using Workbench.Storage.Continuity;

namespace Workbench.App.ProjectWorld;

public sealed record GuidedHandoffRequest(
    ProjectRef ProjectRef,
    UserPrincipalRef UserPrincipalRef,
    string PrimaryResult,
    IReadOnlyList<string> Validations,
    IReadOnlyList<string> UnresolvedIssues,
    IReadOnlyList<string> ProposedChanges,
    string? ProposedAssignmentRevision,
    IReadOnlyList<string> EvidenceReferences);

public sealed class GuidedHandoffComposerService(
    B1AuthorityRepository authorityRepository,
    GuidedHandoffCommandService guidedHandoffCommands,
    TimeProvider timeProvider)
{
    private readonly B1AuthorityRepository _authorityRepository =
        authorityRepository ?? throw new ArgumentNullException(nameof(authorityRepository));
    private readonly GuidedHandoffCommandService _guidedHandoffCommands =
        guidedHandoffCommands ?? throw new ArgumentNullException(nameof(guidedHandoffCommands));
    private readonly TimeProvider _timeProvider =
        timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public async Task<Handoff> RecordAsync(
        AttemptRef attemptRef,
        AssignmentRef assignmentRef,
        GuidedHandoffRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        var state = await _authorityRepository.LoadProjectStateAsync(request.ProjectRef, cancellationToken);
        var projection = B1Projector.Build(state);
        var attempt = state.Attempts.SingleOrDefault(value => value.AttemptRef == attemptRef)
            ?? throw new B1CommandException(B1FailureCode.InvalidReference, "The selected Attempt does not exist.");
        if (attempt.AssignmentRef != assignmentRef)
            throw new B1CommandException(B1FailureCode.WrongProject, "The Attempt does not belong to the selected Assignment.");
        if (!projection.EffectiveCurrentAttemptRefs.TryGetValue(assignmentRef, out var selected) || selected != attemptRef)
            throw new B1CommandException(B1FailureCode.StaleRoutingSelection, "The Attempt is not the explicitly selected continuation.");

        var assignment = projection.AcceptedProjectState.Assignments.GetValueOrDefault(assignmentRef)
            ?? throw new B1CommandException(B1FailureCode.InvalidReference, "The Assignment is no longer current.");
        var actor = assignment.AssigneeActorRef;
        var now = _timeProvider.GetUtcNow();
        var evidence = request.EvidenceReferences
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => new EvidenceRef(value.Trim()))
            .ToArray();
        var claims = new List<Claim>();
        var result = NewClaim(request.ProjectRef, actor, new ClaimPayload.Result(request.PrimaryResult), evidence, now);
        claims.Add(result);

        var validationClaims = request.Validations
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => NewClaim(request.ProjectRef, actor, new ClaimPayload.Validation(value.Trim()), evidence, now))
            .ToArray();
        claims.AddRange(validationClaims);
        var issueClaims = request.UnresolvedIssues
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => NewClaim(request.ProjectRef, actor, new ClaimPayload.UnresolvedIssue(value.Trim()), evidence, now))
            .ToArray();
        claims.AddRange(issueClaims);
        var contributionClaims = request.ProposedChanges
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => NewClaim(
                request.ProjectRef,
                actor,
                new ClaimPayload.ProposedStateContribution(
                    value.Trim(),
                    new ContributionScopeRef.Assignment(assignmentRef),
                    ProposedSupersedes: null),
                evidence,
                now))
            .ToArray();
        claims.AddRange(contributionClaims);
        var revisionClaims = string.IsNullOrWhiteSpace(request.ProposedAssignmentRevision)
            ? Array.Empty<Claim>()
            : [NewClaim(
                request.ProjectRef,
                actor,
                new ClaimPayload.ProposedAssignmentRevision(
                    assignmentRef,
                    attempt.EffectiveRevisionRef,
                    new AssignmentRevisionContract(request.ProposedAssignmentRevision!.Trim())),
                evidence,
                now)];
        claims.AddRange(revisionClaims);

        var handoff = new Handoff(
            new HandoffRef(Guid.NewGuid()),
            attemptRef,
            result.ClaimRef,
            validationClaims.Select(value => value.ClaimRef).ToArray(),
            issueClaims.Select(value => value.ClaimRef).ToArray(),
            contributionClaims.Select(value => value.ClaimRef).ToArray(),
            revisionClaims.Select(value => value.ClaimRef).ToArray(),
            evidence,
            now);
        projection.EffectiveCurrentHandoffRefs.TryGetValue(attemptRef, out var expectedStored);
        return await _guidedHandoffCommands.RecordAndSelectAsync(
            new RecordGuidedHandoffCommand(request.ProjectRef, request.UserPrincipalRef, handoff, claims),
            expectedStored,
            cancellationToken);
    }

    private static Claim NewClaim(
        ProjectRef projectRef,
        LogicalActorRef actor,
        ClaimPayload payload,
        IReadOnlyList<EvidenceRef> evidence,
        DateTimeOffset createdAt) =>
        new(new ClaimRef(Guid.NewGuid()), projectRef, new ClaimantRef.LogicalActor(actor), null, payload, evidence, createdAt);

    private static void Validate(GuidedHandoffRequest request)
    {
        if (request.ProjectRef.Value == Guid.Empty)
            throw new ArgumentException("A Project reference is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.UserPrincipalRef.Value))
            throw new ArgumentException("A UserPrincipal reference is required.", nameof(request));
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PrimaryResult);
        ArgumentNullException.ThrowIfNull(request.Validations);
        ArgumentNullException.ThrowIfNull(request.UnresolvedIssues);
        ArgumentNullException.ThrowIfNull(request.ProposedChanges);
        ArgumentNullException.ThrowIfNull(request.EvidenceReferences);
    }
}
