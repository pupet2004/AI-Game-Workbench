using Workbench.App.Continuity;
using Workbench.Core.Continuity;
using Workbench.Storage.Continuity;

namespace Workbench.App.ProjectWorld;

public sealed record ProjectWorldInitializationRequest(
    ProjectRef ProjectRef,
    UserPrincipalRef UserPrincipalRef,
    RoleKind RoleKind,
    string ResponsibilityObligation,
    string ResponsibilityExpectedOutcome,
    string InitialAssignment);

public sealed record ProjectWorldInitializationPreview(
    ProjectRef ProjectRef,
    UserPrincipalRef UserPrincipalRef,
    RoleKind RoleKind,
    string ResponsibilityObligation,
    string ResponsibilityExpectedOutcome,
    string InitialAssignment,
    string EffectsSummary);

public sealed class ProjectWorldInitializationService(
    B1AuthorityRepository authorityRepository,
    B1AuthorityCommandService authorityCommands,
    B1ProjectGovernanceRepository governanceRepository)
{
    private readonly B1AuthorityRepository _authorityRepository =
        authorityRepository ?? throw new ArgumentNullException(nameof(authorityRepository));
    private readonly B1AuthorityCommandService _authorityCommands =
        authorityCommands ?? throw new ArgumentNullException(nameof(authorityCommands));
    private readonly B1ProjectGovernanceRepository _governanceRepository =
        governanceRepository ?? throw new ArgumentNullException(nameof(governanceRepository));

    public async Task<ProjectWorldInitializationPreview> PreviewAsync(
        ProjectWorldInitializationRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        var governance = await _governanceRepository.GetAsync(request.ProjectRef, cancellationToken)
            ?? throw new B1CommandException(B1FailureCode.InvalidReference, "The Project has no B1 governance root.");
        if (governance.BootstrapPrincipalRef != request.UserPrincipalRef)
        {
            throw new B1CommandException(B1FailureCode.NotAuthorized, "The current UserPrincipal is not the Project bootstrap principal.");
        }

        var state = await _authorityRepository.LoadProjectStateAsync(request.ProjectRef, cancellationToken);
        if (state.LogicalActors.Count != 0 || state.Responsibilities.Count != 0 ||
            state.Assignments.Count != 0 || state.AuthorityDecisions.Count != 0)
        {
            throw new B1CommandException(B1FailureCode.InvalidDecisionShape, "Project World initialization is only available for an empty B1 world.");
        }

        return new(
            request.ProjectRef,
            request.UserPrincipalRef,
            request.RoleKind,
            request.ResponsibilityObligation,
            request.ResponsibilityExpectedOutcome,
            request.InitialAssignment,
            "Establish one LogicalActor, one Responsibility, one Assignment, and one initial Revision in one AuthorityDecision.");
    }

    public Task<AuthorityDecision> CommitAsync(
        ProjectWorldInitializationRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        var command = new EstablishResponsibilityCommand(
            request.ProjectRef,
            request.UserPrincipalRef,
            new DecidingAuthorityRef.UserPrincipal(request.UserPrincipalRef),
            new ResponsibilityContract(
                request.ResponsibilityObligation,
                request.ResponsibilityExpectedOutcome,
                AuthorityBoundary.Empty),
            new AssignmentDelegationInstruction(
                new ResponsibilityTarget.EstablishedByThisDecision(),
                new AssignmentAssigneeTarget.EstablishedByThisDecision(),
                new AssignmentRevisionContract(request.InitialAssignment, AuthorityBoundary.Empty),
                null),
            request.RoleKind,
            [],
            []);
        return _authorityCommands.EstablishResponsibilityAsync(command, cancellationToken);
    }

    private static void ValidateRequest(ProjectWorldInitializationRequest request)
    {
        if (request.ProjectRef.Value == Guid.Empty)
            throw new ArgumentException("A Project reference is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.UserPrincipalRef.Value))
            throw new ArgumentException("A UserPrincipal reference is required.", nameof(request));
        if (!Enum.IsDefined(request.RoleKind))
            throw new ArgumentOutOfRangeException(nameof(request.RoleKind));
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ResponsibilityObligation);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ResponsibilityExpectedOutcome);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.InitialAssignment);
    }
}
