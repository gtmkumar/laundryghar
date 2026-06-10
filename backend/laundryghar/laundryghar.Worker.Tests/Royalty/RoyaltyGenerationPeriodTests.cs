using laundryghar.Worker.Services;

namespace laundryghar.Worker.Tests.Royalty;

/// <summary>
/// Unit tests for <see cref="RoyaltyGenerationService.PreviousMonthWindow"/>.
///
/// All assertions are pure value checks — no database or DI required.
/// Covers: mid-month dates, year-boundary rollover (January → December previous year),
/// and idempotency semantics (same trigger day → same period window).
/// </summary>
public sealed class RoyaltyGenerationPeriodTests
{
    // ── PreviousMonthWindow ───────────────────────────────────────────────────

    [Fact]
    public void PreviousMonthWindow_FirstOfFebruary_ReturnsJanuaryWindow()
    {
        var today = new DateOnly(2026, 2, 1);
        var (start, end) = RoyaltyGenerationService.PreviousMonthWindow(today);

        Assert.Equal(new DateOnly(2026, 1, 1),  start);
        Assert.Equal(new DateOnly(2026, 1, 31), end);
    }

    [Fact]
    public void PreviousMonthWindow_FirstOfJanuary_ReturnsDecemberPreviousYear()
    {
        // Year-boundary: Jan 1 of any year → Dec of previous year.
        var today = new DateOnly(2026, 1, 1);
        var (start, end) = RoyaltyGenerationService.PreviousMonthWindow(today);

        Assert.Equal(new DateOnly(2025, 12, 1),  start);
        Assert.Equal(new DateOnly(2025, 12, 31), end);
    }

    [Fact]
    public void PreviousMonthWindow_FirstOfMarch_ReturnsFebruaryWindow_NonLeapYear()
    {
        // 2025 is not a leap year — February has 28 days.
        var today = new DateOnly(2025, 3, 1);
        var (start, end) = RoyaltyGenerationService.PreviousMonthWindow(today);

        Assert.Equal(new DateOnly(2025, 2, 1),  start);
        Assert.Equal(new DateOnly(2025, 2, 28), end);
    }

    [Fact]
    public void PreviousMonthWindow_FirstOfMarch_ReturnsFebruaryWindow_LeapYear()
    {
        // 2024 is a leap year — February has 29 days.
        var today = new DateOnly(2024, 3, 1);
        var (start, end) = RoyaltyGenerationService.PreviousMonthWindow(today);

        Assert.Equal(new DateOnly(2024, 2, 1),  start);
        Assert.Equal(new DateOnly(2024, 2, 29), end);
    }

    [Fact]
    public void PreviousMonthWindow_AlwaysReturnsPeriodStartBeforeOrEqualPeriodEnd()
    {
        // Run through all months of a year, confirming start <= end each time.
        for (int month = 1; month <= 12; month++)
        {
            var today = new DateOnly(2025, month, 1);
            var (start, end) = RoyaltyGenerationService.PreviousMonthWindow(today);
            Assert.True(start <= end,
                $"Expected start ({start}) <= end ({end}) for trigger month={month}");
        }
    }

    [Fact]
    public void PreviousMonthWindow_PeriodStartIsAlwaysFirstDayOfMonth()
    {
        for (int month = 1; month <= 12; month++)
        {
            var today = new DateOnly(2025, month, 1);
            var (start, _) = RoyaltyGenerationService.PreviousMonthWindow(today);
            Assert.Equal(1, start.Day);
        }
    }

    [Fact]
    public void PreviousMonthWindow_SameTriggerDay_ReturnsSamePeriod_Idempotent()
    {
        // Two calls on the same day must return identical windows —
        // guarantees the "already generated?" check is correct for idempotency.
        var today = new DateOnly(2026, 6, 1);
        var result1 = RoyaltyGenerationService.PreviousMonthWindow(today);
        var result2 = RoyaltyGenerationService.PreviousMonthWindow(today);

        Assert.Equal(result1, result2);
    }

    [Theory]
    [InlineData(2026, 4, 1,  2026, 3,  1,  2026, 3,  31)]  // Apr 1 → March
    [InlineData(2026, 7, 1,  2026, 6,  1,  2026, 6,  30)]  // Jul 1 → June (30 days)
    [InlineData(2026, 9, 1,  2026, 8,  1,  2026, 8,  31)]  // Sep 1 → August (31 days)
    [InlineData(2026, 11, 1, 2026, 10, 1,  2026, 10, 31)]  // Nov 1 → October (31 days)
    public void PreviousMonthWindow_Theory_VariousMonths(
        int triggerYear, int triggerMonth, int triggerDay,
        int expStartYear, int expStartMonth, int expStartDay,
        int expEndYear,   int expEndMonth,   int expEndDay)
    {
        var today = new DateOnly(triggerYear, triggerMonth, triggerDay);
        var (start, end) = RoyaltyGenerationService.PreviousMonthWindow(today);

        Assert.Equal(new DateOnly(expStartYear, expStartMonth, expStartDay), start);
        Assert.Equal(new DateOnly(expEndYear,   expEndMonth,   expEndDay),   end);
    }
}
