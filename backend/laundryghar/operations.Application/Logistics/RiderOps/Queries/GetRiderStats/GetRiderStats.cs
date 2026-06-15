using laundryghar.SharedDataModel.Entities.Logistics;
using laundryghar.Utilities.Services;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Logistics.Common;
using operations.Application.Logistics.RiderOps.Dtos;

namespace operations.Application.Logistics.RiderOps.Queries.GetRiderStats;

// ── Per-rider productivity over a date range ─────────────────────────────────────

public sealed record GetRiderStatsQuery(Guid RiderId, DateOnly? From, DateOnly? To) : IQuery<RiderStatsDto?>;

public sealed class GetRiderStatsQueryHandler : IQueryHandler<GetRiderStatsQuery, RiderStatsDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    public GetRiderStatsQueryHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<RiderStatsDto?> HandleAsync(GetRiderStatsQuery query, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();

        var rider = await _db.Riders
            .Where(r => r.Id == query.RiderId && r.BrandId == brandId)
            .Select(r => new { r.Id, r.UserId, r.RiderCode, r.FranchiseId })
            .FirstOrDefaultAsync(cancellationToken);
        if (rider is null) return null;
        if (_user.FranchiseId is Guid fid && rider.FranchiseId != fid) return null;

        var today = RiderOpsTime.TodayIst();
        var from  = query.From ?? today;
        var to    = query.To   ?? today;
        if (to < from) (from, to) = (to, from);
        var (startUtc, endUtc) = RiderOpsTime.IstRangeUtc(from, to);

        var legs = await _db.DeliveryAssignments.AsNoTracking()
            .Where(d => d.BrandId == brandId && d.RiderId == query.RiderId
                     && d.AssignedAt >= startUtc && d.AssignedAt < endUtc)
            .Select(d => new { d.LegType, d.Status, d.DistanceKm, d.CodAmount, d.PayoutAmount })
            .ToListAsync(cancellationToken);

        var name = await _db.UserProfiles.AsNoTracking()
            .Where(p => p.UserId == rider.UserId)
            .Select(p => ((p.FirstName ?? "") + " " + (p.LastName ?? "")).Trim())
            .FirstOrDefaultAsync(cancellationToken);

        // Distance actually travelled, from the GPS breadcrumb: haversine over
        // consecutive pings in the window. This reflects real movement even when no
        // leg has completed yet (the old `sum(distance_km) over completed legs` read
        // 0.0 km for an active-but-unfinished shift). Falls back to completed-leg
        // distance when there are no pings (GPS off / older data).
        var trackedKm = await TrackedDistanceKmAsync(query.RiderId, brandId, startUtc, endUtc, cancellationToken);
        var completedLegKm = legs.Where(l => l.Status == "completed").Sum(l => l.DistanceKm ?? 0m);

        return new RiderStatsDto(
            rider.Id, rider.RiderCode,
            string.IsNullOrWhiteSpace(name) ? null : name,
            from, to,
            PickupsDone:    legs.Count(l => l.LegType == "pickup"   && l.Status == "completed"),
            DeliveriesDone: legs.Count(l => l.LegType == "delivery" && l.Status == "completed"),
            AssignmentsTotal:  legs.Count,
            AssignmentsFailed: legs.Count(l => l.Status is "failed" or "cancelled"),
            TotalKm:        trackedKm > 0m ? trackedKm : completedLegKm,
            CodCollected:   legs.Sum(l => l.CodAmount ?? 0m),     // Phase 3
            Earnings:       legs.Sum(l => l.PayoutAmount ?? 0m)); // Phase 4
    }

    /// <summary>
    /// Distance travelled (km) summed from the rider's GPS breadcrumb over the window.
    /// Steps under ~15 m are dropped as GPS jitter (pings are ~25 s apart, so a parked
    /// rider would otherwise accrue phantom drift). Materialises the geography Point
    /// first — ST_Y/ST_X are geometry-only, so reading .Y/.X happens client-side.
    /// </summary>
    private async Task<decimal> TrackedDistanceKmAsync(
        Guid riderId, Guid brandId, DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken ct)
    {
        var points = await _db.RiderLocationPings.AsNoTracking()
            .Where(p => p.RiderId == riderId && p.BrandId == brandId
                     && p.PingedAt >= startUtc && p.PingedAt < endUtc)
            .OrderBy(p => p.PingedAt)
            .Select(p => p.Location)
            .ToListAsync(ct);

        const double minStepMeters = 15.0;
        double meters = 0;
        for (var i = 1; i < points.Count; i++)
        {
            var step = GeofenceEvaluator.DistanceMeters(
                points[i - 1].Y, points[i - 1].X, points[i].Y, points[i].X);
            if (step >= minStepMeters) meters += step;
        }
        return Math.Round((decimal)(meters / 1000.0), 2);
    }
}
