using Workbench.App.Leader;
using Workbench.App.Tests.Support;
using Workbench.Core.Projects;
using Workbench.Storage.Database;
using Workbench.Storage.Leaders;
using Workbench.Storage.Memory;
using Workbench.Storage.Projects;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.Tests;

public sealed class LeaderSummaryRecoveryTests
{
    [Fact]
    public async Task Crash_after_metadata_before_append_recovers_without_runtime_send()
    {
        await using var context = await RecoveryContext.CreateAsync();
        var pending = await context.SeedPendingAsync(context.ProjectA, Guid.NewGuid(), RecoveryContext.ValidPayload);

        var report = await context.Service.RecoverAsync();

        var summary = Assert.Single(await context.ReadSummariesAsync(context.ProjectA.Id));
        Assert.Equal(pending.ResultId, summary.ResultId);
        Assert.Equal(0, summary.DeltaOrdinal);
        Assert.Equal("Durable decision.", summary.Text);
        Assert.Empty(context.Runtime.SentRequests);
        Assert.Equal(1, report.RecoveredCount);
        Assert.Empty(report.Errors);
    }

    [Fact]
    public async Task Crash_after_append_before_mark_replays_without_duplicate()
    {
        await using var context = await RecoveryContext.CreateAsync();
        var resultId = Guid.NewGuid();
        var pending = await context.SeedPendingAsync(context.ProjectA, resultId, RecoveryContext.ValidPayload);
        await context.Summaries.AppendAsync(
            context.ProjectA.Id,
            resultId,
            [RecoveryContext.ValidDelta],
            pending.CreatedAt);

        var report = await context.Service.RecoverAsync();

        var summaries = await context.ReadSummariesAsync(context.ProjectA.Id);
        Assert.Single(summaries);
        Assert.Single(summaries[0].SourceRefs);
        Assert.Empty(context.Runtime.SentRequests);
        Assert.Equal(1, report.RecoveredCount);
        Assert.Empty(report.Errors);
    }

    [Fact]
    public async Task Recovery_marks_successfully_replayed_message()
    {
        await using var context = await RecoveryContext.CreateAsync();
        var pending = await context.SeedPendingAsync(context.ProjectA, Guid.NewGuid(), RecoveryContext.ValidPayload);

        await context.Service.RecoverAsync();

        Assert.Empty(await context.Messages.GetPendingSummaryResultsAsync());
        var epochMessages = await context.Messages.GetAllAsync(pending.EpochId);
        Assert.NotNull(Assert.Single(epochMessages).SummaryPersistedAt);
    }

    [Fact]
    public async Task Recovery_uses_authoritative_project_from_pending_row()
    {
        await using var context = await RecoveryContext.CreateAsync();
        var pending = await context.SeedPendingAsync(context.ProjectB, Guid.NewGuid(), RecoveryContext.ValidPayload);

        await context.Service.RecoverAsync();

        Assert.Empty(await context.ReadSummariesAsync(context.ProjectA.Id));
        var summary = Assert.Single(await context.ReadSummariesAsync(context.ProjectB.Id));
        Assert.Equal(context.ProjectB.Id, summary.ProjectId);
        Assert.Equal(pending.ResultId, summary.ResultId);
    }

    [Fact]
    public async Task Recovery_deserializes_summary_delta_array_without_full_response_parser()
    {
        await using var context = await RecoveryContext.CreateAsync();
        await context.SeedPendingAsync(context.ProjectA, Guid.NewGuid(), RecoveryContext.ValidPayload);

        var report = await context.Service.RecoverAsync();

        Assert.Equal(1, report.RecoveredCount);
        Assert.Empty(report.Errors);
        Assert.Equal("LeaderMessage", Assert.Single(await context.ReadSummariesAsync(context.ProjectA.Id)).SourceRefs[0].SourceKind);
    }

    [Fact]
    public async Task Invalid_durable_summary_payload_stays_pending_and_reports_error()
    {
        await using var context = await RecoveryContext.CreateAsync();
        var pending = await context.SeedPendingAsync(
            context.ProjectA,
            Guid.NewGuid(),
            "[{\"occurred_at\":\"2026-08-20T10:15:30+00:00\",\"kind\":\"Decision\",\"text\":\"\",\"source_refs\":[]}]");

        var report = await context.Service.RecoverAsync();

        Assert.Empty(await context.ReadSummariesAsync(context.ProjectA.Id));
        Assert.Equal(pending.MessageId, Assert.Single(await context.Messages.GetPendingSummaryResultsAsync()).MessageId);
        var error = Assert.Single(report.Errors);
        Assert.Equal(pending.MessageId, error.MessageId);
        Assert.Equal(pending.ResultId, error.ResultId);
        Assert.NotEmpty(error.Message);
    }

    [Theory]
    [InlineData("2026-08-20T10:15:30", "Decision")]
    [InlineData("2026-08-20T10:15:30+00:00", "decision")]
    [InlineData("2026-08-20T10:15:30+00:00", "0")]
    [InlineData("2026-08-20T10:15:30+00:00", " Decision ")]
    public async Task Recovery_rejects_payload_outside_strict_summary_contract(
        string occurredAt,
        string kind)
    {
        await using var context = await RecoveryContext.CreateAsync();
        var payload = $"[{{\"occurred_at\":\"{occurredAt}\",\"kind\":\"{kind}\",\"text\":\"Invalid contract.\",\"source_refs\":[]}}]";
        var pending = await context.SeedPendingAsync(context.ProjectA, Guid.NewGuid(), payload);

        var report = await context.Service.RecoverAsync();

        Assert.Empty(await context.ReadSummariesAsync(context.ProjectA.Id));
        Assert.Equal(pending.MessageId, Assert.Single(await context.Messages.GetPendingSummaryResultsAsync()).MessageId);
        Assert.Equal(pending.MessageId, Assert.Single(report.Errors).MessageId);
    }

    [Theory]
    [InlineData("[{\"occurred_at\":\"2026-08-20T10:15:30+00:00\",\"text\":\"Missing kind.\",\"source_refs\":[]}]")]
    [InlineData("[{\"occurred_at\":\"2026-08-20T10:15:30+00:00\",\"kind\":0,\"text\":\"Numeric kind.\",\"source_refs\":[]}]")]
    public async Task Recovery_rejects_missing_or_non_string_contract_fields(string payload)
    {
        await using var context = await RecoveryContext.CreateAsync();
        var pending = await context.SeedPendingAsync(context.ProjectA, Guid.NewGuid(), payload);

        var report = await context.Service.RecoverAsync();

        Assert.Empty(await context.ReadSummariesAsync(context.ProjectA.Id));
        Assert.Equal(pending.MessageId, Assert.Single(await context.Messages.GetPendingSummaryResultsAsync()).MessageId);
        Assert.Equal(pending.MessageId, Assert.Single(report.Errors).MessageId);
    }

    [Fact]
    public async Task Invalid_pending_row_does_not_block_later_valid_row()
    {
        await using var context = await RecoveryContext.CreateAsync();
        var invalid = await context.SeedPendingAsync(context.ProjectA, Guid.NewGuid(), "not-json");
        var valid = await context.SeedPendingAsync(context.ProjectB, Guid.NewGuid(), RecoveryContext.ValidPayload);

        var report = await context.Service.RecoverAsync();

        Assert.Equal(1, report.RecoveredCount);
        Assert.Equal(invalid.MessageId, Assert.Single(report.Errors).MessageId);
        Assert.Equal(invalid.MessageId, Assert.Single(await context.Messages.GetPendingSummaryResultsAsync()).MessageId);
        Assert.Equal(valid.ResultId, Assert.Single(await context.ReadSummariesAsync(context.ProjectB.Id)).ResultId);
    }

    [Fact]
    public async Task Recovery_with_no_pending_rows_is_noop()
    {
        await using var context = await RecoveryContext.CreateAsync();

        var report = await context.Service.RecoverAsync();

        Assert.Equal(0, report.RecoveredCount);
        Assert.Empty(report.Errors);
        Assert.Empty(await context.ReadSummariesAsync(context.ProjectA.Id));
    }

    [Fact]
    public async Task Recovery_does_not_call_runtime()
    {
        await using var context = await RecoveryContext.CreateAsync();
        await context.SeedPendingAsync(context.ProjectA, Guid.NewGuid(), RecoveryContext.ValidPayload);

        await context.Service.RecoverAsync();

        Assert.Empty(context.Runtime.SentRequests);
        Assert.Empty(context.Runtime.CreatedSessions);
        Assert.Empty(context.Runtime.ResumedSessions);
    }

    [Fact]
    public async Task Recovery_does_not_query_summary_to_deduplicate_by_text()
    {
        await using var context = await RecoveryContext.CreateAsync();
        await context.Summaries.AppendAsync(
            context.ProjectA.Id,
            Guid.NewGuid(),
            [RecoveryContext.ValidDelta],
            context.Time.GetUtcNow());
        var pending = await context.SeedPendingAsync(context.ProjectA, Guid.NewGuid(), RecoveryContext.ValidPayload);

        await context.Service.RecoverAsync();

        var summaries = await context.ReadSummariesAsync(context.ProjectA.Id);
        Assert.Equal(2, summaries.Count);
        Assert.Contains(summaries, summary => summary.ResultId == pending.ResultId);
    }
}

internal sealed class RecoveryContext : IAsyncDisposable
{
    private readonly TemporaryDirectory _directory;
    private readonly WorkbenchDatabase _database;

    private RecoveryContext(
        TemporaryDirectory directory,
        WorkbenchDatabase database,
        CoreProject projectA,
        CoreProject projectB,
        MutableTimeProvider time)
    {
        _directory = directory;
        _database = database;
        ProjectA = projectA;
        ProjectB = projectB;
        Time = time;
        Leaders = new ProjectLeaderRepository(database);
        Messages = new LeaderMessageRepository(database);
        Summaries = new ProjectSummaryRepository(database);
        Runtime = new FakeAgentRuntime();
        Service = new LeaderSummaryRecoveryService(Messages, Summaries, time);
    }

    public const string ValidPayload = "[{\"occurred_at\":\"2026-08-20T10:15:30+00:00\",\"kind\":\"Decision\",\"text\":\"Durable decision.\",\"source_refs\":[{\"source_kind\":\"LeaderMessage\",\"source_locator\":\"message-42\"}]}]";

    public static SummaryDelta ValidDelta { get; } = new(
        DateTimeOffset.Parse("2026-08-20T10:15:30+00:00"),
        SummaryDeltaKind.Decision,
        "Durable decision.",
        [new SummarySourceRef("LeaderMessage", "message-42")]);

    public CoreProject ProjectA { get; }
    public CoreProject ProjectB { get; }
    public MutableTimeProvider Time { get; }
    public ProjectLeaderRepository Leaders { get; }
    public LeaderMessageRepository Messages { get; }
    public ProjectSummaryRepository Summaries { get; }
    public FakeAgentRuntime Runtime { get; }
    public LeaderSummaryRecoveryService Service { get; }

    public static async Task<RecoveryContext> CreateAsync()
    {
        var directory = new TemporaryDirectory("summary-recovery");
        var database = new WorkbenchDatabase(Path.Combine(directory.Path, "workbench.db"));
        await database.InitializeAsync();
        var now = DateTimeOffset.Parse("2026-08-22T10:00:00+00:00");
        var projectA = new CoreProject(Guid.NewGuid(), "A", "C:/Projects/A", ProjectType.Generic, null, now, now);
        var projectB = new CoreProject(Guid.NewGuid(), "B", "C:/Projects/B", ProjectType.Generic, null, now, now);
        var projects = new ProjectRepository(database);
        await projects.UpsertAsync(projectA);
        await projects.UpsertAsync(projectB);
        return new RecoveryContext(directory, database, projectA, projectB, new MutableTimeProvider(now));
    }

    public async Task<PendingLeaderSummaryResult> SeedPendingAsync(
        CoreProject project,
        Guid resultId,
        string payload)
    {
        var epochId = Guid.NewGuid();
        var now = Time.GetUtcNow();
        await Leaders.CreateCurrentEpochAsync(
            new StoredProjectLeader(project.Id, null, now, now),
            new StoredLeaderSessionEpoch(
                epochId,
                project.Id,
                "provider",
                Guid.NewGuid(),
                "model",
                Guid.NewGuid(),
                "external-session",
                project.RootPath,
                now,
                now,
                null,
                null,
                null));
        var message = await Messages.AppendAsync(
            epochId,
            "assistant",
            "Visible.",
            now,
            metadata: new LeaderResultMetadata(resultId, payload));
        return new PendingLeaderSummaryResult(
            message.Id,
            project.Id,
            epochId,
            resultId,
            payload,
            message.CreatedAt);
    }

    public Task<IReadOnlyList<StoredSummaryEntry>> ReadSummariesAsync(Guid projectId) =>
        Summaries.QueryAsync(new SummaryQuery(projectId, 200));

    public ValueTask DisposeAsync()
    {
        _directory.Dispose();
        return ValueTask.CompletedTask;
    }
}
