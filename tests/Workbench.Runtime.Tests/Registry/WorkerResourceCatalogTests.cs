using Workbench.Core.Tasks;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Providers;
using Workbench.Runtime.Registry;

namespace Workbench.Runtime.Tests.Registry;

public sealed class WorkerResourceCatalogTests
{
    [Fact]
    public async Task Connected_runtime_is_projected_as_one_ready_worker_resource()
    {
        var registry = CreateRegistry("codex", new ProviderAccountId(Guid.Parse("c0de0001-4a49-4741-8d45-574f524b424e")), "codex-app-server");

        var resource = Assert.Single(await registry.GetWorkerResourcesAsync());

        Assert.True(resource.IsReady);
        Assert.False(string.IsNullOrWhiteSpace(resource.ProviderId));
        Assert.False(string.IsNullOrWhiteSpace(resource.ProviderAccountId));
        Assert.False(string.IsNullOrWhiteSpace(resource.ModelProfileId));
        Assert.False(string.IsNullOrWhiteSpace(resource.AgentRuntimeId));
    }

    [Fact]
    public async Task Ready_worker_resource_creates_a_strict_execution_profile()
    {
        var registry = CreateRegistry("codex", new ProviderAccountId(Guid.Parse("c0de0001-4a49-4741-8d45-574f524b424e")), "codex-app-server");

        var resource = Assert.Single(await registry.GetWorkerResourcesAsync());
        var profile = resource.CreateExecutionProfile();

        Assert.Equal(resource.ProviderId, profile.ProviderId);
        Assert.Equal(resource.ProviderAccountId, profile.ProviderAccountId);
        Assert.Equal(resource.ModelProfileId, profile.ModelProfileId);
        Assert.Equal(resource.AgentRuntimeId, profile.AgentRuntimeId);
    }

    [Fact]
    public async Task Runtime_identity_is_stable_across_reconnection_of_the_same_logical_runtime()
    {
        var account = new ProviderAccountId(Guid.Parse("c0de0001-4a49-4741-8d45-574f524b424e"));

        var first = Assert.Single(await CreateRegistry("codex", account, "codex-app-server").GetWorkerResourcesAsync());
        var second = Assert.Single(await CreateRegistry("codex", account, "codex-app-server").GetWorkerResourcesAsync());

        Assert.Equal(first.AgentRuntimeId, second.AgentRuntimeId);
    }

    [Fact]
    public async Task Different_runtime_kind_or_account_gets_a_distinct_runtime_identity()
    {
        var first = Assert.Single(await CreateRegistry("codex", ProviderAccountId.New(), "codex-app-server").GetWorkerResourcesAsync());
        var differentAccount = Assert.Single(await CreateRegistry("codex", ProviderAccountId.New(), "codex-app-server").GetWorkerResourcesAsync());
        var differentKind = Assert.Single(await CreateRegistry("codex", new ProviderAccountId(Guid.Parse("c0de0001-4a49-4741-8d45-574f524b424e")), "other-runtime").GetWorkerResourcesAsync());

        Assert.NotEqual(first.AgentRuntimeId, differentAccount.AgentRuntimeId);
        Assert.NotEqual(first.AgentRuntimeId, differentKind.AgentRuntimeId);
    }

    [Fact]
    public async Task No_ready_resource_is_reported_when_no_runtime_is_connected()
    {
        var resources = await new AgentRuntimeRegistry().GetWorkerResourcesAsync();

        Assert.DoesNotContain(resources, resource => resource.IsReady);
    }

    private static AgentRuntimeRegistry CreateRegistry(string provider, ProviderAccountId account, string runtimeKind)
    {
        var providerId = new ProviderId(provider);
        var runtime = new FakeAgentRuntime(
            new ProviderDescriptor(providerId, "Codex"),
            new ProviderAccountSummary(account, providerId, "Connected account", true),
            [new ModelProfile(providerId, "gpt-5.6-sol", "GPT-5.6-Sol", AgentCapability.StructuredEvents)],
            runtimeKind);
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        return registry;
    }
}
