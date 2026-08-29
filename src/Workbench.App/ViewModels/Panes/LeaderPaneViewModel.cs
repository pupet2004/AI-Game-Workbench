using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.App.ViewModels.Leader;
using Workbench.App.Leader;
using Workbench.Core.Leaders;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Registry;
using Workbench.Storage.Leaders;
using Workbench.Storage.Tasks;
using Workbench.App.Worker;
using Workbench.App.Memory;
using Workbench.App.Services;
using Workbench.App.Skills;
using Workbench.Core.Tasks;
using Workbench.Core.Workers;
using Workbench.Project.Git;
using Workbench.Storage.Memory;
using Workbench.App.AgentHost;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.ViewModels.Panes;

public sealed partial class LeaderPaneViewModel : ViewModelBase
{
    private readonly CoreProject _project;
    private readonly AgentRuntimeRegistry _runtimeRegistry;
    private readonly ProjectLeaderSessionManager _sessionManager;
    private readonly LeaderConversationState _conversation;
    private readonly Func<Task> _focus;
    private readonly Func<CancellationToken, Task>? _reconnectRuntime;
    private readonly LeaderSessionRotationStateService? _rotationState;
    private readonly LeaderSessionRolloverService? _rolloverService;
    private readonly ILeaderBootContextBuilder? _bootContextBuilder;
    private readonly LeaderMemoryPolicyCoordinator? _memoryPolicyCoordinator;
    private readonly LeaderDraftProposalBuilder? _draftProposalBuilder;
    private readonly TaskRevisionRepository? _taskRevisions;
    private readonly WorkerSessionRouter? _workerSessionRouter;
    private readonly Func<CancellationToken, Task>? _refreshWorkPane;
    private readonly IProjectMemoryApi? _projectMemoryApi;
    private readonly TimeProvider _timeProvider;
    private readonly Func<CancellationToken, Task>? _refreshLibraryPane;
    private readonly ILeaderReviewUserResponseBinder? _responseBinder;
    private readonly GitSnapshot? _git;
    private readonly ProjectSummaryRepository? _projectSummaryRepository;
    private readonly IAgentHost _agentHost;
    private readonly SynchronizationContext? _leaderContext;
    private CancellationTokenSource? _activeTurnCancellation;
    private AgentSessionId? _workerApprovalSessionId;
    private bool _initialAnchorRequested;

    public LeaderPaneViewModel(
        CoreProject project,
        AgentRuntimeRegistry runtimeRegistry,
        ProjectLeaderSessionManager sessionManager,
        Func<Task> focus,
        string? runtimeUnavailableDetail = null,
        Func<CancellationToken, Task>? reconnectRuntime = null,
        LeaderSessionRotationStateService? rotationState = null,
        LeaderSessionRolloverService? rolloverService = null,
        LeaderSessionEpochRepository? epochRepository = null,
        LeaderMessageRepository? messageRepository = null,
        ILeaderBootContextBuilder? bootContextBuilder = null,
        LeaderMemoryPolicyCoordinator? memoryPolicyCoordinator = null,
        TaskRepository? taskRepository = null,
        TaskRevisionRepository? taskRevisionRepository = null,
        WorkerSessionRouter? workerSessionRouter = null,
        Func<CancellationToken, Task>? refreshWorkPane = null,
        IProjectMemoryApi? projectMemoryApi = null,
        TimeProvider? timeProvider = null,
        Func<CancellationToken, Task>? refreshLibraryPane = null,
        ILeaderReviewUserResponseBinder? responseBinder = null,
        GitSnapshot? git = null,
        ProjectSummaryRepository? projectSummaryRepository = null,
        IAgentHost? agentHost = null)
    {
        _project = project;
        _runtimeRegistry = runtimeRegistry;
        _sessionManager = sessionManager;
        _conversation = sessionManager.GetOrCreate(project.Id);
        _focus = focus;
        _reconnectRuntime = reconnectRuntime;
        _rotationState = rotationState;
        _rolloverService = rolloverService;
        _bootContextBuilder = bootContextBuilder;
        _memoryPolicyCoordinator = memoryPolicyCoordinator;
        _draftProposalBuilder = taskRepository is null ? null : new LeaderDraftProposalBuilder(project.Id, taskRepository);
        _taskRevisions = taskRevisionRepository;
        _workerSessionRouter = workerSessionRouter;
        _refreshWorkPane = refreshWorkPane;
        _projectMemoryApi = projectMemoryApi;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _refreshLibraryPane = refreshLibraryPane;
        _responseBinder = responseBinder;
        _git = git;
        _projectSummaryRepository = projectSummaryRepository;
        _agentHost = agentHost ?? new InProcessAgentHost(runtimeRegistry);
        _leaderContext = SynchronizationContext.Current;
        _agentHost.EventReceived += OnAgentHostEvent;
        if ((epochRepository is null) != (messageRepository is null))
        {
            throw new ArgumentException("History repositories must be supplied together.");
        }
        if (epochRepository is not null)
        {
            History = new LeaderEpochHistoryViewModel(project.Id, epochRepository, messageRepository!);
        }
        if (_conversation.RuntimeErrorDetail is null && runtimeUnavailableDetail is not null)
        {
            _conversation.RuntimeErrorDetail = runtimeUnavailableDetail;
        }

        RebuildApprovalOptions();
    }

    public ObservableCollection<LeaderModelOptionViewModel> AvailableModels => _conversation.AvailableModels;

    public ObservableCollection<LeaderMessageViewModel> Messages => _conversation.Messages;

    public ObservableCollection<LeaderActivityViewModel> Activities => _conversation.Activities;

    public ObservableCollection<LeaderChangedFileViewModel> ChangedFiles => _conversation.ChangedFiles;

    internal ProjectSummaryRepository? SummaryRepository => _projectSummaryRepository;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDraftConfirmation), nameof(CurrentWorkerProfile))]
    public partial LeaderDraftConfirmation? DraftConfirmation { get; set; }

    public bool HasDraftConfirmation => DraftConfirmation is not null;
    public string? CurrentWorkerProfile => DraftConfirmation is null ? null : $"{DraftConfirmation.Resource.DisplayLabel} · {DraftConfirmation.Resource.ModelDisplayName}";
    public ObservableCollection<WorkerResource> WorkerResources { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMemoryCommandStatus))]
    public partial string? MemoryCommandStatus { get; set; }

    public bool HasMemoryCommandStatus => !string.IsNullOrWhiteSpace(MemoryCommandStatus);

    public LeaderEpochHistoryViewModel? History { get; }

    public bool HasHistory => History is not null;

    [ObservableProperty]
    public partial int InitialAnchorRequestVersion { get; set; }

    public ObservableCollection<LeaderApprovalOptionViewModel> ApprovalOptions { get; } = [];

    public AgentSession? Session => _conversation.Session;

    public Guid ProjectLeaderId => _project.Id;

    public Guid? SessionEpochId => _conversation.Epoch?.Id;

    public bool IsBusy => _conversation.IsBusy;

    public bool IsRuntimeAvailable => _conversation.RuntimeAccountAvailable;

    public bool CanRetryRuntime => !IsBusy && !IsRuntimeAvailable;

    public bool IsNativeAgentSurfaceEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("WORKBENCH_NATIVE_AGENT_SURFACE"), "1", StringComparison.Ordinal) ||
        string.Equals(Environment.GetEnvironmentVariable("WORKBENCH_NATIVE_AGENT_SURFACE"), "true", StringComparison.OrdinalIgnoreCase);

    public bool IsLegacyTranscriptVisible => !IsNativeAgentSurfaceEnabled;

    public bool ShowRuntimeUnavailableOverlay => CanRetryRuntime && Messages.Count == 0;

    public bool HasRuntimeStatus => !string.IsNullOrWhiteSpace(RuntimeStatus);

    public string? RuntimeStatus => _conversation.RuntimeStatus;

    public string? RuntimeErrorDetail => _conversation.RuntimeErrorDetail;

    public bool IsModelSelectionLocked => Session is not null;

    public bool IsModelSelectionEnabled => IsRuntimeAvailable && !IsModelSelectionLocked && !IsBusy;

    public AgentAccessMode AccessMode => _conversation.AccessMode;

    public string AccessModeLabel => AccessMode == AgentAccessMode.Full
        ? LocalizationService.Current["Surface.FullAccess"]
        : LocalizationService.Current["Surface.RestrictedAccess"];

    public string AccessModeDescription => AccessMode == AgentAccessMode.Full
        ? LocalizationService.Current["Surface.FullAccessDescription"]
        : LocalizationService.Current["Surface.RestrictedAccessDescription"];

    public bool CanChangeAccessMode => !IsBusy && !HasPendingApproval && !HasPendingQuestion;

    [RelayCommand(CanExecute = nameof(CanChangeAccessMode))]
    private void ToggleAccessMode()
    {
        _conversation.AccessMode = AccessMode == AgentAccessMode.Full
            ? AgentAccessMode.Restricted
            : AgentAccessMode.Full;
        OnPropertyChanged(nameof(AccessMode));
        OnPropertyChanged(nameof(AccessModeLabel));
        OnPropertyChanged(nameof(AccessModeDescription));
        NotifyCommandState();
    }

    public bool CanSend =>
        !IsBusy &&
        !_conversation.IsRolloverRunning &&
        !HasPendingApproval &&
        !HasPendingRotationDecision &&
        (IsRuntimeAvailable ||
         (_conversation.Session is not null && _conversation.SessionNeedsResume && _reconnectRuntime is not null)) &&
        SelectedModel is not null &&
        !string.IsNullOrWhiteSpace(DraftMessage);

    public bool CanStop => IsBusy && Session is not null;

    public bool CanSteer => IsBusy && Session is not null &&
        _runtimeRegistry.GetByAccount(Session.AccountId).Capabilities.HasFlag(AgentCapability.Steer);

    public bool SupportsSteer => Session is not null &&
        _runtimeRegistry.GetByAccount(Session.AccountId).Capabilities.HasFlag(AgentCapability.Steer);

    public AgentApprovalRequested? PendingApproval => _conversation.PendingApproval;

    public AgentQuestionRequested? PendingQuestion => _conversation.PendingQuestion;

    public bool HasPendingQuestion => PendingQuestion is not null;

    public bool HasPendingApproval => PendingApproval is not null;

    public bool ShowNativeApproval => HasPendingApproval && !IsNativeAgentSurfaceEnabled;

    public bool HasPendingRotationDecision => _conversation.HasPendingRotationDecision;

    public bool CanStartNewBrain =>
        Session is not null &&
        _conversation.Epoch is not null &&
        !IsBusy &&
        !HasPendingApproval &&
        !HasPendingRotationDecision &&
        !_conversation.IsRolloverRunning &&
        _rolloverService is not null;

    public bool IsBrainPickerVisible => _conversation.IsBrainPickerVisible;

    public bool CanChangeBrain =>
        Session is not null &&
        AvailableModels.Count > 0 &&
        !IsBusy &&
        !HasPendingApproval &&
        !HasPendingRotationDecision &&
        !_conversation.IsRolloverRunning &&
        _rolloverService is not null;

    public LeaderModelOptionViewModel? BrainTargetModel
    {
        get => _conversation.BrainTargetModel;
        set
        {
            if (ReferenceEquals(value, _conversation.BrainTargetModel)) return;
            _conversation.BrainTargetModel = value;
            OnPropertyChanged();
            NotifyCommandState();
        }
    }

    public string? ApprovalError => _conversation.ApprovalError;

    public string? RotationMessage => _conversation.RotationMessage;

    public bool HasRotationMessage => !string.IsNullOrWhiteSpace(RotationMessage);

    public bool ShowRotationMessage => HasRotationMessage && !HasPendingRotationDecision;

    public LeaderModelOptionViewModel? SelectedModel
    {
        get => _conversation.SelectedModel;
        set
        {
            if (IsModelSelectionLocked || ReferenceEquals(value, _conversation.SelectedModel))
            {
                return;
            }

            _conversation.SelectedModel = value;
            OnPropertyChanged();
            NotifyCommandState();
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    public partial string DraftMessage { get; set; } = string.Empty;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _sessionManager.LoadAsync(_project.Id, cancellationToken);
        if (History is not null)
        {
            await History.InitializeAsync(cancellationToken);
        }
        if (!_initialAnchorRequested)
        {
            _initialAnchorRequested = true;
            InitialAnchorRequestVersion++;
        }
        if (_rotationState is not null)
        {
            var state = await _rotationState.GetAsync(_project.Id, cancellationToken);
            _conversation.RotationMessage = state.Evaluation.IsDue
                ? state.EffectivePolicy switch
                {
                    LeaderSessionRotationPolicy.Auto => LocalizationService.Current["Dynamic.RotationAuto"],
                    LeaderSessionRotationPolicy.Ask => LocalizationService.Current["Dynamic.RotationAsk"],
                    _ => null
                }
                : null;
        }

        if (_conversation.Session is not null)
        {
            try
            {
                if (_runtimeRegistry.Runtimes.Count == 0 && _reconnectRuntime is not null)
                {
                    await _reconnectRuntime(cancellationToken);
                }

                _runtimeRegistry.GetByAccount(_conversation.Session.AccountId);
                _conversation.RuntimeAccountAvailable = true;
                _conversation.ModelsLoaded = true;
                _conversation.RuntimeStatus = null;
                _conversation.RuntimeErrorDetail = null;
            }
            catch (KeyNotFoundException)
            {
                SetCurrentSessionUnavailable();
            }

            NotifyAllState();
            return;
        }

        if (_conversation.ModelsLoaded)
        {
            NotifyAllState();
            return;
        }

        _conversation.RuntimeStatus = null;
        try
        {
            if (_runtimeRegistry.Runtimes.Count == 0 && _reconnectRuntime is not null)
            {
                await _reconnectRuntime(cancellationToken);
            }

            await LoadAvailableModelsAsync(cancellationToken);

            if (AvailableModels.Count == 0)
            {
                SetRuntimeUnavailable("No agent runtime is available.");
            }
            else
            {
                _conversation.RuntimeErrorDetail = null;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            SetRuntimeUnavailable("Codex could not be started.");
        }

        NotifyAllState();
    }

    private async Task LoadAvailableModelsAsync(CancellationToken cancellationToken)
    {
        var previous = _conversation.SelectedModel;
        var available = await _runtimeRegistry.GetAvailableModelsAsync(cancellationToken);
        AvailableModels.Clear();
        foreach (var profile in available)
        {
            var runtime = _runtimeRegistry.GetByAccount(profile.AccountId);
            AvailableModels.Add(new LeaderModelOptionViewModel(
                profile,
                runtime.Provider.DisplayName,
                runtime.Account.DisplayName));
        }

        _conversation.ModelsLoaded = AvailableModels.Count > 0;
        _conversation.RuntimeAccountAvailable = AvailableModels.Count > 0;
        if (previous is not null)
        {
            _conversation.SelectedModel = AvailableModels.FirstOrDefault(option =>
                option.Profile.AccountId == previous.Profile.AccountId &&
                string.Equals(option.Profile.Model.ModelId, previous.Profile.Model.ModelId, StringComparison.Ordinal)) ?? previous;
        }
        else if (AvailableModels.Count == 1)
        {
            _conversation.SelectedModel = AvailableModels[0];
        }

        _conversation.BrainTargetModel = _conversation.SelectedModel;
    }

    public Task SendAsync(CancellationToken cancellationToken = default) =>
        SendAsync([], cancellationToken);

    public async Task SendAsync(
        IReadOnlyList<AgentInputPart> inputs,
        CancellationToken cancellationToken = default)
    {
        if (IsBusy || _conversation.IsRolloverRunning)
        {
            throw new InvalidOperationException("The Project Leader already has an active turn or rollover.");
        }

        if (HasPendingRotationDecision)
        {
            throw new InvalidOperationException("Choose whether to continue the previous Leader session or start fresh.");
        }

        var selectedModel = SelectedModel
            ?? throw new InvalidOperationException("Select a model before sending a message.");
        var text = DraftMessage;
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        if (_conversation.Session is not null && _rotationState is not null)
        {
            var rotation = await _rotationState.GetAsync(_project.Id, cancellationToken);
            if (rotation.Evaluation.IsDue && rotation.EffectivePolicy == LeaderSessionRotationPolicy.Ask)
            {
                _conversation.HasPendingRotationDecision = true;
                _conversation.RotationMessage = LocalizationService.Current["Dynamic.FreshSessionAvailable"];
                NotifyAllState();
                return;
            }

            if (rotation.Evaluation.IsDue && rotation.EffectivePolicy == LeaderSessionRotationPolicy.Auto)
            {
                try
                {
                    await ExecuteRolloverAsync("WorkdayBoundary", cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    AddErrorMessage(LocalizationService.Current["Dynamic.LeaderStartFailed"]);
                    NotifyAllState();
                    return;
                }
            }
        }

        await SendCoreAsync(text, inputs, cancellationToken);
    }

    public async Task ContinuePreviousAsync(CancellationToken cancellationToken = default)
    {
        if (!HasPendingRotationDecision)
        {
            throw new InvalidOperationException("No Leader session rotation decision is pending.");
        }

        var text = DraftMessage;
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        _conversation.HasPendingRotationDecision = false;
        _conversation.RotationMessage = null;
        await SendCoreAsync(text, [], cancellationToken);
    }

    public async Task StartFreshAsync(CancellationToken cancellationToken = default)
    {
        if (!HasPendingRotationDecision)
        {
            throw new InvalidOperationException("No Leader session rotation decision is pending.");
        }

        var text = DraftMessage;
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        try
        {
            await ExecuteRolloverAsync("WorkdayBoundary", cancellationToken);
            _conversation.HasPendingRotationDecision = false;
            await SendCoreAsync(text, [], cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            AddErrorMessage(LocalizationService.Current["Dynamic.LeaderStartFailed"]);
            NotifyAllState();
        }
    }

    public async Task StartNewBrainAsync(CancellationToken cancellationToken = default)
    {
        if (!CanStartNewBrain)
        {
            throw new InvalidOperationException(LocalizationService.Current["Dynamic.NewBrainUnavailable"]);
        }

        try
        {
            await ExecuteRolloverAsync("Manual", cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            AddErrorMessage(LocalizationService.Current["Dynamic.LeaderFreshSessionFailed"]);
            NotifyAllState();
        }
    }

    private async Task SendCoreAsync(
        string text,
        IReadOnlyList<AgentInputPart> inputs,
        CancellationToken cancellationToken)
    {
        var resultId = Guid.NewGuid();
        var selectedModel = SelectedModel
            ?? throw new InvalidOperationException("Select a model before sending a message.");

        _conversation.IsBusy = true;
        _conversation.Activities.Clear();
        _conversation.ChangedFiles.Clear();
        _conversation.ApprovalError = null;
        MemoryCommandStatus = null;
        NotifyAllState();

        var bootPendingDelivery = _bootContextBuilder is not null &&
                                  (_conversation.Epoch is null ||
                                   _conversation.Epoch.BootContextDeliveredAt is null);
        AgentRequest runtimeRequest;
        if (bootPendingDelivery)
        {
            try
            {
                runtimeRequest = await _bootContextBuilder!.BuildAsync(_project, text, cancellationToken);
                runtimeRequest = new AgentRequest(
                    $"{runtimeRequest.Text}\n\n{LeaderSummaryAdmissionInstruction.Text}",
                    LeaderResponseSchema.Json,
                    inputs,
                    AccessMode);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _conversation.IsBusy = false;
                NotifyAllState();
                throw;
            }
            catch
            {
                AddErrorMessage("Project memory could not be loaded.");
                _conversation.IsBusy = false;
                NotifyAllState();
                return;
            }
        }
        else
        {
            runtimeRequest = new AgentRequest(
                $"{text}\n\n{LeaderSummaryAdmissionInstruction.Text}",
                LeaderResponseSchema.Json,
                inputs,
                AccessMode);
        }
        runtimeRequest = new AgentRequest(
            $"{runtimeRequest.Text}\n\n{WorkbenchSkillCatalog.Load(WorkbenchSkillRole.Leader)}",
            runtimeRequest.OutputSchema,
            runtimeRequest.Inputs,
            AccessMode);

        if (_responseBinder is not null && await _responseBinder.HasSingletonOpenGateAsync(_project.Id, cancellationToken))
        {
            DraftMessage = string.Empty;
            Messages.Add(new LeaderMessageViewModel(LeaderMessageRole.User, text));
            var persisted = await _sessionManager.PersistUserMessageAsync(_conversation, text, cancellationToken);
            if (persisted is not null) await _responseBinder.BindAsync(_project.Id, persisted.Id, cancellationToken);
            _conversation.IsBusy = false;
            NotifyAllState();
            return;
        }

        DraftMessage = string.Empty;
        Messages.Add(new LeaderMessageViewModel(LeaderMessageRole.User, text));
        NotifyAllState();

        LeaderMessageViewModel? assistant = null;
        var turnCompleted = false;
        var structuredResponseParsed = false;
        var structuredOutputExpected = _draftProposalBuilder is not null || _projectMemoryApi is not null;
        var structuredOutputBuffer = new System.Text.StringBuilder();
        IReadOnlyList<SummaryDelta> summaryDeltas = [];
        var suppressSummaryDeltas = LeaderSummaryAdmissionInstruction.IsReadOnlyRequest(text);
        using var turnCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _activeTurnCancellation = turnCancellation;
        cancellationToken = turnCancellation.Token;
        var retainRuntimeStatus = false;
        try
        {
            if (_conversation.SessionNeedsResume &&
                !_conversation.RuntimeAccountAvailable &&
                _reconnectRuntime is not null)
            {
                await _reconnectRuntime(cancellationToken);
            }

            var runtime = _runtimeRegistry.GetByAccount(selectedModel.Profile.AccountId);
            if (_conversation.Session is null)
            {
                var createdSession = await _agentHost.CreateSessionAsync(
                    new CreateAgentSessionRequest(
                        selectedModel.Profile.AccountId,
                        selectedModel.Profile.Model.ModelId,
                        _project.RootPath,
                        AccessMode),
                    cancellationToken);
                try
                {
                    await _sessionManager.PersistNewEpochAsync(
                        _conversation,
                        createdSession,
                        cancellationToken);
                    _conversation.Session = createdSession;
                }
                catch
                {
                    _conversation.Session = null;
                    throw;
                }
            }
            else if (_conversation.SessionNeedsResume)
            {
                try
                {
                    _conversation.Session = await _agentHost.ResumeSessionAsync(
                        _conversation.Session,
                        cancellationToken);
                    _conversation.SessionNeedsResume = false;
                    _conversation.RuntimeAccountAvailable = true;
                    _conversation.RuntimeStatus = null;
                    _conversation.RuntimeErrorDetail = null;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    retainRuntimeStatus = true;
                    _conversation.RuntimeAccountAvailable = false;
                    _conversation.RuntimeStatus = "Leader 会话无法恢复";
                    _conversation.RuntimeErrorDetail = exception.Message;
                    AddErrorMessage($"无法恢复当前 Leader 会话：{exception.Message}");
                    NotifyAllState();
                    return;
                }
            }

            var persistedUserMessage = await _sessionManager.PersistUserMessageAsync(_conversation, text, cancellationToken);
            if (persistedUserMessage is not null && _responseBinder is not null && await _responseBinder.BindAsync(_project.Id, persistedUserMessage.Id, cancellationToken) is not null)
            {
                _conversation.IsBusy = false;
                NotifyAllState();
                return;
            }
            NotifyAllState();

            await foreach (var agentEvent in _agentHost.RunTurnAsync(
                               _conversation.Session,
                               new HostedAgentIntent(
                                   AgentIntentSource.User,
                                   runtimeRequest.Text,
                                   runtimeRequest.Inputs,
                                   runtimeRequest.OutputSchema,
                                   runtimeRequest.AccessMode),
                               cancellationToken))
            {
                if (bootPendingDelivery)
                {
                    await _sessionManager.MarkBootContextDeliveredAsync(_conversation, CancellationToken.None);
                    bootPendingDelivery = false;
                }

                switch (agentEvent)
                {
                    case AgentStatusChanged status:
                        _conversation.RuntimeStatus = status.Status == AgentSessionStatus.Running
                            ? "Working"
                            : status.Status.ToString();
                        NotifyAllState();
                        break;
                    case AgentTextDelta delta:
                        if (structuredOutputExpected)
                        {
                            structuredOutputBuffer.Append(delta.Text);
                            var buffered = structuredOutputBuffer.ToString();
                            var first = buffered.TrimStart();
                            if (first.Length > 0 &&
                                first[0] is not ('{' or '`'))
                            {
                                assistant ??= AddAssistantMessage();
                                assistant.Append(buffered);
                                structuredOutputBuffer.Clear();
                            }
                        }
                        else
                        {
                            assistant ??= AddAssistantMessage();
                            assistant.Append(delta.Text);
                        }
                        break;
                    case AgentToolEvent tool:
                        var currentActivity = _conversation.Activities.LastOrDefault(item => !item.IsComplete);
                        if (currentActivity is null || !string.Equals(currentActivity.Title, tool.ToolName, StringComparison.Ordinal))
                        {
                            currentActivity = new LeaderActivityViewModel(
                                string.IsNullOrWhiteSpace(tool.ToolName) ? "Agent activity" : tool.ToolName,
                                tool.Detail);
                            _conversation.Activities.Add(currentActivity);
                        }
                        else
                        {
                            currentActivity.Detail = tool.Detail;
                        }
                        currentActivity.IsComplete = tool.IsCompleted;
                        NotifyAllState();
                        break;
                    case AgentApprovalRequested approval:
                        _conversation.PendingApproval = approval;
                        _conversation.ApprovalError = null;
                        RebuildApprovalOptions();
                        NotifyAllState();
                        break;
                    case AgentQuestionRequested question:
                        _conversation.PendingQuestion = question;
                        NotifyAllState();
                        break;
                    case AgentError:
                        AddErrorMessage(LocalizationService.Current["Dynamic.LeaderRuntimeError"]);
                        break;
                    case AgentTurnCompleted completed:
                        turnCompleted = true;
                        var finalText = string.IsNullOrWhiteSpace(completed.Result.FinalText)
                            ? structuredOutputBuffer.Length > 0
                                ? structuredOutputBuffer.ToString()
                                : assistant?.Text ?? string.Empty
                            : completed.Result.FinalText!;
                        if (structuredOutputExpected &&
                            LeaderStructuredResponse.TryParse(finalText, _project.Id, out var structured))
                        {
                            structuredResponseParsed = true;
                            finalText = structured.Response;
                            summaryDeltas = suppressSummaryDeltas ? [] : structured.SummaryDeltas;
                            if (structured.MemoryCommandError is not null)
                            {
                                MemoryCommandStatus = structured.MemoryCommandError;
                            }
                            else if (structured.MemoryCommands?.LibraryProposal is not null)
                            {
                                await CreateLibraryProposalAsync(structured.MemoryCommands.LibraryProposal, cancellationToken);
                            }
                            if (_draftProposalBuilder is not null && structured.Proposal is not null)
                            {
                                var resources = await _runtimeRegistry.GetWorkerResourcesAsync(cancellationToken);
                                var candidate = SelectCandidate(resources, structured.Proposal.Recommendation);
                                if (candidate is null)
                                {
                                    AddErrorMessage(LocalizationService.Current["Dynamic.NoWorkerResource"]);
                                }
                                else
                                {
                                    var created = await _draftProposalBuilder.CreateDraftAsync(structured.Proposal, candidate.CreateExecutionProfile(), cancellationToken);
                                    if (created.Succeeded && _taskRevisions is not null)
                                    {
                                        var revision = (await _taskRevisions.ListAsync(_project.Id, created.TaskId!.Value, cancellationToken)).Single();
                                        WorkerResources.Clear();
                                        foreach (var resource in resources.Where(resource => resource.IsReady)) WorkerResources.Add(resource);
                                        DraftConfirmation = new LeaderDraftConfirmation(created.TaskId.Value, structured.Proposal.Title, structured.Proposal.Goal, structured.Proposal.Scope, structured.Proposal.Acceptance, structured.Proposal.RiskLevel, structured.Proposal.Recommendation, candidate, revision);
                                    }
                                    else if (!created.Succeeded)
                                    {
                                        AddErrorMessage(LocalizationService.Current["Dynamic.LeaderResponseInvalid"]);
                                    }
                                }
                            }
                        }
                        else if (structuredOutputExpected &&
                                 !string.IsNullOrWhiteSpace(finalText) &&
                                 (finalText.TrimStart().StartsWith('{') || finalText.TrimStart().StartsWith("```", StringComparison.Ordinal)))
                        {
                            finalText = string.Empty;
                            AddErrorMessage(LocalizationService.Current["Dynamic.LeaderResponseInvalid"]);
                        }
                        if (assistant is null && (structuredResponseParsed || !string.IsNullOrWhiteSpace(finalText)))
                        {
                            assistant = AddAssistantMessage();
                        }

                        if (assistant is not null &&
                            (structuredResponseParsed || !string.IsNullOrWhiteSpace(finalText)))
                        {
                            assistant.Text = finalText;
                        }

                        if (assistant is not null)
                        {
                            assistant.IsStreaming = false;
                        }

                        foreach (var activity in _conversation.Activities)
                        {
                            activity.IsComplete = true;
                        }

                        if (completed.Result.FinalStatus is AgentSessionStatus.Failed or
                            AgentSessionStatus.Interrupted or
                            AgentSessionStatus.Stopped)
                        {
                            AddErrorMessage(string.Format(LocalizationService.Current["Dynamic.LeaderTurnEnded"], completed.Result.FinalStatus));
                        }
                        else if (completed.Result.FinalStatus == AgentSessionStatus.Completed)
                        {
                            await RefreshChangedFilesAsync(cancellationToken);
                            var persistedAssistantText = structuredResponseParsed
                                ? finalText
                                : string.IsNullOrWhiteSpace(finalText)
                                    ? assistant?.Text ?? string.Empty
                                    : finalText;
                            var metadata = summaryDeltas.Count == 0
                                ? null
                                : new LeaderResultMetadata(resultId, LeaderSummaryPayload.Serialize(summaryDeltas));
                            var persistedAssistant = await _sessionManager.PersistCompletedAssistantAsync(
                                _conversation,
                                persistedAssistantText,
                                metadata,
                                cancellationToken);
                            if (metadata is not null)
                            {
                                if (persistedAssistant is null || _projectSummaryRepository is null)
                                {
                                    MemoryCommandStatus = LocalizationService.Current["Dynamic.PendingSummary"];
                                }
                                else
                                {
                                    try
                                    {
                                        await _projectSummaryRepository.AppendAsync(
                                            _project.Id,
                                            resultId,
                                            summaryDeltas,
                                            persistedAssistant.CreatedAt,
                                            cancellationToken);
                                        var marked = await _sessionManager.MarkSummaryPersistedAsync(
                                            persistedAssistant.Id,
                                            resultId,
                                            _timeProvider.GetUtcNow(),
                                            cancellationToken);
                                        if (!marked)
                                        {
                                            MemoryCommandStatus = LocalizationService.Current["Dynamic.PendingSummary"];
                                        }
                                    }
                                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                                    {
                                        throw;
                                    }
                                    catch
                                    {
                                        MemoryCommandStatus = LocalizationService.Current["Dynamic.PendingSummary"];
                                    }
                                }
                            }
                            _conversation.RotationMessage = null;
                        }

                        break;
                }
            }

            if (!turnCompleted)
            {
                AddErrorMessage(LocalizationService.Current["Dynamic.LeaderTurnIncomplete"]);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            AddErrorMessage(LocalizationService.Current["Dynamic.LeaderTurnCancelled"]);
        }
        catch (Exception exception)
        {
            if (IsActiveWriterConflict(exception)) MarkSessionConflict(exception);
            else AddErrorMessage(LocalizationService.Current["Dynamic.LeaderTurnFailed"]);
        }
        finally
        {
            if (assistant is not null)
            {
                assistant.IsStreaming = false;
            }

            foreach (var activity in _conversation.Activities)
            {
                activity.IsComplete = true;
            }

            ClearPendingApproval();
            _conversation.PendingQuestion = null;
            _conversation.IsBusy = false;
            // RuntimeStatus is a transient activity indicator; never leave the last turn's
            // "Working" value visible after the send lifecycle has ended.
            if (!retainRuntimeStatus && !(_conversation.SessionNeedsResume && !_conversation.RuntimeAccountAvailable))
            {
                _conversation.RuntimeStatus = null;
            }
            if (ReferenceEquals(_activeTurnCancellation, turnCancellation))
            {
                _activeTurnCancellation = null;
            }
            NotifyAllState();
        }
    }

    public async Task BeginBrainPickerAsync(CancellationToken cancellationToken = default)
    {
        if (!CanChangeBrain) return;
        try
        {
            await LoadAvailableModelsAsync(cancellationToken);
            BrainTargetModel = SelectedModel;
            _conversation.IsBrainPickerVisible = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            AddErrorMessage(LocalizationService.Current["Dynamic.BrainSwitchFailed"]);
        }
        finally
        {
            NotifyAllState();
        }
    }

    public void DismissBrainPicker()
    {
        _conversation.IsBrainPickerVisible = false;
        BrainTargetModel = SelectedModel;
        NotifyAllState();
    }

    public async Task SwitchBrainAsync(CancellationToken cancellationToken = default)
    {
        if (!CanChangeBrain || BrainTargetModel is null)
        {
            throw new InvalidOperationException(LocalizationService.Current["Dynamic.BrainSwitchUnavailable"]);
        }

        try
        {
            await ExecuteRolloverAsync("Manual", cancellationToken, BrainTargetModel.Profile);
            _conversation.IsBrainPickerVisible = false;
            _conversation.BrainTargetModel = SelectedModel;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            AddErrorMessage(LocalizationService.Current["Dynamic.BrainSwitchFailed"]);
        }
        finally
        {
            NotifyAllState();
        }
    }

    private async Task ExecuteRolloverAsync(
        string reason,
        CancellationToken cancellationToken,
        AvailableModelProfile? targetModel = null)
    {
        if (_rolloverService is null || _conversation.Session is null || _conversation.Epoch is null)
        {
            throw new InvalidOperationException("No current Leader session is available for rollover.");
        }

        if (_conversation.IsBusy || HasPendingApproval || _conversation.IsRolloverRunning)
        {
            throw new InvalidOperationException("The current Leader interaction must finish before rollover.");
        }

        _conversation.IsRolloverRunning = true;
        NotifyAllState();
        try
        {
            if (_conversation.SessionNeedsResume &&
                !_conversation.RuntimeAccountAvailable &&
                _reconnectRuntime is not null)
            {
                await _reconnectRuntime(cancellationToken);
            }

            LeaderMemoryPolicyDecision? policyDecision = null;
            if (_memoryPolicyCoordinator is not null)
            {
                var preparation = await _memoryPolicyCoordinator.PrepareForNewBrainAsync(
                    _project,
                    _conversation.Session,
                    _conversation.Epoch,
                    _conversation.SessionNeedsResume,
                    cancellationToken);
                policyDecision = preparation.Decision;
            }

            var result = await _rolloverService.RolloverAsync(
                _project,
                _conversation.Session,
                _conversation.Epoch,
                _conversation.SessionNeedsResume,
                reason,
                policyDecision,
                _memoryPolicyCoordinator is not null,
                targetModel,
                cancellationToken);
            _conversation.Session = result.NewSession;
            _conversation.Epoch = result.NewEpoch;
            _conversation.SelectedModel = targetModel is null
                ? _conversation.SelectedModel
                : AvailableModels.FirstOrDefault(option =>
                    option.Profile.AccountId == targetModel.AccountId &&
                    string.Equals(option.Profile.Model.ModelId, targetModel.Model.ModelId, StringComparison.Ordinal)) ??
                    _conversation.SelectedModel;
            _conversation.BrainTargetModel = _conversation.SelectedModel;
            _conversation.SessionNeedsResume = false;
            _conversation.RuntimeAccountAvailable = true;
            _conversation.RuntimeStatus = null;
            _conversation.RuntimeErrorDetail = null;
            _conversation.Messages.Clear();
            _conversation.RotationMessage = LocalizationService.Current["Dynamic.FreshSessionStarted"];
            if (History is not null)
            {
                await History.RefreshAfterRolloverAsync(cancellationToken);
            }
        }
        finally
        {
            _conversation.IsRolloverRunning = false;
            NotifyAllState();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_conversation.Session is null || !IsBusy)
        {
            return;
        }

        try
        {
            _activeTurnCancellation?.Cancel();
            await _agentHost.StopAsync(_conversation.Session, cancellationToken);
        }
        catch (Exception exception)
        {
            if (IsActiveWriterConflict(exception))
            {
                MarkSessionConflict(exception);
                _conversation.IsBusy = false;
                NotifyAllState();
            }
            else AddErrorMessage(LocalizationService.Current["Dynamic.StopFailed"]);
        }
    }

    private void MarkSessionConflict(Exception exception)
    {
        _conversation.SessionNeedsResume = true;
        _conversation.RuntimeAccountAvailable = false;
        _conversation.RuntimeStatus = LocalizationService.Current["Dynamic.LeaderSessionConflict"];
        _conversation.RuntimeErrorDetail = exception.Message;
        AddErrorMessage(LocalizationService.Current["Dynamic.LeaderSessionConflictDetail"]);
    }

    private static bool IsActiveWriterConflict(Exception exception) =>
        exception.Message.Contains("active writer", StringComparison.OrdinalIgnoreCase) ||
        exception.Message.Contains("thread/resume failed", StringComparison.OrdinalIgnoreCase);

    public async Task RespondToApprovalAsync(
        LeaderApprovalOptionViewModel option,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(option);
        var approval = _conversation.PendingApproval;
        if (approval is null || _conversation.IsApprovalResponding)
        {
            return;
        }

        if (!approval.Options.Any(candidate => candidate.Id == option.Option.Id))
        {
            throw new InvalidOperationException(LocalizationService.Current["Dynamic.InvalidApprovalOption"]);
        }

        _conversation.IsApprovalResponding = true;
        _conversation.ApprovalError = null;
        NotifyAllState();
        try
        {
            var decision = new AgentApprovalDecision(approval.RequestId, option.Option.Id);
            if (_conversation.Session?.Id == approval.SessionId)
            {
                await _agentHost.RespondToApprovalAsync(_conversation.Session, decision, cancellationToken);
            }
            else
            {
                await _agentHost.RespondToApprovalAsync(approval.SessionId, decision, cancellationToken);
            }
            if (_workerApprovalSessionId == approval.SessionId)
            {
                _workerApprovalSessionId = null;
                _conversation.RuntimeStatus = string.Empty;
            }
            _conversation.PendingApproval = null;
            ApprovalOptions.Clear();
        }
        catch (Exception)
        {
            _conversation.ApprovalError = LocalizationService.Current["Dynamic.ApprovalFailed"];
        }
        finally
        {
            _conversation.IsApprovalResponding = false;
            NotifyAllState();
        }
    }

    public void RecordContentInteraction()
    {
    }

    [RelayCommand]
    private Task Focus() => _focus();

    public async Task RetryRuntimeAsync(CancellationToken cancellationToken = default)
    {
        _conversation.ModelsLoaded = false;
        _conversation.RuntimeStatus = null;
        if (_reconnectRuntime is not null)
        {
            await _reconnectRuntime(cancellationToken);
        }

        await InitializeAsync(cancellationToken);
    }

    [RelayCommand]
    private Task RetryRuntime() => RetryRuntimeAsync();

    [RelayCommand]
    private Task Send() => SendAsync();

    [RelayCommand]
    private Task Stop() => StopAsync();

    [RelayCommand]
    private Task ContinuePrevious() => ContinuePreviousAsync();

    [RelayCommand]
    private Task StartFresh() => StartFreshAsync();

    [RelayCommand]
    private Task StartNewBrain() => StartNewBrainAsync();

    [RelayCommand]
    private Task OpenBrainPicker() => BeginBrainPickerAsync();

    [RelayCommand]
    private void CancelBrainPicker() => DismissBrainPicker();

    [RelayCommand]
    private Task SwitchBrain() => SwitchBrainAsync();

    private LeaderMessageViewModel AddAssistantMessage()
    {
        var message = new LeaderMessageViewModel(LeaderMessageRole.Assistant, string.Empty, true);
        Messages.Add(message);
        return message;
    }

    private void AddErrorMessage(string text) =>
        Messages.Add(new LeaderMessageViewModel(LeaderMessageRole.Error, text));

    private async Task CreateLibraryProposalAsync(
        LeaderLibraryProposalCommand command,
        CancellationToken cancellationToken)
    {
        if (_projectMemoryApi is null || _conversation.Session is null)
        {
            MemoryCommandStatus = LocalizationService.Current["Dynamic.LibraryProposalProcessingFailed"];
            return;
        }

        try
        {
            var draft = new ProjectLibraryProposalDraft(
                Guid.NewGuid(),
                _project.Id,
                _conversation.Session.Id.Value,
                command.Action,
                command.TargetObjectId,
                command.TargetNodeId,
                command.ExpectedNodeRevision,
                command.ExpectedOverviewRevision,
                command.Category,
                command.Topic,
                command.LocalDate,
                command.NodeContent,
                command.CurrentOverview,
                command.Materials,
                _timeProvider.GetUtcNow());
            await _projectMemoryApi.CreateLibraryProposalAsync(draft, cancellationToken);
            MemoryCommandStatus = LocalizationService.Current["Dynamic.LibraryProposalReady"];
            if (_refreshLibraryPane is not null) await _refreshLibraryPane(cancellationToken);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            MemoryCommandStatus = LocalizationService.Current["Dynamic.LibraryProposalProcessingFailed"];
        }
    }

    public async Task RespondToQuestionAsync(
        string requestId,
        IReadOnlyDictionary<string, string> answers,
        CancellationToken cancellationToken = default)
    {
        var question = _conversation.PendingQuestion;
        var session = _conversation.Session;
        if (question is null || session is null || !string.Equals(question.RequestId, requestId, StringComparison.Ordinal)) return;

        try
        {
            await _agentHost.RespondToQuestionAsync(session, requestId, answers, cancellationToken);
            _conversation.PendingQuestion = null;
            NotifyAllState();
        }
        catch (Exception)
        {
            AddErrorMessage(LocalizationService.Current["Dynamic.LeaderTurnFailed"]);
            NotifyAllState();
        }
    }

    public async Task SteerAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var session = _conversation.Session;
        if (session is null || !IsBusy)
        {
            return;
        }

        var runtime = _runtimeRegistry.GetByAccount(session.AccountId);
        if (!runtime.Capabilities.HasFlag(AgentCapability.Steer))
        {
            throw new NotSupportedException("The current Agent runtime does not support steering.");
        }

        Messages.Add(new LeaderMessageViewModel(LeaderMessageRole.User, text));
        await _sessionManager.PersistUserMessageAsync(_conversation, text, cancellationToken);
        NotifyAllState();
        try
        {
            await _agentHost.SteerAsync(
                session,
                new HostedAgentIntent(AgentIntentSource.User, text, AccessMode: AccessMode),
                cancellationToken);
        }
        catch (Exception)
        {
            AddErrorMessage(LocalizationService.Current["Dynamic.LeaderSteerFailed"]);
            NotifyAllState();
        }
    }

    private void ClearPendingApproval()
    {
        _conversation.PendingApproval = null;
        _conversation.ApprovalError = null;
        ApprovalOptions.Clear();
    }

    private void OnAgentHostEvent(object? sender, HostedAgentEvent item)
    {
        if (_conversation.Session?.Id == item.SessionId)
            return;

        switch (item.Event)
        {
            case AgentApprovalRequested approval:
                DispatchToLeader(() =>
                {
                    _conversation.PendingApproval = approval;
                    _conversation.ApprovalError = null;
                    _workerApprovalSessionId = approval.SessionId;
                    RebuildApprovalOptions();
                    _conversation.RuntimeStatus = "Worker 等待 Leader 审批";
                    NotifyAllState();
                });
                break;
            case AgentTurnCompleted when _workerApprovalSessionId == item.SessionId:
                DispatchToLeader(() =>
                {
                    if (_conversation.PendingApproval?.SessionId == item.SessionId)
                        ClearPendingApproval();
                    _workerApprovalSessionId = null;
                    _conversation.RuntimeStatus = string.Empty;
                    NotifyAllState();
                });
                break;
        }
    }

    private void DispatchToLeader(Action action)
    {
        if (_leaderContext is null || ReferenceEquals(SynchronizationContext.Current, _leaderContext))
        {
            action();
            return;
        }

        _leaderContext.Post(static state => ((Action)state!).Invoke(), action);
    }

    public ValueTask DisposeAsync()
    {
        _agentHost.EventReceived -= OnAgentHostEvent;
        return ValueTask.CompletedTask;
    }

    private void SetRuntimeUnavailable(string fallbackDetail)
    {
        _conversation.ModelsLoaded = false;
        _conversation.RuntimeAccountAvailable = false;
        _conversation.RuntimeStatus = LocalizationService.Current["Dynamic.LeaderUnavailable"];
        _conversation.RuntimeErrorDetail ??= fallbackDetail;
    }

    private void SetCurrentSessionUnavailable()
    {
        _conversation.ModelsLoaded = true;
        _conversation.RuntimeAccountAvailable = false;
        _conversation.RuntimeStatus = LocalizationService.Current["Dynamic.CurrentLeaderUnavailable"];
        _conversation.RuntimeErrorDetail = null;
    }

    private void RebuildApprovalOptions()
    {
        ApprovalOptions.Clear();
        if (_conversation.PendingApproval is null)
        {
            return;
        }

        foreach (var option in _conversation.PendingApproval.Options)
        {
            ApprovalOptions.Add(new LeaderApprovalOptionViewModel(
                option,
                selected => RespondToApprovalAsync(selected)));
        }
    }

    private void NotifyAllState()
    {
        OnPropertyChanged(nameof(SelectedModel));
        OnPropertyChanged(nameof(Session));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsRuntimeAvailable));
        OnPropertyChanged(nameof(CanRetryRuntime));
        OnPropertyChanged(nameof(IsNativeAgentSurfaceEnabled));
        OnPropertyChanged(nameof(IsLegacyTranscriptVisible));
        OnPropertyChanged(nameof(ShowRuntimeUnavailableOverlay));
        OnPropertyChanged(nameof(HasRuntimeStatus));
        OnPropertyChanged(nameof(RuntimeStatus));
        OnPropertyChanged(nameof(RuntimeErrorDetail));
        OnPropertyChanged(nameof(IsModelSelectionLocked));
        OnPropertyChanged(nameof(IsModelSelectionEnabled));
        OnPropertyChanged(nameof(AccessMode));
        OnPropertyChanged(nameof(AccessModeLabel));
        OnPropertyChanged(nameof(AccessModeDescription));
        OnPropertyChanged(nameof(CanChangeAccessMode));
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanSteer));
        OnPropertyChanged(nameof(SupportsSteer));
        OnPropertyChanged(nameof(PendingApproval));
        OnPropertyChanged(nameof(HasPendingApproval));
        OnPropertyChanged(nameof(PendingQuestion));
        OnPropertyChanged(nameof(HasPendingQuestion));
        OnPropertyChanged(nameof(ShowNativeApproval));
        OnPropertyChanged(nameof(HasPendingRotationDecision));
        OnPropertyChanged(nameof(CanStartNewBrain));
        OnPropertyChanged(nameof(IsBrainPickerVisible));
        OnPropertyChanged(nameof(CanChangeBrain));
        OnPropertyChanged(nameof(BrainTargetModel));
        OnPropertyChanged(nameof(ApprovalError));
        OnPropertyChanged(nameof(RotationMessage));
        OnPropertyChanged(nameof(HasRotationMessage));
        OnPropertyChanged(nameof(ShowRotationMessage));
        OnPropertyChanged(nameof(ProjectLeaderId));
        OnPropertyChanged(nameof(SessionEpochId));
        OnPropertyChanged(nameof(HasHistory));
        OnPropertyChanged(nameof(HasDraftConfirmation));
        OnPropertyChanged(nameof(CurrentWorkerProfile));
        OnPropertyChanged(nameof(HasMemoryCommandStatus));
        OnPropertyChanged(nameof(Activities));
        OnPropertyChanged(nameof(ChangedFiles));
        NotifyCommandState();
    }

    private async Task RefreshChangedFilesAsync(CancellationToken cancellationToken)
    {
        if (_git?.IsRepository != true || string.IsNullOrWhiteSpace(_git.RepositoryRoot)) return;
        try
        {
            var files = await new GitDiffReader().ReadAsync(_git.RepositoryRoot, cancellationToken);
            ChangedFiles.Clear();
            foreach (var file in files)
            {
                var projectPath = GetProjectRelativePath(_git.RepositoryRoot, file.Path);
                if (projectPath is not null)
                {
                    ChangedFiles.Add(new LeaderChangedFileViewModel(projectPath, file.Added, file.Removed, file.Diff));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Diff presentation is advisory and must not fail the completed turn.
        }
    }

    private string? GetProjectRelativePath(string repositoryRoot, string gitPath)
    {
        try
        {
            var projectRoot = Path.GetFullPath(_project.RootPath);
            var absoluteFilePath = Path.GetFullPath(gitPath, Path.GetFullPath(repositoryRoot));
            var relativePath = Path.GetRelativePath(projectRoot, absoluteFilePath);
            if (Path.IsPathFullyQualified(relativePath) ||
                relativePath.Equals("..", StringComparison.Ordinal) ||
                relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
            {
                return null;
            }

            return relativePath;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    public async Task ChangeWorkerResourceAsync(WorkerResource resource, CancellationToken cancellationToken = default)
    {
        var confirmation = DraftConfirmation ?? throw new InvalidOperationException("No draft confirmation is active.");
        if (_taskRevisions is null || !resource.IsReady) return;
        var successor = new TaskRevision(confirmation.TaskId, confirmation.Revision.RevisionNumber + 1, confirmation.Revision.Goal,
            confirmation.Revision.Scope, confirmation.Revision.OutOfScope, confirmation.Revision.Acceptance, confirmation.Revision.RiskLevel,
            resource.CreateExecutionProfile(), "Worker profile changed", TaskRevisionApprover.User, DateTimeOffset.UtcNow, confirmation.Revision.Id);
        if (await _taskRevisions.CreateSuccessorAsync(_project.Id, confirmation.TaskId, confirmation.Revision.Id, successor, cancellationToken))
        {
            DraftConfirmation = confirmation with { Resource = resource, Revision = successor };
        }
    }

    public async Task ConfirmDraftAsync(CancellationToken cancellationToken = default)
    {
        var confirmation = DraftConfirmation ?? throw new InvalidOperationException("No draft confirmation is active.");
        if (_workerSessionRouter is null)
        {
            AddErrorMessage("Worker 路由服务不可用，任务草案未启动。");
            NotifyAllState();
            return;
        }
        var profile = confirmation.Revision.RecommendedExecutionProfile;
        var request = new WorkerStartRequest(_project, confirmation.TaskId, confirmation.Revision.Id, confirmation.Title,
            profile, confirmation.Goal, null, "Worker",
            (handoff, token) => _sessionManager.IngestWorkerHandoffAsync(handoff, token),
            WaitForCompletion: false);
        if (_git is { IsRepository: true, HeadCommit: not null, BranchName: not null } git)
        {
            var executionId = Guid.NewGuid();
            var identity = WorkerExecutionIdentity.Start(confirmation.Revision.CreateReference(), git.HeadCommit, git.BranchName,
                ProviderAccountBinding.Create(profile.ProviderId, profile.ProviderAccountId), profile, git.BranchName, _project.RootPath);
            request = request with { ExecutionId = executionId, ExecutionIdentity = identity };
        }
        WorkerStartResult result;
        try
        {
            result = await _workerSessionRouter.StartAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            AddErrorMessage($"无法启动 Worker：{exception.Message}");
            NotifyAllState();
            return;
        }
        if (!result.Succeeded)
        {
            AddErrorMessage(result.Error is { Length: > 0 } error
                ? $"无法启动 Worker：{error}"
                : LocalizationService.Current["Dynamic.WorkerStartFailed"]);
            NotifyAllState();
            return;
        }

        DraftConfirmation = null;
        NotifyAllState();
        if (_refreshWorkPane is not null)
            await _refreshWorkPane(cancellationToken);
    }

    /// <summary>
    /// Sends a follow-up direction from the Leader to an existing Workbench
    /// Worker session. The Worker Router decides whether this is an in-turn
    /// steer or a serialized continuation; the Leader never opens a second
    /// runtime/CLI writer for the session.
    /// </summary>
    public async Task SendWorkerIntentAsync(
        Guid taskId,
        AgentSessionId workerSessionId,
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (_workerSessionRouter is null)
            throw new InvalidOperationException("The Worker routing service is not available.");

        var result = await _workerSessionRouter.SendIntentAsync(
            _project,
            taskId,
            workerSessionId,
            text,
            AgentIntentSource.Leader,
            cancellationToken);
        if (!result.Succeeded)
        {
            AddErrorMessage(result.Error ?? LocalizationService.Current["Dynamic.WorkerStartFailed"]);
            return;
        }

        if (_refreshWorkPane is not null)
            await _refreshWorkPane(cancellationToken);
    }

    [RelayCommand]
    private Task ConfirmDraft() => ConfirmDraftAsync();

    [RelayCommand]
    private Task ChangeWorkerResource(WorkerResource resource) => ChangeWorkerResourceAsync(resource);

    private static WorkerResource? SelectCandidate(IReadOnlyList<WorkerResource> resources, LeaderExecutionRecommendation recommendation) =>
        resources.FirstOrDefault(resource => resource.IsReady &&
            (string.IsNullOrWhiteSpace(recommendation.ProviderHint) || string.Equals(resource.ProviderId, recommendation.ProviderHint, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(recommendation.ModelHint) || string.Equals(resource.ModelProfileId, recommendation.ModelHint, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(recommendation.RuntimeHint) || string.Equals(resource.AgentRuntimeId.Split(':')[0], recommendation.RuntimeHint, StringComparison.OrdinalIgnoreCase)))
        ?? resources.FirstOrDefault(resource => resource.IsReady);

    private void NotifyCommandState()
    {
        SendCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        RetryRuntimeCommand.NotifyCanExecuteChanged();
        ContinuePreviousCommand.NotifyCanExecuteChanged();
        StartFreshCommand.NotifyCanExecuteChanged();
        StartNewBrainCommand.NotifyCanExecuteChanged();
    }
}
