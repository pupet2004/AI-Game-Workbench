using Workbench.App.Leader;
using Workbench.Core.Tasks;

namespace Workbench.App.Tests;

public sealed class LeaderReviewPayloadParserTests
{
    [Fact]
    public void Valid_pass_decision_is_parsed_as_independent_outcome_action_and_depth()
    {
        var json = """{"taskId":"00000000-0000-0000-0000-000000000001","taskRevisionId":"00000000-0000-0000-0000-000000000002","finalReportEventId":"00000000-0000-0000-0000-000000000003","outcome":"PASS","actionLevel":"L1_LOCAL_FIX","reviewDepth":"REPORT_ONLY","summary":"Acceptance met.","nextAction":"None"}""";

        var decision = LeaderReviewPayloadParser.Parse(json);

        Assert.Equal(LeaderReviewOutcome.Pass, decision.Outcome);
        Assert.Equal(LeaderReviewActionLevel.L1LocalFix, decision.ActionLevel);
        Assert.Equal(LeaderReviewDepth.ReportOnly, decision.ReviewDepth);
    }

    [Theory]
    [InlineData("UNKNOWN", "L1_LOCAL_FIX", "REPORT_ONLY")]
    [InlineData("PASS", "UNKNOWN", "REPORT_ONLY")]
    [InlineData("PASS", "L1_LOCAL_FIX", "UNKNOWN")]
    public void Unknown_structured_values_are_rejected(string outcome, string level, string depth)
    {
        var json = $$"""{"taskId":"00000000-0000-0000-0000-000000000001","taskRevisionId":"00000000-0000-0000-0000-000000000002","finalReportEventId":"00000000-0000-0000-0000-000000000003","outcome":"{{outcome}}","actionLevel":"{{level}}","reviewDepth":"{{depth}}","summary":"x","nextAction":"x"}""";
        Assert.Throws<LeaderReviewPayloadException>(() => LeaderReviewPayloadParser.Parse(json));
    }

    [Theory]
    [InlineData("Summary", 601)]
    [InlineData("Issue", 601)]
    [InlineData("NextAction", 601)]
    [InlineData("ImportantNote", 401)]
    public void Oversized_free_text_is_rejected(string field, int length)
    {
        var json = $$"""{"taskId":"00000000-0000-0000-0000-000000000001","taskRevisionId":"00000000-0000-0000-0000-000000000002","finalReportEventId":"00000000-0000-0000-0000-000000000003","outcome":"PASS","actionLevel":"L1_LOCAL_FIX","reviewDepth":"REPORT_ONLY","summary":"ok","nextAction":"none","{{field}}":"{{new string('x', length)}}"}""";
        Assert.Throws<LeaderReviewPayloadException>(() => LeaderReviewPayloadParser.Parse(json));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{}")]
    public void Malformed_or_missing_required_payload_is_rejected(string json) =>
        Assert.Throws<LeaderReviewPayloadException>(() => LeaderReviewPayloadParser.Parse(json));
}
