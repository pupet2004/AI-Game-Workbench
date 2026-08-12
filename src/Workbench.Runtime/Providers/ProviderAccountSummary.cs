namespace Workbench.Runtime.Providers;

public sealed record ProviderAccountSummary(
    ProviderAccountId Id,
    ProviderId ProviderId,
    string DisplayName,
    bool IsConnected);
