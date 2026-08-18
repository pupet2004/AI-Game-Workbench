namespace Workbench.Storage.Tests.Memory;

using Workbench.Storage.Memory;

public sealed class ProjectSummaryRepositoryTests
{
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
}
