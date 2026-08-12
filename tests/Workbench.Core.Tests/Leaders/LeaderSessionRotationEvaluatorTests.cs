using Workbench.Core.Leaders;

namespace Workbench.Core.Tests.Leaders;

public sealed class LeaderSessionRotationEvaluatorTests
{
    private static readonly TimeZoneInfo TestZone = TimeZoneInfo.CreateCustomTimeZone("Test/+02", TimeSpan.FromHours(2), "Test/+02", "Test/+02");

    [Fact]
    public void Same_day_12_hours_is_not_due()
    {
        var evaluation = Evaluate("2026-08-12T08:00:00+02:00", "2026-08-12T20:00:00+02:00");

        Assert.False(evaluation.IsDue);
        Assert.Null(evaluation.Reason);
    }

    [Theory]
    [InlineData("2026-08-12T23:50:00+02:00", "2026-08-13T00:20:00+02:00")]
    [InlineData("2026-08-12T18:01:00+02:00", "2026-08-13T00:00:00+02:00")]
    public void Next_day_less_than_6_hours_is_not_due(string lastActive, string now)
    {
        var evaluation = Evaluate(lastActive, now);

        Assert.False(evaluation.IsDue);
        Assert.Null(evaluation.Reason);
    }

    [Theory]
    [InlineData("2026-08-12T18:00:00+02:00", "2026-08-13T00:00:00+02:00")]
    [InlineData("2026-08-12T22:00:00+02:00", "2026-08-13T09:00:00+02:00")]
    [InlineData("2026-08-11T08:00:00+02:00", "2026-08-13T09:00:00+02:00")]
    public void Different_day_at_least_6_hours_is_due(string lastActive, string now)
    {
        var evaluation = Evaluate(lastActive, now);

        Assert.True(evaluation.IsDue);
        Assert.Equal(LeaderSessionRotationReason.WorkdayBoundary, evaluation.Reason);
        Assert.Equal(LeaderSessionRotationPolicy.Auto, evaluation.EffectivePolicy);
    }

    [Fact]
    public void No_current_epoch_is_not_due()
    {
        var evaluation = LeaderSessionRotationEvaluator.Evaluate(
            LeaderSessionRotationPolicy.Auto,
            null,
            null,
            DateTimeOffset.Parse("2026-08-13T09:00:00+02:00"),
            TestZone);

        Assert.False(evaluation.IsDue);
    }

    [Fact]
    public void Ended_epoch_is_not_due()
    {
        var evaluation = LeaderSessionRotationEvaluator.Evaluate(
            LeaderSessionRotationPolicy.Ask,
            DateTimeOffset.Parse("2026-08-12T22:00:00+02:00"),
            DateTimeOffset.Parse("2026-08-13T08:00:00+02:00"),
            DateTimeOffset.Parse("2026-08-13T09:00:00+02:00"),
            TestZone);

        Assert.False(evaluation.IsDue);
    }

    [Fact]
    public void Evaluation_does_not_modify_input_values()
    {
        var lastActive = DateTimeOffset.Parse("2026-08-12T22:00:00+02:00");
        var now = DateTimeOffset.Parse("2026-08-13T09:00:00+02:00");

        _ = LeaderSessionRotationEvaluator.Evaluate(LeaderSessionRotationPolicy.ManualOnly, lastActive, null, now, TestZone);

        Assert.Equal(DateTimeOffset.Parse("2026-08-12T22:00:00+02:00"), lastActive);
        Assert.Equal(DateTimeOffset.Parse("2026-08-13T09:00:00+02:00"), now);
    }

    [Fact]
    public void Utc_date_change_without_local_date_change_is_not_due()
    {
        var evaluation = LeaderSessionRotationEvaluator.Evaluate(
            LeaderSessionRotationPolicy.Auto,
            DateTimeOffset.Parse("2026-08-12T22:30:00+00:00"),
            null,
            DateTimeOffset.Parse("2026-08-13T04:30:00+00:00"),
            TimeZoneInfo.CreateCustomTimeZone("Test/-05", TimeSpan.FromHours(-5), "Test/-05", "Test/-05"));

        Assert.False(evaluation.IsDue);
    }

    [Fact]
    public void Local_date_change_is_due_even_when_utc_date_is_unchanged()
    {
        var evaluation = LeaderSessionRotationEvaluator.Evaluate(
            LeaderSessionRotationPolicy.Auto,
            DateTimeOffset.Parse("2026-08-12T16:00:00+00:00"),
            null,
            DateTimeOffset.Parse("2026-08-12T22:00:00+00:00"),
            TimeZoneInfo.CreateCustomTimeZone("Test/+03", TimeSpan.FromHours(3), "Test/+03", "Test/+03"));

        Assert.True(evaluation.IsDue);
    }

    private static LeaderSessionRotationEvaluation Evaluate(string lastActive, string now) =>
        LeaderSessionRotationEvaluator.Evaluate(
            LeaderSessionRotationPolicy.Auto,
            DateTimeOffset.Parse(lastActive),
            null,
            DateTimeOffset.Parse(now),
            TestZone);
}
