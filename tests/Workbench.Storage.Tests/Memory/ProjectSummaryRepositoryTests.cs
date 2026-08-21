namespace Workbench.Storage.Tests.Memory;

using Workbench.Core.Projects;
using Workbench.Storage.Database;
using Workbench.Storage.Memory;
using Workbench.Storage.Projects;
using Workbench.Storage.Tests.Database;

public sealed class ProjectSummaryRepositoryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 20, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SummaryQuery_rejects_empty_project_or_limit_outside_1_to_200()
    {
        Assert.Throws<ArgumentException>(() => new SummaryQuery(Guid.Empty, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SummaryQuery(Guid.NewGuid(), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SummaryQuery(Guid.NewGuid(), 201));
        Assert.Throws<ArgumentException>(() => new SummaryQuery(Guid.NewGuid(), 1, OccurredFrom: DateTimeOffset.UnixEpoch.AddDays(2), OccurredTo: DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void SummaryDelta_rejects_blank_text()
    {
        Assert.Throws<ArgumentException>(() => new SummaryDelta(DateTimeOffset.UtcNow, SummaryDeltaKind.Decision, " ", []));
    }

    [Fact]
    public void SummaryDelta_rejects_unknown_kind()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SummaryDelta(DateTimeOffset.UtcNow, (SummaryDeltaKind)999, "text", []));
    }

    [Fact]
    public void SummaryDelta_rejects_null_source_refs()
    {
        Assert.Throws<ArgumentNullException>(() => new SummaryDelta(DateTimeOffset.UtcNow, SummaryDeltaKind.Decision, "text", null!));
    }

    [Fact]
    public void SummarySourceRef_rejects_blank_kind_or_locator()
    {
        Assert.Throws<ArgumentException>(() => new SummarySourceRef(" ", "locator"));
        Assert.Throws<ArgumentException>(() => new SummarySourceRef("kind", " "));
    }

    [Fact]
    public void SummaryQuery_accepts_optional_filters()
    {
        var from = DateTimeOffset.UtcNow.AddDays(-1);
        var to = from.AddDays(1);
        var query = new SummaryQuery(
            Guid.NewGuid(),
            25,
            [SummaryDeltaKind.Decision, SummaryDeltaKind.Change],
            from,
            to,
            "LeaderMessage",
            "message-42");

        Assert.Equal(25, query.Limit);
        Assert.Equal([SummaryDeltaKind.Decision, SummaryDeltaKind.Change], query.Kinds);
        Assert.Equal(from, query.OccurredFrom);
        Assert.Equal(to, query.OccurredTo);
        Assert.Equal("LeaderMessage", query.SourceKind);
        Assert.Equal("message-42", query.SourceLocator);
    }

    [Fact]
    public async Task AppendAsync_persists_parent_and_source_refs_atomically()
    {
        await using var fixture = await SummaryStorageContext.CreateAsync();
        var resultId = Guid.NewGuid();
        var deltas = new[]
        {
            Delta(T0, SummaryDeltaKind.Decision, "Use SQLite", Ref("LeaderMessage", "message-1"), Ref("Task", "task-7")),
            Delta(T0.AddMinutes(1), SummaryDeltaKind.Constraint, "Remain project-local", Ref("LeaderMessage", "message-1"))
        };

        var stored = await fixture.Summaries.AppendAsync(fixture.ProjectA.Id, resultId, deltas, T0.AddHours(1));
        var queried = await fixture.Summaries.QueryAsync(new SummaryQuery(fixture.ProjectA.Id, 20));

        Assert.Equal(2, stored.Count);
        Assert.Equal([0, 1], stored.Select(entry => entry.DeltaOrdinal));
        Assert.Equal(deltas[0].SourceRefs, stored[0].SourceRefs);
        Assert.Equal(deltas[1].SourceRefs, stored[1].SourceRefs);
        Assert.Equal(2, queried.Count);
        Assert.All(queried, entry => Assert.Equal(resultId, entry.ResultId));
    }

    [Fact]
    public async Task AppendAsync_replays_same_result_and_ordinal_without_duplicate_rows()
    {
        await using var fixture = await SummaryStorageContext.CreateAsync();
        var resultId = Guid.NewGuid();
        var delta = Delta(T0, SummaryDeltaKind.Change, "Changed", Ref("LeaderMessage", "message-2"));

        var first = await fixture.Summaries.AppendAsync(fixture.ProjectA.Id, resultId, [delta], T0.AddHours(1));
        var replay = await fixture.Summaries.AppendAsync(fixture.ProjectA.Id, resultId, [delta], T0.AddHours(2));
        var queried = await fixture.Summaries.QueryAsync(new SummaryQuery(fixture.ProjectA.Id, 20));

        Assert.Single(queried);
        Assert.Equal(first[0].EntryId, replay[0].EntryId);
        Assert.Equal(first[0].ProjectId, replay[0].ProjectId);
        Assert.Equal(first[0].OccurredAt, replay[0].OccurredAt);
        Assert.Equal(first[0].Kind, replay[0].Kind);
        Assert.Equal(first[0].Text, replay[0].Text);
        Assert.Equal(first[0].ResultId, replay[0].ResultId);
        Assert.Equal(first[0].DeltaOrdinal, replay[0].DeltaOrdinal);
        Assert.Equal(first[0].SourceRefs.ToArray(), replay[0].SourceRefs.ToArray());
        Assert.Equal(T0.AddHours(1), replay[0].CreatedAt);
    }

    [Fact]
    public async Task AppendAsync_rejects_same_identity_with_changed_payload()
    {
        await using var fixture = await SummaryStorageContext.CreateAsync();
        var resultId = Guid.NewGuid();
        await fixture.Summaries.AppendAsync(
            fixture.ProjectA.Id,
            resultId,
            [Delta(T0, SummaryDeltaKind.Decision, "Original", Ref("Task", "task-1"))],
            T0.AddHours(1));

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Summaries.AppendAsync(
            fixture.ProjectA.Id,
            resultId,
            [Delta(T0, SummaryDeltaKind.Decision, "Changed", Ref("Task", "task-1"))],
            T0.AddHours(2)));
    }

    [Fact]
    public async Task AppendAsync_rejects_unknown_project()
    {
        await using var fixture = await SummaryStorageContext.CreateAsync();

        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Summaries.AppendAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            [Delta(T0, SummaryDeltaKind.Decision, "Orphan")],
            T0.AddHours(1)));
    }

    [Fact]
    public async Task QueryAsync_orders_by_occurred_created_entry()
    {
        await using var fixture = await SummaryStorageContext.CreateAsync();
        var older = await fixture.Summaries.AppendAsync(fixture.ProjectA.Id, Guid.NewGuid(), [Delta(T0, SummaryDeltaKind.Decision, "older")], T0);
        var sameOccurredOlderCreated = await fixture.Summaries.AppendAsync(fixture.ProjectA.Id, Guid.NewGuid(), [Delta(T0.AddHours(1), SummaryDeltaKind.Change, "same-1")], T0.AddMinutes(1));
        var sameOccurredNewerCreated = await fixture.Summaries.AppendAsync(fixture.ProjectA.Id, Guid.NewGuid(), [Delta(T0.AddHours(1), SummaryDeltaKind.Constraint, "same-2")], T0.AddMinutes(2));
        var tieA = await fixture.Summaries.AppendAsync(fixture.ProjectA.Id, Guid.NewGuid(), [Delta(T0.AddHours(2), SummaryDeltaKind.Unresolved, "tie-a")], T0.AddMinutes(3));
        var tieB = await fixture.Summaries.AppendAsync(fixture.ProjectA.Id, Guid.NewGuid(), [Delta(T0.AddHours(2), SummaryDeltaKind.Unresolved, "tie-b")], T0.AddMinutes(3));

        var queried = await fixture.Summaries.QueryAsync(new SummaryQuery(fixture.ProjectA.Id, 20));
        var expectedTies = new[] { tieA[0], tieB[0] }.OrderBy(entry => entry.EntryId.ToString(), StringComparer.Ordinal);
        var expected = expectedTies.Concat([sameOccurredNewerCreated[0], sameOccurredOlderCreated[0], older[0]]);

        Assert.Equal(expected.Select(entry => entry.EntryId), queried.Select(entry => entry.EntryId));
    }

    [Fact]
    public async Task QueryAsync_applies_kind_and_date_filters()
    {
        await using var fixture = await SummaryStorageContext.CreateAsync();
        await fixture.Summaries.AppendAsync(
            fixture.ProjectA.Id,
            Guid.NewGuid(),
            [
                Delta(T0, SummaryDeltaKind.Decision, "before"),
                Delta(T0.AddHours(1), SummaryDeltaKind.Change, "matching"),
                Delta(T0.AddHours(2), SummaryDeltaKind.Decision, "wrong-kind"),
                Delta(T0.AddHours(3), SummaryDeltaKind.Change, "after")
            ],
            T0.AddDays(1));

        var queried = await fixture.Summaries.QueryAsync(new SummaryQuery(
            fixture.ProjectA.Id,
            20,
            [SummaryDeltaKind.Change],
            T0.AddMinutes(30),
            T0.AddHours(2)));

        var entry = Assert.Single(queried);
        Assert.Equal("matching", entry.Text);
    }

    [Fact]
    public async Task QueryAsync_applies_source_filters_without_duplicate_parents()
    {
        await using var fixture = await SummaryStorageContext.CreateAsync();
        await fixture.Summaries.AppendAsync(
            fixture.ProjectA.Id,
            Guid.NewGuid(),
            [
                Delta(T0, SummaryDeltaKind.Decision, "matching", Ref("Task", "task-1"), Ref("Task", "task-1")),
                Delta(T0.AddMinutes(1), SummaryDeltaKind.Change, "other-locator", Ref("Task", "task-2")),
                Delta(T0.AddMinutes(2), SummaryDeltaKind.Change, "other-kind", Ref("LeaderMessage", "task-1"))
            ],
            T0.AddHours(1));

        var queried = await fixture.Summaries.QueryAsync(new SummaryQuery(
            fixture.ProjectA.Id,
            20,
            SourceKind: "Task",
            SourceLocator: "task-1"));

        var entry = Assert.Single(queried);
        Assert.Equal("matching", entry.Text);
        Assert.Equal(2, entry.SourceRefs.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public void QueryAsync_enforces_limit_1_to_200(int limit)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SummaryQuery(Guid.NewGuid(), limit));
    }

    [Fact]
    public async Task Summary_query_is_project_local()
    {
        await using var fixture = await SummaryStorageContext.CreateAsync();
        await fixture.Summaries.AppendAsync(fixture.ProjectA.Id, Guid.NewGuid(), [Delta(T0, SummaryDeltaKind.Decision, "A")], T0);
        await fixture.Summaries.AppendAsync(fixture.ProjectB.Id, Guid.NewGuid(), [Delta(T0, SummaryDeltaKind.Decision, "B")], T0);

        var queried = await fixture.Summaries.QueryAsync(new SummaryQuery(fixture.ProjectA.Id, 20));

        var entry = Assert.Single(queried);
        Assert.Equal(fixture.ProjectA.Id, entry.ProjectId);
        Assert.Equal("A", entry.Text);
    }

    private static SummaryDelta Delta(DateTimeOffset occurredAt, SummaryDeltaKind kind, string text, params SummarySourceRef[] refs) =>
        new(occurredAt, kind, text, refs);

    private static SummarySourceRef Ref(string kind, string locator) => new(kind, locator);
}

internal sealed class SummaryStorageContext : IAsyncDisposable
{
    private readonly TemporaryDatabase _temporary;

    private SummaryStorageContext(
        TemporaryDatabase temporary,
        ProjectSummaryRepository summaries,
        Project projectA,
        Project projectB)
    {
        _temporary = temporary;
        Summaries = summaries;
        ProjectA = projectA;
        ProjectB = projectB;
    }

    public ProjectSummaryRepository Summaries { get; }
    public Project ProjectA { get; }
    public Project ProjectB { get; }

    public static async Task<SummaryStorageContext> CreateAsync()
    {
        var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        var projects = new ProjectRepository(database);
        var projectA = new Project(Guid.NewGuid(), "Summary A", $"C:/Summary-A-{Guid.NewGuid():N}", ProjectType.Generic, null, T0, T0);
        var projectB = new Project(Guid.NewGuid(), "Summary B", $"C:/Summary-B-{Guid.NewGuid():N}", ProjectType.Generic, null, T0, T0);
        await projects.UpsertAsync(projectA);
        await projects.UpsertAsync(projectB);
        return new SummaryStorageContext(temporary, new ProjectSummaryRepository(database), projectA, projectB);
    }

    public ValueTask DisposeAsync() => _temporary.DisposeAsync();

    private static readonly DateTimeOffset T0 = new(2026, 8, 20, 8, 0, 0, TimeSpan.Zero);
}
