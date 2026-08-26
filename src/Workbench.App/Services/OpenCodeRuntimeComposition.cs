using Workbench.Runtime.Providers;
using Workbench.Runtime.Providers.OpenCode;
using Workbench.Runtime.Runtime;
using Workbench.Storage.Settings;

namespace Workbench.App.Services;

public static class OpenCodeRuntimeComposition
{
    private static readonly ProviderId OpenCodeProviderId = new("opencode");
    private static readonly ProviderAccountId LocalAccountId =
        new(Guid.Parse("0c0de001-4a49-4741-8d45-574f524b424e"));
    private const string ExecutableVariable = "WORKBENCH_OPENCODE_EXECUTABLE";
    private const string WorkingDirectoryVariable = "WORKBENCH_OPENCODE_CWD";

    public static async Task<IAgentRuntime> ConnectAsync(CancellationToken cancellationToken)
        => await ConnectAsync(new AgentRuntimeSettings("opencode", true, null), cancellationToken);

    public static async Task<IAgentRuntime> ConnectAsync(
        AgentRuntimeSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var options = CreateOptions(
            Environment.GetEnvironmentVariable,
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppContext.BaseDirectory,
            settings.NormalizedExecutablePath);

        if (!File.Exists(options.ExecutablePath))
        {
            throw new FileNotFoundException("The local OpenCode CLI installation was not found.");
        }

        return await OpenCodeAcpAgentRuntime.ConnectAsync(options, LocalAccountId, cancellationToken);
    }

    internal static ProviderAccountSummary CreateLocalAccountSummary() =>
        new(LocalAccountId, OpenCodeProviderId, "Local OpenCode Account", true);

    internal static OpenCodeAcpOptions CreateOptions(
        Func<string, string?> getEnvironmentVariable,
        string applicationDataPath,
        string appBaseDirectory,
        string? executableOverride = null)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

        var executable = executableOverride
            ?? getEnvironmentVariable(ExecutableVariable)
            ?? Path.Combine(applicationDataPath, "npm", "opencode.cmd");
        var workingDirectory = getEnvironmentVariable(WorkingDirectoryVariable) ?? appBaseDirectory;
        return new OpenCodeAcpOptions(executable, workingDirectory);
    }
}
