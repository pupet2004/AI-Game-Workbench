namespace Workbench.Runtime.Providers;

public sealed record ProviderId
{
    public ProviderId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim();
    }

    public string Value { get; }

    public override string ToString() => Value;
}
