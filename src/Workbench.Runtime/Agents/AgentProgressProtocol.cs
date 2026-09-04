using System.Text;
using System.Text.RegularExpressions;

namespace Workbench.Runtime.Agents;

/// <summary>
/// Control-line protocol used by Workbench to project Worker plan progress
/// without exposing transport details in the user-facing transcript.
/// </summary>
public static class AgentProgressProtocol
{
    public const string StepCompletedMarker = "WORKBENCH_STEP_COMPLETED:";
    private static readonly Regex MarkerRegex = new(
        $@"{Regex.Escape(StepCompletedMarker)}\s*(?<step>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string StripControlLines(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        return MarkerRegex.Replace(text, string.Empty);
    }

    public static bool TryParseStepCompletedLine(string line, out int step)
    {
        step = 0;
        var match = MarkerRegex.Match(line);
        return match.Success && int.TryParse(match.Groups["step"].Value, out step) && step > 0;
    }

    public static IReadOnlyList<int> ExtractCompletedSteps(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        return MarkerRegex.Matches(text)
            .Select(match => int.TryParse(match.Groups["step"].Value, out var step) ? step : 0)
            .Where(step => step > 0)
            .ToArray();
    }

    public static bool TryConsumeNextMarker(StringBuilder buffer, out int step)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        step = 0;
        var text = buffer.ToString();
        var markerStart = text.IndexOf(StepCompletedMarker, StringComparison.OrdinalIgnoreCase);
        if (markerStart < 0)
        {
            if (text.Length > StepCompletedMarker.Length)
            {
                buffer.Clear();
                buffer.Append(text[^StepCompletedMarker.Length..]);
            }
            return false;
        }

        var match = MarkerRegex.Match(text, markerStart);
        if (!match.Success)
            return false;

        if (!int.TryParse(match.Groups["step"].Value, out step) || step <= 0)
        {
            step = 0;
            buffer.Clear();
            buffer.Append(text[(markerStart + StepCompletedMarker.Length)..]);
            return false;
        }

        buffer.Clear();
        buffer.Append(text[(match.Index + match.Length)..]);
        return true;
    }
}
