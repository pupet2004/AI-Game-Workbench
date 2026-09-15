using Workbench.Runtime.Agents;
using Workbench.Runtime.Providers.OpenCode;

namespace Workbench.Runtime.Tests.Providers.OpenCode;

public sealed class OpenCodePromptTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Unstructured_requests_keep_the_original_prompt(string? schema)
    {
        var request = new AgentRequest("Discuss the next step.", schema);

        Assert.Equal(request.Text, OpenCodeAcpAgentRuntime.BuildPromptText(request));
    }

    [Fact]
    public void Structured_requests_deliver_the_exact_schema_without_granting_authority()
    {
        const string schema = """
            {"type":"object","required":["response","draft_proposal"],"properties":{"response":{"type":"string"},"draft_proposal":{"type":"null"}}}
            """;
        var request = new AgentRequest("Plan only. Wait for confirmation.", schema);

        var prompt = OpenCodeAcpAgentRuntime.BuildPromptText(request);

        Assert.StartsWith(request.Text, prompt);
        Assert.EndsWith(schema, prompt);
        Assert.Contains("Return only valid JSON", prompt);
        Assert.Contains("does not authorize execution or project acceptance", prompt);
    }
}
