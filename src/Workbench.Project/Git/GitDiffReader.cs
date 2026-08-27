using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Workbench.Project.Git;

public sealed record GitChangedFile(string Path, int Added, int Removed, string Diff);

public sealed class GitDiffReader
{
    public async Task<IReadOnlyList<GitChangedFile>> ReadAsync(string workingDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        var result = await RunAsync(workingDirectory, ["diff", "--no-color", "--numstat"], cancellationToken);
        if (result.ExitCode != 0) return [];
        var files = new List<GitChangedFile>();
        foreach (var line in result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t', 3);
            if (parts.Length != 3) continue;
            var path = parts[2].Trim();
            if (string.IsNullOrWhiteSpace(path)) continue;
            _ = int.TryParse(parts[0], out var added);
            _ = int.TryParse(parts[1], out var removed);
            var diff = await RunAsync(workingDirectory, ["diff", "--no-color", "--", path], cancellationToken);
            files.Add(new GitChangedFile(path, added, removed, diff.StandardOutput));
        }
        return files;
    }

    private static async Task<GitCommandResult> RunAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo { FileName = "git", WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(startInfo);
            if (process is null) return new(-1, string.Empty, "Unable to start git.");
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return new(process.ExitCode, await output, await error);
        }
        catch (Win32Exception exception)
        {
            return new(-1, string.Empty, exception.Message);
        }
    }

    private sealed record GitCommandResult(int ExitCode, string StandardOutput, string StandardError);
}
