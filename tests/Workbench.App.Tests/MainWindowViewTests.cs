namespace Workbench.App.Tests;

using Workbench.App.Views;
using Workbench.App.ProjectWorld;
using Workbench.App.ViewModels;
using Workbench.App.Tests.Support;

public sealed class MainWindowViewTests
{
    [Fact]
    public async Task Reopening_a_governance_only_project_enters_resumable_setup()
    {
        using var folder = new TemporaryDirectory("incomplete-project-route");
        await using var context = await AppTestContext.CreateAsync();
        var opened = await context.Services.ProjectOpenService.OpenAsync(folder.Path);
        var projectRef = new Workbench.Core.Continuity.ProjectRef(opened.Project.Id);
        await context.Services.B1ProjectGovernance.CreateGovernedProjectForExistingProjectAsync(
            projectRef, context.Services.UserPrincipalProvider.GetCurrent());
        var main = context.CreateMain();

        await main.InitializeAsync();
        await ((HomeViewModel)main.CurrentPage).OpenPathAsync(folder.Path);

        var setup = Assert.IsType<ProjectWorldSetupViewModel>(main.CurrentPage);
        Assert.True(setup.IsGovernanceEstablished);
        Assert.Equal(ProjectWorldEntryKind.ProjectWorldSetupIncomplete, setup.Status.Kind);
        Assert.False(setup.CanEstablishGovernance);
    }

    [Fact]
    public async Task Creating_a_new_local_project_enters_project_setup()
    {
        using var folder = new TemporaryDirectory("new-project-route");
        await using var context = await AppTestContext.CreateAsync(folder.Path);
        var main = context.CreateMain();

        await main.InitializeAsync();
        await ((HomeViewModel)main.CurrentPage).CreateProjectAsync();

        var setup = Assert.IsType<ProjectWorldSetupViewModel>(main.CurrentPage);
        await setup.EstablishGovernanceCommand.ExecuteAsync(null);
        await setup.PreviewInitializationCommand.ExecuteAsync(null);
        await setup.ConfirmInitializationCommand.ExecuteAsync(null);

        Assert.IsType<ProjectWorldExplorerViewModel>(main.CurrentPage);
        Assert.IsType<ProjectWorldExplorerView>(new ViewLocator().Build(main.CurrentPage));

        await ((ProjectWorldExplorerViewModel)main.CurrentPage).OpenWorkspaceCommand.ExecuteAsync(null);
        Assert.IsType<WorkspaceViewModel>(main.CurrentPage);
    }

    [Fact]
    public async Task Startup_project_argument_opens_the_project_after_home_load()
    {
        using var folder = new TemporaryDirectory("startup-project-route");
        await using var context = await AppTestContext.CreateAsync();
        var main = context.CreateMain();

        await main.InitializeAsync(
            startupArgs: ["--project", folder.Path]);

        var workspace = Assert.IsType<WorkspaceViewModel>(main.CurrentPage);
        Assert.Equal(Path.GetFullPath(folder.Path), workspace.Result.Project.RootPath);
    }

    [Fact]
    public void Main_window_starts_centered_in_normal_state()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var markup = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Workbench.App", "Views", "MainWindow.axaml"));

        Assert.Contains("WindowStartupLocation=\"CenterScreen\"", markup, StringComparison.Ordinal);
        Assert.Contains("WindowState=\"Normal\"", markup, StringComparison.Ordinal);
        Assert.Contains("Width=\"1400\"", markup, StringComparison.Ordinal);
        Assert.Contains("Height=\"850\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_window_placement_reserves_physical_screen_margin_under_dpi_scaling()
    {
        var placement = MainWindowPlacement.Calculate(
            workingX: 0,
            workingY: 0,
            workingWidth: 1920,
            workingHeight: 1040,
            scaling: 1.25,
            frameWidth: 14.4,
            frameHeight: 37.6);

        Assert.Equal(1400, placement.Width);
        Assert.Equal(717.6, placement.Height, 3);
        Assert.Equal(76, placement.X);
        Assert.Equal(48, placement.Y);
    }
}
