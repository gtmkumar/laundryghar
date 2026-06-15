namespace operations.Application.Logistics.Common;

/// <summary>
/// Converts date-only calendar bounds (as a user sees them on a list filter) into the
/// UTC instants that bracket those local days, so timestamp columns stored in UTC are
/// filtered against the operator's wall-clock day rather than the UTC day.
///
/// Without this, a "today" filter of <c>dateFrom=dateTo=2026-06-13</c> in IST would drop
/// orders placed between 00:00 and 05:29 IST (which fall on 2026-06-12 in UTC).
///
/// <para>Ported from the legacy Orders bounded context into the Logistics sub-area so the
/// rider-task feed handlers stay free of any cross-domain project reference.</para>
/// </summary>
public static class LocalDateRange
{
    /// <summary>The platform default timezone when no store-scoped timezone is available.</summary>
    public const string DefaultTimeZoneId = "Asia/Kolkata";

    /// <summary>
    /// Resolves an IANA timezone id to a <see cref="TimeZoneInfo"/>, falling back to the
    /// platform default (Asia/Kolkata) when the id is null/blank or cannot be resolved.
    /// </summary>
    public static TimeZoneInfo Resolve(string? timeZoneId)
    {
        if (!string.IsNullOrWhiteSpace(timeZoneId))
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
            catch (TimeZoneNotFoundException) { /* fall through to default */ }
            catch (InvalidTimeZoneException)  { /* fall through to default */ }
        }
        return TimeZoneInfo.FindSystemTimeZoneById(DefaultTimeZoneId);
    }

    /// <summary>
    /// Inclusive lower UTC bound for the given local calendar day: the UTC instant of
    /// local <paramref name="date"/> at 00:00. Use as <c>placed_at &gt;= StartUtc(...)</c>.
    /// </summary>
    public static DateTimeOffset StartUtc(DateOnly date, TimeZoneInfo tz)
    {
        var localMidnight = date.ToDateTime(TimeOnly.MinValue);
        return ToUtcInstant(localMidnight, tz);
    }

    /// <summary>
    /// Exclusive upper UTC bound for the given local calendar day: the UTC instant of the
    /// NEXT local day at 00:00. Use as <c>placed_at &lt; EndUtcExclusive(...)</c> so the full
    /// local day (including 23:59:59.999…) is covered without floating-point edge cases.
    /// </summary>
    public static DateTimeOffset EndUtcExclusive(DateOnly date, TimeZoneInfo tz)
    {
        var nextLocalMidnight = date.AddDays(1).ToDateTime(TimeOnly.MinValue);
        return ToUtcInstant(nextLocalMidnight, tz);
    }

    private static DateTimeOffset ToUtcInstant(DateTime localWallClock, TimeZoneInfo tz)
    {
        var unspecified = DateTime.SpecifyKind(localWallClock, DateTimeKind.Unspecified);
        var offset = tz.GetUtcOffset(unspecified);
        return new DateTimeOffset(unspecified, offset).ToUniversalTime();
    }
}
