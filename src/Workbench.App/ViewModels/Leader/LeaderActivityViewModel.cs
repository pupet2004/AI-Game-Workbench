using CommunityToolkit.Mvvm.ComponentModel;

namespace Workbench.App.ViewModels.Leader;

public sealed partial class LeaderActivityViewModel : ObservableObject
{
    public LeaderActivityViewModel(string title, string? detail = null)
    {
        Title = title;
        Detail = detail;
    }

    public string Title { get; }

    [ObservableProperty]
    public partial string? Detail { get; set; }

    [ObservableProperty]
    public partial bool IsComplete { get; set; }
}
