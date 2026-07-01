using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;

namespace operations.Application.Logistics.Cod;

// ── DTOs ──────────────────────────────────────────────────────────────────────

/// <summary>Per-rider outstanding-COD summary: cash the rider collected on
/// pickup/delivery legs that has not yet been cleared by a rider settlement.</summary>
public sealed record RiderCodOutstandingDto(
    Guid RiderId,
    string? RiderName,
    int UnclearedCount,
    decimal OutstandingAmount);

/// <summary>Outstanding-COD reconciliation view: per-rider breakdown plus grand totals.</summary>
public sealed record CodOutstandingResponse(
    IReadOnlyList<RiderCodOutstandingDto> Riders,
    int TotalCount,
    decimal TotalOutstanding);

// ── Query ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Computes OUTSTANDING COD for the brand — cash a rider has collected in the field
/// but has not yet remitted/settled. A delivery assignment counts as outstanding when
/// it holds collected cash (<c>cod_amount &gt; 0</c> and <c>cod_collected_at IS NOT NULL</c>)
/// that has not been cleared by a settlement (<c>settlement_id IS NULL</c>).
/// Grouped and summed per rider, with a grand total. Optionally narrowed to a single rider.
/// RLS scopes by brand; the explicit brand filter is defense-in-depth.
/// </summary>
public sealed record GetCodOutstandingQuery(Guid BrandId, Guid? RiderId)
    : IQuery<CodOutstandingResponse>;

public sealed class GetCodOutstandingHandler
    : IQueryHandler<GetCodOutstandingQuery, CodOutstandingResponse>
{
    private readonly IOperationsDbContext _db;

    public GetCodOutstandingHandler(IOperationsDbContext db) => _db = db;

    public async Task<CodOutstandingResponse> HandleAsync(GetCodOutstandingQuery query, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;

        // Aggregate uncleared collected cash per rider on the DB side (no row hydration).
        //   cod_amount        > 0     → cash is due on this leg
        //   cod_collected_at  IS NOT NULL → cash was physically collected by the rider
        //   settlement_id     IS NULL → the collection has not been cleared by a settlement
        var perRider = await _db.DeliveryAssignments.AsNoTracking()
            .Where(d => d.BrandId == query.BrandId
                     && d.CodAmount > 0m
                     && d.CodCollectedAt != null
                     && d.SettlementId == null
                     && (query.RiderId == null || d.RiderId == query.RiderId))
            .GroupBy(d => d.RiderId)
            .Select(g => new
            {
                RiderId = g.Key,
                UnclearedCount = g.Count(),
                OutstandingAmount = g.Sum(x => x.CodAmount ?? 0m),
            })
            .ToListAsync(ct);

        // Resolve rider display names (riders → user profile). Cross-schema, so a
        // separate set-based lookup rather than a join in the aggregate projection.
        var riderIds = perRider.Select(r => r.RiderId).Distinct().ToList();

        var riderUsers = riderIds.Count == 0
            ? []
            : await _db.Riders.AsNoTracking()
                .Where(r => riderIds.Contains(r.Id))
                .Select(r => new { r.Id, r.UserId })
                .ToListAsync(ct);

        var userIds = riderUsers.Select(x => x.UserId).Distinct().ToList();

        var names = userIds.Count == 0
            ? []
            : await _db.UserProfiles.AsNoTracking()
                .Where(p => userIds.Contains(p.UserId))
                .Select(p => new { p.UserId, Name = ((p.FirstName ?? "") + " " + (p.LastName ?? "")).Trim() })
                .ToListAsync(ct);

        string? NameFor(Guid riderId)
        {
            var uid = riderUsers.FirstOrDefault(x => x.Id == riderId)?.UserId;
            var n = uid is null ? null : names.FirstOrDefault(x => x.UserId == uid)?.Name;
            return string.IsNullOrWhiteSpace(n) ? null : n;
        }

        var riders = perRider
            .OrderByDescending(r => r.OutstandingAmount)
            .Select(r => new RiderCodOutstandingDto(
                r.RiderId, NameFor(r.RiderId), r.UnclearedCount, r.OutstandingAmount))
            .ToList();

        var totalCount = riders.Sum(r => r.UnclearedCount);
        var totalOutstanding = riders.Sum(r => r.OutstandingAmount);

        return new CodOutstandingResponse(riders, totalCount, totalOutstanding);
    }
}
