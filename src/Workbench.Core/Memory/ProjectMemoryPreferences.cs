namespace Workbench.Core.Memory;

public enum LibraryGranularityMode { Balanced, Detailed, Compact, Custom }

public enum ContinuityMode { Balanced, HighContinuity, LowToken, Custom }

public sealed record ProjectMemoryPreferences(
    Guid ProjectId,
    LibraryGranularityMode LibraryGranularity,
    ContinuityMode Continuity,
    string TimeZoneId,
    string? CustomInstructions,
    DateTimeOffset UpdatedAt);
