using System.Collections.ObjectModel;

namespace Workbench.Core.Continuity;

public enum B1GovernanceOrigin
{
    Created,
    Adopted
}

public enum RoleKind
{
    Leader,
    Worker,
    Reviewer
}

public enum B1AuthorityCapability
{
    EstablishLogicalActor,
    EstablishResponsibility,
    DelegateAssignment,
    DecideAssignmentDisposition,
    ActivateAssignmentRevision,
    AcceptProjectStateContribution,
    AcceptResponsibilityStateContribution,
    AcceptAssignmentStateContribution
}

public enum AssignmentDisposition
{
    Accepted,
    Rejected,
    RevisionRequired
}

public sealed class AuthorityBoundary : IEquatable<AuthorityBoundary>
{
    private readonly ReadOnlySet<B1AuthorityCapability> _capabilities;

    private AuthorityBoundary(IEnumerable<B1AuthorityCapability> capabilities)
    {
        var owned = new HashSet<B1AuthorityCapability>(capabilities);
        _capabilities = new ReadOnlySet<B1AuthorityCapability>(owned);
    }

    public static AuthorityBoundary Empty { get; } = new([]);

    public IReadOnlySet<B1AuthorityCapability> Capabilities => _capabilities;

    public static AuthorityBoundary Create(IEnumerable<B1AuthorityCapability> capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        var values = capabilities.ToArray();
        foreach (var capability in values)
        {
            if (!Enum.IsDefined(capability))
            {
                throw new ArgumentOutOfRangeException(nameof(capabilities), capability, "Unknown B1 authority capability.");
            }
        }

        return values.Length == 0 ? Empty : new AuthorityBoundary(values);
    }

    public bool Contains(B1AuthorityCapability capability)
    {
        if (!Enum.IsDefined(capability))
        {
            throw new ArgumentOutOfRangeException(nameof(capability), capability, "Unknown B1 authority capability.");
        }

        return _capabilities.Contains(capability);
    }

    public AuthorityBoundary Except(AuthorityBoundary other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Create(_capabilities.Except(other._capabilities));
    }

    public bool IsSubsetOf(AuthorityBoundary other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return _capabilities.IsSubsetOf(other._capabilities);
    }

    public bool Equals(AuthorityBoundary? other) =>
        other is not null && _capabilities.SetEquals(other._capabilities);

    public override bool Equals(object? obj) => obj is AuthorityBoundary other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var capability in _capabilities.OrderBy(value => value))
        {
            hash.Add(capability);
        }

        return hash.ToHashCode();
    }
}

public sealed record ProjectGovernance
{
    public ProjectGovernance(
        ProjectRef projectRef,
        UserPrincipalRef bootstrapPrincipalRef,
        B1GovernanceOrigin origin,
        DateTimeOffset? adoptedAt,
        long lastProjectCommitSequence)
    {
        B1ContractValidation.Require(projectRef, nameof(projectRef));
        B1ContractValidation.Require(bootstrapPrincipalRef, nameof(bootstrapPrincipalRef));
        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(nameof(origin));
        }

        if (origin == B1GovernanceOrigin.Created && adoptedAt is not null)
        {
            throw new ArgumentException("Created governance cannot have adoption provenance.", nameof(adoptedAt));
        }

        if (origin == B1GovernanceOrigin.Adopted && adoptedAt is null)
        {
            throw new ArgumentException("Adopted governance requires adoption provenance.", nameof(adoptedAt));
        }

        if (lastProjectCommitSequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lastProjectCommitSequence));
        }

        ProjectRef = projectRef;
        BootstrapPrincipalRef = bootstrapPrincipalRef;
        Origin = origin;
        AdoptedAt = adoptedAt;
        LastProjectCommitSequence = lastProjectCommitSequence;
    }

    public ProjectRef ProjectRef { get; }
    public UserPrincipalRef BootstrapPrincipalRef { get; }
    public B1GovernanceOrigin Origin { get; }
    public DateTimeOffset? AdoptedAt { get; }
    public long LastProjectCommitSequence { get; }
}

public sealed record LogicalActor
{
    public LogicalActor(
        LogicalActorRef logicalActorRef,
        ProjectRef projectRef,
        RoleKind roleKind,
        AuthorityDecisionRef authorizedByDecisionRef,
        DateTimeOffset createdAt)
    {
        B1ContractValidation.Require(logicalActorRef, nameof(logicalActorRef));
        B1ContractValidation.Require(projectRef, nameof(projectRef));
        B1ContractValidation.Require(authorizedByDecisionRef, nameof(authorizedByDecisionRef));
        if (!Enum.IsDefined(roleKind))
        {
            throw new ArgumentOutOfRangeException(nameof(roleKind));
        }

        LogicalActorRef = logicalActorRef;
        ProjectRef = projectRef;
        RoleKind = roleKind;
        AuthorizedByDecisionRef = authorizedByDecisionRef;
        CreatedAt = createdAt;
    }

    public LogicalActorRef LogicalActorRef { get; }
    public ProjectRef ProjectRef { get; }
    public RoleKind RoleKind { get; }
    public AuthorityDecisionRef AuthorizedByDecisionRef { get; }
    public DateTimeOffset CreatedAt { get; }
}

public sealed record ResponsibilityContract
{
    public ResponsibilityContract(
        string obligation,
        string expectedOutcome,
        AuthorityBoundary maximumDelegableAuthorityBoundary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(obligation);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedOutcome);
        ArgumentNullException.ThrowIfNull(maximumDelegableAuthorityBoundary);
        Obligation = obligation;
        ExpectedOutcome = expectedOutcome;
        MaximumDelegableAuthorityBoundary = maximumDelegableAuthorityBoundary;
    }

    public string Obligation { get; }
    public string ExpectedOutcome { get; }
    public AuthorityBoundary MaximumDelegableAuthorityBoundary { get; }
}

public sealed record Responsibility
{
    public Responsibility(
        ResponsibilityRef responsibilityRef,
        ProjectRef projectRef,
        ResponsibilityContract contract,
        AuthorityDecisionRef authorizedByDecisionRef,
        DateTimeOffset createdAt)
    {
        B1ContractValidation.Require(responsibilityRef, nameof(responsibilityRef));
        B1ContractValidation.Require(projectRef, nameof(projectRef));
        ArgumentNullException.ThrowIfNull(contract);
        B1ContractValidation.Require(authorizedByDecisionRef, nameof(authorizedByDecisionRef));
        ResponsibilityRef = responsibilityRef;
        ProjectRef = projectRef;
        Contract = contract;
        AuthorizedByDecisionRef = authorizedByDecisionRef;
        CreatedAt = createdAt;
    }

    public ResponsibilityRef ResponsibilityRef { get; }
    public ProjectRef ProjectRef { get; }
    public ResponsibilityContract Contract { get; }
    public AuthorityDecisionRef AuthorizedByDecisionRef { get; }
    public DateTimeOffset CreatedAt { get; }
}

public sealed record AssignmentRevisionContract
{
    public AssignmentRevisionContract(string workContract, AuthorityBoundary? delegatedAuthorityBoundary = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workContract);
        WorkContract = workContract;
        DelegatedAuthorityBoundary = delegatedAuthorityBoundary ?? AuthorityBoundary.Empty;
    }

    public string WorkContract { get; }
    public AuthorityBoundary DelegatedAuthorityBoundary { get; }
}

public sealed record AssignmentRevision
{
    public AssignmentRevision(
        RevisionRef revisionRef,
        AssignmentRef assignmentRef,
        RevisionRef? priorRevisionRef,
        AssignmentRevisionContract contract,
        AuthorityDecisionRef authorizedByDecisionRef)
    {
        B1ContractValidation.Require(revisionRef, nameof(revisionRef));
        B1ContractValidation.Require(assignmentRef, nameof(assignmentRef));
        if (priorRevisionRef is { } prior)
        {
            B1ContractValidation.Require(prior, nameof(priorRevisionRef));
        }

        ArgumentNullException.ThrowIfNull(contract);
        B1ContractValidation.Require(authorizedByDecisionRef, nameof(authorizedByDecisionRef));
        RevisionRef = revisionRef;
        AssignmentRef = assignmentRef;
        PriorRevisionRef = priorRevisionRef;
        Contract = contract;
        AuthorizedByDecisionRef = authorizedByDecisionRef;
    }

    public RevisionRef RevisionRef { get; }
    public AssignmentRef AssignmentRef { get; }
    public RevisionRef? PriorRevisionRef { get; }
    public AssignmentRevisionContract Contract { get; }
    public AuthorityDecisionRef AuthorizedByDecisionRef { get; }
}

public sealed record Assignment
{
    public Assignment(
        AssignmentRef assignmentRef,
        ResponsibilityRef responsibilityRef,
        LogicalActorRef assigneeActorRef,
        RevisionRef initialRevisionRef,
        AuthorityDecisionRef authorizedByDecisionRef)
    {
        B1ContractValidation.Require(assignmentRef, nameof(assignmentRef));
        B1ContractValidation.Require(responsibilityRef, nameof(responsibilityRef));
        B1ContractValidation.Require(assigneeActorRef, nameof(assigneeActorRef));
        B1ContractValidation.Require(initialRevisionRef, nameof(initialRevisionRef));
        B1ContractValidation.Require(authorizedByDecisionRef, nameof(authorizedByDecisionRef));
        AssignmentRef = assignmentRef;
        ResponsibilityRef = responsibilityRef;
        AssigneeActorRef = assigneeActorRef;
        InitialRevisionRef = initialRevisionRef;
        AuthorizedByDecisionRef = authorizedByDecisionRef;
    }

    public AssignmentRef AssignmentRef { get; }
    public ResponsibilityRef ResponsibilityRef { get; }
    public LogicalActorRef AssigneeActorRef { get; }
    public RevisionRef InitialRevisionRef { get; }
    public AuthorityDecisionRef AuthorizedByDecisionRef { get; }
}

public sealed record Attempt
{
    public Attempt(
        AttemptRef attemptRef,
        AssignmentRef assignmentRef,
        RevisionRef effectiveRevisionRef,
        DateTimeOffset createdAt)
    {
        B1ContractValidation.Require(attemptRef, nameof(attemptRef));
        B1ContractValidation.Require(assignmentRef, nameof(assignmentRef));
        B1ContractValidation.Require(effectiveRevisionRef, nameof(effectiveRevisionRef));
        AttemptRef = attemptRef;
        AssignmentRef = assignmentRef;
        EffectiveRevisionRef = effectiveRevisionRef;
        CreatedAt = createdAt;
    }

    public AttemptRef AttemptRef { get; }
    public AssignmentRef AssignmentRef { get; }
    public RevisionRef EffectiveRevisionRef { get; }
    public DateTimeOffset CreatedAt { get; }
}

public sealed record SessionBinding
{
    public SessionBinding(
        SessionBindingRef sessionBindingRef,
        AttemptRef attemptRef,
        LogicalActorRef logicalActorRef,
        ExternalSessionRef externalSessionRef,
        DateTimeOffset createdAt)
    {
        B1ContractValidation.Require(sessionBindingRef, nameof(sessionBindingRef));
        B1ContractValidation.Require(attemptRef, nameof(attemptRef));
        B1ContractValidation.Require(logicalActorRef, nameof(logicalActorRef));
        B1ContractValidation.Require(externalSessionRef, nameof(externalSessionRef));
        SessionBindingRef = sessionBindingRef;
        AttemptRef = attemptRef;
        LogicalActorRef = logicalActorRef;
        ExternalSessionRef = externalSessionRef;
        CreatedAt = createdAt;
    }

    public SessionBindingRef SessionBindingRef { get; }
    public AttemptRef AttemptRef { get; }
    public LogicalActorRef LogicalActorRef { get; }
    public ExternalSessionRef ExternalSessionRef { get; }
    public DateTimeOffset CreatedAt { get; }
}

internal static class B1ContractValidation
{
    public static void Require(ProjectRef value, string parameterName) => Require(value.Value, parameterName);
    public static void Require(LogicalActorRef value, string parameterName) => Require(value.Value, parameterName);
    public static void Require(ResponsibilityRef value, string parameterName) => Require(value.Value, parameterName);
    public static void Require(AssignmentRef value, string parameterName) => Require(value.Value, parameterName);
    public static void Require(RevisionRef value, string parameterName) => Require(value.Value, parameterName);
    public static void Require(AttemptRef value, string parameterName) => Require(value.Value, parameterName);
    public static void Require(SessionBindingRef value, string parameterName) => Require(value.Value, parameterName);
    public static void Require(AuthorityDecisionRef value, string parameterName) => Require(value.Value, parameterName);

    public static void Require(UserPrincipalRef value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value.Value))
        {
            throw new ArgumentException("A nonblank external principal reference is required.", parameterName);
        }
    }

    public static void Require(ExternalSessionRef value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value.Value))
        {
            throw new ArgumentException("A nonblank external session reference is required.", parameterName);
        }
    }

    private static void Require(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty durable reference is required.", parameterName);
        }
    }
}
