using operations.Application.Fulfillment.Recurring;
using Xunit;

namespace operations.Tests.Fulfillment;

/// <summary>
/// The delivery calendar for §3 Mode 3. An off-by-one here is invisible in review and glaring to a
/// customer — "you charged me for Sunday and nothing came" — so the boundaries get the attention:
/// the ISO/NET weekday mismatch, the start/end bounds, and schedules that can never fire.
/// </summary>
public class DeliveryCadenceTests
{
    // 2026-08-24 is a Monday; the week below runs Mon..Sun.
    private static readonly DateOnly Mon = new(2026, 8, 24);
    private static DateOnly Day(int offset) => Mon.AddDays(offset);
    private static readonly DateOnly Sat = Day(5);
    private static readonly DateOnly Sun = Day(6);

    // 1 ── the trap: .NET numbers Sunday 0, ISO numbers it 7. Getting this wrong silently shifts a
    //      whole schedule by a day, or drops Sunday entirely.
    [Fact]
    public void sunday_is_iso_day_seven_not_zero()
    {
        Assert.True(DeliveryCadence.OccursOn(Sun, DeliveryCadence.Weekly, new[] { 7 }));
        Assert.False(DeliveryCadence.OccursOn(Sun, DeliveryCadence.Weekly, new[] { 0 }));
        Assert.True(DeliveryCadence.OccursOn(Mon, DeliveryCadence.Weekly, new[] { 1 }));
    }

    [Fact]
    public void daily_delivers_every_day_including_the_weekend()
    {
        for (var i = 0; i < 7; i++)
            Assert.True(DeliveryCadence.OccursOn(Day(i), DeliveryCadence.Daily, null));
    }

    [Fact]
    public void weekdays_excludes_saturday_and_sunday()
    {
        for (var i = 0; i < 5; i++)
            Assert.True(DeliveryCadence.OccursOn(Day(i), DeliveryCadence.Weekdays, null));

        Assert.False(DeliveryCadence.OccursOn(Sat, DeliveryCadence.Weekdays, null));
        Assert.False(DeliveryCadence.OccursOn(Sun, DeliveryCadence.Weekdays, null));
    }

    [Fact]
    public void a_custom_pattern_delivers_only_on_its_days()
    {
        var monWedFri = new[] { 1, 3, 5 };

        Assert.True(DeliveryCadence.OccursOn(Day(0), DeliveryCadence.Custom, monWedFri)); // Mon
        Assert.False(DeliveryCadence.OccursOn(Day(1), DeliveryCadence.Custom, monWedFri)); // Tue
        Assert.True(DeliveryCadence.OccursOn(Day(2), DeliveryCadence.Custom, monWedFri)); // Wed
        Assert.True(DeliveryCadence.OccursOn(Day(4), DeliveryCadence.Custom, monWedFri)); // Fri
        Assert.False(DeliveryCadence.OccursOn(Sun, DeliveryCadence.Custom, monWedFri));
    }

    // 2 ── a schedule that can never fire must deliver NOTHING rather than everything. Failing open
    //      here would silently bill a customer for deliveries nobody agreed to.
    [Theory]
    [InlineData(DeliveryCadence.Weekly)]
    [InlineData(DeliveryCadence.Custom)]
    public void a_pattern_with_no_days_never_occurs(string cadence)
    {
        Assert.False(DeliveryCadence.OccursOn(Mon, cadence, null));
        Assert.False(DeliveryCadence.OccursOn(Mon, cadence, []));
    }

    [Fact]
    public void an_unknown_cadence_never_occurs()
    {
        Assert.False(DeliveryCadence.OccursOn(Mon, "fortnightly-ish", new[] { 1 }));
    }

    // 3 ── bounds. A schedule must not deliver before it starts or after it ends.
    [Fact]
    public void nothing_is_delivered_before_the_start_date()
    {
        var next = DeliveryCadence.NextOccurrence(
            from: Mon, cadence: DeliveryCadence.Daily, daysOfWeek: null,
            startsOn: Day(3), endsOn: null);

        Assert.Equal(Day(3), next);
    }

    [Fact]
    public void nothing_is_delivered_after_the_end_date()
    {
        var next = DeliveryCadence.NextOccurrence(
            from: Day(3), cadence: DeliveryCadence.Daily, daysOfWeek: null,
            startsOn: Mon, endsOn: Day(2));

        Assert.Null(next);
    }

    [Fact]
    public void an_impossible_schedule_terminates_instead_of_looping()
    {
        // `custom` with no days can never fire; NextOccurrence must give up, not scan forever.
        var next = DeliveryCadence.NextOccurrence(
            from: Mon, cadence: DeliveryCadence.Custom, daysOfWeek: [],
            startsOn: Mon, endsOn: null);

        Assert.Null(next);
    }

    // 4 ── the range the generator actually materialises.
    [Fact]
    public void occurrences_lists_every_delivery_date_in_the_window()
    {
        var dates = DeliveryCadence.Occurrences(
            from: Mon, to: Sun, cadence: DeliveryCadence.Custom, daysOfWeek: [1, 3, 5],
            startsOn: Mon, endsOn: null);

        Assert.Equal([Day(0), Day(2), Day(4)], dates);
    }

    [Fact]
    public void occurrences_is_clamped_by_both_bounds()
    {
        var dates = DeliveryCadence.Occurrences(
            from: Mon, to: Sun, cadence: DeliveryCadence.Daily, daysOfWeek: null,
            startsOn: Day(2), endsOn: Day(4));

        Assert.Equal([Day(2), Day(3), Day(4)], dates);
    }

    [Fact]
    public void an_inverted_window_yields_nothing()
    {
        Assert.Empty(DeliveryCadence.Occurrences(
            from: Sun, to: Mon, cadence: DeliveryCadence.Daily, daysOfWeek: null,
            startsOn: Mon, endsOn: null));
    }
}
