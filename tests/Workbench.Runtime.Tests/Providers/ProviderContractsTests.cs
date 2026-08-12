using Workbench.Runtime.Agents;
using Workbench.Runtime.Providers;

namespace Workbench.Runtime.Tests.Providers;

public sealed class ProviderContractsTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ProviderId_rejects_empty_value(string value)
    {
        Assert.Throws<ArgumentException>(() => new ProviderId(value));
    }

    [Fact]
    public void ProviderId_preserves_provider_identity()
    {
        var id = new ProviderId("  future-provider  ");

        Assert.Equal("future-provider", id.Value);
        Assert.Equal(new ProviderId("future-provider"), id);
        Assert.NotEqual(new ProviderId("FUTURE-PROVIDER"), id);
    }

    [Fact]
    public void Different_accounts_can_share_provider()
    {
        var providerId = new ProviderId("provider-a");
        var first = new ProviderAccountSummary(ProviderAccountId.New(), providerId, "Account A", true);
        var second = new ProviderAccountSummary(ProviderAccountId.New(), providerId, "Account B", false);

        Assert.Equal(first.ProviderId, second.ProviderId);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void Model_profile_preserves_opaque_model_id()
    {
        var profile = new ModelProfile(
            new ProviderId("provider-a"),
            "opaque:model/version@2026",
            "Reasoning Model",
            AgentCapability.StructuredEvents | AgentCapability.Resume);

        Assert.Equal("opaque:model/version@2026", profile.ModelId);
        Assert.Equal(new ProviderId("provider-a"), profile.ProviderId);
    }
}
