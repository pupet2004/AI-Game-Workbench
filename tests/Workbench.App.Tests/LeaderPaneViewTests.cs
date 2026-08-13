namespace Workbench.App.Tests;

public sealed class LeaderPaneViewTests
{
    [Fact]
    public void Leader_view_exposes_model_conversation_input_send_stop_and_retry_controls()
    {
        var markup = ReadLeaderView();

        Assert.Contains("AvailableModels", markup, StringComparison.Ordinal);
        Assert.Contains("SelectedModel", markup, StringComparison.Ordinal);
        Assert.Contains("Messages", markup, StringComparison.Ordinal);
        Assert.Contains("DraftMessage", markup, StringComparison.Ordinal);
        Assert.Contains("SendCommand", markup, StringComparison.Ordinal);
        Assert.Contains("StopCommand", markup, StringComparison.Ordinal);
        Assert.Contains("RetryRuntimeCommand", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Leader_conversation_is_scrollable_and_input_stays_in_bottom_row()
    {
        var markup = ReadLeaderView();

        Assert.Contains("RowDefinitions=\"Auto,Auto,*,Auto,Auto\"", markup, StringComparison.Ordinal);
        Assert.Contains("<ScrollViewer", markup, StringComparison.Ordinal);
        Assert.Contains("Grid.Row=\"2\"", markup, StringComparison.Ordinal);
        Assert.Contains("Grid.Row=\"4\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Light_status_cards_use_an_explicit_dark_foreground()
    {
        var markup = ReadLeaderView();

        Assert.Equal(3, CountOccurrences(markup, "Background=\"#F5F6F8\""));
        Assert.Equal(8, CountOccurrences(markup, "Foreground=\"#101828\""));
        Assert.Contains("Background=\"#FFF8E7\"", markup, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"#101828\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Approval_controls_are_dynamic_and_do_not_hardcode_a_decision()
    {
        var markup = ReadLeaderView();

        Assert.Contains("ApprovalOptions", markup, StringComparison.Ordinal);
        Assert.Contains("SelectCommand", markup, StringComparison.Ordinal);
        Assert.Contains("PendingApproval.Summary", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Allow once\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Decline\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Draft_confirmation_uses_theme_resources_for_dark_theme_contrast()
    {
        var markup = ReadLeaderView();

        Assert.Contains("Background=\"{DynamicResource SystemControlBackgroundBaseLowBrush}\"", markup, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{DynamicResource SystemControlForegroundBaseHighBrush}\"", markup, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{DynamicResource SystemControlForegroundBaseLowBrush}\"", markup, StringComparison.Ordinal);
        Assert.Contains("Background=\"{DynamicResource SystemControlBackgroundAccentBrush}\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Rollover_controls_are_inline_and_bind_to_single_view_model_commands()
    {
        var markup = ReadLeaderView();

        Assert.Contains("Content=\"New Brain\"", markup, StringComparison.Ordinal);
        Assert.Contains("StartNewBrainCommand", markup, StringComparison.Ordinal);
        Assert.Contains("HasPendingRotationDecision", markup, StringComparison.Ordinal);
        Assert.Contains("Content=\"Continue Previous\"", markup, StringComparison.Ordinal);
        Assert.Contains("ContinuePreviousCommand", markup, StringComparison.Ordinal);
        Assert.Contains("Content=\"Start Fresh\"", markup, StringComparison.Ordinal);
        Assert.Contains("StartFreshCommand", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Window.ShowDialog", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Light_rotation_surfaces_define_readable_dark_foregrounds()
    {
        var markup = ReadLeaderView().Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("Text=\"{Binding RotationMessage}\"\n                               Foreground=\"#101828\"", markup, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(markup, "Foreground=\"#101828\"\n                                    Command=\"{Binding"));
    }

    [Fact]
    public void Archived_transcript_and_header_define_readable_foregrounds_and_bounded_preview()
    {
        var markup = ReadLeaderView();

        Assert.Contains("Content=\"{Binding Header}\"", markup, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"#344054\"", markup, StringComparison.Ordinal);
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", markup, StringComparison.Ordinal);
        Assert.Contains("MaxLines=\"2\"", markup, StringComparison.Ordinal);
        Assert.Contains("<SelectableTextBlock Text=\"{Binding Text}\" Foreground=\"#101828\" TextWrapping=\"Wrap\" />", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Conversation_message_bodies_are_selectable_in_current_and_archived_transcripts()
    {
        var markup = ReadLeaderView();

        Assert.Equal(2, CountOccurrences(markup, "<SelectableTextBlock Text=\"{Binding Text}\""));
        Assert.Contains("<SelectableTextBlock IsVisible=\"{Binding IsExpanded}\" Text=\"{Binding FullHandoff}\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Leader_view_requests_the_initial_current_latest_anchor_only_on_view_load()
    {
        var markup = ReadLeaderView();
        var codeBehind = ReadLeaderViewCodeBehind();

        Assert.Contains("x:Name=\"ConversationScrollViewer\"", markup, StringComparison.Ordinal);
        Assert.Contains("Loaded=\"OnLoaded\"", markup, StringComparison.Ordinal);
        Assert.Contains("ScrollToEnd", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadEarlier", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("Toggle", codeBehind, StringComparison.Ordinal);
    }

    private static string ReadLeaderView()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        return File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "Workbench.App",
            "Views",
            "Panes",
            "LeaderPaneView.axaml"));
    }

    private static int CountOccurrences(string value, string fragment) =>
        value.Split(fragment, StringSplitOptions.None).Length - 1;

    private static string ReadLeaderViewCodeBehind()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        return File.ReadAllText(Path.Combine(repositoryRoot, "src", "Workbench.App", "Views", "Panes", "LeaderPaneView.axaml.cs"));
    }
}
