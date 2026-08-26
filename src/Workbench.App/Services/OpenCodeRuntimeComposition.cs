using Workbench.Runtime.Providers;
using Workbench.Runtime.Providers.OpenCode;
using Workbench.Runtime.Runtime;

namespace Workbench.App.Services;

public static class OpenCodeRuntimeComposition
{
    private static readonly ProviderId OpenCodeProviderId = new("opencode");
    private static readonly ProviderAccountId LocalAccountId =
        new(Guid.Parse("0c0de001-4a49-4741-8d45-574f524b424e"));
    private const string ExecutableVariable = "WORKBENCH_OPENCODE_EXECUTABLE";
    private const string WorkingDirectoryVariable = "WORKBENCH_OPENCODE_CWD";

    public static async Task<IAgentRuntime> ConnectAsync(CancellationToken cancellationToken)
    {
        var options = CreateOptions(
            Environment.GetEnvironmentVariable,
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppContext.BaseDirectory);

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
        string appBaseDirectory)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

        var executable = getEnvironmentVariable(ExecutableVariable)
            ?? Path.Combine(applicationDataPath, "npm", "opencode.cmd");
        var workingDirectory = getEnvironmentVariable(WorkingDirectoryVariable) ?? appBaseDirectory;
        return new OpenCodeAcpOptions(executable, workingDirectory);
    }
}
