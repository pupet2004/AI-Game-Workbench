using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Workbench.Storage.Leaders;
using Workbench.Storage.Memory;

namespace Workbench.App.Leader;

public sealed record LeaderSummaryRecoveryError(
    long MessageId,
    Guid ResultId,
    string Message);

public sealed record LeaderSummaryRecoveryReport(
    int RecoveredCount,
    IReadOnlyList<LeaderSummaryRecoveryError> Errors);

public sealed class LeaderSummaryRecoveryService(
    LeaderMessageRepository messages,
    ProjectSummaryRepository summaries,
    TimeProvider? timeProvider = null)
{
    private readonly LeaderMessageRepository _messages =
        messages ?? throw new ArgumentNullException(nameof(messages));
    private readonly ProjectSummaryRepository _summaries =
        summaries ?? throw new ArgumentNullException(nameof(summaries));
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    internal ProjectSummaryRepository SummaryRepository => _summaries;

    public async Task<LeaderSummaryRecoveryReport> RecoverAsync(
        CancellationToken cancellationToken = default)
    {
        var recoveredCount = 0;
        var errors = new List<LeaderSummaryRecoveryError>();
        foreach (var pending in await _messages.GetPendingSummaryResultsAsync(cancellationToken))
        {
            try
            {
                var deltas = LeaderSummaryPayload.Deserialize(pending.SummaryDeltaPayloadJson);
                await _summaries.AppendAsync(
                    pending.ProjectId,
                    pending.ResultId,
                    deltas,
                    pending.CreatedAt,
                    cancellationToken);
                if (!await _messages.MarkSummaryPersistedAsync(
                        pending.MessageId,
                        pending.ResultId,
                        _timeProvider.GetUtcNow(),
                        cancellationToken))
                {
                    throw new InvalidOperationException("The pending Leader summary result could not be marked persisted.");
                }

                recoveredCount++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                errors.Add(new LeaderSummaryRecoveryError(
                    pending.MessageId,
                    pending.ResultId,
                    exception.Message));
            }
        }

        return new LeaderSummaryRecoveryReport(recoveredCount, errors);
    }
}

internal static class LeaderSummaryPayload
{
    private static readonly Regex OccurredAtPattern = new(
        @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$",
        RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize(IReadOnlyList<SummaryDelta> deltas) =>
        JsonSerializer.Serialize(deltas, Options);

    public static IReadOnlyList<SummaryDelta> Deserialize(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Summary delta payload must be an array.");
        }

        var deltas = new List<SummaryDelta>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            RequireExactProperties(item, "occurred_at", "kind", "text", "source_refs");
            var occurredAtText = RequiredString(item, "occurred_at");
            if (!OccurredAtPattern.IsMatch(occurredAtText) ||
                !DateTimeOffset.TryParse(
                    occurredAtText,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var occurredAt))
            {
                throw new JsonException("Summary occurred_at must use the round-trip format with an explicit offset.");
            }

            var kindText = RequiredString(item, "kind");
            if (!Enum.GetNames<SummaryDeltaKind>().Contains(kindText, StringComparer.Ordinal) ||
                !Enum.TryParse<SummaryDeltaKind>(kindText, false, out var kind))
            {
                throw new JsonException("Summary kind is invalid.");
            }

            var text = RequiredString(item, "text");
            var sourceArray = item.GetProperty("source_refs");
            if (sourceArray.ValueKind != JsonValueKind.Array)
            {
                throw new JsonException("Summary source_refs must be an array.");
            }

            var sourceRefs = sourceArray.EnumerateArray()
                .Select(source =>
                {
                    RequireExactProperties(source, "source_kind", "source_locator");
                    return new SummarySourceRef(
                        RequiredString(source, "source_kind"),
                        RequiredString(source, "source_locator"));
                })
                .ToArray();
            deltas.Add(new SummaryDelta(occurredAt, kind, text, sourceRefs));
        }

        if (deltas.Count == 0)
        {
            throw new JsonException("Summary delta payload must contain at least one item.");
        }

        return deltas;
    }

    private static string RequiredString(JsonElement value, string property)
    {
        var item = value.GetProperty(property);
        var result = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
        return string.IsNullOrWhiteSpace(result)
            ? throw new JsonException($"Summary {property} is required.")
            : result;
    }

    private static void RequireExactProperties(JsonElement value, params string[] expected)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Summary item must be an object.");
        }

        var actual = value.EnumerateObject().Select(property => property.Name).Order().ToArray();
        if (!actual.SequenceEqual(expected.Order()))
        {
            throw new JsonException("Summary item contains missing or unsupported properties.");
        }
    }
}
