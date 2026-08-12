using System.Diagnostics;
using Workbench.Project.Tests.Support;

namespace Workbench.Project.Tests.Git;

internal sealed class TemporaryGitRepository : IAsyncDisposable
{
    private readonly TemporaryDirectory _directory;

    private TemporaryGitRepository(TemporaryDirectory directory)
    {
        _directory = directory;
    }

    public string Path => _directory.Path;

    public static async Task<TemporaryGitRepository> CreateAsync(string? name = null)
    {
        var repository = new TemporaryGitRepository(new TemporaryDirectory(name ?? "repository"));
        await repository.RunGitAsync("init", "-b", "test-main");
        await repository.RunGitAsync("config", "user.name", "Workbench Tests");
        await repository.RunGitAsync("config", "user.email", "workbench-tests@local.invalid");
        await File.WriteAllTextAsync(System.IO.Path.Combine(repository.Path, "README.md"), "test");
        await repository.RunGitAsync("add", "README.md");
        await repository.RunGitAsync("commit", "-m", "initial");
        return repository;
    }

    public Task<string> RunGitAsync(params string[] arguments) => RunProcessAsync("git", arguments, Path);

    public async ValueTask DisposeAsync()
    {
        await Task.Yield();
        _directory.Dispose();
    }

    private static async Task<string> RunProcessAsync(string fileName, IEnumerable<string> arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start git.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await standardOutput;
        var error = await standardError;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(error);
        }

        return output;
    }
}
