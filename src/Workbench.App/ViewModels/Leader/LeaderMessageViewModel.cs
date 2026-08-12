using CommunityToolkit.Mvvm.ComponentModel;

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
        LeaderMessageRole.User => "You",
        LeaderMessageRole.Assistant => "Leader",
        _ => "Error"
    };

    [ObservableProperty]
    public partial string Text { get; set; }

    [ObservableProperty]
    public partial bool IsStreaming { get; set; }

    public void Append(string text) => Text += text;
}
