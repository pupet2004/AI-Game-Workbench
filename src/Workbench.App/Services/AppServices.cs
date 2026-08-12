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

namespace Workbench.App.Services;

public sealed class AppServices : IAsyncDisposable
{
    private readonly Func<CancellationToken, Task<IAgentRuntime>>? _runtimeFactory;
    private readonly List<IAsyncDisposable> _ownedRuntimes = [];
    private readonly SemaphoreSlim _runtimeConnectionGate = new(1, 1);
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
        LeaderSessionRolloverService leaderSessionRolloverService,
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
        LeaderSessionRolloverService = leaderSessionRolloverService;
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

    public LeaderSessionRolloverService LeaderSessionRolloverService { get; }

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
        var projectOpenService = new ProjectOpenService(
            projectRepository,
            layoutRepository,
            new GitCliInspector(),
            effectiveTimeProvider);

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
            new ProjectMemoryService(new ProjectActivityRepository(database), new ProjectMemoryRepository(database), effectiveTimeProvider),
            new LeaderSessionRolloverService(
                effectiveRuntimeRegistry,
                projectLeaders,
                leaderEpochs,
                leaderMessages,
                effectiveTimeProvider),
            effectiveRuntimeRegistry,
            effectiveTimeProvider,
            runtimeFactory);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await Database.InitializeAsync(cancellationToken);
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
