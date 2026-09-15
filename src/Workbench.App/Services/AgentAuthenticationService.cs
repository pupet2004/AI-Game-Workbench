using System.Diagnostics;
using Workbench.Storage.Settings;

namespace Workbench.App.Services;

public sealed class AgentAuthenticationService
{
    public async Task<AgentAuthenticationResult> SignInAsync(
        AgentRuntimeSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!string.Equals(settings.AgentId, "opencode", StringComparison.OrdinalIgnoreCase))
            return new(false, "This provider does not expose a Workbench sign-in flow.");

        var options = OpenCodeRuntimeComposition.CreateOptions(
            Environment.GetEnvironmentVariable,
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppContext.BaseDirectory,
            settings.NormalizedExecutablePath);
        if (!File.Exists(options.ExecutablePath))
            return new(false, "The local OpenCode CLI installation was not found.");

        try
        {
            using var process = Process.Start(CreateStartInfo(options.ExecutablePath, options.WorkingDirectory))
                ?? throw new InvalidOperationException("OpenCode sign-in could not be started.");
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode == 0
                ? new(true, "OpenCode sign-in finished. Checking provider readiness.")
                : new(false, $"OpenCode sign-in exited with code {process.ExitCode}.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new(false, "OpenCode sign-in could not be completed.");
        }
    }

    internal static ProcessStartInfo CreateStartInfo(string executablePath, string workingDirectory) =>
        new()
        {
            FileName = executablePath,
            Arguments = "providers login",
            WorkingDirectory = workingDirectory,
            UseShellExecute = true
        };
}

public sealed record AgentAuthenticationResult(bool Succeeded, string Message);
