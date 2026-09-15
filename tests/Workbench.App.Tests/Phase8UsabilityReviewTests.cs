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

        Assert.Contains("[Home.Title]", markup, StringComparison.Ordinal);
        Assert.Contains("[Home.Open]", markup, StringComparison.Ordinal);
        Assert.Contains("[Home.Create]", markup, StringComparison.Ordinal);
        Assert.Contains("[Home.Empty]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Manual_path_uses_plain_language_and_keeps_signed_in_and_acting_identity_visible()
    {
        var markup = ReadView("ManualWorkView.axaml");

        Assert.Contains("[Manual.SignedIn]", markup, StringComparison.Ordinal);
        Assert.Contains("[Manual.ActingAs]", markup, StringComparison.Ordinal);
        Assert.Contains("[Manual.RecordHandoff]", markup, StringComparison.Ordinal);
        Assert.Contains("[Manual.Review]", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("B1", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Decision_path_requires_preview_before_confirm_and_explains_deciding_identity()
    {
        var markup = ReadView("GuidedDecisionView.axaml");

        Assert.Contains("[Decision.SubmittedAs]", markup, StringComparison.Ordinal);
        Assert.Contains("[Decision.DecidingAs]", markup, StringComparison.Ordinal);
        Assert.Contains("AcceptCommand", markup, StringComparison.Ordinal);
        Assert.Contains("IsPreviewVisible", markup, StringComparison.Ordinal);
        Assert.Contains("[Decision.Confirm]", markup, StringComparison.Ordinal);
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
    public void Project_overview_exposes_current_state_and_one_continue_action()
    {
        var markup = ReadView("ProjectWorldExplorerView.axaml");

        Assert.Contains("[Overview.CurrentState]", markup, StringComparison.Ordinal);
        Assert.Contains("[Explorer.Continue]", markup, StringComparison.Ordinal);
        Assert.Contains("ContinueProjectCommand", markup, StringComparison.Ordinal);
        Assert.Contains("AcceptedStatementCountText", markup, StringComparison.Ordinal);
        Assert.Contains("NeedsAttentionSummary", markup, StringComparison.Ordinal);
        Assert.Contains("CurrentWorkHeadline", markup, StringComparison.Ordinal);
        Assert.Contains("OpenReviewCommand", markup, StringComparison.Ordinal);
        Assert.Contains("[Overview.Review]", markup, StringComparison.Ordinal);
        Assert.Contains("[Explorer.Settings]", markup, StringComparison.Ordinal);
        Assert.Contains("OpenSettingsCommand", markup, StringComparison.Ordinal);
        Assert.Contains("[Explorer.History]", markup, StringComparison.Ordinal);
        Assert.Contains("OpenHistoryCommand", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Review_queue_is_a_first_class_project_destination()
    {
        var markup = ReadView("ProjectReviewView.axaml");

        Assert.Contains("[Review.Title]", markup, StringComparison.Ordinal);
        Assert.Contains("PendingHandoffs", markup, StringComparison.Ordinal);
        Assert.Contains("ReviewHandoffCommand", markup, StringComparison.Ordinal);
        Assert.Contains("[Review.Back]", markup, StringComparison.Ordinal);
        Assert.Contains("[Review.WhatChanged]", markup, StringComparison.Ordinal);
        Assert.Contains("[Review.Verification]", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding HandoffRef}\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Decision_surface_exposes_product_actions_before_the_advanced_form()
    {
        var markup = ReadView("GuidedDecisionView.axaml");

        Assert.Contains("AcceptCommand", markup, StringComparison.Ordinal);
        Assert.Contains("RequestRevisionCommand", markup, StringComparison.Ordinal);
        Assert.Contains("RejectCommand", markup, StringComparison.Ordinal);
        Assert.Contains("[Decision.Advanced]", markup, StringComparison.Ordinal);
        Assert.Contains("PreviewDecisionCommand", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Workspace_keeps_review_and_settings_reachable_from_the_three_pane_work_surface()
    {
        var markup = ReadView("WorkspaceView.axaml");

        Assert.Contains("[Workspace.Review]", markup, StringComparison.Ordinal);
        Assert.Contains("OpenProjectReviewPageCommand", markup, StringComparison.Ordinal);
        Assert.Contains("[Workspace.Settings]", markup, StringComparison.Ordinal);
        Assert.Contains("OpenSettingsPageCommand", markup, StringComparison.Ordinal);
        Assert.Contains("[Workspace.History]", markup, StringComparison.Ordinal);
        Assert.Contains("OpenProjectHistoryPageCommand", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_back_label_does_not_assume_the_entry_page()
    {
        var markup = ReadView("SettingsView.axaml");

        Assert.Contains("[Settings.Back]", markup, StringComparison.Ordinal);
        Assert.Contains("BackCommand", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("[Settings.Projects]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void History_view_is_a_read_only_project_destination()
    {
        var markup = ReadView("ProjectHistoryView.axaml");

        Assert.Contains("[History.Description]", markup, StringComparison.Ordinal);
        Assert.Contains("Entries", markup, StringComparison.Ordinal);
        Assert.Contains("[History.Back]", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Accept", markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Reject", markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Settings_exposes_diagnostics_without_internal_runtime_controls()
    {
        var markup = ReadView("SettingsView.axaml");

        Assert.Contains("[Settings.Diagnostics]", markup, StringComparison.Ordinal);
        Assert.Contains("OpenDiagnosticsCommand", markup, StringComparison.Ordinal);
        Assert.Contains("[Settings.Authority]", markup, StringComparison.Ordinal);
        Assert.Contains("UseBalancedAuthorityCommand", markup, StringComparison.Ordinal);
        Assert.Contains("[Settings.ProjectOverrides]", markup, StringComparison.Ordinal);
        Assert.Contains("InheritGlobalAuthorityCommand", markup, StringComparison.Ordinal);
        Assert.Contains("LeaderSessionRotationPolicyText", markup, StringComparison.Ordinal);
        Assert.Contains("LeaderAuthorityModeText", markup, StringComparison.Ordinal);
        Assert.Contains("ProjectRotationPolicyOverrideText", markup, StringComparison.Ordinal);
        Assert.Contains("ProjectAuthorityModeOverrideText", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding LeaderSessionRotationPolicy}\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding LeaderAuthorityMode}\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding ProjectRotationPolicyOverride}\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding ProjectAuthorityModeOverride}\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Diagnostics_view_exposes_database_and_runtime_health()
    {
        var markup = ReadView("DiagnosticsView.axaml");

        Assert.Contains("[Diagnostics.Database]", markup, StringComparison.Ordinal);
        Assert.Contains("DatabaseStatus", markup, StringComparison.Ordinal);
        Assert.Contains("[Diagnostics.Runtimes]", markup, StringComparison.Ordinal);
        Assert.Contains("RuntimeCountText", markup, StringComparison.Ordinal);
        Assert.Contains("[Diagnostics.CreateBackup]", markup, StringComparison.Ordinal);
        Assert.Contains("CreateBackupCommand", markup, StringComparison.Ordinal);
        Assert.Contains("[Diagnostics.Backups]", markup, StringComparison.Ordinal);
        Assert.Contains("ValidateBackupsCommand", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Work_cards_can_send_canonical_handoffs_to_review_without_relabeling_legacy_results()
    {
        var markup = ReadView(Path.Combine("Panes", "WorkPaneView.axaml"));

        Assert.Contains("HandoffDisplay.CanReview", markup, StringComparison.Ordinal);
        Assert.Contains("ReviewHandoffCommand", markup, StringComparison.Ordinal);
        Assert.Contains("[Worker.Review]", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Work_cards_lead_with_product_status_and_keep_technical_details_secondary()
    {
        var markup = ReadView(Path.Combine("Panes", "WorkPaneView.axaml"));
        var viewModel = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "Workbench.App", "ViewModels", "Panes", "WorkPaneViewModel.cs"));

        Assert.Contains("AgentSummary", markup, StringComparison.Ordinal);
        Assert.Contains("UserFacingStatus", markup, StringComparison.Ordinal);
        Assert.Contains("StatusDetail", markup, StringComparison.Ordinal);
        Assert.Contains("[Worker.Attention]", markup, StringComparison.Ordinal);
        Assert.Contains("AttentionText", markup, StringComparison.Ordinal);
        Assert.Contains("[\"Worker.ProjectStateUnchanged\"]", viewModel, StringComparison.Ordinal);
        Assert.Contains("[Worker.TechnicalDetails]", markup, StringComparison.Ordinal);
        Assert.Contains("StopWorkerCommand", markup, StringComparison.Ordinal);
        Assert.Contains("ContinueWorkerCommand", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecutionStateText", markup, StringComparison.Ordinal);
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
