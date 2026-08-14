using Workbench.Storage.Memory;

namespace Workbench.App.Memory;

public sealed record LeaderMemoryPolicyDecision(
    string? BrainHandoff,
    DailySummaryWrite? DailySummary,
    int? TotalContinuityBudgetUtf8Bytes,
    IReadOnlyList<ContinuityMaterialSelection> ContinuitySelection);

public sealed record LeaderMemoryPolicyPreparation(
    bool Available,
    LeaderMemoryPolicyDecision? Decision,
    string? Error);
