using Workbench.Project.Git;
using Workbench.Project.Opening;
using Workbench.Runtime.Registry;
using Workbench.Runtime.Runtime;
using Workbench.Storage.Database;
using Workbench.Storage.Leaders;
using Workbench.Storage.Projects;
using Workbench.Storage.Settings;
using Workbench.App.Leader;
using Workbench.Storage.Memory;
using Workbench.Storage.Tasks;
using Workbench.App.Memory;
using Workbench.App.Worker;
using System.Collections.Concurrent;
using Workbench.Storage.Workers;

namespace Workbench.App.Services;

public sealed class AppServices : IAsyncDisposable
{
    private readonly Func<CancellationToken, Task<IAgentRuntime>>? _runtimeFactory;
    private readonly List<IAsyncDisposable> _ownedRuntimes = [];
    private readonly SemaphoreSlim _runtimeConnectionGate = new(1, 1);
    private readonly CancellationTokenSource _synthesisShutdown = new();
    private readonly ConcurrentDictionary<Guid, byte> _scheduledSynthesis = [];
    private int _disposeRequested;

    private AppServices(
        WorkbenchDatabase database,
        ProjectRepository projectRepository,
        ProjectLayoutRepository projectLayoutRepository,
        ProjectOpenService projectOpenService,
        ProjectLeaderRepository projectLeaderRepository,
        LeaderSessionEpochRepository leaderSessionEpochRepository,
        LeaderMessageRepository leaderMessageRepository,
        WorkbenchSettingsRepository workbenchSettingsRepository,
        ProjectSettingsRepository projectSettingsRepository,
        ProjectMemoryService projectMemoryService,
        ProjectMemorySynthesisRepository projectMemorySynthesisRepository,
        ProjectMemorySynthesisCoordinator projectMemorySynthesisCoordinator,
        DailySummaryRepository dailySummaryRepository,
        ProjectMemoryPreferencesRepository projectMemoryPreferencesRepository,
        IProjectMemoryApi projectMemoryApi,
        LeaderMemoryPolicyCoordinator leaderMemoryPolicyCoordinator,
        LeaderBootContextBuilder leaderBootContextBuilder,
        LeaderSessionRolloverService leaderSessionRolloverService,
        TaskRepository taskRepository,
        TaskRevisionRepository taskRevisionRepository,
        ProjectLibraryRepository projectLibraryRepository,
        WorkerSessionRouter workerSessionRouter,
        IWorkerRoutingStore workerRoutingStore,
        AgentRuntimeRegistry runtimeRegistry,
        TimeProvider timeProvider,
        Func<CancellationToken, Task<IAgentRuntime>>? runtimeFactory)
    {
        Database = database;
        ProjectRepository = projectRepository;
        ProjectLayoutRepository = projectLayoutRepository;
        ProjectOpenService = projectOpenService;
        ProjectLeaderRepository = projectLeaderRepository;
        LeaderSessionEpochRepository = leaderSessionEpochRepository;
        LeaderMessageRepository = leaderMessageRepository;
        WorkbenchSettingsRepository = workbenchSettingsRepository;
        ProjectSettingsRepository = projectSettingsRepository;
        ProjectMemoryService = projectMemoryService;
        ProjectMemorySynthesisRepository = projectMemorySynthesisRepository;
        ProjectMemorySynthesisCoordinator = projectMemorySynthesisCoordinator;
        DailySummaryRepository = dailySummaryRepository;
        ProjectMemoryPreferencesRepository = projectMemoryPreferencesRepository;
        ProjectMemoryApi = projectMemoryApi;
        LeaderMemoryPolicyCoordinator = leaderMemoryPolicyCoordinator;
        LeaderBootContextBuilder = leaderBootContextBuilder;
        LeaderSessionRolloverService = leaderSessionRolloverService;
        TaskRepository = taskRepository;
        TaskRevisionRepository = taskRevisionRepository;
        ProjectLibraryRepository = projectLibraryRepository;
        WorkerSessionRouter = workerSessionRouter;
        WorkerRoutingStore = workerRoutingStore;
        RuntimeRegistry = runtimeRegistry;
        TimeProvider = timeProvider;
        _runtimeFactory = runtimeFactory;
    }

    public WorkbenchDatabase Database { get; }

    public ProjectRepository ProjectRepository { get; }

    public ProjectLayoutRepository ProjectLayoutRepository { get; }

    public ProjectOpenService ProjectOpenService { get; }

    public ProjectLeaderRepository ProjectLeaderRepository { get; }

    public LeaderSessionEpochRepository LeaderSessionEpochRepository { get; }

    public LeaderMessageRepository LeaderMessageRepository { get; }

    public WorkbenchSettingsRepository WorkbenchSettingsRepository { get; }

    public ProjectSettingsRepository ProjectSettingsRepository { get; }
    public ProjectMemoryService ProjectMemoryService { get; }
    public ProjectMemorySynthesisRepository ProjectMemorySynthesisRepository { get; }
    public ProjectMemorySynthesisCoordinator ProjectMemorySynthesisCoordinator { get; }
    public DailySummaryRepository DailySummaryRepository { get; }
    public ProjectMemoryPreferencesRepository ProjectMemoryPreferencesRepository { get; }
    public IProjectMemoryApi ProjectMemoryApi { get; }
    public LeaderMemoryPolicyCoordinator LeaderMemoryPolicyCoordinator { get; }
    public LeaderBootContextBuilder LeaderBootContextBuilder { get; }

    public LeaderSessionRolloverService LeaderSessionRolloverService { get; }
    public TaskRepository TaskRepository { get; }
    public TaskRevisionRepository TaskRevisionRepository { get; }
    public ProjectLibraryRepository ProjectLibraryRepository { get; }
    public WorkerSessionRouter WorkerSessionRouter { get; }
    public IWorkerRoutingStore WorkerRoutingStore { get; }

    public AgentRuntimeRegistry RuntimeRegistry { get; }

    public TimeProvider TimeProvider { get; }

    public string? RuntimeUnavailableDetail { get; private set; }

    public static AppServices CreateDefault(
        Func<CancellationToken, Task<IAgentRuntime>>? runtimeFactory = null) =>
        CreateForDatabasePath(
            DatabasePathProvider.GetDefaultDatabasePath(),
            TimeProvider.System,
            runtimeFactory: runtimeFactory);

    public static AppServices CreateForDatabasePath(
        string databasePath,
        TimeProvider? timeProvider = null,
        AgentRuntimeRegistry? runtimeRegistry = null,
        Func<CancellationToken, Task<IAgentRuntime>>? runtimeFactory = null)
    {
        var database = new WorkbenchDatabase(databasePath);
        var projectRepository = new ProjectRepository(database);
        var layoutRepository = new ProjectLayoutRepository(database);
        var effectiveTimeProvider = timeProvider ?? TimeProvider.System;
        var effectiveRuntimeRegistry = runtimeRegistry ?? new AgentRuntimeRegistry();
        var projectLeaders = new ProjectLeaderRepository(database);
        var leaderEpochs = new LeaderSessionEpochRepository(database);
        var leaderMessages = new LeaderMessageRepository(database);
        var memoryRepository = new ProjectMemoryRepository(database);
        var synthesisRepository = new ProjectMemorySynthesisRepository(database, effectiveTimeProvider);
        var dailySummaryRepository = new DailySummaryRepository(database);
        var projectMemoryPreferencesRepository = new ProjectMemoryPreferencesRepository(database, effectiveTimeProvider);
        var continuityPlans = new LeaderEpochContinuityRepository(database);
        var projectOpenService = new ProjectOpenService(
            projectRepository,
            layoutRepository,
            new GitCliInspector(),
            effectiveTimeProvider);
        var projectMemoryApi = new ProjectMemoryApi(dailySummaryRepository, projectMemoryPreferencesRepository, leaderEpochs, leaderMessages);
        var leaderMemoryPolicyCoordinator = new LeaderMemoryPolicyCoordinator(effectiveRuntimeRegistry, projectMemoryApi, effectiveTimeProvider);

        return new AppServices(
            database,
            projectRepository,
            layoutRepository,
            projectOpenService,
            projectLeaders,
            leaderEpochs,
            leaderMessages,
            new WorkbenchSettingsRepository(database),
            new ProjectSettingsRepository(database),
            new ProjectMemoryService(new ProjectActivityRepository(database), memoryRepository, effectiveTimeProvider),
            synthesisRepository,
            new ProjectMemorySynthesisCoordinator(
                effectiveRuntimeRegistry,
                synthesisRepository,
                leaderEpochs,
                leaderMessages,
                memoryRepository,
                effectiveTimeProvider),
            dailySummaryRepository,
            projectMemoryPreferencesRepository,
            projectMemoryApi,
            leaderMemoryPolicyCoordinator,
            new LeaderBootContextBuilder(projectMemoryApi, leaderEpochs, continuityPlans),
            new LeaderSessionRolloverService(
                effectiveRuntimeRegistry,
                projectLeaders,
                leaderMessages,
                effectiveTimeProvider),
            new TaskRepository(database),
            new TaskRevisionRepository(database),
            new ProjectLibraryRepository(database),
            new WorkerSessionRouter(effectiveRuntimeRegistry, new TaskEventWorkerRoutingStore(new TaskEventRepository(database)), effectiveTimeProvider),
            new TaskEventWorkerRoutingStore(new TaskEventRepository(database)),
            effectiveRuntimeRegistry,
            effectiveTimeProvider,
            runtimeFactory);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await Database.InitializeAsync(cancellationToken);
        await ProjectMemorySynthesisRepository.RecoverRunningAsync(cancellationToken);
    }

    public void ScheduleMemorySynthesis(Guid projectId)
    {
        if (Volatile.Read(ref _disposeRequested) != 0 || !_scheduledSynthesis.TryAdd(projectId, 0))
        {
            return;
        }

        _ = RunScheduledSynthesisAsync(projectId);
    }

    private async Task RunScheduledSynthesisAsync(Guid projectId)
    {
        try
        {
            await ProjectMemorySynthesisCoordinator.TryProcessNextAsync(projectId, _synthesisShutdown.Token);
        }
        catch
        {
            // Coordinator failures are persisted for a later safe trigger.
        }
        finally
        {
            _scheduledSynthesis.TryRemove(projectId, out _);
        }
    }

    public async Task RetryRuntimeAsync(CancellationToken cancellationToken = default)
    {
        if (_runtimeFactory is null || Volatile.Read(ref _disposeRequested) != 0)
        {
            return;
        }

        await _runtimeConnectionGate.WaitAsync(cancellationToken);
        try
        {
            if (Volatile.Read(ref _disposeRequested) != 0 || RuntimeRegistry.Runtimes.Count > 0)
            {
                return;
            }

            RuntimeUnavailableDetail = null;
            try
            {
                var runtime = await _runtimeFactory(cancellationToken);
                if (Volatile.Read(ref _disposeRequested) != 0)
                {
                    if (runtime is IAsyncDisposable lateDisposable)
                    {
                        await lateDisposable.DisposeAsync();
                    }

                    return;
                }

                RuntimeRegistry.Register(runtime);
                if (runtime is IAsyncDisposable disposable)
                {
                    _ownedRuntimes.Add(disposable);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                RuntimeUnavailableDetail = "Codex could not be started.";
            }
        }
        finally
        {
            _runtimeConnectionGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposeRequested, 1);
        _synthesisShutdown.Cancel();
        await _runtimeConnectionGate.WaitAsync();
        try
        {
            for (var index = _ownedRuntimes.Count - 1; index >= 0; index--)
            {
                await _ownedRuntimes[index].DisposeAsync();
            }

            _ownedRuntimes.Clear();
        }
        finally
        {
            _runtimeConnectionGate.Release();
        }
    }
}
