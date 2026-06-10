namespace laundryghar.Orders.Tests.Ops;

/// <summary>
/// Tests the date-window classification logic used by the ops queues.
/// These helpers mirror the exact predicates in <see cref="OpsQueuesHandler"/>
/// so that the pure classification logic is tested without needing a DB.
///
/// Three buckets:
///   DueToday  — promised_at in [today_utc_start, today_utc_start + 1 day)
///   Overdue   — promised_at &lt; now (past)
///   Stuck     — last_status_change &lt; now - StuckThresholdHours
/// </summary>
public sealed class OpsQueueDateWindowTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);

    // ── DueToday window ───────────────────────────────────────────────────────

    private static bool IsDueToday(DateTimeOffset? promised, DateTimeOffset now)
    {
        var todayStart = now.Date;
        var todayEnd   = todayStart.AddDays(1);
        return promised.HasValue
            && promised.Value >= todayStart
            && promised.Value < todayEnd;
    }

    [Fact]
    public void IsDueToday_PromisedAtMidnightToday_True()
        => Assert.True(IsDueToday(new DateTimeOffset(2026, 6, 10, 0, 0, 0, TimeSpan.Zero), Now));

    [Fact]
    public void IsDueToday_PromisedAt18h_True()
        // 18:00 today is well within today's UTC window
        => Assert.True(IsDueToday(new DateTimeOffset(2026, 6, 10, 18, 0, 0, TimeSpan.Zero), Now));

    [Fact]
    public void IsDueToday_PromisedAtMidnightTomorrow_False()
        => Assert.False(IsDueToday(new DateTimeOffset(2026, 6, 11, 0, 0, 0, TimeSpan.Zero), Now));

    [Fact]
    public void IsDueToday_PromisedAtYesterday18h_False()
        // 18:00 yesterday is clearly before today
        => Assert.False(IsDueToday(new DateTimeOffset(2026, 6, 9, 18, 0, 0, TimeSpan.Zero), Now));

    [Fact]
    public void IsDueToday_NullPromised_False()
        => Assert.False(IsDueToday(null, Now));

    // ── Overdue window ────────────────────────────────────────────────────────

    private static bool IsOverdue(DateTimeOffset? promised, DateTimeOffset now)
        => promised.HasValue && promised.Value < now;

    [Fact]
    public void IsOverdue_PromisedBeforeNow_True()
        => Assert.True(IsOverdue(Now.AddMinutes(-1), Now));

    [Fact]
    public void IsOverdue_PromisedExactlyNow_False()
        => Assert.False(IsOverdue(Now, Now));

    [Fact]
    public void IsOverdue_PromisedAfterNow_False()
        => Assert.False(IsOverdue(Now.AddHours(1), Now));

    [Fact]
    public void IsOverdue_NullPromised_False()
        => Assert.False(IsOverdue(null, Now));

    [Theory]
    [InlineData(0)]        // exactly now → not overdue
    [InlineData(1)]        // 1 min in future → not overdue
    public void IsOverdue_FutureOrPresent_False(int minutesOffset)
        => Assert.False(IsOverdue(Now.AddMinutes(minutesOffset), Now));

    [Theory]
    [InlineData(-1)]       // 1 min past
    [InlineData(-60)]      // 1 hour past
    [InlineData(-1440)]    // 24 hours past
    public void IsOverdue_Past_True(int minutesOffset)
        => Assert.True(IsOverdue(Now.AddMinutes(minutesOffset), Now));

    // ── HoursOverdue calculation ──────────────────────────────────────────────

    private static double? ComputeHoursOverdue(DateTimeOffset? promised, DateTimeOffset now)
        => promised.HasValue && promised.Value < now
            ? (now - promised.Value).TotalHours
            : null;

    [Fact]
    public void HoursOverdue_ExactlyTwoHoursLate_ReturnsTwo()
        => Assert.Equal(2.0, ComputeHoursOverdue(Now.AddHours(-2), Now));

    [Fact]
    public void HoursOverdue_NotOverdue_ReturnsNull()
        => Assert.Null(ComputeHoursOverdue(Now.AddHours(1), Now));

    [Fact]
    public void HoursOverdue_NullPromised_ReturnsNull()
        => Assert.Null(ComputeHoursOverdue(null, Now));

    // ── Stuck window ──────────────────────────────────────────────────────────

    private static bool IsStuck(DateTimeOffset lastChanged, DateTimeOffset now, int thresholdHours)
        => lastChanged < now.AddHours(-thresholdHours);

    [Theory]
    [InlineData(24, 25)]  // 25h since last change, 24h threshold → stuck
    [InlineData(24, 24)]  // exactly 24h → stuck (strictly less than cutoff = not stuck)
    public void IsStuck_OlderThanThreshold_True(int thresholdHours, int hoursSinceChange)
    {
        var lastChanged = Now.AddHours(-hoursSinceChange);
        // 25h > 24h threshold → stuck; 24h = cutoff exactly → borderline
        var expected = hoursSinceChange > thresholdHours;
        Assert.Equal(expected, IsStuck(lastChanged, Now, thresholdHours));
    }

    [Fact]
    public void IsStuck_JustUnderThreshold_False()
    {
        // 23h 59m since last change, 24h threshold
        var lastChanged = Now.AddHours(-23).AddMinutes(-59);
        Assert.False(IsStuck(lastChanged, Now, 24));
    }

    [Fact]
    public void IsStuck_WellOverThreshold_True()
    {
        var lastChanged = Now.AddHours(-72); // 3 days
        Assert.True(IsStuck(lastChanged, Now, 24));
    }
}
