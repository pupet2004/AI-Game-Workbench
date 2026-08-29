namespace Workbench.Runtime.Agents;

/// <summary>
/// Control-line protocol used by Workbench to project Worker plan progress
/// without exposing transport details in the user-facing transcript.
/// </summary>
public static class AgentProgressProtocol
{
    public const string StepCompletedMarker = "WORKBENCH_STEP_COMPLETED:";

    public static string StripControlLines(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var kept = lines.Where(line => !TryParseStepCompletedLine(line, out _)).ToArray();
        return string.Join('\n', kept);
    }

    public static bool TryParseStepCompletedLine(string line, out int step)
    {
        step = 0;
        var value = line.Trim();
        if (!value.StartsWith(StepCompletedMarker, StringComparison.OrdinalIgnoreCase)) return false;
        return int.TryParse(value[StepCompletedMarker.Length..].Trim(), out step) && step > 0;
    }
}
