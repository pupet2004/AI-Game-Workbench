using Workbench.Core.Continuity;

namespace Workbench.App.Leader;

/// <summary>
/// Ephemeral user-review state for project facts. It is deliberately not a
/// Task, Worker execution, or persisted approval record.
/// </summary>
public sealed record AuthorityConfirmationDraft(
    Guid ProjectId,
    string Title,
    IReadOnlyList<AcceptedContributionInstruction> Contributions,
    string? SourceRef = null)
{
    public IReadOnlyList<string> Statements => Contributions.Select(value => value.Statement).ToArray();

    public IReadOnlyList<ConsideredRef> ConsideredRefs
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SourceRef)) return [];
            const string prefix = "workbench:evolution-candidate/";
            return SourceRef.StartsWith(prefix, StringComparison.Ordinal) &&
                   Guid.TryParse(SourceRef[prefix.Length..], out var candidateId)
                ? [new ConsideredRef.EvolutionCandidate(candidateId)]
                : [new ConsideredRef.Evidence(new EvidenceRef(SourceRef))];
        }
    }
}
