using Workbench.App.Memory;
using Workbench.Storage.Memory;

namespace Workbench.App.Tests;

public sealed class LeaderMemoryPolicyPayloadParserTests
{
    [Fact]
    public void Explicit_policy_parses_optional_writes_and_exact_continuity_selection()
    {
        const string payload = """
            {
              "brain_handoff":"CURRENT FOCUS: continue Slice C",
              "daily_summary":{"local_date":"2026-08-14","content":"Decision and tradeoff recorded.","expected_revision":null,"sources":[]},
              "total_continuity_budget_utf8_bytes":12000,
              "continuity_selection":[
                {"ordinal":0,"kind":"DailySummary","reference":"daily:2026-08-14","max_utf8_bytes":5000,"selector":null},
                {"ordinal":1,"kind":"RecentConversation","reference":"raw:00000000-0000-0000-0000-000000000001","max_utf8_bytes":4000,"selector":{"beforeSequence":4,"maxMessages":2,"maxUtf8Bytes":4000}}
              ]
            }
            """;

        var parsed = LeaderMemoryPolicyPayloadParser.TryParse(Guid.Parse("00000000-0000-0000-0000-0000000000AA"), payload, out var decision);

        Assert.True(parsed);
        Assert.Equal("CURRENT FOCUS: continue Slice C", decision.BrainHandoff);
        Assert.Equal(new DateOnly(2026, 8, 14), decision.DailySummary!.LocalDate);
        Assert.Equal(12000, decision.TotalContinuityBudgetUtf8Bytes);
        Assert.Equal([ContinuityMaterialKind.DailySummary, ContinuityMaterialKind.RecentConversation], decision.ContinuitySelection.Select(item => item.Kind));
        Assert.Contains("beforeSequence", decision.ContinuitySelection[1].SelectorJson, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"brain_handoff\":null,\"daily_summary\":null,\"total_continuity_budget_utf8_bytes\":null,\"continuity_selection\":[],\"unexpected\":true}")]
    [InlineData("{\"brain_handoff\":null,\"daily_summary\":null,\"total_continuity_budget_utf8_bytes\":null,\"continuity_selection\":[{\"ordinal\":0,\"kind\":\"DailySummary\",\"reference\":\"daily:2026-08-14\",\"max_utf8_bytes\":100,\"selector\":null}]} ")]
    public void Invalid_or_incomplete_policy_is_rejected(string payload)
    {
        Assert.False(LeaderMemoryPolicyPayloadParser.TryParse(Guid.NewGuid(), payload, out _));
    }
}
