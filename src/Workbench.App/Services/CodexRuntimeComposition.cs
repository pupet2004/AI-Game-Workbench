using Workbench.Runtime.Providers;
using Workbench.Runtime.Providers.Codex;
using Workbench.Runtime.Runtime;

namespace Workbench.App.Services;

internal static class CodexRuntimeComposition
{
    private const string ExecutableVariable = "WORKBENCH_CODEX_EXECUTABLE";
    private const string EntryVariable = "WORKBENCH_CODEX_ENTRY";
    private const string WorkingDirectoryVariable = "WORKBENCH_CODEX_CWD";

    public static async Task<IAgentRuntime> ConnectAsync(CancellationToken cancellationToken)
    {
        var options = CreateOptions(
            Environment.GetEnvironmentVariable,
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppContext.BaseDirectory);

        if (!File.Exists(options.ExecutablePath) || !File.Exists(options.Arguments[0]))
        {
            throw new FileNotFoundException("The local Codex CLI installation was not found.");
        }

        return await CodexAgentRuntime.ConnectAsync(
            options,
            ProviderAccountId.New(),
            cancellationToken);
    }

    internal static CodexAppServerOptions CreateOptions(
        Func<string, string?> getEnvironmentVariable,
        string programFilesPath,
        string applicationDataPath,
        string appBaseDirectory)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

        var executable = getEnvironmentVariable(ExecutableVariable)
            ?? Path.Combine(programFilesPath, "nodejs", "node.exe");
        var entry = getEnvironmentVariable(EntryVariable)
            ?? Path.Combine(
                applicationDataPath,
                "npm",
                "node_modules",
                "@openai",
                "codex",
                "bin",
                "codex.js");
        var workingDirectory = getEnvironmentVariable(WorkingDirectoryVariable)
            ?? appBaseDirectory;

        return new CodexAppServerOptions(
            executable,
            [entry, "app-server", "--stdio"],
            workingDirectory,
            TimeSpan.FromMinutes(2));
    }
}
