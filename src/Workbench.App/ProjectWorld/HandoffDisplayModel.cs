using Workbench.Core.Continuity;
using Workbench.Storage.Workers;
using Workbench.App.Worker;

namespace Workbench.App.ProjectWorld;

public enum HandoffDisplaySourceKind
{
    B1Handoff,
    LegacyWorkerCompletion
}

public sealed record HandoffDisplayModel(
    HandoffDisplaySourceKind SourceKind,
    string Result,
    IReadOnlyList<string> ArtifactPaths,
    IReadOnlyList<string> ChangedPaths,
    IReadOnlyList<string> UnresolvedIssues,
    IReadOnlyList<string> Recommendations,
    string AuthorityStatus,
    string Provenance,
    string? SourceReference);

public static class HandoffDisplayModelFactory
{
    public static HandoffDisplayModel FromB1(
        B1ProjectState state,
        Handoff handoff,
        string provenance,
        IReadOnlyList<string>? artifactPaths = null,
        IReadOnlyList<string>? changedPaths = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(handoff);
        var claims = state.Claims.ToDictionary(value => value.ClaimRef);
        var result = GetStatement(claims, handoff.ResultClaimRef);
        var unresolved = handoff.UnresolvedIssueClaimRefs
            .Select(reference => GetStatement(claims, reference))
            .ToArray();
        var recommendations = handoff.ProposedContributionClaimRefs
            .Select(reference => GetStatement(claims, reference))
            .ToArray();
        return new(
            HandoffDisplaySourceKind.B1Handoff,
            result,
            Copy(artifactPaths),
            Copy(changedPaths),
            unresolved,
            recommendations,
            "Needs review; not Accepted Project State",
            provenance,
            handoff.HandoffRef.ToString());
    }

    public static HandoffDisplayModel FromLegacyCompletion(
        StoredCompletionPackage completion,
        string provenance,
        IReadOnlyList<string>? artifactPaths = null,
        IReadOnlyList<string>? changedPaths = null,
        IReadOnlyList<string>? unresolvedIssues = null,
        IReadOnlyList<string>? recommendations = null)
    {
        ArgumentNullException.ThrowIfNull(completion);
        return new(
            HandoffDisplaySourceKind.LegacyWorkerCompletion,
            completion.Report,
            Copy(artifactPaths),
            Copy(changedPaths),
            Copy(unresolvedIssues),
            Copy(recommendations),
            "Legacy completion; not Accepted Project State",
            provenance,
            completion.PackageId.ToString());
    }

    public static HandoffDisplayModel FromLegacyWorkerHandoff(
        WorkerHandoff handoff,
        string provenance)
    {
        ArgumentNullException.ThrowIfNull(handoff);
        return new(
            HandoffDisplaySourceKind.LegacyWorkerCompletion,
            handoff.Message,
            [],
            [],
            string.IsNullOrWhiteSpace(handoff.ValidationSummary) ? [] : [handoff.ValidationSummary],
            [],
            "Legacy Worker handoff; not Accepted Project State",
            provenance,
            handoff.SourceEventId?.ToString() ?? handoff.WorkerSessionId.ToString());
    }

    private static string GetStatement(IReadOnlyDictionary<ClaimRef, Claim> claims, ClaimRef reference)
    {
        if (!claims.TryGetValue(reference, out var claim))
            return $"Missing claim {reference}";
        return claim.Payload switch
        {
            ClaimPayload.Result result => result.Statement,
            ClaimPayload.Validation validation => validation.Statement,
            ClaimPayload.UnresolvedIssue issue => issue.Statement,
            ClaimPayload.ProposedStateContribution contribution => contribution.Statement,
            ClaimPayload.ProposedAssignmentRevision revision => revision.ProposedContract.WorkContract,
            _ => "Unknown claim payload"
        };
    }

    private static IReadOnlyList<string> Copy(IReadOnlyList<string>? values) =>
        Array.AsReadOnly((values ?? Array.Empty<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray());
}
