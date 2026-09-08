namespace Workbench.App.Leader;

internal static class AuthorityContributionCanonicalizer
{
    private static readonly string[] PendingMarkers =
    [
        "该规则拟作为",
        "待用户确认",
        "尚未进入 Accepted Project State",
        "尚未进入Accepted Project State"
    ];

    public static string Canonicalize(string statement)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statement);
        var boundary = PendingMarkers
            .Select(marker => statement.IndexOf(marker, StringComparison.Ordinal))
            .Where(index => index >= 0)
            .DefaultIfEmpty(statement.Length)
            .Min();
        var acceptedFact = statement[..boundary].Trim().TrimEnd('，', ',', ';', '；');
        if (string.IsNullOrWhiteSpace(acceptedFact))
            throw new ArgumentException("An Authority contribution must describe accepted project state.", nameof(statement));
        return acceptedFact;
    }
}
