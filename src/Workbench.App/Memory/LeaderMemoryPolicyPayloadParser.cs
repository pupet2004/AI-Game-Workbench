using System.Text.Json;
using Workbench.Storage.Memory;

namespace Workbench.App.Memory;

public static class LeaderMemoryPolicyPayloadParser
{
    public static bool TryParse(Guid projectId, string? payload, out LeaderMemoryPolicyDecision decision)
    {
        decision = new(null, null, null, []);
        if (projectId == Guid.Empty || string.IsNullOrWhiteSpace(payload)) return false;
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !HasOnly(root, "brain_handoff", "daily_summary", "total_continuity_budget_utf8_bytes", "continuity_selection") ||
                !root.TryGetProperty("brain_handoff", out var handoff) ||
                !root.TryGetProperty("daily_summary", out var daily) ||
                !root.TryGetProperty("total_continuity_budget_utf8_bytes", out var total) ||
                !root.TryGetProperty("continuity_selection", out var selection)) return false;

            var parsedHandoff = OptionalString(handoff);
            if (handoff.ValueKind is not (JsonValueKind.Null or JsonValueKind.String) || string.IsNullOrWhiteSpace(parsedHandoff) && handoff.ValueKind == JsonValueKind.String) return false;
            var parsedDaily = ParseDaily(projectId, daily);
            if (daily.ValueKind != JsonValueKind.Null && parsedDaily is null) return false;
            int? parsedTotal = total.ValueKind == JsonValueKind.Null ? null : total.ValueKind == JsonValueKind.Number && total.TryGetInt32(out var value) && value > 0 ? value : null;
            if (total.ValueKind != JsonValueKind.Null && parsedTotal is null || selection.ValueKind != JsonValueKind.Array) return false;
            var selections = ParseSelections(selection);
            if (selections is null || selections.Count > 0 && parsedTotal is null) return false;

            decision = new(parsedHandoff, parsedDaily, parsedTotal, selections);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static DailySummaryWrite? ParseDaily(Guid projectId, JsonElement daily)
    {
        if (daily.ValueKind == JsonValueKind.Null) return null;
        if (daily.ValueKind != JsonValueKind.Object || !HasOnly(daily, "local_date", "content", "expected_revision", "sources") ||
            !daily.TryGetProperty("local_date", out var localDate) || !daily.TryGetProperty("content", out var content) ||
            !daily.TryGetProperty("expected_revision", out var expectedRevision) || !daily.TryGetProperty("sources", out var sources) ||
            localDate.ValueKind != JsonValueKind.String || !DateOnly.TryParse(localDate.GetString(), out var date) ||
            content.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(content.GetString()) ||
            expectedRevision.ValueKind is not (JsonValueKind.Null or JsonValueKind.Number) ||
            sources.ValueKind != JsonValueKind.Array) return null;
        var revision = expectedRevision.ValueKind == JsonValueKind.Null ? (int?)null : expectedRevision.TryGetInt32(out var parsedRevision) && parsedRevision >= 1 ? parsedRevision : null;
        if (expectedRevision.ValueKind == JsonValueKind.Number && revision is null) return null;

        var references = new List<DailySummarySourceReference>();
        foreach (var source in sources.EnumerateArray())
        {
            if (source.ValueKind != JsonValueKind.Object || !HasOnly(source, "source_type", "source_ref") ||
                !source.TryGetProperty("source_type", out var type) || !source.TryGetProperty("source_ref", out var reference) ||
                type.ValueKind != JsonValueKind.String || reference.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(type.GetString()) || string.IsNullOrWhiteSpace(reference.GetString())) return null;
            references.Add(new(type.GetString()!, reference.GetString()!));
        }
        return new(projectId, date, content.GetString()!, revision, references);
    }

    private static IReadOnlyList<ContinuityMaterialSelection>? ParseSelections(JsonElement selection)
    {
        var result = new List<ContinuityMaterialSelection>();
        foreach (var item in selection.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !HasOnly(item, "ordinal", "kind", "reference", "max_utf8_bytes", "selector") ||
                !item.TryGetProperty("ordinal", out var ordinal) || !item.TryGetProperty("kind", out var kind) ||
                !item.TryGetProperty("reference", out var reference) || !item.TryGetProperty("max_utf8_bytes", out var max) || !item.TryGetProperty("selector", out var selector) ||
                !ordinal.TryGetInt32(out var ordinalValue) || ordinalValue < 0 || kind.ValueKind != JsonValueKind.String ||
                !Enum.TryParse<ContinuityMaterialKind>(kind.GetString(), false, out var kindValue) || reference.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(reference.GetString()) || !max.TryGetInt32(out var maxValue) || maxValue <= 0 ||
                selector.ValueKind is not (JsonValueKind.Null or JsonValueKind.Object)) return null;
            result.Add(new(ordinalValue, kindValue, reference.GetString()!, maxValue, selector.ValueKind == JsonValueKind.Null ? null : selector.GetRawText()));
        }
        return result.Select((item, index) => item.Ordinal == index ? item : null).Any(item => item is null) ? null : result;
    }

    private static string? OptionalString(JsonElement element) => element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    private static bool HasOnly(JsonElement value, params string[] expected) => value.EnumerateObject().All(property => expected.Contains(property.Name, StringComparer.Ordinal)) && expected.All(name => value.TryGetProperty(name, out _));
}
