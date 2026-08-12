namespace Workbench.Project.Git;

public sealed class GitCliInspector : IGitInspector
{
    private readonly GitCommandRunner _commandRunner;

    public GitCliInspector()
        : this(new GitCommandRunner())
    {
    }

    internal GitCliInspector(GitCommandRunner commandRunner)
    {
        _commandRunner = commandRunner;
    }

    public async Task<GitSnapshot> InspectAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        var root = await _commandRunner.RunAsync(
            projectPath,
            ["rev-parse", "--show-toplevel"],
            cancellationToken);

        if (!root.GitInstalled)
        {
            return new GitSnapshot(false, false, null, null, null, false, false, "Git executable was not found.");
        }

        if (root.ExitCode != 0)
        {
            return new GitSnapshot(true, false, null, null, null, false, false, null);
        }

        var head = await _commandRunner.RunAsync(projectPath, ["rev-parse", "HEAD"], cancellationToken);
        var branch = await _commandRunner.RunAsync(projectPath, ["branch", "--show-current"], cancellationToken);
        var status = await _commandRunner.RunAsync(projectPath, ["status", "--short"], cancellationToken);
        if (head.ExitCode != 0 || branch.ExitCode != 0 || status.ExitCode != 0)
        {
            return new GitSnapshot(true, true, NormalizePath(root.StandardOutput), null, null, false, false, "Git inspection failed.");
        }

        var branchName = branch.StandardOutput.Trim();
        return new GitSnapshot(
            true,
            true,
            NormalizePath(root.StandardOutput),
            head.StandardOutput.Trim(),
            string.IsNullOrEmpty(branchName) ? null : branchName,
            string.IsNullOrEmpty(branchName),
            !string.IsNullOrWhiteSpace(status.StandardOutput),
            null);
    }

    private static string NormalizePath(string path) => Path.GetFullPath(path.Trim());
}
