using CommunityToolkit.Mvvm.Input;

namespace Workbench.App.ViewModels.Panes;

public sealed partial class WorkPaneViewModel : ViewModelBase
{
    private readonly Func<Task> _focus;

    public WorkPaneViewModel(Func<Task> focus)
    {
        _focus = focus;
    }

    [RelayCommand]
    private Task Focus() => _focus();
}
