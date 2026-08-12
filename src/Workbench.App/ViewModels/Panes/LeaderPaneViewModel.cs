using CommunityToolkit.Mvvm.Input;

namespace Workbench.App.ViewModels.Panes;

public sealed partial class LeaderPaneViewModel : ViewModelBase
{
    private readonly Func<Task> _focus;

    public LeaderPaneViewModel(Func<Task> focus)
    {
        _focus = focus;
    }

    public void RecordContentInteraction()
    {
    }

    [RelayCommand]
    private Task Focus() => _focus();
}
