using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Logistics.Application.RiderCod;

// ── Outstanding COD across riders (reconciliation list) ──────────────────────────

public sealed record GetCodOutstandingQuery(Guid? FranchiseId) : IRequest<List<RiderCodSummaryDto>>;

public sealed class GetCodOutstandingHandler : IRequestHandler<GetCodOutstandingQuery, List<RiderCodSummaryDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;
    public GetCodOutstandingHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<List<RiderCodSummaryDto>> Handle(GetCodOutstandingQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();

        // Outstanding = collected on a delivery leg, not yet cleared by a settlement.
        var grouped = await _db.DeliveryAssignments.AsNoTracking()
            .Where(d => d.BrandId == brandId && d.CodAmount != null && d.SettlementId == null)
            .GroupBy(d => d.RiderId)
            .Select(g => new
            {
                RiderId = g.Key,
                Amount  = g.Sum(x => x.CodAmount) ?? 0m,
                Count   = g.Count(),
                Oldest  = g.Min(x => x.CodCollectedAt),
            })
            .ToListAsync(ct);
        if (grouped.Count == 0) return [];

        var riderIds = grouped.Select(g => g.RiderId).ToList();
        var ridersQ  = _db.Riders.Where(r => r.BrandId == brandId && riderIds.Contains(r.Id));
        if (_user.FranchiseId is Guid fid) ridersQ = ridersQ.Where(r => r.FranchiseId == fid);

        var riders = await ridersQ.AsNoTracking()
            .Select(r => new { r.Id, r.UserId, r.RiderCode, r.FranchiseId })
            .ToListAsync(ct);
        var riderMap = riders.ToDictionary(r => r.Id);

        var userIds = riders.Select(r => r.UserId).Distinct().ToList();
        var nameMap = (await _db.UserProfiles.AsNoTracking()
            .Where(p => userIds.Contains(p.UserId))
            .Select(p => new { p.UserId, Name = ((p.FirstName ?? "") + " " + (p.LastName ?? "")).Trim() })
            .ToListAsync(ct)).ToDictionary(p => p.UserId, p => p.Name);

        var franchiseIds = riders.Select(r => r.FranchiseId).Distinct().ToList();
        var franchiseMap = (await _db.Franchises.AsNoTracking()
            .Where(f => franchiseIds.Contains(f.Id))
            .Select(f => new { f.Id, Name = f.DisplayName ?? f.LegalName })
            .ToListAsync(ct)).ToDictionary(f => f.Id, f => f.Name);

        return grouped
            // Drop riders filtered out by franchise scoping.
            .Where(g => riderMap.ContainsKey(g.RiderId))
            .Select(g =>
            {
                var r = riderMap[g.RiderId];
                var name = nameMap.TryGetValue(r.UserId, out var n) && !string.IsNullOrWhiteSpace(n) ? n : null;
                var fn = franchiseMap.TryGetValue(r.FranchiseId, out var f) ? f : null;
                return new RiderCodSummaryDto(r.Id, r.RiderCode, name, fn, g.Amount, g.Count, g.Oldest);
            })
            .OrderByDescending(x => x.OutstandingAmount)
            .ToList();
    }
}

// ── One rider's outstanding collections (detail) ─────────────────────────────────

public sealed record GetRiderCodDetailQuery(Guid RiderId) : IRequest<RiderCodDetailDto?>;

public sealed class GetRiderCodDetailHandler : IRequestHandler<GetRiderCodDetailQuery, RiderCodDetailDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;
    public GetRiderCodDetailHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<RiderCodDetailDto?> Handle(GetRiderCodDetailQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var rider = await _db.Riders
            .Where(r => r.Id == q.RiderId && r.BrandId == brandId)
            .Select(r => new { r.Id, r.UserId, r.RiderCode, r.FranchiseId })
            .FirstOrDefaultAsync(ct);
        if (rider is null) return null;
        if (_user.FranchiseId is Guid fid && rider.FranchiseId != fid) return null;

        var rows = await _db.DeliveryAssignments.AsNoTracking()
            .Where(d => d.BrandId == brandId && d.RiderId == q.RiderId
                     && d.CodAmount != null && d.SettlementId == null)
            .OrderBy(d => d.CodCollectedAt)
            .Select(d => new { d.Id, d.OrderId, d.CodAmount, d.CodCollectedAt })
            .ToListAsync(ct);

        var orderIds = rows.Where(x => x.OrderId.HasValue).Select(x => x.OrderId!.Value).Distinct().ToList();
        var orderNumbers = orderIds.Count == 0
            ? []
            : await _db.Orders.AsNoTracking()
                .Where(o => orderIds.Contains(o.Id))
                .Select(o => new { o.Id, o.OrderNumber })
                .ToDictionaryAsync(o => o.Id, o => o.OrderNumber, ct);

        var collections = rows.Select(x => new CodCollectionDto(
            x.Id, x.OrderId,
            x.OrderId is Guid oid && orderNumbers.TryGetValue(oid, out var on) ? on : null,
            x.CodAmount ?? 0m,
            x.CodCollectedAt ?? default)).ToList();

        var name = await _db.UserProfiles.AsNoTracking()
            .Where(p => p.UserId == rider.UserId)
            .Select(p => ((p.FirstName ?? "") + " " + (p.LastName ?? "")).Trim())
            .FirstOrDefaultAsync(ct);

        return new RiderCodDetailDto(
            rider.Id, rider.RiderCode,
            string.IsNullOrWhiteSpace(name) ? null : name,
            collections.Sum(c => c.Amount), collections.Count, collections);
    }
}

// ── A rider's settlement history ─────────────────────────────────────────────────

public sealed record GetRiderSettlementsQuery(Guid RiderId, int Page, int PageSize)
    : IRequest<PaginatedList<RiderSettlementDto>?>;

public sealed class GetRiderSettlementsHandler
    : IRequestHandler<GetRiderSettlementsQuery, PaginatedList<RiderSettlementDto>?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;
    public GetRiderSettlementsHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<PaginatedList<RiderSettlementDto>?> Handle(GetRiderSettlementsQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var rider = await _db.Riders
            .Where(r => r.Id == q.RiderId && r.BrandId == brandId)
            .Select(r => new { r.FranchiseId })
            .FirstOrDefaultAsync(ct);
        if (rider is null) return null;
        if (_user.FranchiseId is Guid fid && rider.FranchiseId != fid) return null;

        var query = _db.RiderSettlements
            .Where(s => s.BrandId == brandId && s.RiderId == q.RiderId)
            .OrderByDescending(s => s.SettledAt);

        var page = await PaginatedList<RiderSettlement>.CreateAsync(query, q.Page, q.PageSize, ct);
        if (page.List.Count == 0) return page.Map(_ => default(RiderSettlementDto)!);

        var storeIds = page.List.Where(s => s.StoreId.HasValue).Select(s => s.StoreId!.Value).Distinct().ToList();
        var storeNames = storeIds.Count == 0
            ? []
            : await _db.Stores.AsNoTracking()
                .Where(s => storeIds.Contains(s.Id))
                .Select(s => new { s.Id, s.Name })
                .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        return page.Map(s => new RiderSettlementDto(
            s.Id, s.RiderId, s.StoreId,
            s.StoreId is Guid sid && storeNames.TryGetValue(sid, out var sn) ? sn : null,
            s.TotalAmount, s.CollectionCount, s.Reference, s.Status, s.SettledAt, s.SettledBy, s.Notes));
    }
}
