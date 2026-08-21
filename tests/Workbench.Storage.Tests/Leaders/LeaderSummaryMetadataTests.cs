namespace Workbench.Storage.Tests.Leaders;

using Workbench.Storage.Leaders;

public sealed class LeaderSummaryMetadataTests
{
    [Fact]
    public async Task Leader_message_metadata_round_trips_result_id_and_summary_delta_payload()
    {
        await using var context = await LeaderStorageContext.CreateAsync();
        var epoch = context.CreateEpoch(context.ProjectA.Id);
        await context.CreateCurrentEpochAsync(epoch);
        var resultId = Guid.NewGuid();
        const string payload = "[{\"occurred_at\":\"2026-01-01T00:00:00Z\",\"kind\":\"Decision\",\"text\":\"Keep it durable\",\"source_refs\":[]}]";

        var appended = await context.Messages.AppendAsync(
            epoch.Id,
            "assistant",
            "Visible answer",
            context.T0,
            metadata: new LeaderResultMetadata(resultId, payload));
        var stored = Assert.Single(await context.Messages.GetAllAsync(epoch.Id));

        Assert.Equal(resultId, appended.ResultId);
        Assert.Equal(payload, appended.SummaryDeltaPayloadJson);
        Assert.Null(appended.SummaryPersistedAt);
        Assert.Equal(resultId, stored.ResultId);
        Assert.Equal(payload, stored.SummaryDeltaPayloadJson);
        Assert.DoesNotContain("Visible answer", stored.SummaryDeltaPayloadJson);
        Assert.DoesNotContain("draft", stored.SummaryDeltaPayloadJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("memory", stored.SummaryDeltaPayloadJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Pending_summary_query_returns_only_unmarked_summary_rows()
    {
        await using var context = await LeaderStorageContext.CreateAsync();
        var epoch = context.CreateEpoch(context.ProjectA.Id);
        await context.CreateCurrentEpochAsync(epoch);
        var pendingResult = Guid.NewGuid();
        var persistedResult = Guid.NewGuid();
        var pending = await context.Messages.AppendAsync(epoch.Id, "assistant", "pending", context.T0, metadata: Metadata(pendingResult, "pending"));
        var persisted = await context.Messages.AppendAsync(epoch.Id, "assistant", "persisted", context.T1, metadata: Metadata(persistedResult, "persisted"));
        await context.Messages.AppendAsync(epoch.Id, "assistant", "ordinary", context.T1.AddMinutes(1));
        Assert.True(await context.Messages.MarkSummaryPersistedAsync(persisted.Id, persistedResult, context.T1.AddHours(1)));

        var results = await context.Messages.GetPendingSummaryResultsAsync();

        var result = Assert.Single(results);
        Assert.Equal(pending.Id, result.MessageId);
        Assert.Equal(pendingResult, result.ResultId);
        Assert.Equal(context.ProjectA.Id, result.ProjectId);
    }

    [Fact]
    public async Task MarkSummaryPersisted_is_idempotent_for_same_message_and_result()
    {
        await using var context = await LeaderStorageContext.CreateAsync();
        var epoch = context.CreateEpoch(context.ProjectA.Id);
        await context.CreateCurrentEpochAsync(epoch);
        var resultId = Guid.NewGuid();
        var message = await context.Messages.AppendAsync(epoch.Id, "assistant", "answer", context.T0, metadata: Metadata(resultId, "answer"));

        Assert.True(await context.Messages.MarkSummaryPersistedAsync(message.Id, resultId, context.T1));
        Assert.True(await context.Messages.MarkSummaryPersistedAsync(message.Id, resultId, context.T1.AddDays(1)));

        var stored = Assert.Single(await context.Messages.GetAllAsync(epoch.Id));
        Assert.Equal(context.T1, stored.SummaryPersistedAt);
        Assert.Empty(await context.Messages.GetPendingSummaryResultsAsync());
    }

    [Fact]
    public async Task Existing_message_without_summary_metadata_remains_compatible()
    {
        await using var context = await LeaderStorageContext.CreateAsync();
        var epoch = context.CreateEpoch(context.ProjectA.Id);
        await context.CreateCurrentEpochAsync(epoch);

        var appended = await context.Messages.AppendAsync(epoch.Id, "assistant", "legacy", context.T0);
        var stored = Assert.Single(await context.Messages.GetAllAsync(epoch.Id));

        Assert.Null(appended.ResultId);
        Assert.Null(appended.SummaryDeltaPayloadJson);
        Assert.Null(appended.SummaryPersistedAt);
        var (id, epochId, sequence, role, text, createdAt) = appended;
        Assert.Equal(appended.Id, id);
        Assert.Equal(appended.EpochId, epochId);
        Assert.Equal(appended.Sequence, sequence);
        Assert.Equal(appended.Role, role);
        Assert.Equal(appended.Text, text);
        Assert.Equal(appended.CreatedAt, createdAt);
        Assert.Null(stored.ResultId);
        Assert.Null(stored.SummaryDeltaPayloadJson);
        Assert.Null(stored.SummaryPersistedAt);
        Assert.Empty(await context.Messages.GetPendingSummaryResultsAsync());
    }

    [Fact]
    public async Task Wrong_result_id_cannot_mark_other_pending_message()
    {
        await using var context = await LeaderStorageContext.CreateAsync();
        var epoch = context.CreateEpoch(context.ProjectA.Id);
        await context.CreateCurrentEpochAsync(epoch);
        var resultId = Guid.NewGuid();
        var message = await context.Messages.AppendAsync(epoch.Id, "assistant", "answer", context.T0, metadata: Metadata(resultId, "answer"));

        Assert.False(await context.Messages.MarkSummaryPersistedAsync(message.Id, Guid.NewGuid(), context.T1));

        var pending = Assert.Single(await context.Messages.GetPendingSummaryResultsAsync());
        Assert.Equal(resultId, pending.ResultId);
    }

    [Fact]
    public async Task Pending_summary_result_returns_authoritative_project_ownership()
    {
        await using var context = await LeaderStorageContext.CreateAsync();
        var epoch = context.CreateEpoch(context.ProjectB.Id);
        await context.CreateCurrentEpochAsync(epoch);
        var resultId = Guid.NewGuid();
        var message = await context.Messages.AppendAsync(epoch.Id, "assistant", "B answer", context.T0, metadata: Metadata(resultId, "B"));

        var pending = Assert.Single(await context.Messages.GetPendingSummaryResultsAsync());

        Assert.Equal(message.Id, pending.MessageId);
        Assert.Equal(context.ProjectB.Id, pending.ProjectId);
        Assert.Equal(epoch.Id, pending.EpochId);
    }

    [Fact]
    public async Task Metadata_is_rejected_for_non_assistant_messages()
    {
        await using var context = await LeaderStorageContext.CreateAsync();
        var epoch = context.CreateEpoch(context.ProjectA.Id);
        await context.CreateCurrentEpochAsync(epoch);

        await Assert.ThrowsAsync<ArgumentException>(() => context.Messages.AppendAsync(
            epoch.Id,
            "user",
            "user message",
            context.T0,
            metadata: Metadata(Guid.NewGuid(), "invalid")));
    }

    private static LeaderResultMetadata Metadata(Guid resultId, string marker) =>
        new(resultId, $"[{{\"occurred_at\":\"2026-01-01T00:00:00Z\",\"kind\":\"Change\",\"text\":\"{marker}\",\"source_refs\":[]}}]");
}
