using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.Logistics;
using laundryghar.Utilities.Common;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;

namespace operations.Application.Logistics.Cod;

// ── Query ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Paged settlement history for one rider, newest handover first. Each row is projected to
/// <see cref="RiderSettlementDto"/> with the store name resolved set-based for the page.
/// RLS scopes by brand; the explicit brand filter is defense-in-depth.
/// </summary>
public sealed record GetRiderSettlementsQuery(Guid BrandId, Guid RiderId, int Page, int PageSize)
    : IQuery<PaginatedList<RiderSettlementDto>>;

public sealed class GetRiderSettlementsHandler
    : IQueryHandler<GetRiderSettlementsQuery, PaginatedList<RiderSettlementDto>>
{
    private readonly IOperationsDbContext _db;

    public GetRiderSettlementsHandler(IOperationsDbContext db) => _db = db;

    public async Task<PaginatedList<RiderSettlementDto>> HandleAsync(GetRiderSettlementsQuery query, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;

        // Paginate the raw entity in SQL, then project + name-resolve in memory.
        var source = _db.RiderSettlements.AsNoTracking()
            .Where(s => s.BrandId == query.BrandId && s.RiderId == query.RiderId)
            .OrderByDescending(s => s.SettledAt);

        var page = await PaginatedList<RiderSettlement>.CreateAsync(source, query.Page, query.PageSize, ct);

        // Resolve store display names for just this page's rows (cross lookup, display-only).
        var storeIds = page.List.Where(s => s.StoreId != null).Select(s => s.StoreId!.Value).Distinct().ToList();
        var storeNames = storeIds.Count == 0
            ? []
            : await _db.Stores.AsNoTracking()
                .Where(s => storeIds.Contains(s.Id))
                .Select(s => new { s.Id, s.Name })
                .ToListAsync(ct);
        string? StoreNameFor(Guid? id) =>
            id is null ? null : storeNames.FirstOrDefault(s => s.Id == id.Value)?.Name;

        return page.Map(s => new RiderSettlementDto(
            s.Id, s.RiderId, s.StoreId, StoreNameFor(s.StoreId),
            s.TotalAmount, s.CollectionCount, s.Reference, s.Status,
            s.SettledAt, s.SettledBy, s.Notes));
    }
}
