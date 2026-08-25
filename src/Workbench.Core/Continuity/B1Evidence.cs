using System.Text.RegularExpressions;
using System.Security.Cryptography;

namespace Workbench.Core.Continuity;

public enum EvidenceKind
{
    ExternalLocator,
    Text,
    File,
    TestRun,
    CodeDiff,
    Image,
    Other
}

public sealed record EvidenceRecord
{
    public EvidenceRecord(
        ProjectRef projectRef,
        EvidenceRef evidenceRef,
        EvidenceKind kind,
        string locator,
        string? digestAlgorithm,
        string? digestHex,
        long? contentLength,
        DateTimeOffset createdAt)
    {
        B1ContractValidation.Require(projectRef, nameof(projectRef));
        ArgumentException.ThrowIfNullOrWhiteSpace(locator);
        if (contentLength is < 0)
            throw new ArgumentOutOfRangeException(nameof(contentLength));
        if ((digestAlgorithm is null) != (digestHex is null))
            throw new ArgumentException("Digest algorithm and digest must be supplied together.");
        if (digestAlgorithm is not null && !string.Equals(digestAlgorithm, EvidenceDigest.Sha256Algorithm, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Unsupported evidence digest algorithm: {digestAlgorithm}.", nameof(digestAlgorithm));
        if (digestHex is not null && !EvidenceDigest.IsSha256(digestHex))
            throw new ArgumentException("SHA-256 evidence digests must contain exactly 64 hexadecimal characters.", nameof(digestHex));

        ProjectRef = projectRef;
        EvidenceRef = evidenceRef;
        Kind = kind;
        Locator = locator.Trim();
        DigestAlgorithm = digestAlgorithm is null ? null : EvidenceDigest.Sha256Algorithm;
        DigestHex = digestHex?.ToUpperInvariant();
        ContentLength = contentLength;
        CreatedAt = createdAt;
    }

    public ProjectRef ProjectRef { get; }
    public EvidenceRef EvidenceRef { get; }
    public EvidenceKind Kind { get; }
    public string Locator { get; }
    public string? DigestAlgorithm { get; }
    public string? DigestHex { get; }
    public long? ContentLength { get; }
    public DateTimeOffset CreatedAt { get; }
}

public static partial class EvidenceDigest
{
    public const string Sha256Algorithm = "SHA-256";

    public static string ComputeSha256(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content));

    public static bool IsSha256(string value) =>
        Sha256Regex().IsMatch(value);

    public static bool VerifySha256(ReadOnlySpan<byte> content, string digestHex) =>
        IsSha256(digestHex) &&
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(content),
            Convert.FromHexString(digestHex));

    [GeneratedRegex("^[0-9A-Fa-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Regex();
}
