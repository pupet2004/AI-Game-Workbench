using System.Text;
using Workbench.Storage.Memory;

namespace Workbench.App.Leader;

internal static class EvolutionCandidateGovernanceSourceResolver
{
    public static ProjectEvolutionCandidate? Resolve(
        IReadOnlyList<ProjectEvolutionCandidate> candidates,
        LeaderEvolutionRouteHint route,
        string governanceText)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentException.ThrowIfNullOrWhiteSpace(governanceText);
        var target = Normalize(governanceText);
        var ranked = candidates
            .Where(candidate => IsCompatible(candidate, route))
            .Select(candidate => (Candidate: candidate, Score: Score(candidate, target)))
            .Where(item => item.Score >= 4)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Candidate.CreatedAt)
            .ToArray();
        if (ranked.Length == 0) return null;
        if (ranked.Length > 1 && ranked[0].Score == ranked[1].Score) return null;
        return ranked[0].Candidate;
    }

    private static bool IsCompatible(ProjectEvolutionCandidate candidate, LeaderEvolutionRouteHint route) => route switch
    {
        LeaderEvolutionRouteHint.AuthorityConfirmation =>
            candidate.RouteHint == nameof(LeaderEvolutionRouteHint.AuthorityConfirmation) ||
            candidate.ImpactClass == nameof(LeaderEvolutionImpactClass.WorldRule),
        LeaderEvolutionRouteHint.LibraryProposal =>
            candidate.RouteHint == nameof(LeaderEvolutionRouteHint.LibraryProposal) ||
            candidate.ImpactClass is nameof(LeaderEvolutionImpactClass.Content) or nameof(LeaderEvolutionImpactClass.CharacterOrObject),
        _ => false
    };

    private static int Score(ProjectEvolutionCandidate candidate, string target)
    {
        var objectName = Normalize(candidate.Object);
        if (objectName.Length >= 3 && target.Contains(objectName, StringComparison.Ordinal))
            return 1000 + objectName.Length;
        var source = Normalize($"{candidate.Object} {candidate.After} {candidate.Reason}");
        return LongestCommonSubstring(source, target);
    }

    private static int LongestCommonSubstring(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0) return 0;
        var previous = new int[right.Length + 1];
        var best = 0;
        foreach (var leftCharacter in left)
        {
            var current = new int[right.Length + 1];
            for (var rightIndex = 1; rightIndex <= right.Length; rightIndex++)
            {
                if (leftCharacter != right[rightIndex - 1]) continue;
                current[rightIndex] = previous[rightIndex - 1] + 1;
                best = Math.Max(best, current[rightIndex]);
            }
            previous = current;
        }
        return best;
    }

    private static string Normalize(string? value)
    {
        var builder = new StringBuilder();
        foreach (var character in value ?? string.Empty)
        {
            if (char.IsLetterOrDigit(character))
                builder.Append(char.ToLowerInvariant(character));
        }
        return builder.ToString();
    }
}
