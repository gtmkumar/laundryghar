namespace operations.Application.Logistics.RiderOps;

// ── Shared helpers ──────────────────────────────────────────────────────────────

public static class RiderOpsTime
{
    // Laundry Ghar operates in IST (UTC+5:30, no DST). "Today" on the ops board and
    // in productivity stats is the IST calendar day, matched to mv_rider_performance
    // which groups on DATE(assigned_at AT TIME ZONE 'Asia/Kolkata').
    public static readonly TimeSpan Ist = TimeSpan.FromHours(5.5);

    public static DateOnly TodayIst()
        => DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(Ist).Date);

    /// <summary>UTC half-open range [start, end) covering the given IST calendar days.</summary>
    public static (DateTimeOffset startUtc, DateTimeOffset endUtc) IstRangeUtc(DateOnly fromIst, DateOnly toIst)
    {
        var start = new DateTimeOffset(fromIst.ToDateTime(TimeOnly.MinValue), Ist).ToUniversalTime();
        var end   = new DateTimeOffset(toIst.AddDays(1).ToDateTime(TimeOnly.MinValue), Ist).ToUniversalTime();
        return (start, end);
    }
}
