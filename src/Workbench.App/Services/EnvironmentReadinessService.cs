using System.Diagnostics;
using Workbench.Project.Git;
using Workbench.Runtime.Registry;

namespace Workbench.App.Services;

public enum EnvironmentReadinessStatus
{
    Ready,
    NotInstalled,
    InstalledButNotConfigured,
    AuthenticationRequired,
    Unavailable,
    UnsupportedVersion,
    NotApplicable
}

public sealed record EnvironmentCheckResult(
    string Name,
    EnvironmentReadinessStatus Status,
    string Detail,
    string? ResolvedPath = null,
    Version? DetectedVersion = null);

public sealed record EnvironmentReadinessSnapshot(
    EnvironmentCheckResult Git,
    EnvironmentCheckResult Godot,
    EnvironmentCheckResult WorkbenchData,
    EnvironmentCheckResult Agent)
{
    public bool CanStart => WorkbenchData.Status == EnvironmentReadinessStatus.Ready &&
        Git.Status is EnvironmentReadinessStatus.Ready or EnvironmentReadinessStatus.InstalledButNotConfigured &&
        Godot.Status is EnvironmentReadinessStatus.Ready or EnvironmentReadinessStatus.NotApplicable &&
        Agent.Status == EnvironmentReadinessStatus.Ready;
}

public sealed class EnvironmentReadinessService
{
    private static readonly Version MinimumGodotVersion = new(4, 0);

    public EnvironmentReadinessSnapshot Evaluate(
        GitSnapshot git,
        bool workbenchDataReady,
        bool isGodotProject,
        string? godotExecutablePath,
        IReadOnlyList<WorkerResource> agentResources,
        string? runtimeUnavailableDetail)
    {
        ArgumentNullException.ThrowIfNull(git);
        ArgumentNullException.ThrowIfNull(agentResources);

        return new(
            EvaluateGit(git),
            EvaluateGodot(isGodotProject, godotExecutablePath),
            new EnvironmentCheckResult(
                "Workbench data",
                workbenchDataReady ? EnvironmentReadinessStatus.Ready : EnvironmentReadinessStatus.Unavailable,
                workbenchDataReady ? "Local data is ready." : "Local data could not be opened."),
            EvaluateAgent(agentResources, runtimeUnavailableDetail));
    }

    private static EnvironmentCheckResult EvaluateGit(GitSnapshot git)
    {
        if (!git.GitInstalled)
            return new("Git", EnvironmentReadinessStatus.NotInstalled, "Git was not found.");
        if (!string.IsNullOrWhiteSpace(git.Error))
            return new("Git", EnvironmentReadinessStatus.Unavailable, git.Error);
        if (!git.IsRepository)
            return new("Git", EnvironmentReadinessStatus.InstalledButNotConfigured, "This project is not a Git repository.");
        return new("Git", EnvironmentReadinessStatus.Ready, git.IsDirty
            ? "Repository found with uncommitted changes."
            : "Repository is ready.", git.RepositoryRoot);
    }

    private static EnvironmentCheckResult EvaluateGodot(bool isGodotProject, string? path)
    {
        if (!isGodotProject)
            return new("Godot", EnvironmentReadinessStatus.NotApplicable, "This project does not require Godot.");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new("Godot", EnvironmentReadinessStatus.NotInstalled, "Godot executable was not found.");

        try
        {
            var version = FileVersionInfo.GetVersionInfo(path).FileVersion;
            if (!Version.TryParse(version?.Split('+', 2)[0], out var parsed))
                return new("Godot", EnvironmentReadinessStatus.InstalledButNotConfigured, "Godot was found, but its version could not be determined.", path);
            if (parsed < MinimumGodotVersion)
                return new("Godot", EnvironmentReadinessStatus.UnsupportedVersion, $"Godot {parsed} is not supported.", path, parsed);
            return new("Godot", EnvironmentReadinessStatus.Ready, $"Godot {parsed} is ready.", path, parsed);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new("Godot", EnvironmentReadinessStatus.Unavailable, "Godot was found but could not be inspected.", path);
        }
    }

    private static EnvironmentCheckResult EvaluateAgent(
        IReadOnlyList<WorkerResource> resources,
        string? runtimeUnavailableDetail)
    {
        if (resources.Count > 0)
        {
            var labels = string.Join(", ", resources.Select(resource => resource.DisplayLabel));
            return new("Agent provider", EnvironmentReadinessStatus.Ready, $"Ready: {labels}.");
        }

        var detail = runtimeUnavailableDetail ?? "No connected provider exposes a usable model.";
        var status = detail.Contains("not found", StringComparison.OrdinalIgnoreCase)
            ? EnvironmentReadinessStatus.NotInstalled
            : EnvironmentReadinessStatus.AuthenticationRequired;
        return new("Agent provider", status, detail);
    }
}
