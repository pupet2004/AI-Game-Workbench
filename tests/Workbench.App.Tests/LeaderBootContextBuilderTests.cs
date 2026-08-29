using System.Text;
using Workbench.App.Leader;
using Workbench.Storage.Memory;
using CoreProject = Workbench.Core.Projects.Project;
using ProjectType = Workbench.Core.Projects.ProjectType;

namespace Workbench.App.Tests;

public sealed class LeaderBootContextBuilderTests
{
    [Fact]
    public void Boot_context_requires_structured_delegation_proposals_and_confirmation()
    {
        var text = LeaderBootContextBuilder.Build(
            Project, [], [], null, "Please delegate this task.").Text;

        Assert.Contains("draft_proposal", text, StringComparison.Ordinal);
        Assert.Contains("wait for user confirmation", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do not claim a Worker started, executed", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WORKER SCOPE", text, StringComparison.Ordinal);
        Assert.Contains("runtime-local sub-agents", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Assignment, Attempt, Execution", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Both_boot_paths_define_stage_end_library_judgment_and_user_confirmation_semantics()
    {
        var legacy = LeaderBootContextBuilder.Build(Project, [], [], null, "Close the stage.").Text;
        var selected = LeaderBootContextBuilder.BuildSelected(Project, [], "Close the stage.").Text;

        foreach (var text in new[] { legacy, selected })
        {
            Assert.Contains("LIBRARY PROPOSAL CONTRACT", text, StringComparison.Ordinal);
            Assert.Contains("long-lived factual evolution", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("stage", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("memory_commands", text, StringComparison.Ordinal);
            Assert.Contains("user confirmation", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Daily Summary", text, StringComparison.Ordinal);
            Assert.Contains("reasons", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ordinary turns", text, StringComparison.OrdinalIgnoreCase);
        }
    }
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-08-13T00:00:00+00:00");
    private static readonly CoreProject Project = new(
        Guid.Parse("10000000-0000-4000-8000-000000000001"),
        "Memory Project",
        "C:/Games/Memory Project",
        ProjectType.Generic,
        null,
        T0,
        T0);

    [Fact]
    public void Build_creates_one_authority_ordered_envelope_with_original_user_message()
    {
        var request = LeaderBootContextBuilder.Build(
            Project,
            [Memory("Formal", "Engine", "Use Godot.", certifiedAt: T0)],
            [Memory("Learned", "Phase", "M1.5C is underway.")],
            "CURRENT FOCUS\nContinue boot work.",
            "Ship the next slice.");
        var text = NormalizeNewlines(request.Text);

        Assert.Equal(1, Count(text, "WORKBENCH PROJECT CONTEXT"));
        Assert.Contains("Name: Memory Project", text, StringComparison.Ordinal);
        Assert.Contains("Root: C:/Games/Memory Project", text, StringComparison.Ordinal);
        Assert.Contains("USER-CERTIFIED PROJECT MEMORY", text, StringComparison.Ordinal);
        Assert.Contains("authoritative", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AI-SYNTHESIZED PROJECT MEMORY", text, StringComparison.Ordinal);
        Assert.Contains("provisional", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PREVIOUS SESSION HANDOFF", text, StringComparison.Ordinal);
        Assert.EndsWith("CURRENT USER MESSAGE\nShip the next slice.", text, StringComparison.Ordinal);
        Assert.True(text.IndexOf("CURRENT USER MESSAGE", StringComparison.Ordinal) >
                    text.IndexOf("PREVIOUS SESSION HANDOFF", StringComparison.Ordinal));
    }

    [Fact]
    public void Formal_topic_suppresses_learned_after_trim_case_and_whitespace_normalization()
    {
        var request = LeaderBootContextBuilder.Build(
            Project,
            [Memory("Formal", " Runtime   Choice ", "Use the certified runtime.", certifiedAt: T0)],
            [
                Memory("Learned", "runtime choice", "LEARNED_CONFLICT_MUST_NOT_APPEAR"),
                Memory("Learned", "Input", "Keep explicit user text.")
            ],
            null,
            "Do it.");
        var text = NormalizeNewlines(request.Text);

        Assert.Contains("Use the certified runtime.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("LEARNED_CONFLICT_MUST_NOT_APPEAR", text, StringComparison.Ordinal);
        Assert.Contains("Keep explicit user text.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Active_formal_topic_suppresses_learned_even_when_the_formal_item_exceeds_its_budget()
    {
        var request = LeaderBootContextBuilder.Build(
            Project,
            [Memory("Formal", "Runtime Choice", new string('界', 4001), certifiedAt: T0)],
            [Memory("Learned", " runtime   choice ", "LOWER_AUTHORITY_MUST_NOT_LEAK")],
            null,
            "Choose now.");

        Assert.DoesNotContain("LOWER_AUTHORITY_MUST_NOT_LEAK", request.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Deterministic_order_uses_certification_update_and_stable_id_ties()
    {
        var olderCertification = T0.AddDays(-1);
        var request = LeaderBootContextBuilder.Build(
            Project,
            [
                Memory("Formal", "Second", "second", Guid.Parse("30000000-0000-4000-8000-000000000002"), T0, T0),
                Memory("Formal", "Third", "third", Guid.Parse("30000000-0000-4000-8000-000000000003"), T0, T0),
                Memory("Formal", "Old", "old", Guid.Parse("30000000-0000-4000-8000-000000000001"), T0.AddDays(1), olderCertification)
            ],
            [
                Memory("Learned", "Learned B", "learned-b", Guid.Parse("40000000-0000-4000-8000-000000000002"), T0),
                Memory("Learned", "Learned A", "learned-a", Guid.Parse("40000000-0000-4000-8000-000000000001"), T0)
            ],
            null,
            "continue");
        var text = NormalizeNewlines(request.Text);

        AssertOrder(text, "second", "third", "old");
        AssertOrder(text, "learned-a", "learned-b");
    }

    [Fact]
    public void Budgets_skip_oversized_items_without_mid_item_truncation_and_bound_unicode_handoff()
    {
        var oversized = new string('界', 4001);
        var boundedHandoff = new string('续', 4000);
        var request = LeaderBootContextBuilder.Build(
            Project,
            [
                Memory("Formal", "Oversized", oversized, updatedAt: T0.AddMinutes(1), certifiedAt: T0.AddMinutes(1)),
                Memory("Formal", "Fits", "FORMAL_FITS", updatedAt: T0, certifiedAt: T0)
            ],
            [Memory("Learned", "Learned", new string('忆', 3000))],
            boundedHandoff,
            "用户原文");
        var text = NormalizeNewlines(request.Text);

        Assert.DoesNotContain("Oversized", text, StringComparison.Ordinal);
        Assert.DoesNotContain(oversized[..100], text, StringComparison.Ordinal);
        Assert.Contains("FORMAL_FITS", text, StringComparison.Ordinal);
        Assert.DoesNotContain("AI-SYNTHESIZED PROJECT MEMORY", text, StringComparison.Ordinal);
        Assert.DoesNotContain('\uFFFD', text);
        Assert.True(Encoding.UTF8.GetByteCount(Section(text, "PREVIOUS SESSION HANDOFF\n", "\n\nCURRENT USER MESSAGE")) <=
                    LeaderBootContextBuilder.MaxHandoffUtf8Bytes);
    }

    [Fact]
    public void Formal_section_authority_note_and_items_together_stay_within_the_section_budget()
    {
        var request = LeaderBootContextBuilder.Build(
            Project,
            [Memory("Formal", "Near Limit", new string('界', 3980), certifiedAt: T0)],
            [],
            null,
            "question");
        var text = NormalizeNewlines(request.Text);

        var section = Section(text, "USER-CERTIFIED PROJECT MEMORY\n", "\n\nCURRENT USER MESSAGE");
        Assert.True(Encoding.UTF8.GetByteCount(section) <= LeaderBootContextBuilder.MaxFormalUtf8Bytes);
    }

    [Fact]
    public void Empty_memory_and_handoff_omit_empty_sections_but_keep_project_and_user()
    {
        var request = LeaderBootContextBuilder.Build(Project, [], [], null, "First question");
        var text = NormalizeNewlines(request.Text);

        Assert.Contains("WORKBENCH PROJECT CONTEXT", text, StringComparison.Ordinal);
        Assert.Contains("PROJECT\nName: Memory Project", text, StringComparison.Ordinal);
        Assert.DoesNotContain("USER-CERTIFIED PROJECT MEMORY", text, StringComparison.Ordinal);
        Assert.DoesNotContain("AI-SYNTHESIZED PROJECT MEMORY", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PREVIOUS SESSION HANDOFF", text, StringComparison.Ordinal);
        Assert.EndsWith("CURRENT USER MESSAGE\nFirst question", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Non_active_wrong_layer_and_foreign_project_items_are_excluded()
    {
        var foreignProject = Project.Id == Guid.Empty ? Guid.NewGuid() : Guid.Empty;
        var items = new[]
        {
            Memory("Candidate", "Candidate", "CANDIDATE_MUST_NOT_APPEAR"),
            Memory("Formal", "Rejected", "REJECTED_MUST_NOT_APPEAR") with { Status = "Rejected", CertifiedAt = T0 },
            Memory("Learned", "Superseded", "SUPERSEDED_MUST_NOT_APPEAR") with { Status = "Superseded" },
            Memory("Formal", "Foreign", "FOREIGN_MUST_NOT_APPEAR", certifiedAt: T0) with { ProjectId = foreignProject }
        };

        var request = LeaderBootContextBuilder.Build(Project, items, items, null, "question");

        Assert.DoesNotContain("MUST_NOT_APPEAR", request.Text, StringComparison.Ordinal);
    }

    private static ProjectMemoryItem Memory(
        string layer,
        string topic,
        string content,
        Guid? id = null,
        DateTimeOffset? updatedAt = null,
        DateTimeOffset? certifiedAt = null)
    {
        var updated = updatedAt ?? T0;
        return new ProjectMemoryItem(
            id ?? Guid.NewGuid(),
            Project.Id,
            layer,
            topic,
            content,
            "Active",
            updated,
            updated,
            certifiedAt);
    }

    private static int Count(string value, string needle) =>
        (value.Length - value.Replace(needle, string.Empty, StringComparison.Ordinal).Length) / needle.Length;

    private static string NormalizeNewlines(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static void AssertOrder(string value, params string[] needles)
    {
        var previous = -1;
        foreach (var needle in needles)
        {
            var current = value.IndexOf(needle, StringComparison.Ordinal);
            Assert.True(current > previous, $"Expected '{needle}' after the previous marker.");
            previous = current;
        }
    }

    private static string Section(string value, string start, string end)
    {
        var first = value.IndexOf(start, StringComparison.Ordinal) + start.Length;
        var last = value.IndexOf(end, first, StringComparison.Ordinal);
        return value[first..last];
    }
}
