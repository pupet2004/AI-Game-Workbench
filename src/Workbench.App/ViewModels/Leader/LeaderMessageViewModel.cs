using CommunityToolkit.Mvvm.ComponentModel;
using Workbench.App.Services;

namespace Workbench.App.ViewModels.Leader;

public enum LeaderMessageRole
{
    User,
    Assistant,
    Error
}

public partial class LeaderMessageViewModel : ViewModelBase
{
    public LeaderMessageViewModel(LeaderMessageRole role, string text, bool isStreaming = false)
    {
        Role = role;
        Text = text;
        IsStreaming = isStreaming;
    }

    public LeaderMessageRole Role { get; }

    public string RoleLabel => Role switch
    {
        LeaderMessageRole.User => LocalizationService.Current["Dynamic.You"],
        LeaderMessageRole.Assistant => "Leader",
        _ => LocalizationService.Current["Dynamic.Error"]
    };

    [ObservableProperty]
    public partial string Text { get; set; }

    [ObservableProperty]
    public partial bool IsStreaming { get; set; }

    public void Append(string text) => Text += text;
}
