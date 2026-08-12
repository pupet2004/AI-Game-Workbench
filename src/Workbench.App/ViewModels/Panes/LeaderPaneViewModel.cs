using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.App.ViewModels.Leader;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Registry;
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

    public LeaderPaneViewModel(
        CoreProject project,
        AgentRuntimeRegistry runtimeRegistry,
        ProjectLeaderSessionManager sessionManager,
        Func<Task> focus,
        string? runtimeUnavailableDetail = null,
        Func<CancellationToken, Task>? reconnectRuntime = null)
    {
        _project = project;
        _runtimeRegistry = runtimeRegistry;
        _sessionManager = sessionManager;
        _conversation = sessionManager.GetOrCreate(project.Id);
        _focus = focus;
        _reconnectRuntime = reconnectRuntime;
        if (_conversation.RuntimeErrorDetail is null && runtimeUnavailableDetail is not null)
        {
            _conversation.RuntimeErrorDetail = runtimeUnavailableDetail;
        }

        RebuildApprovalOptions();
    }

    public ObservableCollection<LeaderModelOptionViewModel> AvailableModels => _conversation.AvailableModels;

    public ObservableCollection<LeaderMessageViewModel> Messages => _conversation.Messages;

    public ObservableCollection<LeaderApprovalOptionViewModel> ApprovalOptions { get; } = [];

    public AgentSession? Session => _conversation.Session;

    public Guid ProjectLeaderId => _project.Id;

    public Guid? SessionEpochId => _conversation.Epoch?.Id;

    public bool IsBusy => _conversation.IsBusy;

    public bool IsRuntimeAvailable => _conversation.RuntimeAccountAvailable;

    public bool CanRetryRuntime => !IsBusy && !IsRuntimeAvailable;

    public bool ShowRuntimeUnavailableOverlay => CanRetryRuntime && Messages.Count == 0;

    public bool HasRuntimeStatus => !string.IsNullOrWhiteSpace(RuntimeStatus);

    public string? RuntimeStatus => _conversation.RuntimeStatus;

    public string? RuntimeErrorDetail => _conversation.RuntimeErrorDetail;

    public bool IsModelSelectionLocked => Session is not null;

    public bool IsModelSelectionEnabled => IsRuntimeAvailable && !IsModelSelectionLocked && !IsBusy;

    public bool CanSend =>
        !IsBusy &&
        !HasPendingApproval &&
        (IsRuntimeAvailable ||
         (_conversation.Session is not null && _conversation.SessionNeedsResume && _reconnectRuntime is not null)) &&
        SelectedModel is not null &&
        !string.IsNullOrWhiteSpace(DraftMessage);

    public bool CanStop => IsBusy && Session is not null;

    public AgentApprovalRequested? PendingApproval => _conversation.PendingApproval;

    public bool HasPendingApproval => PendingApproval is not null;

    public string? ApprovalError => _conversation.ApprovalError;

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
        if (_conversation.Session is not null)
        {
            try
            {
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
            if (AvailableModels.Count == 1)
            {
                _conversation.SelectedModel = AvailableModels[0];
            }

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

    public async Task SendAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            throw new InvalidOperationException("The Project Leader already has an active turn.");
        }

        var selectedModel = SelectedModel
            ?? throw new InvalidOperationException("Select a model before sending a message.");
        var text = DraftMessage;
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        _conversation.IsBusy = true;
        _conversation.ApprovalError = null;
        DraftMessage = string.Empty;
        Messages.Add(new LeaderMessageViewModel(LeaderMessageRole.User, text));
        NotifyAllState();

        LeaderMessageViewModel? assistant = null;
        var turnCompleted = false;
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
                var createdSession = await runtime.CreateSessionAsync(
                    new CreateAgentSessionRequest(
                        selectedModel.Profile.AccountId,
                        selectedModel.Profile.Model.ModelId,
                        _project.RootPath),
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
                _conversation.Session = await runtime.ResumeSessionAsync(
                    _conversation.Session,
                    cancellationToken);
                _conversation.SessionNeedsResume = false;
                _conversation.RuntimeAccountAvailable = true;
                _conversation.RuntimeStatus = null;
                _conversation.RuntimeErrorDetail = null;
            }

            await _sessionManager.PersistUserMessageAsync(_conversation, text, cancellationToken);
            NotifyAllState();

            await foreach (var agentEvent in runtime.SendAsync(
                               _conversation.Session,
                               new AgentRequest(text),
                               cancellationToken))
            {
                switch (agentEvent)
                {
                    case AgentTextDelta delta:
                        assistant ??= AddAssistantMessage();
                        assistant.Append(delta.Text);
                        break;
                    case AgentApprovalRequested approval:
                        _conversation.PendingApproval = approval;
                        _conversation.ApprovalError = null;
                        RebuildApprovalOptions();
                        NotifyAllState();
                        break;
                    case AgentError:
                        AddErrorMessage("Leader runtime reported an error.");
                        break;
                    case AgentTurnCompleted completed:
                        turnCompleted = true;
                        var finalText = completed.Result.FinalText;
                        if (assistant is null && !string.IsNullOrWhiteSpace(finalText))
                        {
                            assistant = AddAssistantMessage();
                        }

                        if (assistant is not null && !string.IsNullOrWhiteSpace(finalText))
                        {
                            assistant.Text = finalText;
                        }

                        if (assistant is not null)
                        {
                            assistant.IsStreaming = false;
                        }

                        if (completed.Result.FinalStatus is AgentSessionStatus.Failed or
                            AgentSessionStatus.Interrupted or
                            AgentSessionStatus.Stopped)
                        {
                            AddErrorMessage($"Leader turn ended: {completed.Result.FinalStatus}.");
                        }
                        else if (completed.Result.FinalStatus == AgentSessionStatus.Completed)
                        {
                            var persistedAssistantText = string.IsNullOrWhiteSpace(finalText)
                                ? assistant?.Text ?? string.Empty
                                : finalText;
                            await _sessionManager.PersistCompletedAssistantAsync(
                                _conversation,
                                persistedAssistantText,
                                cancellationToken);
                        }

                        break;
                }
            }

            if (!turnCompleted)
            {
                AddErrorMessage("Leader turn ended without completion.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            AddErrorMessage("Leader turn was cancelled.");
        }
        catch (Exception)
        {
            AddErrorMessage("Leader request failed. You can try again.");
        }
        finally
        {
            if (assistant is not null)
            {
                assistant.IsStreaming = false;
            }

            ClearPendingApproval();
            _conversation.IsBusy = false;
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
            var runtime = _runtimeRegistry.GetByAccount(_conversation.Session.AccountId);
            await runtime.StopAsync(_conversation.Session, cancellationToken);
        }
        catch (Exception)
        {
            AddErrorMessage("Could not stop the Leader turn.");
        }
    }

    public async Task RespondToApprovalAsync(
        LeaderApprovalOptionViewModel option,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(option);
        var approval = _conversation.PendingApproval;
        var session = _conversation.Session;
        if (approval is null || session is null || _conversation.IsApprovalResponding)
        {
            return;
        }

        if (!approval.Options.Any(candidate => candidate.Id == option.Option.Id))
        {
            throw new InvalidOperationException("The approval option is not valid for the pending request.");
        }

        _conversation.IsApprovalResponding = true;
        _conversation.ApprovalError = null;
        NotifyAllState();
        try
        {
            var runtime = _runtimeRegistry.GetByAccount(session.AccountId);
            await runtime.RespondToApprovalAsync(
                session,
                new AgentApprovalDecision(approval.RequestId, option.Option.Id),
                cancellationToken);
            _conversation.PendingApproval = null;
            ApprovalOptions.Clear();
        }
        catch (Exception)
        {
            _conversation.ApprovalError = "Could not send the approval response.";
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

    private LeaderMessageViewModel AddAssistantMessage()
    {
        var message = new LeaderMessageViewModel(LeaderMessageRole.Assistant, string.Empty, true);
        Messages.Add(message);
        return message;
    }

    private void AddErrorMessage(string text) =>
        Messages.Add(new LeaderMessageViewModel(LeaderMessageRole.Error, text));

    private void ClearPendingApproval()
    {
        _conversation.PendingApproval = null;
        _conversation.ApprovalError = null;
        ApprovalOptions.Clear();
    }

    private void SetRuntimeUnavailable(string fallbackDetail)
    {
        _conversation.ModelsLoaded = false;
        _conversation.RuntimeAccountAvailable = false;
        _conversation.RuntimeStatus = "Leader unavailable";
        _conversation.RuntimeErrorDetail ??= fallbackDetail;
    }

    private void SetCurrentSessionUnavailable()
    {
        _conversation.ModelsLoaded = true;
        _conversation.RuntimeAccountAvailable = false;
        _conversation.RuntimeStatus = "Current Leader session is unavailable.";
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
        OnPropertyChanged(nameof(ShowRuntimeUnavailableOverlay));
        OnPropertyChanged(nameof(HasRuntimeStatus));
        OnPropertyChanged(nameof(RuntimeStatus));
        OnPropertyChanged(nameof(RuntimeErrorDetail));
        OnPropertyChanged(nameof(IsModelSelectionLocked));
        OnPropertyChanged(nameof(IsModelSelectionEnabled));
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(PendingApproval));
        OnPropertyChanged(nameof(HasPendingApproval));
        OnPropertyChanged(nameof(ApprovalError));
        OnPropertyChanged(nameof(ProjectLeaderId));
        OnPropertyChanged(nameof(SessionEpochId));
        NotifyCommandState();
    }

    private void NotifyCommandState()
    {
        SendCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        RetryRuntimeCommand.NotifyCanExecuteChanged();
    }
}
