using laundryghar.SharedDataModel.Entities.Logistics;
using laundryghar.Utilities.Services;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Logistics.RiderOps.Dtos;

namespace operations.Application.Logistics.RiderOps.Queries.GetRidersLive;

// ── Live board: all riders + current location/status + today's throughput ───────

public sealed record GetRidersLiveQuery(Guid? FranchiseId) : IQuery<List<RiderLiveDto>>;

public sealed class GetRidersLiveQueryHandler : IQueryHandler<GetRidersLiveQuery, List<RiderLiveDto>>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    public GetRidersLiveQueryHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    // A rider whose last ping is older than this is shown as "stale" (likely app
    // backgrounded / GPS off) even if still flagged on-duty.
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    // A leg the rider is still carrying. Everything else (completed / failed / cancelled) has
    // been handed back.
    private static readonly string[] OpenLegStatuses = ["assigned", "accepted", "started", "arrived"];

    public async Task<List<RiderLiveDto>> HandleAsync(GetRidersLiveQuery query, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        var now     = DateTimeOffset.UtcNow;
        var (dayStart, dayEnd) = RiderOpsTime.IstRangeUtc(RiderOpsTime.TodayIst(), RiderOpsTime.TodayIst());

        var ridersQ = _db.Riders.Where(r => r.BrandId == brandId && r.Status != "terminated");

        // Franchise scoping (defense-in-depth) — mirrors GetRidersHandler.
        if (_user.FranchiseId is Guid actorFid)
            ridersQ = ridersQ.Where(r => r.FranchiseId == actorFid);
        else if (query.FranchiseId.HasValue)
            ridersQ = ridersQ.Where(r => r.FranchiseId == query.FranchiseId.Value);

        var riders = await ridersQ
            .Select(r => new
            {
                r.Id, r.UserId, r.RiderCode, r.Status, r.IsOnDuty,
                r.LastKnownLocation, r.LastPingAt,
            })
            .ToListAsync(cancellationToken);

        if (riders.Count == 0) return [];

        var riderIds = riders.Select(r => r.Id).ToList();
        var userIds  = riders.Select(r => r.UserId).Distinct().ToList();

        // Names for the markers / list.
        var profiles = await _db.UserProfiles.AsNoTracking()
            .Where(p => userIds.Contains(p.UserId))
            .Select(p => new { p.UserId, Name = ((p.FirstName ?? "") + " " + (p.LastName ?? "")).Trim() })
            .ToListAsync(cancellationToken);
        var phones = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.PhoneE164 })
            .ToListAsync(cancellationToken);
        var nameMap  = profiles.ToDictionary(p => p.UserId, p => p.Name);
        var phoneMap = phones.ToDictionary(u => u.Id, u => u.PhoneE164);

        // TODAY's legs, for throughput only — "how much has this rider got through since
        // midnight IST". Nothing about the rider's CURRENT state may be derived from this set;
        // see the open-legs query below for why.
        var todaysLegs = await _db.DeliveryAssignments.AsNoTracking()
            .Where(d => d.BrandId == brandId
                     && riderIds.Contains(d.RiderId)
                     && d.AssignedAt >= dayStart && d.AssignedAt < dayEnd)
            .Select(d => new { d.RiderId, d.LegType, d.Status })
            .ToListAsync(cancellationToken);
        var todayByRider = todaysLegs.GroupBy(l => l.RiderId).ToDictionary(g => g.Key, g => g.ToList());

        // OPEN legs — what each rider is holding right now, at any age. Both the load count and
        // the ops status come from here, and that is the fix for a board that contradicted itself.
        //
        // Both used to be wrong in the same place, in opposite directions:
        //
        //   • Load was read from riders.current_load. That column is the auto-dispatch capacity
        //     gate (AutoDispatchService weighs it against DailyDeliveryCapacity) and it IS
        //     maintained — RiderLoad.Increment on assign, Decrement on completed/failed/cancelled,
        //     atomic and floored at zero. But it is a counter kept in step by convention, and a
        //     counter can drift: a direct SQL seed writes it, and a crash between the save that
        //     closes a leg and the decrement that follows leaves it high forever.
        //
        //   • Ops status was derived from TODAY's legs. A rider holding a leg assigned last
        //     Tuesday had no leg in today's set, so the board called them "idle" — ready for
        //     work — while they were still carrying a pickup nobody had chased.
        //
        //   Together those produced the row that started this: "1 order" beside "Idle", which is
        //   not a state a rider can be in. Reading both from the same set of open legs means the
        //   two halves of the row can no longer disagree, whatever the counter says and however
        //   old the work is.
        var openLegs = await _db.DeliveryAssignments.AsNoTracking()
            .Where(d => d.BrandId == brandId
                     && riderIds.Contains(d.RiderId)
                     && OpenLegStatuses.Contains(d.Status))
            .Select(d => new { d.RiderId, d.LegType, d.Status, d.OrderId, d.CollectedAt, d.DroppedAt })
            .ToListAsync(cancellationToken);
        var openByRider = openLegs.GroupBy(l => l.RiderId).ToDictionary(g => g.Key, g => g.ToList());

        // Order numbers for the legs we will actually surface as active.
        var activeOrderIds = openLegs.Where(l => l.OrderId.HasValue)
            .Select(l => l.OrderId!.Value).Distinct().ToList();
        var orderNumberMap = activeOrderIds.Count == 0
            ? []
            : await _db.Orders.AsNoTracking()
                .Where(o => activeOrderIds.Contains(o.Id))
                .Select(o => new { o.Id, o.OrderNumber })
                .ToDictionaryAsync(o => o.Id, o => o.OrderNumber, cancellationToken);

        var result = new List<RiderLiveDto>(riders.Count);
        foreach (var r in riders)
        {
            openByRider.TryGetValue(r.Id, out var open);
            open ??= [];
            todayByRider.TryGetValue(r.Id, out var today);
            today ??= [];

            // Active leg: the furthest-along one the rider holds. On site beats en route beats
            // merely handed over — and that last fallback is the point, because a leg sitting in
            // 'assigned' IS work even though nobody has moved yet.
            var active = open.FirstOrDefault(l => l.Status == "arrived")
                      ?? open.FirstOrDefault(l => l.Status == "started")
                      ?? open.FirstOrDefault();

            var lastPing = r.LastPingAt;
            var isStale  = lastPing is null || (now - lastPing.Value) > StaleAfter;

            // A collected pickup that's en route to the store reads as "to_store";
            // otherwise an in-progress leg is on_the_way (to customer) or arrived (on site).
            string opsStatus;
            if (!r.IsOnDuty) opsStatus = "offline";
            else if (active is null) opsStatus = "idle";
            // Holding work they have not started. Distinct from "idle" on purpose: idle means
            // free to be dispatched, and a rider sitting on an unstarted leg is neither free nor
            // in motion. Collapsing the two is what let a leg go unchased for seventy-three days
            // while the board reported the rider as available.
            else if (active.Status is "assigned" or "accepted") opsStatus = "assigned";
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
                r.Status, r.IsOnDuty, open.Count,
                r.LastKnownLocation?.Y, r.LastKnownLocation?.X,
                lastPing, isStale,
                opsStatus,
                active?.LegType, active?.OrderId, activeOrderNumber,
                PickupsToday:   today.Count(l => l.LegType == "pickup"   && l.Status == "completed"),
                DeliveriesToday: today.Count(l => l.LegType == "delivery" && l.Status == "completed")));
        }

        // Moving first (to customer, then to store), then on-site, idle, offline.
        static int Rank(string s) => s switch
        { "on_the_way" => 0, "to_store" => 1, "arrived" => 2, "assigned" => 3, "idle" => 4, _ => 5 };
        return result
            .OrderBy(x => Rank(x.OpsStatus))
            .ThenByDescending(x => x.LastPingAt ?? DateTimeOffset.MinValue)
            .ToList();
    }
}
