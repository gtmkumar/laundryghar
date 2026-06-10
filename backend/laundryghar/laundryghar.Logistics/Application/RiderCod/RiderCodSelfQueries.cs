using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Logistics.Application.RiderCod;

// ── Self-facing DTOs ─────────────────────────────────────────────────────────

/// <summary>Rider's own COD cash summary: outstanding balance + recent settlements.</summary>
public sealed record RiderCashSummaryDto(
    decimal CashInHand,
    DateTimeOffset? LastSettlementAt,
    IReadOnlyList<RiderCashSettlementItemDto> RecentSettlements);

/// <summary>One settlement row for the rider's own cash screen (amount + when).</summary>
public sealed record RiderCashSettlementItemDto(
    DateTimeOffset SettledAt,
    decimal Amount);

// ── GET /api/v1/rider/cash/summary ──────────────────────────────────────────

public sealed record GetMyCashSummaryQuery(Guid UserId, Guid BrandId) : IRequest<RiderCashSummaryDto?>;

public sealed class GetMyCashSummaryHandler : IRequestHandler<GetMyCashSummaryQuery, RiderCashSummaryDto?>
{
    private readonly LaundryGharDbContext _db;
    public GetMyCashSummaryHandler(LaundryGharDbContext db) => _db = db;

    public async Task<RiderCashSummaryDto?> Handle(GetMyCashSummaryQuery q, CancellationToken ct)
    {
        // Resolve this rider's internal id from the JWT userId claim.
        var rider = await _db.Riders
            .Where(r => r.UserId == q.UserId && r.BrandId == q.BrandId)
            .Select(r => new { r.Id })
            .FirstOrDefaultAsync(ct);
        if (rider is null) return null;

        // Cash in hand = sum of COD amounts on delivery legs that have NOT been settled.
        var cashInHand = await _db.DeliveryAssignments.AsNoTracking()
            .Where(d => d.BrandId == q.BrandId && d.RiderId == rider.Id
                     && d.CodAmount != null && d.SettlementId == null)
            .SumAsync(d => (decimal?)d.CodAmount ?? 0m, ct);

        // Last 10 settlements for this rider (newest first).
        var settlements = await _db.RiderSettlements.AsNoTracking()
            .Where(s => s.BrandId == q.BrandId && s.RiderId == rider.Id)
            .OrderByDescending(s => s.SettledAt)
            .Take(10)
            .Select(s => new { s.SettledAt, s.TotalAmount })
            .ToListAsync(ct);

        var lastSettlementAt = settlements.Count > 0 ? settlements[0].SettledAt : (DateTimeOffset?)null;

        var recent = settlements
            .Select(s => new RiderCashSettlementItemDto(s.SettledAt, s.TotalAmount))
            .ToList();

        return new RiderCashSummaryDto(cashInHand, lastSettlementAt, recent);
    }
}
