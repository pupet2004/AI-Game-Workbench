using Workbench.App.ProjectWorld;
using Workbench.App.Tests.Support;
using Workbench.App.ViewModels;
using Workbench.Core.Continuity;
using Workbench.Runtime.Registry;

namespace Workbench.App.Tests;

public sealed class FirstRunSetupTests
{
    [Fact]
    public async Task Failed_model_discovery_is_not_ready_and_retry_does_not_create_project_truth()
    {
        using var folder = new TemporaryDirectory();
        var runtime = new FakeAgentRuntime { ModelException = new IOException("private provider detail") };
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        await using var context = await AppTestContext.CreateAsync(folder.Path, registry);
        var opened = await context.Services.ProjectOpenService.OpenAsync(folder.Path);
        var setup = new ProjectWorldSetupViewModel(context.Services, opened,
            await context.Services.ProjectWorldEntryStatus.GetStatusAsync(opened.Project),
            () => Task.CompletedTask, _ => Task.CompletedTask);

        await setup.RetryAgentCommand.ExecuteAsync(null);
        Assert.False(setup.IsAgentReady);
        Assert.DoesNotContain("private provider detail", setup.AgentDetailText);

        runtime.ModelException = null;
        await setup.RetryAgentCommand.ExecuteAsync(null);
        Assert.True(setup.IsAgentReady);
        Assert.Contains("model-a", setup.AgentDetailText);
        Assert.Null(await context.Services.B1ProjectGovernance.GetAsync(new ProjectRef(opened.Project.Id)));
        Assert.Empty(runtime.CreatedSessions);
    }

    [Theory]
    [InlineData("assignment")]
    [InlineData("responsibility")]
    [InlineData("outcome")]
    [InlineData("principal")]
    [InlineData("role")]
    public async Task Editing_setup_invalidates_preview_and_cannot_commit(string field)
    {
        using var folder = new TemporaryDirectory();
        await using var context = await AppTestContext.CreateAsync(folder.Path);
        var opened = await context.Services.ProjectOpenService.OpenAsync(folder.Path);
        var navigated = false;
        var setup = new ProjectWorldSetupViewModel(context.Services, opened,
            await context.Services.ProjectWorldEntryStatus.GetStatusAsync(opened.Project),
            () => Task.CompletedTask, _ => { navigated = true; return Task.CompletedTask; });
        await setup.EstablishGovernanceCommand.ExecuteAsync(null);
        await setup.PreviewInitializationCommand.ExecuteAsync(null);
        Assert.True(setup.CanConfirm);

        switch (field)
        {
            case "assignment": setup.InitialAssignment = "Add reset"; break;
            case "responsibility": setup.ResponsibilityObligation = "Maintain counter"; break;
            case "outcome": setup.ResponsibilityExpectedOutcome = "Counter resets"; break;
            case "principal": setup.UserPrincipal = "different-user"; break;
            case "role": setup.SelectedRoleKind = RoleKind.Leader; break;
        }

        Assert.False(setup.CanConfirm);
        Assert.False(setup.IsPreviewVisible);
        await setup.ConfirmInitializationCommand.ExecuteAsync(null);
        Assert.False(navigated);
        var state = await context.Services.B1AuthorityRepository.LoadProjectStateAsync(new ProjectRef(opened.Project.Id));
        Assert.Empty(state.AuthorityDecisions);
    }
}
