using Workbench.Runtime.Providers;

namespace Workbench.Runtime.Registry;

public sealed record AvailableModelProfile(
    ProviderAccountId AccountId,
    ModelProfile Model);
