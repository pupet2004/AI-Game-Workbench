using Workbench.Runtime.Providers.Codex;

namespace Workbench.Runtime.Tests.Providers.Codex;

public sealed class CodexAppServerProcessTests
{
    [Fact]
    public void Codex_process_uses_direct_redirected_hidden_launch()
    {
        var options = new CodexAppServerOptions(
            "C:/Tools/node.exe",
            ["C:/Tools/codex.js", "app-server", "--stdio"],
            "C:/Projects/Workbench");

        var startInfo = CodexAppServerProcess.CreateStartInfo(options);

        Assert.Equal("C:/Tools/node.exe", startInfo.FileName);
        Assert.Equal(["C:/Tools/codex.js", "app-server", "--stdio"], startInfo.ArgumentList);
        Assert.True(startInfo.RedirectStandardInput);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
    }
}
