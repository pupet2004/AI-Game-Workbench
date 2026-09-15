using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.App.Services;
using Workbench.Core.Leaders;
using Workbench.Storage.Settings;

namespace Workbench.App.ViewModels;

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly WorkbenchSettingsRepository _settings;
    private readonly LocalizationService _localization;
    private readonly Func<Task> _openDiagnostics;
    private readonly ProjectSettingsRepository? _projectSettings;
    private readonly Guid? _projectId;
    private readonly Func<CancellationToken, Task>? _retryRuntime;

    public SettingsViewModel(
        WorkbenchSettingsRepository settings,
        Func<Task> backToProjects,
        LocalizationService? localization = null,
        Func<Task>? openDiagnostics = null,
        ProjectSettingsRepository? projectSettings = null,
        Guid? projectId = null,
        Func<CancellationToken, Task>? retryRuntime = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        BackToProjects = backToProjects ?? throw new ArgumentNullException(nameof(backToProjects));
        _localization = localization ?? new LocalizationService(_settings);
        _openDiagnostics = openDiagnostics ?? (() => Task.CompletedTask);
        _projectSettings = projectSettings;
        _projectId = projectId;
        _retryRuntime = retryRuntime;
        _localization.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is "Item[]" or nameof(LocalizationService.Language))
            {
                OnPropertyChanged("Item[]");
                OnPropertyChanged(nameof(LeaderSessionRotationPolicyText));
                OnPropertyChanged(nameof(LeaderAuthorityModeText));
                OnPropertyChanged(nameof(ProjectRotationPolicyOverrideText));
                OnPropertyChanged(nameof(ProjectAuthorityModeOverrideText));
            }
        };
    }

    public Func<Task> BackToProjects { get; }
    public bool HasProjectSettings => _projectSettings is not null && _projectId.HasValue;

    public new string this[string key] => _localization[key];

    public IReadOnlyList<LanguageOption> LanguageOptions { get; } =
    [
        new(WorkbenchLanguage.English, "English"),
        new(WorkbenchLanguage.SimplifiedChinese, "简体中文")
    ];

    [ObservableProperty]
    public partial LeaderSessionRotationPolicy LeaderSessionRotationPolicy { get; set; } = LeaderSessionRotationPolicy.Auto;

    public string LeaderSessionRotationPolicyText => LeaderSessionRotationPolicy switch
    {
        LeaderSessionRotationPolicy.Auto => _localization["Settings.Automatic"],
        LeaderSessionRotationPolicy.Ask => _localization["Settings.AskFirst"],
        LeaderSessionRotationPolicy.ManualOnly => _localization["Settings.ManualOnly"],
        _ => LeaderSessionRotationPolicy.ToString()
    };

    [ObservableProperty]
    public partial LeaderAuthorityMode LeaderAuthorityMode { get; set; } = LeaderAuthorityMode.Balanced;

    public string LeaderAuthorityModeText => LeaderAuthorityMode switch
    {
        LeaderAuthorityMode.Cautious => _localization["Settings.Cautious"],
        LeaderAuthorityMode.Balanced => _localization["Settings.Balanced"],
        LeaderAuthorityMode.Autonomous => _localization["Settings.Autonomous"],
        _ => LeaderAuthorityMode.ToString()
    };

    [ObservableProperty]
    public partial LeaderSessionRotationPolicy? ProjectRotationPolicyOverride { get; set; }

    public string ProjectRotationPolicyOverrideText => ProjectRotationPolicyOverride switch
    {
        null => _localization["Settings.ProjectInherit"],
        LeaderSessionRotationPolicy.Auto => _localization["Settings.ProjectAutomatic"],
        LeaderSessionRotationPolicy.Ask => _localization["Settings.ProjectAskFirst"],
        LeaderSessionRotationPolicy.ManualOnly => _localization["Settings.ProjectManualOnly"],
        _ => ProjectRotationPolicyOverride.Value.ToString()
    };

    [ObservableProperty]
    public partial LeaderAuthorityMode? ProjectAuthorityModeOverride { get; set; }

    public string ProjectAuthorityModeOverrideText => ProjectAuthorityModeOverride switch
    {
        null => _localization["Settings.ProjectInherit"],
        LeaderAuthorityMode.Cautious => _localization["Settings.ProjectCautious"],
        LeaderAuthorityMode.Balanced => _localization["Settings.ProjectBalanced"],
        LeaderAuthorityMode.Autonomous => _localization["Settings.ProjectAutonomous"],
        _ => ProjectAuthorityModeOverride.Value.ToString()
    };

    [ObservableProperty]
    public partial bool IsCodexEnabled { get; set; }

    [ObservableProperty]
    public partial string CodexExecutablePath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsOpenCodeEnabled { get; set; }

    [ObservableProperty]
    public partial string OpenCodeExecutablePath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string GodotExecutablePath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? AgentSettingsStatus { get; set; }

    [ObservableProperty]
    public partial bool IsSigningIn { get; set; }

    [ObservableProperty]
    public partial LanguageOption? SelectedLanguage { get; set; }

    partial void OnLeaderSessionRotationPolicyChanged(LeaderSessionRotationPolicy value) =>
        OnPropertyChanged(nameof(LeaderSessionRotationPolicyText));

    partial void OnLeaderAuthorityModeChanged(LeaderAuthorityMode value) =>
        OnPropertyChanged(nameof(LeaderAuthorityModeText));

    partial void OnProjectRotationPolicyOverrideChanged(LeaderSessionRotationPolicy? value) =>
        OnPropertyChanged(nameof(ProjectRotationPolicyOverrideText));

    partial void OnProjectAuthorityModeOverrideChanged(LeaderAuthorityMode? value) =>
        OnPropertyChanged(nameof(ProjectAuthorityModeOverrideText));

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _localization.InitializeAsync(cancellationToken);
        LeaderSessionRotationPolicy = await _settings.GetLeaderSessionRotationPolicyAsync(cancellationToken);
        LeaderAuthorityMode = await _settings.GetLeaderAuthorityModeAsync(cancellationToken);
        if (HasProjectSettings)
        {
            ProjectRotationPolicyOverride = await _projectSettings!.GetLeaderSessionRotationPolicyOverrideAsync(_projectId!.Value, cancellationToken);
            ProjectAuthorityModeOverride = await _projectSettings.GetLeaderAuthorityModeOverrideAsync(_projectId.Value, cancellationToken);
        }
        var codex = await _settings.GetAgentRuntimeSettingsAsync("codex", cancellationToken: cancellationToken);
        var openCode = await _settings.GetAgentRuntimeSettingsAsync("opencode", cancellationToken: cancellationToken);
        IsCodexEnabled = codex.IsEnabled;
        CodexExecutablePath = codex.ExecutablePath ?? string.Empty;
        IsOpenCodeEnabled = openCode.IsEnabled;
        OpenCodeExecutablePath = openCode.ExecutablePath ?? string.Empty;
        GodotExecutablePath = await _settings.GetGodotExecutablePathAsync(cancellationToken) ?? string.Empty;
        SelectedLanguage = LanguageOptions.Single(option => option.Language == _localization.Language);
    }

    public async Task SetLeaderSessionRotationPolicyAsync(
        LeaderSessionRotationPolicy policy,
        CancellationToken cancellationToken = default)
    {
        await _settings.SaveLeaderSessionRotationPolicyAsync(policy, cancellationToken);
        LeaderSessionRotationPolicy = policy;
    }

    [RelayCommand]
    private Task UseAutomaticRotation() => SetLeaderSessionRotationPolicyAsync(LeaderSessionRotationPolicy.Auto);

    [RelayCommand]
    private Task AskBeforeRotation() => SetLeaderSessionRotationPolicyAsync(LeaderSessionRotationPolicy.Ask);

    [RelayCommand]
    private Task UseManualRotation() => SetLeaderSessionRotationPolicyAsync(LeaderSessionRotationPolicy.ManualOnly);

    public async Task SetLeaderAuthorityModeAsync(
        LeaderAuthorityMode mode,
        CancellationToken cancellationToken = default)
    {
        await _settings.SaveLeaderAuthorityModeAsync(mode, cancellationToken);
        LeaderAuthorityMode = mode;
    }

    [RelayCommand]
    private Task UseCautiousAuthority() => SetLeaderAuthorityModeAsync(LeaderAuthorityMode.Cautious);

    [RelayCommand]
    private Task UseBalancedAuthority() => SetLeaderAuthorityModeAsync(LeaderAuthorityMode.Balanced);

    [RelayCommand]
    private Task UseAutonomousAuthority() => SetLeaderAuthorityModeAsync(LeaderAuthorityMode.Autonomous);

    public async Task SetProjectRotationPolicyOverrideAsync(
        LeaderSessionRotationPolicy? policy,
        CancellationToken cancellationToken = default)
    {
        if (!HasProjectSettings)
        {
            return;
        }

        await _projectSettings!.SaveLeaderSessionRotationPolicyOverrideAsync(_projectId!.Value, policy, cancellationToken);
        ProjectRotationPolicyOverride = policy;
    }

    public async Task SetProjectAuthorityModeOverrideAsync(
        LeaderAuthorityMode? mode,
        CancellationToken cancellationToken = default)
    {
        if (!HasProjectSettings)
        {
            return;
        }

        await _projectSettings!.SaveLeaderAuthorityModeOverrideAsync(_projectId!.Value, mode, cancellationToken);
        ProjectAuthorityModeOverride = mode;
    }

    [RelayCommand]
    private Task InheritGlobalRotation() => SetProjectRotationPolicyOverrideAsync(null);

    [RelayCommand]
    private Task UseProjectAutomaticRotation() => SetProjectRotationPolicyOverrideAsync(LeaderSessionRotationPolicy.Auto);

    [RelayCommand]
    private Task UseProjectAskBeforeRotation() => SetProjectRotationPolicyOverrideAsync(LeaderSessionRotationPolicy.Ask);

    [RelayCommand]
    private Task UseProjectManualRotation() => SetProjectRotationPolicyOverrideAsync(LeaderSessionRotationPolicy.ManualOnly);

    [RelayCommand]
    private Task InheritGlobalAuthority() => SetProjectAuthorityModeOverrideAsync(null);

    [RelayCommand]
    private Task UseProjectCautiousAuthority() => SetProjectAuthorityModeOverrideAsync(LeaderAuthorityMode.Cautious);

    [RelayCommand]
    private Task UseProjectBalancedAuthority() => SetProjectAuthorityModeOverrideAsync(LeaderAuthorityMode.Balanced);

    [RelayCommand]
    private Task UseProjectAutonomousAuthority() => SetProjectAuthorityModeOverrideAsync(LeaderAuthorityMode.Autonomous);

    [RelayCommand]
    private async Task SaveAgentSettings()
    {
        await _settings.SaveAgentRuntimeSettingsAsync(new("codex", IsCodexEnabled, CodexExecutablePath));
        await _settings.SaveAgentRuntimeSettingsAsync(new("opencode", IsOpenCodeEnabled, OpenCodeExecutablePath));
        await _settings.SaveGodotExecutablePathAsync(GodotExecutablePath);
        AgentSettingsStatus = _localization["Settings.AgentSaved"];
    }

    [RelayCommand]
    private async Task SignInOpenCode()
    {
        if (IsSigningIn)
            return;

        IsSigningIn = true;
        AgentSettingsStatus = _localization["Settings.AgentSignInStarted"];
        try
        {
            var settings = new AgentRuntimeSettings("opencode", true, OpenCodeExecutablePath);
            var result = await new AgentAuthenticationService().SignInAsync(settings);
            AgentSettingsStatus = result.Succeeded
                ? _localization["Settings.AgentSignInChecking"]
                : result.Message;
            if (result.Succeeded && _retryRuntime is not null)
                await _retryRuntime(CancellationToken.None);
        }
        catch (Exception)
        {
            AgentSettingsStatus = _localization["Settings.AgentSignInFailed"];
        }
        finally
        {
            IsSigningIn = false;
        }
    }

    [RelayCommand]
    private async Task ApplyLanguage()
    {
        var selection = SelectedLanguage ?? LanguageOptions[0];
        await _localization.SetLanguageAsync(selection.Language);
    }

    [RelayCommand]
    private Task Back() => BackToProjects();

    [RelayCommand]
    private Task OpenDiagnostics() => _openDiagnostics();

    public sealed record LanguageOption(WorkbenchLanguage Language, string DisplayName);
}
