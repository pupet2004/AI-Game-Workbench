using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core.Leaders;
using Workbench.Storage.Settings;

namespace Workbench.App.ViewModels;

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly WorkbenchSettingsRepository _settings;

    public SettingsViewModel(WorkbenchSettingsRepository settings, Func<Task> backToProjects)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        BackToProjects = backToProjects ?? throw new ArgumentNullException(nameof(backToProjects));
    }

    public Func<Task> BackToProjects { get; }

    [ObservableProperty]
    public partial LeaderSessionRotationPolicy LeaderSessionRotationPolicy { get; set; } = LeaderSessionRotationPolicy.Auto;

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

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        LeaderSessionRotationPolicy = await _settings.GetLeaderSessionRotationPolicyAsync(cancellationToken);
        var codex = await _settings.GetAgentRuntimeSettingsAsync("codex", cancellationToken: cancellationToken);
        var openCode = await _settings.GetAgentRuntimeSettingsAsync("opencode", cancellationToken: cancellationToken);
        IsCodexEnabled = codex.IsEnabled;
        CodexExecutablePath = codex.ExecutablePath ?? string.Empty;
        IsOpenCodeEnabled = openCode.IsEnabled;
        OpenCodeExecutablePath = openCode.ExecutablePath ?? string.Empty;
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

    [RelayCommand]
    private async Task SaveAgentSettings()
    {
        await _settings.SaveAgentRuntimeSettingsAsync(new("codex", IsCodexEnabled, CodexExecutablePath));
        await _settings.SaveAgentRuntimeSettingsAsync(new("opencode", IsOpenCodeEnabled, OpenCodeExecutablePath));
        AgentSettingsStatus = "Saved. Enabled Agents connect only when a project needs them.";
    }

    [RelayCommand]
    private Task Back() => BackToProjects();
}
