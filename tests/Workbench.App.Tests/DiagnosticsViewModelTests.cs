using Workbench.App.Tests.Support;
using Workbench.App.ViewModels;

namespace Workbench.App.Tests;

public sealed class DiagnosticsViewModelTests
{
    [Fact]
    public async Task Diagnostics_reports_healthy_database_and_empty_runtime_state()
    {
        await using var context = await AppTestContext.CreateAsync();
        var diagnostics = new DiagnosticsViewModel(context.Services, () => Task.CompletedTask);

        await diagnostics.InitializeAsync();

        Assert.Equal(context.Services.Database.DatabasePath, diagnostics.DatabasePath);
        Assert.Equal("Database connection is healthy", diagnostics.DatabaseStatus);
        Assert.Empty(diagnostics.Runtimes);
        Assert.Equal("Registered runtimes: 0", diagnostics.RuntimeCountText);
        Assert.False(diagnostics.HasDatabaseError);
    }

    [Fact]
    public async Task Diagnostics_lists_registered_runtime_identity()
    {
        var runtime = new FakeAgentRuntime();
        await using var context = await AppTestContext.CreateAsync(runtimeRegistry: RegistryWith(runtime));
        var diagnostics = new DiagnosticsViewModel(context.Services, () => Task.CompletedTask);

        await diagnostics.InitializeAsync();

        var item = Assert.Single(diagnostics.Runtimes);
        Assert.Equal("Fake Provider", item.Provider);
        Assert.Equal("Fake Account", item.Account);
        Assert.Equal("fake-runtime", item.RuntimeKind);
        Assert.Equal("Connected", item.ConnectionStatus);
    }

    private static Workbench.Runtime.Registry.AgentRuntimeRegistry RegistryWith(FakeAgentRuntime runtime)
    {
        var registry = new Workbench.Runtime.Registry.AgentRuntimeRegistry();
        registry.Register(runtime);
        return registry;
    }
}
