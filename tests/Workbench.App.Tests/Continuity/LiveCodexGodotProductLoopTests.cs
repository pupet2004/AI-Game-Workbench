using System.Diagnostics;
using System.Text.Json;
using Workbench.App.Continuity;
using Workbench.App.ProjectWorld;
using Workbench.App.Services;
using Workbench.App.Tests.Support;
using Workbench.App.Worker;
using Workbench.Core.Continuity;
using Workbench.Core.Projects;
using Workbench.Core.Tasks;
using Workbench.Core.Workers;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Providers;
using Workbench.Runtime.Registry;
using Workbench.Runtime.Runtime;
using Workbench.Storage.Continuity;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.Tests.Continuity;

public sealed class LiveCodexGodotProductLoopTests
{
    [LiveFact("WORKBENCH_RUN_CODEX_GODOT_PRODUCT_LOOP_LIVE")]
    public async Task Real_codex_accepts_a_godot_counter_change_and_survives_reopen()
    {
        var godot = ResolveGodotExecutable();
        using var directory = new TemporaryDirectory("godot-product-loop");
        IAgentRuntime? runtime = null;
        try
        {
            runtime = await CodexRuntimeComposition.ConnectAsync(CancellationToken.None);
            var model = (await runtime.GetModelsAsync()).First();
            var projectPath = Path.Combine(directory.Path, "counter");
            CopyDirectory(LocateFixture(), projectPath);
            await InitializeGitWorkspaceAsync(projectPath);

            var databasePath = Path.Combine(directory.Path, "workbench.db");
            var now = DateTimeOffset.UtcNow;
            var project = new CoreProject(
                Guid.NewGuid(),
                "Workbench Product Loop Counter",
                projectPath,
                ProjectType.Godot,
                projectPath,
                now,
                now);
            var principal = new UserPrincipalRef("user:godot-product-loop");
            var profile = ExecutionProfile.Create(
                runtime.Provider.Id.Value,
                runtime.Account.Id.Value.ToString(),
                model.ModelId,
                runtime.RuntimeKind);
            var taskId = Guid.NewGuid();
            var revision = new TaskRevision(
                taskId,
                1,
                "Change the score button from +1 to +2 per click.",
                "`scripts/main.gd`",
                "Do not change unrelated files.",
                ["The score button now adds 2 per click."],
                TaskRiskLevel.Low,
                profile,
                "godot-product-loop",
                TaskRevisionApprover.User,
                DateTimeOffset.UtcNow,
                null);

            await using (var services = AppServices.CreateForDatabasePath(databasePath, TimeProvider.System, CreateRegistry(runtime)))
            {
                await services.InitializeAsync();
                await services.B1ProjectGovernance.CreateGovernedProjectAsync(project, principal);
                await services.ProjectWorldInitialization.CommitAsync(new ProjectWorldInitializationRequest(
                    new ProjectRef(project.Id),
                    principal,
                    RoleKind.Worker,
                    "Own the Counter Game score loop.",
                    "A running Godot counter with an accepted score rule.",
                    "Keep the project small and verifiable."));
                await services.TaskRepository.CreateAsync(project.Id, new TaskDraft(
                    taskId,
                    revision.Goal,
                    revision.Goal,
                    revision.Scope,
                    revision.OutOfScope,
                    revision.Acceptance,
                    revision.RiskLevel,
                    profile,
                    DateTimeOffset.UtcNow,
                    revision));

                var projection = await services.B1Projections.GetProjectProjectionAsync(new ProjectRef(project.Id));
                var assignment = Assert.Single(projection.AcceptedProjectState.CurrentDelegationAssignments
                    .Select(value => projection.AcceptedProjectState.Assignments[value]));
                var launch = await services.CanonicalWorkerLaunch.PrepareAsync(
                    project.Id,
                    revision,
                    assignment.AssignmentRef);
                Assert.NotNull(launch);
                var executionId = Guid.NewGuid();
                var identity = WorkerExecutionIdentity.Start(
                    revision.CreateReference(),
                    await GitAsync(projectPath, "rev-parse", "HEAD"),
                    "main",
                    ProviderAccountBinding.Create(profile.ProviderId, profile.ProviderAccountId),
                    profile,
                    "worker/godot-product-loop",
                    projectPath);
                var finalReport = JsonSerializer.Serialize(new
                {
                    Kind = "FinalReport",
                    Message = "Changed the score button to add 2 per click.",
                    ValidationSummary = "Godot headless smoke test passed",
                    ProposedChanges = new[] { "The score button now adds 2 per click." }
                });
                StoredCanonicalWorkerCompletion? completion = null;

                var start = await services.WorkerSessionRouter.StartAsync(new WorkerStartRequest(
                    project,
                    taskId,
                    revision.Id,
                    revision.Goal,
                    profile,
                    $"""
                    Modify this Godot 4 project.
                    Change only `scripts/main.gd`.
                    Change `const CLICK_INCREMENT: int = 1` to `const CLICK_INCREMENT: int = 2`.
                    Run the Godot project headlessly with:
                    "{godot}" --headless --path . --quit
                    Your final response must be exactly one JSON object, with no Markdown fences:
                    {finalReport}
                    """,
                    null,
                    "Godot Product Loop Worker",
                    ExecutionId: executionId,
                    ExecutionIdentity: identity,
                    B1AssignmentRef: launch!.AssignmentRef,
                    B1AssignmentRevisionRef: launch.AssignmentRevisionRef,
                    B1AttemptRef: launch.AttemptRef,
                    B1SessionBindingRef: launch.SessionBindingRef,
                    B1LogicalActorRef: launch.LogicalActorRef,
                    B1OperatorRef: launch.OperatorRef,
                    OnCanonicalCompletion: (value, _) =>
                    {
                        completion = value;
                        return Task.CompletedTask;
                    },
                    AccessMode: AgentAccessMode.Full));

                Assert.True(start.Succeeded, start.Error);
                Assert.NotNull(completion);
                Assert.Equal(CanonicalWorkerCompletionStatus.GovernanceReady, completion!.Status);
                Assert.Contains(
                    "const CLICK_INCREMENT: int = 2",
                    await File.ReadAllTextAsync(Path.Combine(projectPath, "scripts", "main.gd")),
                    StringComparison.Ordinal);
                await RunGodotAsync(godot, projectPath);

                var before = await services.B1Projections.GetAcceptedProjectStateAsync(new ProjectRef(project.Id));
                Assert.Empty(before.CurrentContributions);
                Assert.NotNull(await new B1WorkerBridgeRepository(services.Database)
                    .GetEvidenceAsync(new ProjectRef(project.Id), executionId));

                await services.GuidedDecision.CommitAsync(new GuidedDecisionRequest(
                    new ProjectRef(project.Id),
                    principal,
                    completion.Facts.HandoffRef,
                    AssignmentDisposition.Accepted,
                    ContributionDecisionMode.AdoptVerbatim,
                    null,
                    null));
                var accepted = await services.B1Projections.GetAcceptedProjectStateAsync(new ProjectRef(project.Id));
                Assert.Contains(
                    accepted.CurrentContributions,
                    value => value.Statement == "The score button now adds 2 per click.");
            }

            await using (var reopened = AppServices.CreateForDatabasePath(
                databasePath,
                TimeProvider.System,
                new AgentRuntimeRegistry()))
            {
                await reopened.InitializeAsync();
                var recovered = await reopened.B1Projections.GetAcceptedProjectStateAsync(new ProjectRef(project.Id));
                Assert.Contains(
                    recovered.CurrentContributions,
                    value => value.Statement == "The score button now adds 2 per click.");
            }
        }
        finally
        {
            if (runtime is IAsyncDisposable disposable)
                await disposable.DisposeAsync();
        }
    }

    private static AgentRuntimeRegistry CreateRegistry(IAgentRuntime runtime)
    {
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        return registry;
    }

    private static string LocateFixture()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "demos", "product-loop-godot-counter");
            if (File.Exists(Path.Combine(candidate, "project.godot")))
                return candidate;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Godot product loop fixture.");
    }

    private static string ResolveGodotExecutable()
    {
        var configured = Environment.GetEnvironmentVariable("WORKBENCH_GODOT_EXECUTABLE");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        var wingetRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "WinGet",
            "Packages");
        var discovered = Directory.Exists(wingetRoot)
            ? Directory.EnumerateFiles(wingetRoot, "Godot*_console.exe", SearchOption.AllDirectories).FirstOrDefault()
            : null;
        if (!string.IsNullOrWhiteSpace(discovered))
            return discovered;

        throw new FileNotFoundException(
            "Godot executable was not found. Set WORKBENCH_GODOT_EXECUTABLE to a Godot console executable.");
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static async Task InitializeGitWorkspaceAsync(string path)
    {
        await GitAsync(path, "init", "-b", "main");
        await GitAsync(path, "config", "user.name", "Workbench Product Loop");
        await GitAsync(path, "config", "user.email", "workbench-product-loop@local.invalid");
        await GitAsync(path, "add", ".");
        await GitAsync(path, "commit", "-m", "initial Godot counter");
    }

    private static async Task RunGodotAsync(string executable, string projectPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = projectPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--headless");
        startInfo.ArgumentList.Add("--path");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("--quit");
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start Godot.");
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Godot validation failed: {output}\n{error}");
    }

    private static async Task<string> GitAsync(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start git.");
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(error);
        return output.Trim();
    }
}
