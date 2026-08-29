using CommunityToolkit.Mvvm.Input;
using Workbench.App.Services;
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

    public string Label => Option.Id switch
    {
        "allow-once" or "approve-once" => LocalizationService.Current["Surface.ApprovalApproveOnce"],
        "allow-session" or "approve-session" => LocalizationService.Current["Surface.ApprovalApproveSession"],
        "decline" => LocalizationService.Current["Surface.ApprovalDecline"],
        "cancel" => LocalizationService.Current["Surface.ApprovalCancelTurn"],
        _ => Option.Label
    };

    public string? Description => Option.Id switch
    {
        "allow-once" or "approve-once" => LocalizationService.Current["Surface.ApprovalApproveOnceDescription"],
        "allow-session" or "approve-session" => LocalizationService.Current["Surface.ApprovalApproveSessionDescription"],
        "decline" => LocalizationService.Current["Surface.ApprovalDeclineDescription"],
        "cancel" => LocalizationService.Current["Surface.ApprovalCancelTurnDescription"],
        _ => Option.Description
    };

    [RelayCommand]
    private Task Select() => _respond(this);
}
