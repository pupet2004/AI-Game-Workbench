using System.Text.Json;
using Workbench.Storage.Memory;

namespace Workbench.App.Worker;

/// <summary>
/// Converts a durable Worker FinalReport event into a durable project Summary.
/// The event identity is used as the Summary result identity, making retries safe.
/// </summary>
public sealed class WorkerCompletionSummaryConsumer(ProjectSummaryRepository summaries)
{
    private readonly ProjectSummaryRepository _summaries = summaries ?? throw new ArgumentNullException(nameof(summaries));

    public Task<IReadOnlyList<StoredSummaryEntry>> ConsumeAsync(
        Guid projectId,
        Guid taskId,
        Guid eventId,
        string payload,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        var message = ReadMessage(payload);
        var delta = new SummaryDelta(
            occurredAt,
            SummaryDeltaKind.Change,
            $"Worker completed task {taskId}: {message}",
            [
                new SummarySourceRef("WorkerFinalReport", eventId.ToString()),
                new SummarySourceRef("TaskEvent", eventId.ToString())
            ]);
        return _summaries.AppendAsync(projectId, eventId, [delta], occurredAt, cancellationToken);
    }

    private static string ReadMessage(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.TryGetProperty("Message", out var message) && message.ValueKind == JsonValueKind.String)
                return message.GetString() ?? "completed";
            if (root.TryGetProperty("Report", out var report) && report.ValueKind == JsonValueKind.String)
                return report.GetString() ?? "completed";
        }
        catch (JsonException)
        {
        }
        return payload;
    }
}
