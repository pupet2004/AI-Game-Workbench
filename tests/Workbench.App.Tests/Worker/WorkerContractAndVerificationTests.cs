using Workbench.App.Worker;
using Workbench.Core.Projects;
using Workbench.Core.Tasks;
using Workbench.Core.Workers;
using Workbench.Storage.Workers;
using Workbench.Runtime.Agents;
using Workbench.App.Tests.Support;
using CoreProject = Workbench.Core.Projects.Project;
using System.Diagnostics;

namespace Workbench.App.Tests.Worker;

public sealed class WorkerContractAndVerificationTests
{
    [Fact]
    public void Renderer_preserves_the_complete_revision_contract()
    {
        var revision = Revision();
        var rendered = WorkerTaskContractRenderer.Render(revision);

        Assert.Contains($"TASK GOAL{Environment.NewLine}Goal A", rendered, StringComparison.Ordinal);
        Assert.Contains($"IN SCOPE{Environment.NewLine}Only modify `acceptance/result.txt` (Scope B)", rendered, StringComparison.Ordinal);
        Assert.Contains($"OUT OF SCOPE{Environment.NewLine}Out C", rendered, StringComparison.Ordinal);
        Assert.Contains("1. [ ] Acceptance D", rendered, StringComparison.Ordinal);
        Assert.Contains("WORKBENCH_STEP_COMPLETED: N", rendered, StringComparison.Ordinal);
        Assert.Contains(revision.Id.ToString(), rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Progress_protocol_extracts_markers_from_final_text()
    {
        var steps = AgentProgressProtocol.ExtractCompletedSteps("report\nWORKBENCH_STEP_COMPLETED: 1\nWORKBENCH_STEP_COMPLETED:4");

        Assert.Equal([1, 4], steps);
    }

    [Fact]
    public void Progress_protocol_extracts_markers_when_provider_omits_newlines()
    {
        var text = "WORKBENCH_STEP_COMPLETED: 3Fixed demo conclusionWORKBENCH_STEP_COMPLETED: 4Read-only complete";

        Assert.Equal([3, 4], AgentProgressProtocol.ExtractCompletedSteps(text));
        Assert.Equal("Fixed demo conclusionRead-only complete", AgentProgressProtocol.StripControlLines(text));
    }

    [Fact]
    public async Task Missing_target_and_unexpected_file_fail_verification_without_authority_effect()
    {
        using var directory = new TemporaryDirectory("worker-verification");
        var projectId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var revision = Revision(taskId);
        var profile = ExecutionProfile.Create("provider", Guid.NewGuid().ToString(), "model", "runtime");
        var executionId = Guid.NewGuid();
        var project = new CoreProject(projectId, "Project", directory.Path, ProjectType.Generic, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var execution = new StoredWorkerExecution(executionId, projectId, taskId, revision.CreateReference(), revision.CreateReference(),
            "base", "main", ProviderAccountBinding.Create(profile.ProviderId, profile.ProviderAccountId), profile,
            "worker", directory.Path, WorkerExecutionState.CompletedPendingReview, "agent", "external", directory.Path,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        File.WriteAllText(Path.Combine(directory.Path, "worker-acceptance.md"), "wrong output");

        var result = await new WorkerCompletionVerifier().VerifyAsync(project, revision, execution, "completed",
            ["worker-acceptance.md"]);

        Assert.Equal(WorkerCompletionVerificationResult.Failed, result.Result);
        Assert.Contains(result.Checks, check => check.Name == "declared-deliverable" && check.Result == WorkerCompletionVerificationResult.Failed);
        Assert.Contains(result.Checks, check => check.Name == "scope" && check.Result == WorkerCompletionVerificationResult.Failed);
        Assert.Contains(result.Checks, check => check.Name == "acceptance-content" && check.Result == WorkerCompletionVerificationResult.NotVerifiable);
    }

    [Fact]
    public async Task Existing_target_with_only_declared_change_passes_path_checks_but_free_text_acceptance_is_not_verifiable()
    {
        using var directory = new TemporaryDirectory("worker-verification-pass");
        var projectId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var revision = Revision(taskId);
        var profile = ExecutionProfile.Create("provider", Guid.NewGuid().ToString(), "model", "runtime");
        var project = new CoreProject(projectId, "Project", directory.Path, ProjectType.Generic, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var execution = new StoredWorkerExecution(Guid.NewGuid(), projectId, taskId, revision.CreateReference(), revision.CreateReference(),
            "base", "main", ProviderAccountBinding.Create(profile.ProviderId, profile.ProviderAccountId), profile,
            "worker", directory.Path, WorkerExecutionState.CompletedPendingReview, "agent", "external", directory.Path,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        Directory.CreateDirectory(Path.Combine(directory.Path, "acceptance"));
        File.WriteAllText(Path.Combine(directory.Path, "acceptance", "result.txt"), "ORBIT\nSTRICT\nPHASE_3_WORKER_OK");

        var result = await new WorkerCompletionVerifier().VerifyAsync(project, revision, execution, "completed",
            ["acceptance/result.txt"]);

        Assert.Equal(WorkerCompletionVerificationResult.NotVerifiable, result.Result);
        Assert.Contains(result.Checks, check => check.Name == "declared-deliverable" && check.Result == WorkerCompletionVerificationResult.Passed);
        Assert.Contains(result.Checks, check => check.Name == "scope" && check.Result == WorkerCompletionVerificationResult.Passed);
    }

    [Fact]
    public async Task Workspace_baseline_attributes_only_execution_delta()
    {
        using var directory = new TemporaryDirectory("worker-baseline");
        await RunGitAsync(directory.Path, "init");
        File.WriteAllText(Path.Combine(directory.Path, "README.md"), "before");
        var baseline = await WorkspaceSnapshot.CaptureAsync(directory.Path);
        Assert.NotNull(baseline);
        File.WriteAllText(Path.Combine(directory.Path, "README.md"), "after");
        File.WriteAllText(Path.Combine(directory.Path, "acceptance.txt"), "new");
        var delta = await WorkspaceSnapshot.ComputeDeltaAsync(directory.Path, baseline!.Serialize());
        Assert.Equal(["acceptance.txt", "README.md"], delta);
    }

    private static async Task RunGitAsync(string workingDirectory, string argument)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "git", WorkingDirectory = workingDirectory, Arguments = argument,
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
        })!;
        await process.WaitForExitAsync();
        Assert.Equal(0, process.ExitCode);
    }

    private static TaskRevision Revision(Guid? taskId = null) => new(taskId ?? Guid.NewGuid(), 1,
        "Goal A", "Only modify `acceptance/result.txt` (Scope B)",
        "Out C: no merge, no push, no Authority changes",
        ["Acceptance D: file `acceptance/result.txt` contains ORBIT, STRICT, PHASE_3_WORKER_OK"],
        TaskRiskLevel.Low, ExecutionProfile.Create("provider", Guid.NewGuid().ToString(), "model", "runtime"),
        "test", TaskRevisionApprover.User, DateTimeOffset.UtcNow, null);
}
