using Workbench.Core.Continuity;

namespace Workbench.App.Leader;

/// <summary>
/// Ephemeral user-review state for project facts. It is deliberately not a
/// Task, Worker execution, or persisted approval record.
/// </summary>
public sealed record AuthorityConfirmationDraft(
    Guid ProjectId,
    string Title,
    IReadOnlyList<AcceptedContributionInstruction> Contributions)
{
    public IReadOnlyList<string> Statements => Contributions.Select(value => value.Statement).ToArray();
}
