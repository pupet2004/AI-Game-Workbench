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

    public SettingsViewModel(
        WorkbenchSettingsRepository settings,
        Func<Task> backToProjects,
        LocalizationService? localization = null,
        Func<Task>? openDiagnostics = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        BackToProjects = backToProjects ?? throw new ArgumentNullException(nameof(backToProjects));
        _localization = localization ?? new LocalizationService(_settings);
        _openDiagnostics = openDiagnostics ?? (() => Task.CompletedTask);
        _localization.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is "Item[]" or nameof(LocalizationService.Language))
            {
                OnPropertyChanged("Item[]");
            }
        };
    }

    public Func<Task> BackToProjects { get; }

    public new string this[string key] => _localization[key];

    public IReadOnlyList<LanguageOption> LanguageOptions { get; } =
    [
        new(WorkbenchLanguage.English, "English"),
        new(WorkbenchLanguage.SimplifiedChinese, "简体中文")
    ];

    [ObservableProperty]
    public partial LeaderSessionRotationPolicy LeaderSessionRotationPolicy { get; set; } = LeaderSessionRotationPolicy.Auto;

    [ObservableProperty]
    public partial LeaderAuthorityMode LeaderAuthorityMode { get; set; } = LeaderAuthorityMode.Balanced;

    [ObservableProperty]
    public partial bool IsCodexEnabled { get; set; }

    [ObservableProperty]
    public partial string CodexExecutablePath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsOpenCodeEnabled { get; set; }

    [ObservableProperty]
    public partial string OpenCodeExecutablePath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? AgentSettingsStatus { get; set; }

    [ObservableProperty]
    public partial LanguageOption? SelectedLanguage { get; set; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _localization.InitializeAsync(cancellationToken);
        LeaderSessionRotationPolicy = await _settings.GetLeaderSessionRotationPolicyAsync(cancellationToken);
        LeaderAuthorityMode = await _settings.GetLeaderAuthorityModeAsync(cancellationToken);
        var codex = await _settings.GetAgentRuntimeSettingsAsync("codex", cancellationToken: cancellationToken);
        var openCode = await _settings.GetAgentRuntimeSettingsAsync("opencode", cancellationToken: cancellationToken);
        IsCodexEnabled = codex.IsEnabled;
        CodexExecutablePath = codex.ExecutablePath ?? string.Empty;
        IsOpenCodeEnabled = openCode.IsEnabled;
        OpenCodeExecutablePath = openCode.ExecutablePath ?? string.Empty;
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

    [RelayCommand]
    private async Task SaveAgentSettings()
    {
        await _settings.SaveAgentRuntimeSettingsAsync(new("codex", IsCodexEnabled, CodexExecutablePath));
        await _settings.SaveAgentRuntimeSettingsAsync(new("opencode", IsOpenCodeEnabled, OpenCodeExecutablePath));
        AgentSettingsStatus = _localization["Settings.AgentSaved"];
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
