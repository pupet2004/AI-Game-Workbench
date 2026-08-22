namespace Workbench.Core.Continuity;

public readonly record struct ProjectRef
{
    public ProjectRef(Guid value) => Value = B1ReferenceValidation.RequireGuid(value, nameof(value));
    public Guid Value { get; }
    public override string ToString() => Value.ToString();
}

public readonly record struct LogicalActorRef
{
    public LogicalActorRef(Guid value) => Value = B1ReferenceValidation.RequireGuid(value, nameof(value));
    public Guid Value { get; }
    public override string ToString() => Value.ToString();
}

public readonly record struct ResponsibilityRef
{
    public ResponsibilityRef(Guid value) => Value = B1ReferenceValidation.RequireGuid(value, nameof(value));
    public Guid Value { get; }
    public override string ToString() => Value.ToString();
}

public readonly record struct AssignmentRef
{
    public AssignmentRef(Guid value) => Value = B1ReferenceValidation.RequireGuid(value, nameof(value));
    public Guid Value { get; }
    public override string ToString() => Value.ToString();
}

public readonly record struct RevisionRef
{
    public RevisionRef(Guid value) => Value = B1ReferenceValidation.RequireGuid(value, nameof(value));
    public Guid Value { get; }
    public override string ToString() => Value.ToString();
}

public readonly record struct AttemptRef
{
    public AttemptRef(Guid value) => Value = B1ReferenceValidation.RequireGuid(value, nameof(value));
    public Guid Value { get; }
    public override string ToString() => Value.ToString();
}

public readonly record struct SessionBindingRef
{
    public SessionBindingRef(Guid value) => Value = B1ReferenceValidation.RequireGuid(value, nameof(value));
    public Guid Value { get; }
    public override string ToString() => Value.ToString();
}

public readonly record struct ClaimRef
{
    public ClaimRef(Guid value) => Value = B1ReferenceValidation.RequireGuid(value, nameof(value));
    public Guid Value { get; }
    public override string ToString() => Value.ToString();
}

public readonly record struct HandoffRef
{
    public HandoffRef(Guid value) => Value = B1ReferenceValidation.RequireGuid(value, nameof(value));
    public Guid Value { get; }
    public override string ToString() => Value.ToString();
}

public readonly record struct AuthorityDecisionRef
{
    public AuthorityDecisionRef(Guid value) => Value = B1ReferenceValidation.RequireGuid(value, nameof(value));
    public Guid Value { get; }
    public override string ToString() => Value.ToString();
}

public readonly record struct AcceptedStateContributionRef
{
    public AcceptedStateContributionRef(Guid value) => Value = B1ReferenceValidation.RequireGuid(value, nameof(value));
    public Guid Value { get; }
    public override string ToString() => Value.ToString();
}

public readonly record struct UserPrincipalRef
{
    public UserPrincipalRef(string value) => Value = B1ReferenceValidation.RequireLocator(value, nameof(value));
    public string Value { get; }
    public override string ToString() => Value;
}

public readonly record struct ExternalSessionRef
{
    public ExternalSessionRef(string value) => Value = B1ReferenceValidation.RequireLocator(value, nameof(value));
    public string Value { get; }
    public override string ToString() => Value;
}

public readonly record struct EvidenceRef
{
    public EvidenceRef(string value) => Value = B1ReferenceValidation.RequireLocator(value, nameof(value));
    public string Value { get; }
    public override string ToString() => Value;
}

internal static class B1ReferenceValidation
{
    public static Guid RequireGuid(Guid value, string parameterName) =>
        value != Guid.Empty
            ? value
            : throw new ArgumentException("A non-empty durable reference is required.", parameterName);

    public static string RequireLocator(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }
}
