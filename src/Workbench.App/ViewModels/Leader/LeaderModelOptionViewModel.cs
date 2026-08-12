using Workbench.Runtime.Registry;

namespace Workbench.App.ViewModels.Leader;

public sealed class LeaderModelOptionViewModel
{
    public LeaderModelOptionViewModel(
        AvailableModelProfile profile,
        string providerDisplayName,
        string accountDisplayName)
    {
        Profile = profile;
        DisplayName = $"{providerDisplayName} · {accountDisplayName} · {profile.Model.DisplayName}";
    }

    public AvailableModelProfile Profile { get; }

    public string DisplayName { get; }
}
