namespace operations.Application.Fulfillment.Recurring;

/// <summary>
/// Works out which dates a recurring schedule delivers on — §3 Mode 3's "repeat delivery calendar".
///
/// <para>Pure and date-only on purpose. This is the one piece of Mode 3 where an off-by-one is
/// invisible in code review and obvious to a customer ("you charged me for Sunday and nothing came"),
/// so it is separated from all I/O and tested directly rather than only through the generator.</para>
/// </summary>
public static class DeliveryCadence
{
    public const string Daily    = "daily";
    public const string Weekdays = "weekdays";
    public const string Weekly   = "weekly";
    public const string Custom   = "custom";

    /// <summary>
    /// Does <paramref name="date"/> fall on this schedule?
    /// </summary>
    /// <param name="daysOfWeek">
    /// ISO-8601 weekday numbers — 1 = Monday … 7 = Sunday. Deliberately ISO rather than
    /// <see cref="DayOfWeek"/>, whose Sunday is 0: the schedule is authored by humans in a UI where
    /// the week starts on Monday, and storing the .NET numbering would put the conversion in every
    /// caller instead of here.
    /// </param>
    public static bool OccursOn(DateOnly date, string cadence, IReadOnlyCollection<int>? daysOfWeek) =>
        cadence switch
        {
            Daily    => true,
            Weekdays => IsoDayOf(date) <= 5,                       // Mon–Fri
            Weekly or Custom => daysOfWeek is { Count: > 0 } && daysOfWeek.Contains(IsoDayOf(date)),
            _        => false,                                     // unknown cadence delivers nothing
        };

    /// <summary>
    /// The next delivery date on or after <paramref name="from"/>, or null when the schedule has
    /// run out. Bounded by <paramref name="endsOn"/> and by a hard scan limit so an
    /// impossible schedule (e.g. `custom` with no days) terminates instead of looping forever.
    /// </summary>
    public static DateOnly? NextOccurrence(
        DateOnly from, string cadence, IReadOnlyCollection<int>? daysOfWeek,
        DateOnly startsOn, DateOnly? endsOn)
    {
        var cursor = from < startsOn ? startsOn : from;

        // A week is enough to find any weekly pattern; the extra day covers the inclusive bound.
        for (var i = 0; i <= 8; i++, cursor = cursor.AddDays(1))
        {
            if (endsOn is { } end && cursor > end) return null;
            if (OccursOn(cursor, cadence, daysOfWeek)) return cursor;
        }

        return null;
    }

    /// <summary>
    /// Every delivery date in <paramref name="from"/>..<paramref name="to"/> inclusive — what the
    /// generator materialises orders for, and what a customer sees as "your upcoming deliveries".
    /// </summary>
    public static IReadOnlyList<DateOnly> Occurrences(
        DateOnly from, DateOnly to, string cadence, IReadOnlyCollection<int>? daysOfWeek,
        DateOnly startsOn, DateOnly? endsOn)
    {
        var result = new List<DateOnly>();
        if (to < from) return result;

        var start = from < startsOn ? startsOn : from;
        var finish = endsOn is { } end && end < to ? end : to;

        for (var d = start; d <= finish; d = d.AddDays(1))
            if (OccursOn(d, cadence, daysOfWeek))
                result.Add(d);

        return result;
    }

    /// <summary>ISO-8601 weekday: Monday = 1 … Sunday = 7.</summary>
    private static int IsoDayOf(DateOnly date)
        => date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;
}
