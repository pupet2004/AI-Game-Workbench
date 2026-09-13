using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Workbench.Core.Tasks;
using Workbench.Storage.Workers;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.Worker;

public enum WorkerCompletionVerificationResult
{
    Passed,
    Failed,
    NotVerifiable
}

public sealed record WorkerVerificationCheck(string Name, WorkerCompletionVerificationResult Result, string Detail);

public sealed record WorkerCompletionVerification(
    Guid ProjectId,
    Guid TaskId,
    Guid TaskRevisionId,
    Guid WorkerExecutionId,
    string Workspace,
    DateTimeOffset VerifiedAt,
    WorkerCompletionVerificationResult Result,
    IReadOnlyList<WorkerVerificationCheck> Checks)
{
    public string ToSummary() => string.Join("; ", Checks.Select(check => $"{check.Name}={check.Result}: {check.Detail}"));
}

public sealed record WorkspaceSnapshot(IReadOnlyDictionary<string, string> Files)
{
    public static async Task<WorkspaceSnapshot?> CaptureAsync(string workspace, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(workspace)) return null;
        var paths = await GitPathsAsync(workspace, cancellationToken) ??
            EnumerateWorkspacePaths(workspace);
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var full = Path.Combine(workspace, path);
            files[path] = File.Exists(full) ? await FingerprintAsync(full, cancellationToken) : "<missing>";
        }
        return new WorkspaceSnapshot(files);
    }

    public static async Task<IReadOnlyList<string>?> ComputeDeltaAsync(string workspace, string baselineJson, CancellationToken cancellationToken = default)
    {
        WorkspaceSnapshot? baseline;
        try { baseline = JsonSerializer.Deserialize<WorkspaceSnapshot>(baselineJson); }
        catch (JsonException) { return null; }
        if (baseline is null) return null;
        var current = await CaptureAsync(workspace, cancellationToken);
        if (current is null) return null;
        var all = baseline.Files.Keys.Concat(current.Files.Keys).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return all.Where(path => !baseline.Files.TryGetValue(path, out var before) ||
                                 !current.Files.TryGetValue(path, out var after) ||
                                 !string.Equals(before, after, StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public string Serialize() => JsonSerializer.Serialize(this);

    private static async Task<string> FingerprintAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static async Task<IReadOnlyList<string>?> GitPathsAsync(string workspace, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo { FileName = "git", WorkingDirectory = workspace, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("ls-files"); info.ArgumentList.Add("--cached"); info.ArgumentList.Add("--others");
        info.ArgumentList.Add("--exclude-standard"); info.ArgumentList.Add("-z");
        try
        {
            using var process = Process.Start(info);
            if (process is null) return null;
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0) return null;
            return output.Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .Select(path => path.Trim().Replace('/', Path.DirectorySeparatorChar))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { return null; }
    }

    private static IReadOnlyList<string> EnumerateWorkspacePaths(string workspace)
    {
        try
        {
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
                ReturnSpecialDirectories = false
            };
            return Directory.EnumerateFiles(workspace, "*", options)
                .Where(path => !IsGeneratedOrMetadataPath(workspace, path))
                .Select(path => Path.GetRelativePath(workspace, path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool IsGeneratedOrMetadataPath(string workspace, string path)
    {
        var relative = Path.GetRelativePath(workspace, path);
        var segments = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment => segment.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                                       segment.Equals(".godot", StringComparison.OrdinalIgnoreCase) ||
                                       segment.Equals(".vs", StringComparison.OrdinalIgnoreCase) ||
                                       segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                                       segment.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                                       segment.Equals("node_modules", StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class WorkerCompletionVerifier
{
    private static readonly Regex BacktickToken = new("`(?<value>[^`]+)`", RegexOptions.Compiled);

    public async Task<WorkerCompletionVerification> VerifyAsync(
        CoreProject project,
        TaskRevision revision,
        StoredWorkerExecution execution,
        string? finalReport,
        IReadOnlyList<string>? changedPaths = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(execution);
        var workspace = Path.GetFullPath(execution.WorkerWorktreePath);
        var targetPaths = ExtractTargetPaths(revision);
        var checks = new List<WorkerVerificationCheck>();

        if (targetPaths.Count == 0)
        {
            checks.Add(new("declared-deliverable", WorkerCompletionVerificationResult.NotVerifiable,
                "TaskRevision does not contain a reliably identifiable target path."));
        }
        else
        {
            var missing = targetPaths.Where(path => !File.Exists(Path.Combine(workspace, path))).ToArray();
            checks.Add(new("declared-deliverable", missing.Length == 0 ? WorkerCompletionVerificationResult.Passed : WorkerCompletionVerificationResult.Failed,
                missing.Length == 0 ? string.Join(", ", targetPaths) : $"Missing: {string.Join(", ", missing)}"));
        }

        changedPaths = changedPaths?.Select(NormalizeRelativePath).ToArray();
        if (changedPaths is null && !string.IsNullOrWhiteSpace(execution.WorkspaceBaselineJson))
            changedPaths = await WorkspaceSnapshot.ComputeDeltaAsync(workspace, execution.WorkspaceBaselineJson, cancellationToken);
        if (targetPaths.Count == 0 || changedPaths is null)
        {
            checks.Add(new("scope", WorkerCompletionVerificationResult.NotVerifiable,
                changedPaths is null ? "Execution workspace baseline/delta was not available." : "No target path was identifiable."));
        }
        else
        {
            var unexpected = changedPaths.Where(path => !targetPaths.Contains(path, StringComparer.OrdinalIgnoreCase)).ToArray();
            checks.Add(new("scope", unexpected.Length == 0 ? WorkerCompletionVerificationResult.Passed : WorkerCompletionVerificationResult.Failed,
                unexpected.Length == 0 ? "Changed paths are within the declared target set." : $"Unexpected paths: {string.Join(", ", unexpected)}"));
        }

        checks.Add(new("acceptance-content", WorkerCompletionVerificationResult.NotVerifiable,
            "Acceptance criteria are free text and have no structured machine-checkable form."));

        var result = checks.Any(check => check.Result == WorkerCompletionVerificationResult.Failed)
            ? WorkerCompletionVerificationResult.Failed
            : checks.Any(check => check.Result == WorkerCompletionVerificationResult.NotVerifiable)
                ? WorkerCompletionVerificationResult.NotVerifiable
                : WorkerCompletionVerificationResult.Passed;
        return new(project.Id, revision.TaskId, revision.Id, execution.ExecutionId, workspace,
            DateTimeOffset.UtcNow, result, checks);
    }

    public static string Serialize(WorkerCompletionVerification verification) => JsonSerializer.Serialize(verification);

    private static IReadOnlySet<string> ExtractTargetPaths(TaskRevision revision)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var text in new[] { revision.Scope }.Concat(revision.Acceptance))
        {
            foreach (Match match in BacktickToken.Matches(text))
            {
                var value = match.Groups["value"].Value.Trim().Replace('/', Path.DirectorySeparatorChar);
                if (value.Contains(Path.DirectorySeparatorChar) && !value.EndsWith(Path.DirectorySeparatorChar))
                    values.Add(value);
            }
        }
        return values;
    }

    private static string NormalizeRelativePath(string path) => path.Trim().Replace('/', Path.DirectorySeparatorChar);

    private static async Task<IReadOnlyList<string>?> ReadChangedPathsAsync(string workspace, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(workspace)) return null;
        var info = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workspace,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8
        };
        info.ArgumentList.Add("status");
        info.ArgumentList.Add("--short");
        info.ArgumentList.Add("--untracked-files=all");
        try
        {
            using var process = Process.Start(info);
            if (process is null) return null;
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0) return null;
            return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Length > 3 ? line[3..].Trim().Replace('/', Path.DirectorySeparatorChar) : string.Empty)
                .Where(path => path.Length > 0)
                .ToArray();
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}
