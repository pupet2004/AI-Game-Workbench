using Workbench.App.Services;

namespace Workbench.App.Tests;

public sealed class AgentAuthenticationServiceTests
{
    [Fact]
    public void OpenCode_sign_in_is_provider_owned_and_uses_visible_shell()
    {
        var info = AgentAuthenticationService.CreateStartInfo(
            @"C:\Tools\opencode.cmd",
            @"C:\Projects\Counter");

        Assert.Equal(@"C:\Tools\opencode.cmd", info.FileName);
        Assert.Equal("providers login", info.Arguments);
        Assert.Equal(@"C:\Projects\Counter", info.WorkingDirectory);
        Assert.True(info.UseShellExecute);
    }

    [Fact]
    public async Task Unsupported_provider_does_not_start_a_process()
    {
        var result = await new AgentAuthenticationService().SignInAsync(
            new("codex", true, null));

        Assert.False(result.Succeeded);
        Assert.Contains("does not expose", result.Message, StringComparison.OrdinalIgnoreCase);
    }
}
