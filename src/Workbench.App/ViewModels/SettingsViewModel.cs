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

    public async Task InitializeAsync(CancellationToken cancellationToken = default) =>
        LeaderSessionRotationPolicy = await _settings.GetLeaderSessionRotationPolicyAsync(cancellationToken);

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
    private Task Back() => BackToProjects();
}
