using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Logistics.Application.RiderOps;

// ── Shared helpers ──────────────────────────────────────────────────────────────

internal static class RiderOpsTime
{
    // Laundry Ghar operates in IST (UTC+5:30, no DST). "Today" on the ops board and
    // in productivity stats is the IST calendar day, matched to mv_rider_performance
    // which groups on DATE(assigned_at AT TIME ZONE 'Asia/Kolkata').
    internal static readonly TimeSpan Ist = TimeSpan.FromHours(5.5);

    internal static DateOnly TodayIst()
        => DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(Ist).Date);

    /// <summary>UTC half-open range [start, end) covering the given IST calendar days.</summary>
    internal static (DateTimeOffset startUtc, DateTimeOffset endUtc) IstRangeUtc(DateOnly fromIst, DateOnly toIst)
    {
        var start = new DateTimeOffset(fromIst.ToDateTime(TimeOnly.MinValue), Ist).ToUniversalTime();
        var end   = new DateTimeOffset(toIst.AddDays(1).ToDateTime(TimeOnly.MinValue), Ist).ToUniversalTime();
        return (start, end);
    }
}

// ── Live board: all riders + current location/status + today's throughput ───────

public sealed record GetRidersLiveQuery(Guid? FranchiseId) : IRequest<List<RiderLiveDto>>;

public sealed class GetRidersLiveHandler : IRequestHandler<GetRidersLiveQuery, List<RiderLiveDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;
    public GetRidersLiveHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    // A rider whose last ping is older than this is shown as "stale" (likely app
    // backgrounded / GPS off) even if still flagged on-duty.
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    public async Task<List<RiderLiveDto>> Handle(GetRidersLiveQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var now     = DateTimeOffset.UtcNow;
        var (dayStart, dayEnd) = RiderOpsTime.IstRangeUtc(RiderOpsTime.TodayIst(), RiderOpsTime.TodayIst());

        var ridersQ = _db.Riders.Where(r => r.BrandId == brandId && r.Status != "terminated");

        // Franchise scoping (defense-in-depth) — mirrors GetRidersHandler.
        if (_user.FranchiseId is Guid actorFid)
            ridersQ = ridersQ.Where(r => r.FranchiseId == actorFid);
        else if (q.FranchiseId.HasValue)
            ridersQ = ridersQ.Where(r => r.FranchiseId == q.FranchiseId.Value);

        var riders = await ridersQ
            .Select(r => new
            {
                r.Id, r.UserId, r.RiderCode, r.Status, r.IsOnDuty, r.CurrentLoad,
                r.LastKnownLocation, r.LastPingAt,
            })
            .ToListAsync(ct);

        if (riders.Count == 0) return [];

        var riderIds = riders.Select(r => r.Id).ToList();
        var userIds  = riders.Select(r => r.UserId).Distinct().ToList();

        // Names for the markers / list.
        var profiles = await _db.UserProfiles.AsNoTracking()
            .Where(p => userIds.Contains(p.UserId))
            .Select(p => new { p.UserId, Name = ((p.FirstName ?? "") + " " + (p.LastName ?? "")).Trim() })
            .ToListAsync(ct);
        var phones = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.PhoneE164 })
            .ToListAsync(ct);
        var nameMap  = profiles.ToDictionary(p => p.UserId, p => p.Name);
        var phoneMap = phones.ToDictionary(u => u.Id, u => u.PhoneE164);

        // All of today's legs for these riders, in one query, then grouped in memory:
        //  - the in-progress leg (status started/arrived) drives OpsStatus,
        //  - completed legs are counted by type.
        var legs = await _db.DeliveryAssignments.AsNoTracking()
            .Where(d => d.BrandId == brandId
                     && riderIds.Contains(d.RiderId)
                     && d.AssignedAt >= dayStart && d.AssignedAt < dayEnd)
            .Select(d => new { d.RiderId, d.LegType, d.Status, d.OrderId, d.CollectedAt, d.DroppedAt })
            .ToListAsync(ct);

        // Resolve order numbers only for the active legs we'll surface.
        var activeOrderIds = legs
            .Where(l => l.Status is "started" or "arrived" && l.OrderId.HasValue)
            .Select(l => l.OrderId!.Value).Distinct().ToList();
        var orderNumberMap = activeOrderIds.Count == 0
            ? []
            : await _db.Orders.AsNoTracking()
                .Where(o => activeOrderIds.Contains(o.Id))
                .Select(o => new { o.Id, o.OrderNumber })
                .ToDictionaryAsync(o => o.Id, o => o.OrderNumber, ct);

        var legsByRider = legs.GroupBy(l => l.RiderId).ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<RiderLiveDto>(riders.Count);
        foreach (var r in riders)
        {
            legsByRider.TryGetValue(r.Id, out var rl);
            rl ??= [];

            // Active leg: prefer one that's 'arrived' (on site) over 'started' (en route).
            var active = rl.Where(l => l.Status == "arrived").FirstOrDefault()
                      ?? rl.Where(l => l.Status == "started").FirstOrDefault();

            var lastPing = r.LastPingAt;
            var isStale  = lastPing is null || (now - lastPing.Value) > StaleAfter;

            // A collected pickup that's en route to the store reads as "to_store";
            // otherwise an in-progress leg is on_the_way (to customer) or arrived (on site).
            string opsStatus;
            if (!r.IsOnDuty) opsStatus = "offline";
            else if (active is null) opsStatus = "idle";
            else if (active.Status == "arrived")
                opsStatus = active.LegType == "pickup" && active.CollectedAt is not null && active.DroppedAt is null
                    ? "to_store" : "arrived";
            else opsStatus = "on_the_way";

            string? activeOrderNumber = active?.OrderId is Guid oid && orderNumberMap.TryGetValue(oid, out var onum)
                ? onum : null;

            var rawName = nameMap.TryGetValue(r.UserId, out var n) ? n : null;

            result.Add(new RiderLiveDto(
                r.Id, r.RiderCode,
                string.IsNullOrWhiteSpace(rawName) ? null : rawName,
                phoneMap.TryGetValue(r.UserId, out var ph) ? ph : null,
                r.Status, r.IsOnDuty, r.CurrentLoad,
                r.LastKnownLocation?.Y, r.LastKnownLocation?.X,
                lastPing, isStale,
                opsStatus,
                active?.LegType, active?.OrderId, activeOrderNumber,
                PickupsToday:   rl.Count(l => l.LegType == "pickup"   && l.Status == "completed"),
                DeliveriesToday: rl.Count(l => l.LegType == "delivery" && l.Status == "completed")));
        }

        // Moving first (to customer, then to store), then on-site, idle, offline.
        static int Rank(string s) => s switch
        { "on_the_way" => 0, "to_store" => 1, "arrived" => 2, "idle" => 3, _ => 4 };
        return result
            .OrderBy(x => Rank(x.OpsStatus))
            .ThenByDescending(x => x.LastPingAt ?? DateTimeOffset.MinValue)
            .ToList();
    }
}

// ── Breadcrumb trail for one rider on a given IST day ────────────────────────────

public sealed record GetRiderTrackQuery(Guid RiderId, DateOnly? Date) : IRequest<List<RiderTrackPointDto>?>;

public sealed class GetRiderTrackHandler : IRequestHandler<GetRiderTrackQuery, List<RiderTrackPointDto>?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;
    public GetRiderTrackHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    // Hard cap so a chatty device can't return an unbounded payload to the map.
    private const int MaxPoints = 1500;

    public async Task<List<RiderTrackPointDto>?> Handle(GetRiderTrackQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();

        // Authorize the rider is in this brand/franchise first (404 otherwise).
        var rider = await _db.Riders
            .Where(r => r.Id == q.RiderId && r.BrandId == brandId)
            .Select(r => new { r.FranchiseId })
            .FirstOrDefaultAsync(ct);
        if (rider is null) return null;
        if (_user.FranchiseId is Guid fid && rider.FranchiseId != fid) return null;

        var day = q.Date ?? RiderOpsTime.TodayIst();
        var (startUtc, endUtc) = RiderOpsTime.IstRangeUtc(day, day);

        // Materialize the Point column first, then read X/Y in memory. The pings
        // column is `geography`, and ST_Y/ST_X (what EF emits for .Y/.X) only exist
        // for `geometry` — projecting them in SQL throws 42883. NTS maps the column
        // to a Point on read, so .Y (lat) / .X (lng) are free client-side.
        var pts = await _db.RiderLocationPings.AsNoTracking()
            .Where(p => p.RiderId == q.RiderId && p.BrandId == brandId
                     && p.PingedAt >= startUtc && p.PingedAt < endUtc)
            .OrderBy(p => p.PingedAt)
            .Take(MaxPoints)
            .Select(p => new { p.Location, p.PingedAt, p.SpeedKmph, p.IsMoving })
            .ToListAsync(ct);

        return pts
            .Select(p => new RiderTrackPointDto(p.Location.Y, p.Location.X, p.PingedAt, p.SpeedKmph, p.IsMoving))
            .ToList();
    }
}

// ── Per-rider productivity over a date range ─────────────────────────────────────

public sealed record GetRiderStatsQuery(Guid RiderId, DateOnly? From, DateOnly? To) : IRequest<RiderStatsDto?>;

public sealed class GetRiderStatsHandler : IRequestHandler<GetRiderStatsQuery, RiderStatsDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;
    public GetRiderStatsHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<RiderStatsDto?> Handle(GetRiderStatsQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();

        var rider = await _db.Riders
            .Where(r => r.Id == q.RiderId && r.BrandId == brandId)
            .Select(r => new { r.Id, r.UserId, r.RiderCode, r.FranchiseId })
            .FirstOrDefaultAsync(ct);
        if (rider is null) return null;
        if (_user.FranchiseId is Guid fid && rider.FranchiseId != fid) return null;

        var today = RiderOpsTime.TodayIst();
        var from  = q.From ?? today;
        var to    = q.To   ?? today;
        if (to < from) (from, to) = (to, from);
        var (startUtc, endUtc) = RiderOpsTime.IstRangeUtc(from, to);

        var legs = await _db.DeliveryAssignments.AsNoTracking()
            .Where(d => d.BrandId == brandId && d.RiderId == q.RiderId
                     && d.AssignedAt >= startUtc && d.AssignedAt < endUtc)
            .Select(d => new { d.LegType, d.Status, d.DistanceKm, d.CodAmount })
            .ToListAsync(ct);

        var name = await _db.UserProfiles.AsNoTracking()
            .Where(p => p.UserId == rider.UserId)
            .Select(p => ((p.FirstName ?? "") + " " + (p.LastName ?? "")).Trim())
            .FirstOrDefaultAsync(ct);

        return new RiderStatsDto(
            rider.Id, rider.RiderCode,
            string.IsNullOrWhiteSpace(name) ? null : name,
            from, to,
            PickupsDone:    legs.Count(l => l.LegType == "pickup"   && l.Status == "completed"),
            DeliveriesDone: legs.Count(l => l.LegType == "delivery" && l.Status == "completed"),
            AssignmentsTotal:  legs.Count,
            AssignmentsFailed: legs.Count(l => l.Status is "failed" or "cancelled"),
            TotalKm:        legs.Where(l => l.Status == "completed").Sum(l => l.DistanceKm ?? 0m),
            CodCollected:   legs.Sum(l => l.CodAmount ?? 0m),  // Phase 3
            Earnings:       0m);  // Phase 4
    }
}
