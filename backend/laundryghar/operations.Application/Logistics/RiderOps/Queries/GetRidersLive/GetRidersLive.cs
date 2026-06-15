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
                r.Id, r.UserId, r.RiderCode, r.Status, r.IsOnDuty, r.CurrentLoad,
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

        // All of today's legs for these riders, in one query, then grouped in memory:
        //  - the in-progress leg (status started/arrived) drives OpsStatus,
        //  - completed legs are counted by type.
        var legs = await _db.DeliveryAssignments.AsNoTracking()
            .Where(d => d.BrandId == brandId
                     && riderIds.Contains(d.RiderId)
                     && d.AssignedAt >= dayStart && d.AssignedAt < dayEnd)
            .Select(d => new { d.RiderId, d.LegType, d.Status, d.OrderId, d.CollectedAt, d.DroppedAt })
            .ToListAsync(cancellationToken);

        // Resolve order numbers only for the active legs we'll surface.
        var activeOrderIds = legs
            .Where(l => l.Status is "started" or "arrived" && l.OrderId.HasValue)
            .Select(l => l.OrderId!.Value).Distinct().ToList();
        var orderNumberMap = activeOrderIds.Count == 0
            ? []
            : await _db.Orders.AsNoTracking()
                .Where(o => activeOrderIds.Contains(o.Id))
                .Select(o => new { o.Id, o.OrderNumber })
                .ToDictionaryAsync(o => o.Id, o => o.OrderNumber, cancellationToken);

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
