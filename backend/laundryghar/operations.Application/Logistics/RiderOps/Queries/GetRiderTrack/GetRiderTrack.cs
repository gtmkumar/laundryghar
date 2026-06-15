using laundryghar.SharedDataModel.Entities.Logistics;
using laundryghar.Utilities.Services;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Logistics.RiderOps.Dtos;

namespace operations.Application.Logistics.RiderOps.Queries.GetRiderTrack;

// ── Breadcrumb trail for one rider on a given IST day ────────────────────────────

public sealed record GetRiderTrackQuery(Guid RiderId, DateOnly? Date) : IQuery<List<RiderTrackPointDto>?>;

public sealed class GetRiderTrackQueryHandler : IQueryHandler<GetRiderTrackQuery, List<RiderTrackPointDto>?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    public GetRiderTrackQueryHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    // Hard cap so a chatty device can't return an unbounded payload to the map.
    private const int MaxPoints = 1500;

    public async Task<List<RiderTrackPointDto>?> HandleAsync(GetRiderTrackQuery query, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();

        // Authorize the rider is in this brand/franchise first (404 otherwise).
        var rider = await _db.Riders
            .Where(r => r.Id == query.RiderId && r.BrandId == brandId)
            .Select(r => new { r.FranchiseId })
            .FirstOrDefaultAsync(cancellationToken);
        if (rider is null) return null;
        if (_user.FranchiseId is Guid fid && rider.FranchiseId != fid) return null;

        var day = query.Date ?? RiderOpsTime.TodayIst();
        var (startUtc, endUtc) = RiderOpsTime.IstRangeUtc(day, day);

        // Materialize the Point column first, then read X/Y in memory. The pings
        // column is `geography`, and ST_Y/ST_X (what EF emits for .Y/.X) only exist
        // for `geometry` — projecting them in SQL throws 42883. NTS maps the column
        // to a Point on read, so .Y (lat) / .X (lng) are free client-side.
        var pts = await _db.RiderLocationPings.AsNoTracking()
            .Where(p => p.RiderId == query.RiderId && p.BrandId == brandId
                     && p.PingedAt >= startUtc && p.PingedAt < endUtc)
            .OrderBy(p => p.PingedAt)
            .Take(MaxPoints)
            .Select(p => new { p.Location, p.PingedAt, p.SpeedKmph, p.IsMoving })
            .ToListAsync(cancellationToken);

        return pts
            .Select(p => new RiderTrackPointDto(p.Location.Y, p.Location.X, p.PingedAt, p.SpeedKmph, p.IsMoving))
            .ToList();
    }
}
