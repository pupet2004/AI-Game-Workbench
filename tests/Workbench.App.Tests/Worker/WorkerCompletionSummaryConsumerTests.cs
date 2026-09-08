using System.Text.Json;
using Workbench.App.Worker;
using Workbench.Storage.Memory;

namespace Workbench.App.Tests.Worker;

public sealed class WorkerCompletionSummaryConsumerTests
{
    [Fact]
    public async Task Final_report_event_becomes_idempotent_durable_summary_with_sources()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var directory = new Support.TemporaryDirectory("worker-summary");
        var project = (await context.Services.ProjectOpenService.OpenAsync(directory.Path)).Project;
        var consumer = new WorkerCompletionSummaryConsumer(context.Services.ProjectSummaryRepository);
        var taskId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var occurredAt = context.Time.GetUtcNow();
        var payload = JsonSerializer.Serialize(new { Message = "chapter-01.md produced", ValidationSummary = "green" });

        var first = await consumer.ConsumeAsync(project.Id, taskId, eventId, payload, occurredAt);
        var second = await consumer.ConsumeAsync(project.Id, taskId, eventId, payload, occurredAt);

        Assert.Single(first);
        Assert.Single(second);
        var stored = Assert.Single(await context.Services.ProjectSummaryRepository.QueryAsync(new SummaryQuery(project.Id, 10)));
        Assert.Contains("chapter-01.md produced", stored.Text, StringComparison.Ordinal);
        Assert.Contains(stored.SourceRefs, value => value.SourceKind == "WorkerFinalReport" && value.SourceLocator == eventId.ToString());
        Assert.Contains(stored.SourceRefs, value => value.SourceKind == "TaskEvent" && value.SourceLocator == eventId.ToString());
    }
}
