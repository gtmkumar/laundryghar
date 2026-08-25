using laundryghar.SharedDataModel.Entities.Logistics;
using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using laundryghar.SharedDataModel.Persistence;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Logistics.RiderOps.Queries.GetRidersLive;
using operations.Infrastructure.Persistence;
using operations.Tests.Catalog.Import;
using Xunit;

namespace operations.Tests.Logistics;

/// <summary>
/// The dashboard's live rider board.
///
/// <para>These tests exist because of one screenshot: two riders shown as "on duty", one of them
/// carrying "1 order" while labelled "Idle", when neither had pinged in seventy-three days. Every
/// number on that panel came from a stored column that describes the past — <c>is_on_duty</c>,
/// <c>current_load</c> — and nothing reconciled them against what the legs actually said.</para>
///
/// <para>So what is pinned here is not "the handler returns rows". It is that the two fields a
/// dispatcher acts on cannot contradict the legs shown beside them.</para>
/// </summary>
public class GetRidersLiveTests
{
    private static readonly Guid Brand = Guid.NewGuid();

    // 1 ── THE BUG, exactly as it appeared: a rider holding a leg assigned days ago was shown as
    //      "1 order" AND "Idle" at once. Ops status was derived from TODAY's legs, so the leg was
    //      invisible to it while the load counter still saw it. Both now read the same open set,
    //      so the row cannot disagree with itself — and the rider reads as holding work, which is
    //      the difference between a leg getting chased and a leg sitting for seventy-three days.
    [Fact]
    public async Task A_leg_held_since_before_today_is_not_reported_as_idle()
    {
        var (db, raw) = NewDb();
        var r = RiderRow();
        raw.Add(r);
        raw.Add(Leg(r.Id, "assigned", assignedAt: DateTimeOffset.UtcNow.AddDays(-73)));
        await raw.SaveChangesAsync();

        var rider = Assert.Single(await RunAsync(db));

        Assert.Equal(1, rider.CurrentLoad);
        Assert.Equal("assigned", rider.OpsStatus);
        Assert.NotEqual("idle", rider.OpsStatus);
    }

    // 1b ── "idle" now means genuinely FREE. Anything else, and dispatch is told to hand more work
    //       to someone who already has some.
    [Fact]
    public async Task Idle_means_no_open_leg_at_all()
    {
        var (db, raw) = NewDb();
        var r = RiderRow(currentLoad: 7);   // a drifted counter must not invent work either
        raw.Add(r);
        raw.Add(Leg(r.Id, "completed"));
        await raw.SaveChangesAsync();

        var rider = Assert.Single(await RunAsync(db));

        Assert.Equal(0, rider.CurrentLoad);
        Assert.Equal("idle", rider.OpsStatus);
    }

    // 1c ── an in-progress leg still outranks a merely-assigned one: a rider carrying two legs is
    //       described by the one they are actually doing.
    [Fact]
    public async Task An_in_progress_leg_outranks_one_that_is_only_assigned()
    {
        var (db, raw) = NewDb();
        var r = RiderRow();
        raw.Add(r);
        raw.AddRange(Leg(r.Id, "assigned"), Leg(r.Id, "started"));
        await raw.SaveChangesAsync();

        var rider = Assert.Single(await RunAsync(db));

        Assert.Equal(2, rider.CurrentLoad);
        Assert.Equal("on_the_way", rider.OpsStatus);
    }

    // 1d ── the load count never disagrees with the status, whatever the leg states.
    [Theory]
    [InlineData("assigned",  "assigned")]
    [InlineData("accepted",  "assigned")]
    [InlineData("started",   "on_the_way")]
    [InlineData("arrived",   "arrived")]
    public async Task Holding_a_leg_is_never_reported_as_idle(string legStatus, string expectedOps)
    {
        var (db, raw) = NewDb();
        var r = RiderRow();
        raw.Add(r);
        raw.Add(Leg(r.Id, legStatus, legType: "delivery"));
        await raw.SaveChangesAsync();

        var rider = Assert.Single(await RunAsync(db));

        Assert.Equal(1, rider.CurrentLoad);
        Assert.Equal(expectedOps, rider.OpsStatus);
    }

    [Fact]
    public async Task A_drifted_current_load_does_not_invent_work()
    {
        var (db, raw) = NewDb();
        raw.Add(RiderRow(currentLoad: 7));   // no legs at all behind it
        await raw.SaveChangesAsync();

        var rider = Assert.Single(await RunAsync(db));

        Assert.Equal(0, rider.CurrentLoad);
        Assert.Equal("idle", rider.OpsStatus);
    }

    // 2 ── the load is the OPEN legs, whatever their age. This is the case a date-bounded count
    //      would get wrong: a leg handed out yesterday and never closed is work the rider is
    //      holding right now, and it must not vanish from the board at midnight.
    [Fact]
    public async Task An_open_leg_from_a_previous_day_still_counts_as_load()
    {
        var (db, raw) = NewDb();
        var r = RiderRow();
        raw.Add(r);
        raw.Add(Leg(r.Id, "assigned", assignedAt: DateTimeOffset.UtcNow.AddDays(-2)));
        await raw.SaveChangesAsync();

        var rider = Assert.Single(await RunAsync(db));

        Assert.Equal(1, rider.CurrentLoad);
    }

    // 3 ── every state that has been handed back releases the load. Getting this list wrong in the
    //      other direction is the drift the stored counter already suffers from.
    [Theory]
    [InlineData("completed")]
    [InlineData("cancelled")]
    [InlineData("failed")]
    public async Task A_closed_leg_is_not_load(string status)
    {
        var (db, raw) = NewDb();
        var r = RiderRow(currentLoad: 3);
        raw.Add(r);
        raw.Add(Leg(r.Id, status));
        await raw.SaveChangesAsync();

        var rider = Assert.Single(await RunAsync(db));

        Assert.Equal(0, rider.CurrentLoad);
    }

    // 4 ── and every state that has NOT been handed back is load, including the two that the
    //      today-only throughput query never looks at ("assigned"/"accepted" are not in-progress).
    [Theory]
    [InlineData("assigned")]
    [InlineData("accepted")]
    [InlineData("started")]
    [InlineData("arrived")]
    public async Task An_open_leg_is_load(string status)
    {
        var (db, raw) = NewDb();
        var r = RiderRow();
        raw.Add(r);
        raw.Add(Leg(r.Id, status));
        await raw.SaveChangesAsync();

        var rider = Assert.Single(await RunAsync(db));

        Assert.Equal(1, rider.CurrentLoad);
    }

    // 5 ── load is counted PER RIDER. A dictionary keyed wrong would still satisfy every test
    //      above with a single rider in the set.
    [Fact]
    public async Task Load_is_attributed_to_the_rider_who_holds_it()
    {
        var (db, raw) = NewDb();
        var busy = RiderRow(code: "R-BUSY");
        var free = RiderRow(code: "R-FREE", currentLoad: 9);
        raw.AddRange(busy, free);
        raw.AddRange(Leg(busy.Id, "assigned"), Leg(busy.Id, "started"));
        await raw.SaveChangesAsync();

        var byCode = (await RunAsync(db)).ToDictionary(r => r.RiderCode);

        Assert.Equal(2, byCode["R-BUSY"].CurrentLoad);
        Assert.Equal(0, byCode["R-FREE"].CurrentLoad);
    }

    // 6 ── the freshness flag the dashboard now counts on. `is_on_duty` is a flag a rider sets and
    //      nothing clears, so it cannot mean "available" on its own; IsStale is what separates a
    //      rider who is working from one who closed the app in June.
    [Fact]
    public async Task A_rider_who_has_not_pinged_recently_is_stale_even_while_flagged_on_duty()
    {
        var (db, raw) = NewDb();
        raw.Add(RiderRow(code: "R-GONE",  lastPingAt: DateTimeOffset.UtcNow.AddDays(-73)));
        raw.Add(RiderRow(code: "R-HERE",  lastPingAt: DateTimeOffset.UtcNow.AddMinutes(-1)));
        raw.Add(RiderRow(code: "R-NEVER", neverPinged: true));
        await raw.SaveChangesAsync();

        var byCode = (await RunAsync(db)).ToDictionary(r => r.RiderCode);

        Assert.True(byCode["R-GONE"].IsOnDuty);          // still flagged on…
        Assert.True(byCode["R-GONE"].IsStale);           // …but out of contact
        Assert.False(byCode["R-HERE"].IsStale);
        Assert.True(byCode["R-NEVER"].IsStale);          // never pinged is not "fresh"
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static (IOperationsDbContext Db, LaundryGharDbContext Raw) NewDb()
    {
        var options = new DbContextOptionsBuilder<LaundryGharDbContext>()
            .UseInMemoryDatabase($"riders-live-{Guid.NewGuid():N}")
            .Options;
        var raw = new LaundryGharDbContext(options);
        return (new OperationsDbContext(raw), raw);
    }

    private static async Task<List<Dtos>> RunAsync(IOperationsDbContext db)
    {
        var handler = new GetRidersLiveQueryHandler(db, new ImportTestSupport.FakeCurrentUser(Brand));
        var rows = await handler.HandleAsync(new GetRidersLiveQuery(null), CancellationToken.None);
        return rows.Select(r => new Dtos(r.RiderCode, r.CurrentLoad, r.IsOnDuty, r.IsStale, r.OpsStatus)).ToList();
    }

    private sealed record Dtos(string RiderCode, int CurrentLoad, bool IsOnDuty, bool IsStale, string OpsStatus);

    // `neverPinged` is a separate flag rather than `lastPingAt: null` on purpose: null is this
    // helper's "caller didn't say", and collapsing the two meanings made the never-pinged rider
    // silently get a one-minute-old ping — the assertion caught it, which is the point of having
    // one for a case that looks too obvious to test.
    private static Rider RiderRow(
        string code = "R-001", int currentLoad = 0,
        DateTimeOffset? lastPingAt = null, bool neverPinged = false) => new()
    {
        Id = Guid.NewGuid(), UserId = Guid.NewGuid(), BrandId = Brand, FranchiseId = Guid.NewGuid(),
        RiderCode = code, EmploymentType = "employee", VehicleType = "bike",
        DailyPickupCapacity = 20, DailyDeliveryCapacity = 20, ServiceRadiusKm = 5m,
        IsOnline = true, IsOnDuty = true, CurrentLoad = currentLoad,
        LastPingAt = neverPinged ? null : lastPingAt ?? DateTimeOffset.UtcNow.AddMinutes(-1),
        KycStatus = "verified", VehicleVerificationStatus = "approved",
        Status = "active", Metadata = "{}",
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static DeliveryAssignment Leg(
        Guid riderId, string status, DateTimeOffset? assignedAt = null,
        string legType = "delivery") => new()
    {
        Id = Guid.NewGuid(), BrandId = Brand, StoreId = Guid.NewGuid(), RiderId = riderId,
        LegType = legType, Status = status,
        AssignedAt = assignedAt ?? DateTimeOffset.UtcNow,
        AddressSnapshot = "{}", Metadata = "{}",
    };
}
