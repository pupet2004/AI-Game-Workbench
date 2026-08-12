using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Workbench.Project.Git;

internal sealed class GitCommandRunner
{
    public async Task<GitCommandResult> RunAsync(
        string workingDirectory,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Win32Exception exception)
        {
            return GitCommandResult.GitUnavailable(exception.Message);
        }

        if (process is null)
        {
            return GitCommandResult.GitUnavailable("Unable to start git.");
        }

        using (process)
        {
            var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                throw;
            }

            return new GitCommandResult(
                true,
                process.ExitCode,
                await standardOutput,
                await standardError);
        }
    }
}

internal sealed record GitCommandResult(bool GitInstalled, int ExitCode, string StandardOutput, string StandardError)
{
    public static GitCommandResult GitUnavailable(string error) => new(false, -1, string.Empty, error);
}
