namespace Workbench.Core.Leaders;

public static class LeaderSessionRotationEvaluator
{
    public static readonly TimeSpan WorkdayIdleThreshold = TimeSpan.FromHours(6);

    public static LeaderSessionRotationEvaluation Evaluate(
        LeaderSessionRotationPolicy policy,
        DateTimeOffset? lastActiveAt,
        DateTimeOffset? endedAt,
        DateTimeOffset now,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        if (lastActiveAt is null || endedAt is not null)
        {
            return new LeaderSessionRotationEvaluation(policy, false, null);
        }

        var localLastActive = TimeZoneInfo.ConvertTime(lastActiveAt.Value, timeZone);
        var localNow = TimeZoneInfo.ConvertTime(now, timeZone);
        var isDue = localNow.Date != localLastActive.Date && now - lastActiveAt.Value >= WorkdayIdleThreshold;
        return new LeaderSessionRotationEvaluation(
            policy,
            isDue,
            isDue ? LeaderSessionRotationReason.WorkdayBoundary : null);
    }
}
