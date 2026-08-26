using Workbench.Runtime.Providers;
using Workbench.Runtime.Providers.Codex;
using Workbench.Runtime.Runtime;
using Workbench.Storage.Settings;

namespace Workbench.App.Services;

internal static class CodexRuntimeComposition
{
    private static readonly ProviderId CodexProviderId = new("codex");
    private static readonly ProviderAccountId LocalAccountId =
        new(Guid.Parse("c0de0001-4a49-4741-8d45-574f524b424e"));
    private const string ExecutableVariable = "WORKBENCH_CODEX_EXECUTABLE";
    private const string EntryVariable = "WORKBENCH_CODEX_ENTRY";
    private const string WorkingDirectoryVariable = "WORKBENCH_CODEX_CWD";
    private const string CliPathVariable = "CODEX_CLI_PATH";

    public static async Task<IAgentRuntime> ConnectAsync(CancellationToken cancellationToken)
        => await ConnectAsync(new AgentRuntimeSettings("codex", true, null), cancellationToken);

    public static async Task<IAgentRuntime> ConnectAsync(
        AgentRuntimeSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var options = CreateOptions(
            Environment.GetEnvironmentVariable,
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppContext.BaseDirectory,
            settings.NormalizedExecutablePath);

        if (File.Exists(options.ExecutablePath) && File.Exists(options.Arguments[0]))
        {
            return await CodexAgentRuntime.ConnectAsync(
                options,
                CreateLocalAccountSummary().Id,
                cancellationToken);
        }

        var standaloneOptions = CreateStandaloneOptions(
            Environment.GetEnvironmentVariable,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppContext.BaseDirectory,
            settings.NormalizedExecutablePath);
        if (!File.Exists(standaloneOptions.ExecutablePath))
        {
            throw new FileNotFoundException("The local Codex CLI installation was not found.");
        }

        return await CodexAgentRuntime.ConnectAsync(
            standaloneOptions,
            CreateLocalAccountSummary().Id,
            cancellationToken);
    }

    internal static ProviderAccountSummary CreateLocalAccountSummary() =>
        new(LocalAccountId, CodexProviderId, "Local Codex Account", true);

    internal static CodexAppServerOptions CreateOptions(
        Func<string, string?> getEnvironmentVariable,
        string programFilesPath,
        string applicationDataPath,
        string appBaseDirectory,
        string? executableOverride = null)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

        var executable = executableOverride
            ?? getEnvironmentVariable(ExecutableVariable)
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

    internal static CodexAppServerOptions CreateStandaloneOptions(
        Func<string, string?> getEnvironmentVariable,
        string localApplicationDataPath,
        string appBaseDirectory,
        string? executableOverride = null)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

        var executable = executableOverride
            ?? getEnvironmentVariable(CliPathVariable)
            ?? Path.Combine(localApplicationDataPath, "Programs", "OpenAI", "Codex", "bin", "codex.exe");
        var workingDirectory = getEnvironmentVariable(WorkingDirectoryVariable) ?? appBaseDirectory;
        return new CodexAppServerOptions(
            executable,
            ["app-server", "--stdio"],
            workingDirectory,
            TimeSpan.FromMinutes(2));
    }
}
