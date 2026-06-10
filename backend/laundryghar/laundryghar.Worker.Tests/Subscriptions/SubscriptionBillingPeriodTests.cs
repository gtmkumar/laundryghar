using laundryghar.SharedDataModel.Entities.Commerce.Subscriptions;
using laundryghar.Worker.Services;

namespace laundryghar.Worker.Tests.Subscriptions;

/// <summary>
/// Unit tests for <see cref="SubscriptionBillingService.ComputeNextPeriod"/>.
///
/// All assertions are pure value checks — no database or DI required.
/// Covers: every billing_interval value, intervalCount > 1, and edge cases
/// (month boundaries, leap-year handling via AddMonths semantics).
/// </summary>
public sealed class SubscriptionBillingPeriodTests
{
    private static readonly DateTimeOffset Anchor = new(2026, 1, 31, 0, 0, 0, TimeSpan.Zero);

    // ── Helper: build a minimal CustomerSubscription ────────────────────────

    private static CustomerSubscription MakeSub(
        string         billingInterval,
        short          intervalCount   = 1,
        DateTimeOffset? periodEnd      = null)
        => new()
        {
            Id                 = Guid.NewGuid(),
            BrandId            = Guid.NewGuid(),
            CustomerId         = Guid.NewGuid(),
            PlanId             = Guid.NewGuid(),
            SubscriptionNumber = "SUB-TEST",
            PriceSnapshot      = 999m,
            BillingInterval    = billingInterval,
            IntervalCount      = intervalCount,
            QuotaType          = "unlimited",
            CurrencyCode       = "INR",
            Status             = "active",
            AutoRenew          = true,
            CreditsRemaining   = 0,
            DunningAttempts    = 0,
            FailedPaymentCount = 0,
            TotalCyclesBilled  = 0,
            Metadata           = "{}",
            Version            = 1,
            CurrentPeriodEnd   = periodEnd ?? Anchor
        };

    // ── Interval: weekly ─────────────────────────────────────────────────────

    [Fact]
    public void ComputeNextPeriod_Weekly_AdvancesBy7Days()
    {
        var sub = MakeSub("weekly", intervalCount: 1,
                          periodEnd: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));

        var (start, end) = SubscriptionBillingService.ComputeNextPeriod(sub, Anchor);

        Assert.Equal(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),  start);
        Assert.Equal(new DateTimeOffset(2026, 6, 8, 0, 0, 0, TimeSpan.Zero),  end);
    }

    [Fact]
    public void ComputeNextPeriod_BiWeekly_AdvancesBy14Days()
    {
        var sub = MakeSub("weekly", intervalCount: 2,
                          periodEnd: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));

        var (start, end) = SubscriptionBillingService.ComputeNextPeriod(sub, Anchor);

        Assert.Equal(new DateTimeOffset(2026, 6, 1,  0, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero), end);
    }

    // ── Interval: monthly ────────────────────────────────────────────────────

    [Fact]
    public void ComputeNextPeriod_Monthly_AdvancesOneCalendarMonth()
    {
        var sub = MakeSub("monthly", intervalCount: 1,
                          periodEnd: new DateTimeOffset(2026, 5, 15, 0, 0, 0, TimeSpan.Zero));

        var (start, end) = SubscriptionBillingService.ComputeNextPeriod(sub, Anchor);

        Assert.Equal(new DateTimeOffset(2026, 5, 15, 0, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero), end);
    }

    [Fact]
    public void ComputeNextPeriod_Monthly_JanEndOfMonth_ClampsToEndOfFeb()
    {
        // Jan 31 + 1 month = Feb 28 (2026 is not a leap year — .NET AddMonths clamps to last day)
        var sub = MakeSub("monthly", intervalCount: 1,
                          periodEnd: new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero));

        var (start, end) = SubscriptionBillingService.ComputeNextPeriod(sub, Anchor);

        Assert.Equal(new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(new DateTimeOffset(2026, 2, 28, 0, 0, 0, TimeSpan.Zero), end);
    }

    [Fact]
    public void ComputeNextPeriod_Monthly_LeapYear_JanEndOfMonth_ClampsToFeb29()
    {
        // Jan 31 2024 + 1 month = Feb 29 2024 (2024 is a leap year)
        var sub = MakeSub("monthly", intervalCount: 1,
                          periodEnd: new DateTimeOffset(2024, 1, 31, 0, 0, 0, TimeSpan.Zero));

        var (start, end) = SubscriptionBillingService.ComputeNextPeriod(sub, Anchor);

        Assert.Equal(new DateTimeOffset(2024, 1, 31, 0, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(new DateTimeOffset(2024, 2, 29, 0, 0, 0, TimeSpan.Zero), end);
    }

    [Fact]
    public void ComputeNextPeriod_Bimonthly_AdvancesTwoCalendarMonths()
    {
        var sub = MakeSub("monthly", intervalCount: 2,
                          periodEnd: new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero));

        var (start, end) = SubscriptionBillingService.ComputeNextPeriod(sub, Anchor);

        Assert.Equal(new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(new DateTimeOffset(2026, 5, 10, 0, 0, 0, TimeSpan.Zero), end);
    }

    // ── Interval: quarterly ──────────────────────────────────────────────────

    [Fact]
    public void ComputeNextPeriod_Quarterly_AdvancesThreeCalendarMonths()
    {
        var sub = MakeSub("quarterly", intervalCount: 1,
                          periodEnd: new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero));

        var (start, end) = SubscriptionBillingService.ComputeNextPeriod(sub, Anchor);

        Assert.Equal(new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(new DateTimeOffset(2026, 4, 15, 0, 0, 0, TimeSpan.Zero), end);
    }

    // ── Interval: half_yearly ────────────────────────────────────────────────

    [Fact]
    public void ComputeNextPeriod_HalfYearly_AdvancesSixCalendarMonths()
    {
        var sub = MakeSub("half_yearly", intervalCount: 1,
                          periodEnd: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var (start, end) = SubscriptionBillingService.ComputeNextPeriod(sub, Anchor);

        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero), end);
    }

    // ── Interval: yearly ─────────────────────────────────────────────────────

    [Fact]
    public void ComputeNextPeriod_Yearly_AdvancesOneYear()
    {
        var sub = MakeSub("yearly", intervalCount: 1,
                          periodEnd: new DateTimeOffset(2026, 6, 10, 0, 0, 0, TimeSpan.Zero));

        var (start, end) = SubscriptionBillingService.ComputeNextPeriod(sub, Anchor);

        Assert.Equal(new DateTimeOffset(2026, 6, 10, 0, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(new DateTimeOffset(2027, 6, 10, 0, 0, 0, TimeSpan.Zero), end);
    }

    [Fact]
    public void ComputeNextPeriod_Yearly_IntervalCount2_AdvancesTwoYears()
    {
        var sub = MakeSub("yearly", intervalCount: 2,
                          periodEnd: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var (start, end) = SubscriptionBillingService.ComputeNextPeriod(sub, Anchor);

        Assert.Equal(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), end);
    }

    // ── Unknown interval — falls back to monthly ─────────────────────────────

    [Fact]
    public void ComputeNextPeriod_UnknownInterval_FallsBackToMonthly()
    {
        var sub = MakeSub("bogus_interval", intervalCount: 1,
                          periodEnd: new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero));

        var (start, end) = SubscriptionBillingService.ComputeNextPeriod(sub, Anchor);

        Assert.Equal(new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero), end);
    }

    // ── Period invariants (Start < End for all intervals) ─────────────────────

    [Theory]
    [InlineData("weekly")]
    [InlineData("monthly")]
    [InlineData("quarterly")]
    [InlineData("half_yearly")]
    [InlineData("yearly")]
    public void ComputeNextPeriod_AllIntervals_StartIsBeforeEnd(string interval)
    {
        var sub = MakeSub(interval, intervalCount: 1,
                          periodEnd: Anchor);

        var (start, end) = SubscriptionBillingService.ComputeNextPeriod(sub, Anchor);

        Assert.True(start < end,
            $"Expected start ({start:O}) < end ({end:O}) for interval={interval}");
    }

    [Theory]
    [InlineData("weekly")]
    [InlineData("monthly")]
    [InlineData("quarterly")]
    [InlineData("half_yearly")]
    [InlineData("yearly")]
    public void ComputeNextPeriod_AllIntervals_NewStartEqualsOldEnd(string interval)
    {
        // The new period starts exactly where the old one ended — no gaps, no overlaps.
        var periodEnd = new DateTimeOffset(2026, 3, 31, 23, 59, 59, TimeSpan.Zero);
        var sub = MakeSub(interval, intervalCount: 1, periodEnd: periodEnd);

        var (start, _) = SubscriptionBillingService.ComputeNextPeriod(sub, Anchor);

        Assert.Equal(periodEnd, start);
    }

    // ── IntervalCount = 0 is treated as 1 ────────────────────────────────────

    [Fact]
    public void ComputeNextPeriod_IntervalCountZero_TreatedAsOne()
    {
        // Guard: if someone stores 0, the service must not produce a zero-length period.
        var sub = MakeSub("monthly", intervalCount: 0,
                          periodEnd: new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));

        var (start, end) = SubscriptionBillingService.ComputeNextPeriod(sub, Anchor);

        // Start == old period end, end == start + 1 month (intervalCount clamped to 1)
        Assert.Equal(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), end);
    }
}
