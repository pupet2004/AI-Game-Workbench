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
using Workbench.Storage.Workers;
using Workbench.Storage.Reviews;
using Workbench.App.Continuity;
using Workbench.Core.Continuity;
using Workbench.Storage.Continuity;
using Workbench.App.ProjectWorld;
using Workbench.App.AgentHost;

namespace Workbench.App.Services;

public sealed class AppServices : IAsyncDisposable
{
    private readonly IReadOnlyList<Func<CancellationToken, Task<IAgentRuntime>>> _runtimeFactories;
    private readonly HashSet<int> _connectedRuntimeFactories = [];
    private readonly Dictionary<int, IAgentRuntime> _connectedRuntimeInstances = [];
    private readonly IReadOnlyList<ConfiguredAgentRuntimeFactory> _configuredRuntimeFactories;
    private readonly HashSet<int> _connectedConfiguredRuntimeFactories = [];
    private readonly Dictionary<int, IAgentRuntime> _connectedConfiguredRuntimeInstances = [];
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
        ProjectSummaryRepository projectSummaryRepository,
        LeaderSummaryRecoveryService leaderSummaryRecoveryService,
        WorkbenchSettingsRepository workbenchSettingsRepository,
        ProjectSettingsRepository projectSettingsRepository,
        ProjectMemoryService projectMemoryService,
        ProjectMemorySynthesisRepository projectMemorySynthesisRepository,
        DailySummaryRepository dailySummaryRepository,
        ProjectMemoryPreferencesRepository projectMemoryPreferencesRepository,
        IProjectMemoryApi projectMemoryApi,
        LeaderMemoryPolicyCoordinator leaderMemoryPolicyCoordinator,
        LeaderBootContextBuilder leaderBootContextBuilder,
        LeaderSessionRolloverService leaderSessionRolloverService,
        TaskRepository taskRepository,
        TaskRevisionRepository taskRevisionRepository,
        ProjectLibraryRepository projectLibraryRepository,
        ProjectLibraryEvolutionRepository projectLibraryEvolutionRepository,
        WorkerSessionRouter workerSessionRouter,
        IWorkerRoutingStore workerRoutingStore,
        WorkerExecutionRepository workerExecutionRepository,
        ILeaderReviewOrchestrator leaderReviewOrchestrator,
        LeaderAuthoritySettingsService leaderAuthoritySettings,
        ILeaderReviewAutoProceedExecutor leaderReviewAutoProceed,
        ILeaderReviewAskUserGate leaderReviewAskUserGate,
        ILeaderReviewUserResponseBinder leaderReviewUserResponseBinder,
        B1ProjectGovernanceRepository b1ProjectGovernance,
        B1AuthorityRepository b1AuthorityRepository,
        B1WorkerExecutionBridgeService b1WorkerExecutionBridge,
        CanonicalWorkerLaunchService canonicalWorkerLaunch,
        B1EvidenceRepository b1Evidence,
        B1NonAuthoritativeCommandService b1NonAuthoritativeCommands,
        B1AuthorityCommandService b1AuthorityCommands,
        B1ProjectionService b1Projections,
        LibraryProjectionContractService libraryProjectionContracts,
        LibraryAcceptedStateReader libraryAcceptedStateReader,
        ProjectWorldEntryStatusService projectWorldEntryStatus,
        ProjectWorldInitializationService projectWorldInitialization,
        GuidedHandoffCommandService guidedHandoffCommands,
        GuidedHandoffComposerService guidedHandoffComposer,
        B1AgentParticipationAdapter b1AgentParticipation,
        GuidedDecisionService guidedDecision,
        ManualLibraryProjectionService manualLibraryProjection,
        IUserPrincipalProvider userPrincipalProvider,
        AgentRuntimeRegistry runtimeRegistry,
        IAgentHost agentHost,
        TimeProvider timeProvider,
        IReadOnlyList<Func<CancellationToken, Task<IAgentRuntime>>> runtimeFactories,
        IReadOnlyList<ConfiguredAgentRuntimeFactory> configuredRuntimeFactories)
    {
        Database = database;
        ProjectRepository = projectRepository;
        ProjectLayoutRepository = projectLayoutRepository;
        ProjectOpenService = projectOpenService;
        ProjectLeaderRepository = projectLeaderRepository;
        LeaderSessionEpochRepository = leaderSessionEpochRepository;
        LeaderMessageRepository = leaderMessageRepository;
        ProjectSummaryRepository = projectSummaryRepository;
        LeaderSummaryRecoveryService = leaderSummaryRecoveryService;
        WorkbenchSettingsRepository = workbenchSettingsRepository;
        ProjectSettingsRepository = projectSettingsRepository;
        ProjectMemoryService = projectMemoryService;
        ProjectMemorySynthesisRepository = projectMemorySynthesisRepository;
        DailySummaryRepository = dailySummaryRepository;
        ProjectMemoryPreferencesRepository = projectMemoryPreferencesRepository;
        ProjectMemoryApi = projectMemoryApi;
        LeaderMemoryPolicyCoordinator = leaderMemoryPolicyCoordinator;
        LeaderBootContextBuilder = leaderBootContextBuilder;
        LeaderSessionRolloverService = leaderSessionRolloverService;
        TaskRepository = taskRepository;
        TaskRevisionRepository = taskRevisionRepository;
        ProjectLibraryRepository = projectLibraryRepository;
        ProjectLibraryEvolutionRepository = projectLibraryEvolutionRepository;
        WorkerSessionRouter = workerSessionRouter;
        WorkerRoutingStore = workerRoutingStore;
        WorkerExecutionRepository = workerExecutionRepository;
        LeaderReviewOrchestrator = leaderReviewOrchestrator;
        LeaderAuthoritySettings = leaderAuthoritySettings;
        LeaderReviewAutoProceed = leaderReviewAutoProceed;
        LeaderReviewAskUserGate = leaderReviewAskUserGate;
        LeaderReviewUserResponseBinder = leaderReviewUserResponseBinder;
        B1ProjectGovernance = b1ProjectGovernance;
        B1AuthorityRepository = b1AuthorityRepository;
        B1WorkerExecutionBridge = b1WorkerExecutionBridge;
        CanonicalWorkerLaunch = canonicalWorkerLaunch;
        B1Evidence = b1Evidence;
        B1NonAuthoritativeCommands = b1NonAuthoritativeCommands;
        B1AuthorityCommands = b1AuthorityCommands;
        B1Projections = b1Projections;
        LibraryProjectionContracts = libraryProjectionContracts;
        LibraryAcceptedStateReader = libraryAcceptedStateReader;
        ProjectWorldEntryStatus = projectWorldEntryStatus;
        ProjectWorldInitialization = projectWorldInitialization;
        GuidedHandoffCommands = guidedHandoffCommands;
        GuidedHandoffComposer = guidedHandoffComposer;
        B1AgentParticipation = b1AgentParticipation;
        GuidedDecision = guidedDecision;
        ManualLibraryProjection = manualLibraryProjection;
        UserPrincipalProvider = userPrincipalProvider;
        RuntimeRegistry = runtimeRegistry;
        AgentHost = agentHost;
        TimeProvider = timeProvider;
        _runtimeFactories = runtimeFactories;
        _configuredRuntimeFactories = configuredRuntimeFactories;
    }

    public WorkbenchDatabase Database { get; }

    public ProjectRepository ProjectRepository { get; }

    public ProjectLayoutRepository ProjectLayoutRepository { get; }

    public ProjectOpenService ProjectOpenService { get; }

    public ProjectLeaderRepository ProjectLeaderRepository { get; }

    public LeaderSessionEpochRepository LeaderSessionEpochRepository { get; }

    public LeaderMessageRepository LeaderMessageRepository { get; }

    public ProjectSummaryRepository ProjectSummaryRepository { get; }

    public LeaderSummaryRecoveryService LeaderSummaryRecoveryService { get; }

    public WorkbenchSettingsRepository WorkbenchSettingsRepository { get; }

    public ProjectSettingsRepository ProjectSettingsRepository { get; }
    public ProjectMemoryService ProjectMemoryService { get; }
    public ProjectMemorySynthesisRepository ProjectMemorySynthesisRepository { get; }
    public DailySummaryRepository DailySummaryRepository { get; }
    public ProjectMemoryPreferencesRepository ProjectMemoryPreferencesRepository { get; }
    public IProjectMemoryApi ProjectMemoryApi { get; }
    public LeaderMemoryPolicyCoordinator LeaderMemoryPolicyCoordinator { get; }
    public LeaderBootContextBuilder LeaderBootContextBuilder { get; }

    public LeaderSessionRolloverService LeaderSessionRolloverService { get; }
    public TaskRepository TaskRepository { get; }
    public TaskRevisionRepository TaskRevisionRepository { get; }
    public ProjectLibraryRepository ProjectLibraryRepository { get; }
    public ProjectLibraryEvolutionRepository ProjectLibraryEvolutionRepository { get; }
    public WorkerSessionRouter WorkerSessionRouter { get; }
    public IWorkerRoutingStore WorkerRoutingStore { get; }
    public WorkerExecutionRepository WorkerExecutionRepository { get; }
    public ILeaderReviewOrchestrator LeaderReviewOrchestrator { get; }
    public LeaderAuthoritySettingsService LeaderAuthoritySettings { get; }
    public ILeaderReviewAutoProceedExecutor LeaderReviewAutoProceed { get; }
    public ILeaderReviewAskUserGate LeaderReviewAskUserGate { get; }
    public ILeaderReviewUserResponseBinder LeaderReviewUserResponseBinder { get; }
    public B1ProjectGovernanceRepository B1ProjectGovernance { get; }
    public B1AuthorityRepository B1AuthorityRepository { get; }
    public B1WorkerExecutionBridgeService B1WorkerExecutionBridge { get; }
    public CanonicalWorkerLaunchService CanonicalWorkerLaunch { get; }
    public B1EvidenceRepository B1Evidence { get; }
    public B1NonAuthoritativeCommandService B1NonAuthoritativeCommands { get; }
    public B1AuthorityCommandService B1AuthorityCommands { get; }
    public B1ProjectionService B1Projections { get; }
    public LibraryProjectionContractService LibraryProjectionContracts { get; }
    public LibraryAcceptedStateReader LibraryAcceptedStateReader { get; }
    public ProjectWorldEntryStatusService ProjectWorldEntryStatus { get; }
    public ProjectWorldInitializationService ProjectWorldInitialization { get; }
    public GuidedHandoffCommandService GuidedHandoffCommands { get; }
    public GuidedHandoffComposerService GuidedHandoffComposer { get; }
    public B1AgentParticipationAdapter B1AgentParticipation { get; }
    public GuidedDecisionService GuidedDecision { get; }
    public ManualLibraryProjectionService ManualLibraryProjection { get; }
    public IUserPrincipalProvider UserPrincipalProvider { get; }

    public AgentRuntimeRegistry RuntimeRegistry { get; }

    public IAgentHost AgentHost { get; }

    public TimeProvider TimeProvider { get; }

    public string? RuntimeUnavailableDetail { get; private set; }

    public LeaderSummaryRecoveryReport? LastLeaderSummaryRecoveryReport { get; private set; }

    public static AppServices CreateDefault(
        Func<CancellationToken, Task<IAgentRuntime>>? runtimeFactory = null,
        IReadOnlyList<Func<CancellationToken, Task<IAgentRuntime>>>? additionalRuntimeFactories = null,
        IReadOnlyList<ConfiguredAgentRuntimeFactory>? configuredRuntimeFactories = null) =>
        CreateForDatabasePath(
            DatabasePathProvider.GetDefaultDatabasePath(),
            TimeProvider.System,
            runtimeFactory: runtimeFactory,
            additionalRuntimeFactories: additionalRuntimeFactories,
            configuredRuntimeFactories: configuredRuntimeFactories);

    public static AppServices CreateForDatabasePath(
        string databasePath,
        TimeProvider? timeProvider = null,
        AgentRuntimeRegistry? runtimeRegistry = null,
        Func<CancellationToken, Task<IAgentRuntime>>? runtimeFactory = null,
        IReadOnlyList<Func<CancellationToken, Task<IAgentRuntime>>>? additionalRuntimeFactories = null,
        IReadOnlyList<ConfiguredAgentRuntimeFactory>? configuredRuntimeFactories = null)
    {
        var database = new WorkbenchDatabase(databasePath);
        var projectRepository = new ProjectRepository(database);
        var layoutRepository = new ProjectLayoutRepository(database);
        var effectiveTimeProvider = timeProvider ?? TimeProvider.System;
        var effectiveRuntimeRegistry = runtimeRegistry ?? new AgentRuntimeRegistry();
        var agentHost = new InProcessAgentHost(effectiveRuntimeRegistry);
        var projectLeaders = new ProjectLeaderRepository(database);
        var leaderEpochs = new LeaderSessionEpochRepository(database);
        var leaderMessages = new LeaderMessageRepository(database);
        var projectSummaries = new ProjectSummaryRepository(database);
        var leaderSummaryRecovery = new LeaderSummaryRecoveryService(
            leaderMessages,
            projectSummaries,
            effectiveTimeProvider);
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
        var libraryEvolutionRepository = new ProjectLibraryEvolutionRepository(database);
        var libraryProposalService = new ProjectLibraryProposalService(database, effectiveTimeProvider);
        var taskEvents = new TaskEventRepository(database);
        var reviewState = new AssignmentReviewStateRepository(database);
        var typedReviewState = new LeaderReviewStateRepository(database);
        var leaderAuthoritySettings = new LeaderAuthoritySettingsService(
            new WorkbenchSettingsRepository(database), new ProjectSettingsRepository(database));
        var leaderAutoProceed = new LeaderReviewAutoProceedExecutor(new TaskRepository(database), reviewState, typedReviewState, effectiveTimeProvider);
        var leaderAskUserGate = new LeaderReviewAskUserGate(new TaskRepository(database), reviewState, typedReviewState, leaderAuthoritySettings, projectLeaders, leaderMessages, effectiveTimeProvider);
        var leaderUserResponseBinder = new LeaderReviewUserResponseBinder(typedReviewState, taskEvents, effectiveTimeProvider);
        var workerExecutionRepository = new WorkerExecutionRepository(database);
        var workerRoutingStore = new TaskEventWorkerRoutingStore(taskEvents, workerExecutionRepository);
        var b1AuthorityRepository = new B1AuthorityRepository(database);
        var b1Evidence = new B1EvidenceRepository(database);
        var b1ProjectGovernance = new B1ProjectGovernanceRepository(database);
        var b1ClaimHandoffRepository = new B1ClaimHandoffRepository(database);
        var b1NonAuthoritativeCommands = new B1NonAuthoritativeCommandService(
            new B1RoutingRepository(database),
            b1ClaimHandoffRepository);
        var guidedHandoffCommands = new GuidedHandoffCommandService(b1ClaimHandoffRepository);
        var b1AuthorityEvaluator = new B1AuthorityEvaluator();
        var b1AuthorityCommands = new B1AuthorityCommandService(
            b1AuthorityRepository,
            b1AuthorityEvaluator,
            effectiveTimeProvider);
        var b1WorkerExecutionBridge = new B1WorkerExecutionBridgeService(
            new B1WorkerBridgeRepository(database),
            b1AuthorityRepository,
            b1Evidence);
        var leaderReviewOrchestrator = new LeaderReviewOrchestrator(
            new LeaderReviewInputBuilder(projectRepository, new TaskRepository(database), new TaskRevisionRepository(database), taskEvents, b1WorkerExecutionBridge, workerExecutionRepository),
            new LeaderReviewRuntimeAdapter(agentHost), reviewState, typedReviewState, leaderAuthoritySettings, new TaskRepository(database), projectLeaders, leaderEpochs, effectiveRuntimeRegistry, effectiveTimeProvider,
            leaderAutoProceed, leaderAskUserGate);
        var b1Projections = new B1ProjectionService(b1AuthorityRepository);
        var projectMemoryApi = new ProjectMemoryApi(dailySummaryRepository, projectMemoryPreferencesRepository, leaderEpochs, leaderMessages, libraryProposalService, libraryEvolutionRepository, b1Projections);
        var leaderMemoryPolicyCoordinator = new LeaderMemoryPolicyCoordinator(effectiveRuntimeRegistry, projectMemoryApi, effectiveTimeProvider);
        var libraryProjectionContracts = new LibraryProjectionContractService(
            b1AuthorityRepository,
            libraryEvolutionRepository,
            libraryProposalService);
        var libraryAcceptedStateReader = new LibraryAcceptedStateReader(
            b1AuthorityRepository,
            libraryEvolutionRepository);
        var projectWorldEntryStatus = new ProjectWorldEntryStatusService(
            b1ProjectGovernance,
            b1Projections);
        var userPrincipalProvider = new LocalUserPrincipalProvider();
        var projectWorldInitialization = new ProjectWorldInitializationService(
            b1AuthorityRepository,
            b1AuthorityCommands,
            b1ProjectGovernance);
        var guidedHandoffComposer = new GuidedHandoffComposerService(
            b1AuthorityRepository,
            guidedHandoffCommands,
            effectiveTimeProvider);
        var b1AgentParticipation = new B1AgentParticipationAdapter(
            b1AuthorityRepository,
            b1NonAuthoritativeCommands,
            guidedHandoffComposer,
            effectiveTimeProvider,
            agentHost);
        var guidedDecision = new GuidedDecisionService(
            b1AuthorityRepository,
            b1AuthorityEvaluator,
            b1AuthorityCommands,
            effectiveTimeProvider);
        var manualLibraryProjection = new ManualLibraryProjectionService(
            b1AuthorityRepository,
            libraryProjectionContracts,
            projectMemoryApi,
            libraryProposalService,
            effectiveTimeProvider);
        return new AppServices(
            database,
            projectRepository,
            layoutRepository,
            projectOpenService,
            projectLeaders,
            leaderEpochs,
            leaderMessages,
            projectSummaries,
            leaderSummaryRecovery,
            new WorkbenchSettingsRepository(database),
            new ProjectSettingsRepository(database),
            new ProjectMemoryService(new ProjectActivityRepository(database), memoryRepository, effectiveTimeProvider),
            synthesisRepository,
            dailySummaryRepository,
            projectMemoryPreferencesRepository,
            projectMemoryApi,
            leaderMemoryPolicyCoordinator,
            new LeaderBootContextBuilder(projectMemoryApi, leaderEpochs, continuityPlans),
            new LeaderSessionRolloverService(
                effectiveRuntimeRegistry,
                projectLeaders,
                leaderMessages,
                effectiveTimeProvider,
                agentHost),
            new TaskRepository(database),
            new TaskRevisionRepository(database),
            new ProjectLibraryRepository(database),
            libraryEvolutionRepository,
            new WorkerSessionRouter(effectiveRuntimeRegistry, workerRoutingStore, effectiveTimeProvider, reviewState, leaderReviewOrchestrator, workerExecutionRepository, agentHost, new TaskRevisionRepository(database), taskEvents, b1WorkerExecutionBridge, b1NonAuthoritativeCommands),
            workerRoutingStore,
            workerExecutionRepository,
            leaderReviewOrchestrator,
            leaderAuthoritySettings,
            leaderAutoProceed,
            leaderAskUserGate,
            leaderUserResponseBinder,
            b1ProjectGovernance,
            b1AuthorityRepository,
            b1WorkerExecutionBridge,
            new CanonicalWorkerLaunchService(b1AuthorityRepository, b1NonAuthoritativeCommands, b1ProjectGovernance, effectiveTimeProvider),
            b1Evidence,
            b1NonAuthoritativeCommands,
            b1AuthorityCommands,
            b1Projections,
            libraryProjectionContracts,
            libraryAcceptedStateReader,
            projectWorldEntryStatus,
            projectWorldInitialization,
            guidedHandoffCommands,
            guidedHandoffComposer,
            b1AgentParticipation,
            guidedDecision,
            manualLibraryProjection,
            userPrincipalProvider,
            effectiveRuntimeRegistry,
            agentHost,
            effectiveTimeProvider,
            CreateRuntimeFactories(runtimeFactory, additionalRuntimeFactories),
            configuredRuntimeFactories?.ToArray() ?? []);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await Database.InitializeAsync(cancellationToken);
        LastLeaderSummaryRecoveryReport = await LeaderSummaryRecoveryService.RecoverAsync(cancellationToken);
        await ProjectMemorySynthesisRepository.RecoverRunningAsync(cancellationToken);
    }

    public async Task RetryRuntimeAsync(CancellationToken cancellationToken = default)
    {
        if ((_runtimeFactories.Count == 0 && _configuredRuntimeFactories.Count == 0) || Volatile.Read(ref _disposeRequested) != 0)
        {
            return;
        }

        await _runtimeConnectionGate.WaitAsync(cancellationToken);
        try
        {
            if (Volatile.Read(ref _disposeRequested) != 0 ||
                (_connectedRuntimeFactories.Count == _runtimeFactories.Count &&
                 _connectedConfiguredRuntimeFactories.Count == _configuredRuntimeFactories.Count))
            {
                return;
            }

            RuntimeUnavailableDetail = null;
            for (var index = 0; index < _runtimeFactories.Count; index++)
            {
                if (_connectedRuntimeFactories.Contains(index))
                {
                    continue;
                }

                try
                {
                    var runtime = await _runtimeFactories[index](cancellationToken);
                    if (Volatile.Read(ref _disposeRequested) != 0)
                    {
                        if (runtime is IAsyncDisposable lateDisposable)
                        {
                            await lateDisposable.DisposeAsync();
                        }

                        return;
                    }

                    RuntimeRegistry.Register(runtime);
                    _connectedRuntimeFactories.Add(index);
                    _connectedRuntimeInstances[index] = runtime;
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
                    RuntimeUnavailableDetail ??= "An Agent runtime could not be started.";
                }
            }

            for (var index = 0; index < _configuredRuntimeFactories.Count; index++)
            {
                if (_connectedConfiguredRuntimeFactories.Contains(index))
                {
                    continue;
                }

                var factory = _configuredRuntimeFactories[index];
                var settings = await WorkbenchSettingsRepository.GetAgentRuntimeSettingsAsync(
                    factory.AgentId,
                    cancellationToken: cancellationToken);
                if (!settings.IsEnabled)
                {
                    continue;
                }

                try
                {
                    var runtime = await factory.ConnectAsync(settings, cancellationToken);
                    if (Volatile.Read(ref _disposeRequested) != 0)
                    {
                        if (runtime is IAsyncDisposable lateDisposable)
                        {
                            await lateDisposable.DisposeAsync();
                        }

                        return;
                    }

                    RuntimeRegistry.Register(runtime);
                    _connectedConfiguredRuntimeFactories.Add(index);
                    _connectedConfiguredRuntimeInstances[index] = runtime;
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
                    RuntimeUnavailableDetail ??= "An enabled Agent runtime could not be started.";
                }
            }

            if (RuntimeRegistry.Runtimes.Count > 0)
            {
                RuntimeUnavailableDetail = null;
            }
        }
        finally
        {
            _runtimeConnectionGate.Release();
        }
    }

    public async Task<bool> ReleaseRuntimeForExternalSessionAsync(
        Workbench.Runtime.Providers.ProviderAccountId accountId,
        CancellationToken cancellationToken = default)
    {
        await _runtimeConnectionGate.WaitAsync(cancellationToken);
        try
        {
            if (!RuntimeRegistry.TryGetByAccount(accountId, out var runtime) || runtime is null)
            {
                return false;
            }

            if (runtime.HasActiveTurns)
            {
                return false;
            }

            RuntimeRegistry.Unregister(accountId);
            foreach (var pair in _connectedRuntimeInstances.Where(pair => ReferenceEquals(pair.Value, runtime)).ToArray())
            {
                _connectedRuntimeInstances.Remove(pair.Key);
                _connectedRuntimeFactories.Remove(pair.Key);
            }

            foreach (var pair in _connectedConfiguredRuntimeInstances.Where(pair => ReferenceEquals(pair.Value, runtime)).ToArray())
            {
                _connectedConfiguredRuntimeInstances.Remove(pair.Key);
                _connectedConfiguredRuntimeFactories.Remove(pair.Key);
            }

            if (runtime is IAsyncDisposable disposable)
            {
                _ownedRuntimes.Remove(disposable);
                await disposable.DisposeAsync();
            }

            return true;
        }
        finally
        {
            _runtimeConnectionGate.Release();
        }
    }

    public Task RestoreRuntimeAfterExternalSessionAsync(CancellationToken cancellationToken = default) =>
        RetryRuntimeAsync(cancellationToken);

    private static IReadOnlyList<Func<CancellationToken, Task<IAgentRuntime>>> CreateRuntimeFactories(
        Func<CancellationToken, Task<IAgentRuntime>>? runtimeFactory,
        IReadOnlyList<Func<CancellationToken, Task<IAgentRuntime>>>? additionalRuntimeFactories)
    {
        var factories = new List<Func<CancellationToken, Task<IAgentRuntime>>>();
        if (runtimeFactory is not null)
        {
            factories.Add(runtimeFactory);
        }

        if (additionalRuntimeFactories is not null)
        {
            factories.AddRange(additionalRuntimeFactories);
        }

        return factories;
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
            _connectedRuntimeInstances.Clear();
            _connectedConfiguredRuntimeInstances.Clear();
            _connectedRuntimeFactories.Clear();
            _connectedConfiguredRuntimeFactories.Clear();
        }
        finally
        {
            _runtimeConnectionGate.Release();
        }
    }
}
