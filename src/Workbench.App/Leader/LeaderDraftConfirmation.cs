using Workbench.Core.Tasks;
using Workbench.Runtime.Registry;

namespace Workbench.App.Leader;

public sealed record LeaderDraftConfirmation(
    Guid TaskId,
    string Title,
    string Goal,
    string Scope,
    IReadOnlyList<string> Acceptance,
    TaskRiskLevel RiskLevel,
    LeaderExecutionRecommendation Recommendation,
    WorkerResource Resource,
    TaskRevision Revision);
