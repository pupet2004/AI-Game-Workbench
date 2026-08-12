namespace Workbench.Runtime.Providers;

public readonly record struct ProviderAccountId(Guid Value)
{
    public static ProviderAccountId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
