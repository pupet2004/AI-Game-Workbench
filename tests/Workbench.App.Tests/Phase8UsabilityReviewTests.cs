using Workbench.App.Views;

namespace Workbench.App.Tests;

public sealed class Phase8UsabilityReviewTests
{
    private static readonly string RepositoryRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    [Fact]
    public void Project_home_explains_the_first_action_and_has_a_local_project_entry()
    {
        var markup = ReadView("HomeView.axaml");

        Assert.Contains("Project Home", markup, StringComparison.Ordinal);
        Assert.Contains("Open a local project", markup, StringComparison.Ordinal);
        Assert.Contains("Create Project", markup, StringComparison.Ordinal);
        Assert.Contains("No projects yet. Open a local project folder to begin.", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Manual_path_uses_plain_language_and_keeps_signed_in_and_acting_identity_visible()
    {
        var markup = ReadView("ManualWorkView.axaml");

        Assert.Contains("Signed in as:", markup, StringComparison.Ordinal);
        Assert.Contains("Acting as:", markup, StringComparison.Ordinal);
        Assert.Contains("Record Handoff", markup, StringComparison.Ordinal);
        Assert.Contains("Review Work Result", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("B1", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Decision_path_requires_preview_before_confirm_and_explains_deciding_identity()
    {
        var markup = ReadView("GuidedDecisionView.axaml");

        Assert.Contains("Submitted as:", markup, StringComparison.Ordinal);
        Assert.Contains("Deciding as:", markup, StringComparison.Ordinal);
        Assert.Contains("Preview what will change", markup, StringComparison.Ordinal);
        Assert.Contains("Confirm Decision", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("authority effects", markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Core_manual_views_do_not_require_internal_b1_terminology()
    {
        foreach (var view in new[]
        {
            "HomeView.axaml",
            "ProjectWorldSetupView.axaml",
            "ProjectWorldExplorerView.axaml",
            "ManualWorkView.axaml",
            "GuidedDecisionView.axaml"
        })
        {
            Assert.DoesNotContain("B1", ReadView(view), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Main_window_keeps_the_sealed_journey_inside_the_supported_720p_minimum()
    {
        var markup = ReadView("MainWindow.axaml");

        Assert.Contains("MinWidth=\"1280\"", markup, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"720\"", markup, StringComparison.Ordinal);
        Assert.Contains("WindowStartupLocation=\"CenterScreen\"", markup, StringComparison.Ordinal);
    }

    private static string ReadView(string fileName) =>
        File.ReadAllText(Path.Combine(RepositoryRoot, "src", "Workbench.App", "Views", fileName));
}
