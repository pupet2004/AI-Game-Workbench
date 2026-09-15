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
        Assert.Equal(2, CountOccurrences(markup, "MaxDropDownHeight=\"420\""));
        Assert.Contains("MaxHeight=\"220\"", markup, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding WorkerResources}\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Draft_details_are_bounded_with_confirmation_outside_the_scroll_area()
    {
        var document = System.Xml.Linq.XDocument.Parse(ReadLeaderView());
        System.Xml.Linq.XNamespace ui = "https://github.com/avaloniaui";
        System.Xml.Linq.XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var details = Assert.Single(document.Descendants(ui + "ScrollViewer"),
            element => (string?)element.Attribute(x + "Name") == "DraftDetailsScrollViewer");
        Assert.Equal("280", (string?)details.Attribute("MaxHeight"));
        Assert.DoesNotContain(details.Descendants(ui + "Button"), element =>
            (string?)element.Attribute("Command") == "{Binding ConfirmDraftCommand}");
        Assert.Contains(details.ElementsAfterSelf(ui + "Button"), element =>
            (string?)element.Attribute("Command") == "{Binding ConfirmDraftCommand}");
    }

    [Fact]
    public void Native_surface_uses_explicit_steer_and_chat_keyboard_semantics()
    {
        var codeBehind = ReadLeaderSurfaceCodeBehind();

        Assert.Contains("$('action').disabled=state.busy&&!canSteer", codeBehind, StringComparison.Ordinal);
        Assert.Contains("e.key==='Enter'&&!e.shiftKey", codeBehind, StringComparison.Ordinal);
        Assert.Contains("command:'steer'", codeBehind, StringComparison.Ordinal);
        Assert.Contains("id=\"access\"", codeBehind, StringComparison.Ordinal);
        Assert.Contains("command:'toggle_access_mode'", codeBehind, StringComparison.Ordinal);
        Assert.Contains("meta.accessMode==='restricted'", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Working · type to steer", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void Native_surface_projects_worker_draft_confirmation_and_confirm_command()
    {
        var codeBehind = ReadLeaderSurfaceCodeBehind();

        Assert.Contains("draftConfirmation", codeBehind, StringComparison.Ordinal);
        Assert.Contains("workerResources", codeBehind, StringComparison.Ordinal);
        Assert.Contains("command:'confirm_draft'", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ConfirmDraftAsync", codeBehind, StringComparison.Ordinal);
        Assert.Contains("id=\"draft\"", codeBehind, StringComparison.Ordinal);
        Assert.Contains("确认并启动 Worker", codeBehind, StringComparison.Ordinal);
        Assert.Contains("regenerate_draft", codeBehind, StringComparison.Ordinal);
        Assert.Contains("draftFallback", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void Native_surface_serializes_commands_for_avalonia_web_message_bridge()
    {
        var codeBehind = ReadLeaderSurfaceCodeBehind();

        Assert.Contains("window.invokeCSharpAction(JSON.stringify(m))", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("window.invokeCSharpAction(m)", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void Native_surface_reinitializes_webview_on_visual_tree_reattach()
    {
        var codeBehind = ReadLeaderSurfaceCodeBehind();

        Assert.Contains("OnAttachedToVisualTree", codeBehind, StringComparison.Ordinal);
        Assert.Contains("_ready = false", codeBehind, StringComparison.Ordinal);
        Assert.Contains("_surface.NavigateToString(LoadSurfaceHtml()", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Subscribe(DataContext as LeaderPaneViewModel)", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void Native_surface_detach_does_not_dispose_or_replace_leader_session()
    {
        var codeBehind = ReadLeaderSurfaceCodeBehind();

        Assert.Contains("_ready = false", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("new AgentSession", codeBehind, StringComparison.Ordinal);
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
    public void Leader_status_and_governance_area_is_height_bounded_and_scrollable()
    {
        var markup = ReadLeaderView();

        Assert.Contains("x:Name=\"LeaderStatusScrollViewer\"", markup, StringComparison.Ordinal);
        Assert.Contains("MaxHeight=\"360\"", markup, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", markup, StringComparison.Ordinal);
        Assert.True(
            markup.IndexOf("x:Name=\"LeaderStatusScrollViewer\"", StringComparison.Ordinal) <
            markup.IndexOf("<views:LeaderAgentSurfaceView Grid.Row=\"2\"", StringComparison.Ordinal));
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

        Assert.Contains("Content=\"{Binding [Leader.ChangeBrain]}\"", markup, StringComparison.Ordinal);
        Assert.Contains("OpenBrainPickerCommand", markup, StringComparison.Ordinal);
        Assert.Contains("HasPendingRotationDecision", markup, StringComparison.Ordinal);
        Assert.Contains("Content=\"{Binding [Leader.ContinuePrevious]}\"", markup, StringComparison.Ordinal);
        Assert.Contains("ContinuePreviousCommand", markup, StringComparison.Ordinal);
        Assert.Contains("Content=\"{Binding [Leader.StartFresh]}\"", markup, StringComparison.Ordinal);
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

    private static string ReadLeaderSurfaceCodeBehind()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        return File.ReadAllText(Path.Combine(repositoryRoot, "src", "Workbench.App", "Views", "Panes", "LeaderAgentSurfaceView.axaml.cs"));
    }

    private static int CountOccurrences(string value, string fragment) =>
        value.Split(fragment, StringSplitOptions.None).Length - 1;

    private static string ReadLeaderViewCodeBehind()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        return File.ReadAllText(Path.Combine(repositoryRoot, "src", "Workbench.App", "Views", "Panes", "LeaderPaneView.axaml.cs"));
    }
}
