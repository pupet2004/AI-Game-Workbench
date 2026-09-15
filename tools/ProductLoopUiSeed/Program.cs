using System.Text.Json;
using Workbench.App.ProjectWorld;
using Workbench.App.Services;
using Workbench.Core.Continuity;
using Workbench.Core.Tasks;
using Workbench.Runtime.Runtime;
using Workbench.Storage.Continuity;

var arguments = ParseArguments(args);
if (!arguments.TryGetValue("database", out var databasePath) ||
    !arguments.TryGetValue("project", out var projectPath))
{
    Console.Error.WriteLine("Usage: ProductLoopUiSeed --database <path> --project <path> [--round <1|2|3>] [--real-worker] [--provider <codex|opencode>] [--model <model-id>]");
    return 2;
}

var round = arguments.TryGetValue("round", out var requestedRound) &&
            int.TryParse(requestedRound, out var parsedRound)
    ? parsedRound
    : 1;
var configuration = CreateRoundConfiguration(round, arguments);
var prepareRealWorker = arguments.ContainsKey("real-worker");
var providerId = arguments.TryGetValue("provider", out var requestedProvider)
    ? requestedProvider
    : "codex";
var requestedModel = arguments.TryGetValue("model", out var modelArgument)
    ? modelArgument
    : null;
if (providerId is not ("codex" or "opencode"))
    throw new ArgumentException($"Unsupported Worker provider: {providerId}");

await using var services = AppServices.CreateForDatabasePath(databasePath);
await services.InitializeAsync();

var opened = await services.ProjectOpenService.OpenAsync(projectPath);
var projectRef = new ProjectRef(opened.Project.Id);
var principal = services.UserPrincipalProvider.GetCurrent();
var entryStatus = await services.ProjectWorldEntryStatus.GetStatusAsync(opened.Project);

if (entryStatus.Kind == ProjectWorldEntryKind.UnmanagedProjectUnavailable)
{
    await services.B1ProjectGovernance.CreateGovernedProjectForExistingProjectAsync(projectRef, principal);
}
else if (entryStatus.Kind == ProjectWorldEntryKind.LegacySetupRequired)
{
    await services.B1ProjectGovernance.AdoptLegacyProjectAsync(
        projectRef,
        principal,
        services.TimeProvider.GetUtcNow());
}

var state = await services.B1AuthorityRepository.LoadProjectStateAsync(projectRef);

if (state.AuthorityDecisions.Count == 0)
{
    await services.ProjectWorldInitialization.CommitAsync(
        new ProjectWorldInitializationRequest(
            projectRef,
            principal,
            RoleKind.Worker,
            "Own the Counter Game score loop.",
            "A reviewed and verifiable score rule.",
            "Change the score button from +1 to +2 per click."));
    state = await services.B1AuthorityRepository.LoadProjectStateAsync(projectRef);
}

if (prepareRealWorker)
{
    await services.WorkbenchSettingsRepository.SaveAgentRuntimeSettingsAsync(
        new Workbench.Storage.Settings.AgentRuntimeSettings("codex", providerId == "codex", null));
    await services.WorkbenchSettingsRepository.SaveAgentRuntimeSettingsAsync(
        new Workbench.Storage.Settings.AgentRuntimeSettings("opencode", providerId == "opencode", null));

    IAgentRuntime? runtime = null;
    try
    {
        runtime = providerId == "opencode"
            ? await OpenCodeRuntimeComposition.ConnectAsync(CancellationToken.None)
            : await CodexRuntimeComposition.ConnectAsync(CancellationToken.None);
        var models = await runtime.GetModelsAsync();
        var model = requestedModel is null
            ? models.First()
            : models.FirstOrDefault(value => string.Equals(value.ModelId, requestedModel, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    $"Requested model '{requestedModel}' is not available from {providerId}.");
        var godot = ResolveGodotExecutable();
        var profile = ExecutionProfile.Create(
            runtime.Provider.Id.Value,
            runtime.Account.Id.Value.ToString(),
            model.ModelId,
            runtime.RuntimeKind);
        var taskId = Guid.NewGuid();
        var revision = new TaskRevision(
            taskId,
            1,
            configuration.Goal,
            $$"""
            Declared scope: {{configuration.Scope}}
            {{configuration.Instructions}}
            Run this exact validation command from the project directory:
            "{{godot}}" --headless --path . --quit
            Your final response must be exactly one JSON object with this shape:
            {{JsonSerializer.Serialize(new
            {
                Kind = "FinalReport",
                Message = configuration.Statement,
                ValidationSummary = "Godot headless validation passed",
                ProposedChanges = new[] { configuration.Statement }
            })}}
            """,
            configuration.OutOfScope,
            configuration.Acceptance,
            TaskRiskLevel.Low,
            profile,
            $"Prepared for real desktop Codex Worker certification round {round}.",
            TaskRevisionApprover.User,
            services.TimeProvider.GetUtcNow(),
            null);
        await services.TaskRepository.CreateAsync(
            opened.Project.Id,
            new TaskDraft(
                taskId,
                configuration.Title,
                revision.Goal,
                revision.Scope,
                revision.OutOfScope,
                revision.Acceptance,
                revision.RiskLevel,
                profile,
                services.TimeProvider.GetUtcNow(),
                revision));
        Console.WriteLine($"TaskId={taskId}");
        Console.WriteLine($"Provider={runtime.Provider.Id.Value}");
        Console.WriteLine($"Model={model.ModelId}");
        Console.WriteLine($"Round={round}");
        Console.WriteLine($"Statement={configuration.Statement}");
        Console.WriteLine("RealWorkerPrepared=true");
        return 0;
    }
    finally
    {
        if (runtime is IAsyncDisposable disposable)
            await disposable.DisposeAsync();
    }
}

var projection = B1Projector.Build(state);
var assignmentRef = projection.AcceptedProjectState.CurrentDelegationAssignments.Single();
var assignment = projection.AcceptedProjectState.Assignments[assignmentRef];
var revisionRef = projection.AcceptedProjectState.CurrentEffectiveRevisionRefs[assignmentRef];
var attempt = projection.EffectiveCurrentAttemptRefs.TryGetValue(assignmentRef, out var selectedAttempt)
    ? selectedAttempt
    : null;

if (attempt is null)
{
    await services.B1NonAuthoritativeCommands.CreateAttemptAndSelectAsync(
        new CreateAttemptCommand(
            projectRef,
            principal,
            new AttemptRef(Guid.NewGuid()),
            assignmentRef,
            revisionRef,
            services.TimeProvider.GetUtcNow()),
        projection.StoredAttemptSelections.TryGetValue(assignmentRef, out var expectedAttempt)
            ? expectedAttempt
            : null);

    state = await services.B1AuthorityRepository.LoadProjectStateAsync(projectRef);
    projection = B1Projector.Build(state);
    attempt = projection.EffectiveCurrentAttemptRefs[assignmentRef];
}

var handoff = await services.GuidedHandoffComposer.RecordAsync(
    attempt!.Value,
    assignmentRef,
    new GuidedHandoffRequest(
        projectRef,
        principal,
        $"Godot counter round {round} prepared for review.",
        ["Godot headless validation passed."],
        [],
        [configuration.Statement],
        null,
        ["godot://headless/product-loop-validation"]));

Console.WriteLine($"ProjectId={opened.Project.Id}");
Console.WriteLine($"ProjectPath={opened.Project.RootPath}");
Console.WriteLine($"HandoffRef={handoff.HandoffRef}");
Console.WriteLine($"Statement={configuration.Statement}");
return 0;

static WorkerRoundConfiguration CreateRoundConfiguration(
    int round,
    IReadOnlyDictionary<string, string> arguments)
{
    if (round is < 1 or > 3)
        throw new ArgumentOutOfRangeException(nameof(round), "The product loop certification supports rounds 1, 2, and 3.");

    var defaults = round switch
    {
        1 => new WorkerRoundConfiguration(
            "Change the counter score increment to +2.",
            "Change the score button so each click adds 2 instead of 1.",
            "The score button now adds 2 per click.",
            "Only edit scripts/main.gd. Change CLICK_INCREMENT from 1 to 2.",
            "Do not change project.godot, scenes/main.tscn, unrelated scripts, or generated files.",
            "scripts/main.gd; godot validation",
            [
                "scripts/main.gd defines CLICK_INCREMENT as 2.",
                "The exact Godot command exits successfully.",
                "The final response is a FinalReport JSON with the proposed accepted change."
            ]),
        2 => new WorkerRoundConfiguration(
            "Add a Reset button to the counter.",
            "Add a Reset button that sets the score back to 0.",
            "The counter has a Reset button.",
            "Edit only scripts/main.gd and scenes/main.tscn. Add a Reset button to the existing counter, connect it to a handler, and make that handler set score to 0 and refresh the displayed score.",
            "Do not change project.godot, unrelated scripts, or generated files. Preserve the existing +2 score behavior.",
            "scripts/main.gd; scenes/main.tscn; godot validation",
            [
                "scenes/main.tscn contains a Reset button.",
                "scripts/main.gd connects the Reset button and resets score to 0.",
                "The exact Godot command exits successfully.",
                "The final response is a FinalReport JSON with the proposed accepted change."
            ]),
        _ => new WorkerRoundConfiguration(
            "Add high score tracking to the counter.",
            "Add high score tracking without changing existing score or reset behavior.",
            "The counter records the highest score.",
            "Edit only scripts/main.gd and scenes/main.tscn. Add a visible High Score label/value, update it whenever score reaches a new maximum, and preserve the existing +2 score and Reset behavior.",
            "Do not change project.godot, unrelated scripts, or generated files.",
            "scripts/main.gd; scenes/main.tscn; godot validation",
            [
                "scenes/main.tscn contains a High Score display.",
                "scripts/main.gd stores and updates the highest score.",
                "The exact Godot command exits successfully.",
                "The final response is a FinalReport JSON with the proposed accepted change."
            ])
    };

    return defaults with
    {
        Goal = arguments.TryGetValue("goal", out var goal) ? goal : defaults.Goal,
        Statement = arguments.TryGetValue("statement", out var statement) ? statement : defaults.Statement,
        Scope = arguments.TryGetValue("scope", out var scope) ? scope : defaults.Scope,
        OutOfScope = arguments.TryGetValue("out-of-scope", out var outOfScope) ? outOfScope : defaults.OutOfScope,
        Acceptance = arguments.TryGetValue("acceptance", out var acceptance)
            ? [acceptance]
            : defaults.Acceptance
    };
}

static Dictionary<string, string> ParseArguments(string[] args)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var index = 0; index < args.Length; index++)
    {
        var argument = args[index];
        if (!argument.StartsWith("--", StringComparison.Ordinal))
        {
            continue;
        }

        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            result[argument[2..]] = "true";
            continue;
        }

        result[argument[2..]] = args[++index];
    }

    return result;
}

static string ResolveGodotExecutable()
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

internal sealed record WorkerRoundConfiguration(
    string Title,
    string Goal,
    string Statement,
    string Instructions,
    string OutOfScope,
    string Scope,
    IReadOnlyList<string> Acceptance);
