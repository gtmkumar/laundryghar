using laundryghar.Orders.Application.Common;

namespace laundryghar.Orders.Tests.Ops;

/// <summary>
/// Tests <see cref="LocalDateRange"/> — the helper that converts date-only list filter
/// bounds into UTC instants in the operator's local timezone.
///
/// Regression target (DEFECT C): an admin "today" filter of dateFrom=dateTo=2026-06-13
/// in IST must include an order placed at 2026-06-12T21:32Z (which is 2026-06-13 03:02 IST),
/// even though that instant falls on the previous UTC calendar day.
/// </summary>
public sealed class LocalDateRangeTests
{
    private static readonly TimeZoneInfo Ist = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

    // ── Resolve ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_KnownIanaId_ReturnsThatZone()
        => Assert.Equal(Ist, LocalDateRange.Resolve("Asia/Kolkata"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Not/A_Real_Zone")]
    public void Resolve_NullBlankOrUnknown_FallsBackToDefault(string? tzId)
        => Assert.Equal(Ist, LocalDateRange.Resolve(tzId));

    // ── Bound conversion (IST = UTC+05:30) ────────────────────────────────────────

    [Fact]
    public void StartUtc_IstMidnight_IsPreviousUtcEvening()
    {
        // 2026-06-13 00:00 IST == 2026-06-12 18:30 UTC
        var start = LocalDateRange.StartUtc(new DateOnly(2026, 6, 13), Ist);
        Assert.Equal(new DateTimeOffset(2026, 6, 12, 18, 30, 0, TimeSpan.Zero), start);
    }

    [Fact]
    public void EndUtcExclusive_IstNextMidnight_IsNextDayUtcEvening()
    {
        // exclusive upper bound = 2026-06-14 00:00 IST == 2026-06-13 18:30 UTC
        var end = LocalDateRange.EndUtcExclusive(new DateOnly(2026, 6, 13), Ist);
        Assert.Equal(new DateTimeOffset(2026, 6, 13, 18, 30, 0, TimeSpan.Zero), end);
    }

    // ── The defect's headline boundary case ───────────────────────────────────────

    [Fact]
    public void PreDawnIstOrder_FallsInsideSameIstDayFilter()
    {
        // Order placed 2026-06-12T21:32Z == 2026-06-13 03:02 IST.
        var placedAt = new DateTimeOffset(2026, 6, 12, 21, 32, 0, TimeSpan.Zero);

        // Filter: dateFrom = dateTo = 2026-06-13 (IST calendar day).
        var date  = new DateOnly(2026, 6, 13);
        var from  = LocalDateRange.StartUtc(date, Ist);
        var toEnd = LocalDateRange.EndUtcExclusive(date, Ist);

        // The order must satisfy the half-open window [from, toEnd).
        Assert.True(placedAt >= from,  "pre-dawn IST order should be on/after the local-day start");
        Assert.True(placedAt < toEnd,  "pre-dawn IST order should be before the next local-day start");
    }

    [Fact]
    public void LateNightUtcOnlyOrder_ExcludedFromPreviousIstDay()
    {
        // 2026-06-13T19:00Z == 2026-06-14 00:30 IST → belongs to 2026-06-14 in IST,
        // so it must NOT appear under a 2026-06-13 IST filter.
        var placedAt = new DateTimeOffset(2026, 6, 13, 19, 0, 0, TimeSpan.Zero);
        var date     = new DateOnly(2026, 6, 13);
        var toEnd    = LocalDateRange.EndUtcExclusive(date, Ist);

        Assert.False(placedAt < toEnd, "an order that is next-day in IST must be excluded");
    }

    [Fact]
    public void EndUtcExclusive_IsStartOfNextDay_NoGapNoOverlap()
    {
        // The exclusive end of day N must equal the inclusive start of day N+1, so a
        // multi-day or back-to-back range has neither a gap nor a double-count at midnight.
        var endOf13   = LocalDateRange.EndUtcExclusive(new DateOnly(2026, 6, 13), Ist);
        var startOf14 = LocalDateRange.StartUtc(new DateOnly(2026, 6, 14), Ist);
        Assert.Equal(startOf14, endOf13);
    }

    [Fact]
    public void UnscopedDefault_IsAsiaKolkata()
    {
        // When unscoped, the handler passes null and the helper must default to IST,
        // producing the same bounds as an explicit Asia/Kolkata resolution.
        var tz   = LocalDateRange.Resolve(null);
        var from = LocalDateRange.StartUtc(new DateOnly(2026, 6, 13), tz);
        Assert.Equal(new DateTimeOffset(2026, 6, 12, 18, 30, 0, TimeSpan.Zero), from);
    }
}
