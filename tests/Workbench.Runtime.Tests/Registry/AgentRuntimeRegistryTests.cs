using Workbench.Runtime.Agents;
using Workbench.Runtime.Providers;
using Workbench.Runtime.Registry;

namespace Workbench.Runtime.Tests.Registry;

public sealed class AgentRuntimeRegistryTests
{
    [Fact]
    public void Duplicate_account_registration_is_rejected()
    {
        var accountId = ProviderAccountId.New();
        var registry = new AgentRuntimeRegistry();
        registry.Register(CreateRuntime("provider-a", accountId, "Account A"));

        Assert.Throws<InvalidOperationException>(() =>
            registry.Register(CreateRuntime("provider-a", accountId, "Replacement")));
    }

    [Fact]
    public void Registry_returns_runtime_by_account()
    {
        var accountId = ProviderAccountId.New();
        var runtime = CreateRuntime("provider-a", accountId, "Account A");
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);

        Assert.Same(runtime, registry.GetByAccount(accountId));
    }

    [Fact]
    public void Registry_lists_multiple_providers()
    {
        var registry = new AgentRuntimeRegistry();
        registry.Register(CreateRuntime("provider-a", ProviderAccountId.New(), "Account A"));
        registry.Register(CreateRuntime("provider-b", ProviderAccountId.New(), "Account B"));

        Assert.Collection(
            registry.Runtimes.OrderBy(runtime => runtime.Provider.Id.Value),
            runtime => Assert.Equal(new ProviderId("provider-a"), runtime.Provider.Id),
            runtime => Assert.Equal(new ProviderId("provider-b"), runtime.Provider.Id));
    }

    [Fact]
    public void Registry_allows_different_accounts_for_same_provider()
    {
        var registry = new AgentRuntimeRegistry();
        registry.Register(CreateRuntime("provider-a", ProviderAccountId.New(), "Account A"));
        registry.Register(CreateRuntime("provider-a", ProviderAccountId.New(), "Account B"));

        Assert.Equal(2, registry.Runtimes.Count);
    }

    [Fact]
    public async Task Registry_aggregates_models_without_losing_account_identity()
    {
        var firstAccountId = ProviderAccountId.New();
        var secondAccountId = ProviderAccountId.New();
        var registry = new AgentRuntimeRegistry();
        registry.Register(CreateRuntime("provider-a", firstAccountId, "Account A", "gpt-like"));
        registry.Register(CreateRuntime("provider-b", secondAccountId, "Account B", "gpt-like"));

        var models = await registry.GetAvailableModelsAsync();

        Assert.Collection(
            models.OrderBy(model => model.Model.ProviderId.Value),
            model =>
            {
                Assert.Equal(firstAccountId, model.AccountId);
                Assert.Equal(new ProviderId("provider-a"), model.Model.ProviderId);
                Assert.Equal("gpt-like", model.Model.ModelId);
            },
            model =>
            {
                Assert.Equal(secondAccountId, model.AccountId);
                Assert.Equal(new ProviderId("provider-b"), model.Model.ProviderId);
                Assert.Equal("gpt-like", model.Model.ModelId);
            });
    }

    [Fact]
    public async Task Runtime_can_expose_a_model_from_a_different_provider()
    {
        var accountId = ProviderAccountId.New();
        var agentProvider = new ProviderId("opencode");
        var modelProvider = new ProviderId("deepseek");
        var registry = new AgentRuntimeRegistry();
        registry.Register(new FakeAgentRuntime(
            new ProviderDescriptor(agentProvider, "OpenCode"),
            new ProviderAccountSummary(accountId, agentProvider, "OpenCode Account", true),
            [new ModelProfile(modelProvider, "deepseek-v4-flash", "DeepSeek V4 Flash", AgentCapability.StructuredEvents)],
            "opencode-acp"));

        var resources = await registry.GetWorkerResourcesAsync();

        var resource = Assert.Single(resources);
        Assert.Equal("deepseek", resource.ProviderId);
        Assert.Equal("deepseek-v4-flash", resource.ModelProfileId);
        Assert.Equal("opencode-acp:opencode:" + accountId.Value.ToString("D"), resource.AgentRuntimeId);
    }

    private static FakeAgentRuntime CreateRuntime(
        string provider,
        ProviderAccountId accountId,
        string accountName,
        string modelId = "model-a")
    {
        var providerId = new ProviderId(provider);
        return new FakeAgentRuntime(
            new ProviderDescriptor(providerId, provider),
            new ProviderAccountSummary(accountId, providerId, accountName, true),
            [new ModelProfile(providerId, modelId, modelId, AgentCapability.StructuredEvents)]);
    }
}
