using System.Collections.ObjectModel;
using Workbench.Runtime.Agents;

namespace Workbench.App.ViewModels.Leader;

public sealed class ProjectLeaderSessionManager
{
    private readonly Dictionary<Guid, LeaderConversationState> _conversations = [];

    internal LeaderConversationState GetOrCreate(Guid projectId)
    {
        if (_conversations.TryGetValue(projectId, out var conversation))
        {
            return conversation;
        }

        conversation = new LeaderConversationState();
        _conversations.Add(projectId, conversation);
        return conversation;
    }

    public int Count => _conversations.Count;
}

internal sealed class LeaderConversationState
{
    public ObservableCollection<LeaderModelOptionViewModel> AvailableModels { get; } = [];

    public ObservableCollection<LeaderMessageViewModel> Messages { get; } = [];

    public LeaderModelOptionViewModel? SelectedModel { get; set; }

    public AgentSession? Session { get; set; }

    public AgentApprovalRequested? PendingApproval { get; set; }

    public bool ModelsLoaded { get; set; }

    public bool IsBusy { get; set; }

    public bool IsApprovalResponding { get; set; }

    public string? RuntimeStatus { get; set; }

    public string? RuntimeErrorDetail { get; set; }

    public string? ApprovalError { get; set; }
}
