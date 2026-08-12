using CommunityToolkit.Mvvm.Input;
using Workbench.Runtime.Agents;

namespace Workbench.App.ViewModels.Leader;

public sealed partial class LeaderApprovalOptionViewModel
{
    private readonly Func<LeaderApprovalOptionViewModel, Task> _respond;

    public LeaderApprovalOptionViewModel(
        AgentApprovalOption option,
        Func<LeaderApprovalOptionViewModel, Task> respond)
    {
        Option = option;
        _respond = respond;
    }

    public AgentApprovalOption Option { get; }

    public string Label => Option.Label;

    public string? Description => Option.Description;

    [RelayCommand]
    private Task Select() => _respond(this);
}
