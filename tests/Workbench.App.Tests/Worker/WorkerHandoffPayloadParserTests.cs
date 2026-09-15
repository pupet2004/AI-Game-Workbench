using System.Text.Json;
using Workbench.App.Worker;

namespace Workbench.App.Tests.Worker;

public sealed class WorkerHandoffPayloadParserTests
{
    private const string FinalReport = """
        {"Kind":"FinalReport","Message":"Reset added.","ValidationSummary":"Godot exited 0.","ProposedChanges":["Reset clears the score; +2 remains."]}
        """;

    [Theory]
    [InlineData("Inspecting files.Now validating.\n", "")]
    [InlineData("Validation done.\n```json\n", "\n```")]
    [InlineData("Checked {not JSON}.\n", "")]
    public void Concatenated_progress_preserves_the_explicit_terminal_report(string prefix, string suffix)
    {
        Assert.True(WorkerHandoffPayloadParser.TryParse(prefix + FinalReport + suffix, out var payload));
        Assert.Equal(WorkerHandoffKind.FinalReport, payload!.Kind);
        Assert.Equal("Reset clears the score; +2 remains.", Assert.Single(payload.ProposedChanges));
        Assert.Equal("Godot exited 0.", payload.ValidationSummary);
    }

    [Fact]
    public void Ambiguous_nonterminal_nested_and_invalid_reports_are_not_promoted()
    {
        Assert.False(WorkerHandoffPayloadParser.TryParse(FinalReport + FinalReport, out _));
        Assert.False(WorkerHandoffPayloadParser.TryParse(FinalReport + "\nActually, this was only a plan.", out _));
        Assert.False(WorkerHandoffPayloadParser.TryParse("{\"example\":" + FinalReport + "}", out _));
        Assert.False(WorkerHandoffPayloadParser.TryParse("Reset is implemented and verified.", out _));
        Assert.False(WorkerHandoffPayloadParser.TryParse(FinalReport.Replace("FinalReport", "999"), out _));
        Assert.False(WorkerHandoffPayloadParser.TryParse(FinalReport[..^1], out _));
    }

    [Theory]
    [InlineData("FinalReport")]
    [InlineData("NeedsLeaderDecision")]
    public void Output_contract_matches_existing_handoff_parser(string kind)
    {
        var json = JsonSerializer.Serialize(new
        {
            Kind = kind,
            Message = "Changed the score increment.",
            ValidationSummary = "Godot validation passed.",
            ProposedChanges = new[] { "The score button adds 2 per click." }
        });
        using var schema = JsonDocument.Parse(WorkerHandoffPayloadParser.OutputSchema);
        using var document = JsonDocument.Parse(json);
        Assert.False(schema.RootElement.GetProperty("additionalProperties").GetBoolean());
        var properties = schema.RootElement.GetProperty("properties");
        foreach (var required in schema.RootElement.GetProperty("required").EnumerateArray())
        {
            Assert.True(document.RootElement.TryGetProperty(required.GetString()!, out _));
            Assert.True(properties.TryGetProperty(required.GetString()!, out _));
        }
        Assert.Contains(kind, properties.GetProperty("Kind").GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
        Assert.True(WorkerHandoffPayloadParser.TryParse(json, out var payload));
        Assert.Equal(kind, payload!.Kind.ToString());
        Assert.Equal("Godot validation passed.", payload.ValidationSummary);
        Assert.Equal("The score button adds 2 per click.", Assert.Single(payload.ProposedChanges));
    }

    [Fact]
    public void Empty_proposals_and_unperformed_validation_remain_explicit()
    {
        Assert.True(WorkerHandoffPayloadParser.TryParse(
            """{"Kind":"NeedsLeaderDecision","Message":"Need scope clarification.","ValidationSummary":null,"ProposedChanges":[]}""",
            out var payload));
        Assert.Null(payload!.ValidationSummary);
        Assert.Empty(payload.ProposedChanges);
        Assert.False(WorkerHandoffPayloadParser.TryParse("The score button adds 2 per click.", out _));
    }
}
