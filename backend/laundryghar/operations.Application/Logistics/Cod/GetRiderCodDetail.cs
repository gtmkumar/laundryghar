using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;

namespace operations.Application.Logistics.Cod;

// ── DTOs ──────────────────────────────────────────────────────────────────────

/// <summary>One uncleared COD collection: cash the rider took on a single pickup/delivery
/// leg that has not yet been swept into a settlement.</summary>
public sealed record CodCollection(
    Guid AssignmentId,
    Guid? OrderId,
    string? OrderNumber,
    decimal Amount,
    DateTimeOffset CollectedAt);

/// <summary>A single rider's uncleared COD position: the cash still in hand plus the
/// individual collections that make it up. Drives the admin "Rider Cash / COD" drawer.</summary>
public sealed record RiderCodDetail(
    Guid RiderId,
    string RiderCode,
    string? RiderName,
    decimal OutstandingAmount,
    int OutstandingCount,
    IReadOnlyList<CodCollection> Collections);

// ── Query ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Returns one rider's uncleared COD cash and its component collections. "Uncleared" is the
/// same predicate as <see cref="GetCodOutstandingQuery"/>: <c>cod_amount &gt; 0</c>,
/// <c>cod_collected_at IS NOT NULL</c> and <c>settlement_id IS NULL</c>. A rider with nothing
/// outstanding still returns a valid detail (amount/count 0, empty list) — the drawer opens for
/// any rider on the outstanding board. Missing rider → null (404). RLS scopes by brand; the
/// explicit brand filter is defense-in-depth.
/// </summary>
public sealed record GetRiderCodDetailQuery(Guid BrandId, Guid RiderId)
    : IQuery<RiderCodDetail?>;

public sealed class GetRiderCodDetailHandler
    : IQueryHandler<GetRiderCodDetailQuery, RiderCodDetail?>
{
    private readonly IOperationsDbContext _db;

    public GetRiderCodDetailHandler(IOperationsDbContext db) => _db = db;

    public async Task<RiderCodDetail?> HandleAsync(GetRiderCodDetailQuery query, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;

        // Resolve the rider (brand-scoped) up front so we can echo its code/name and 404 cleanly.
        var rider = await _db.Riders.AsNoTracking()
            .Where(r => r.Id == query.RiderId && r.BrandId == query.BrandId)
            .Select(r => new { r.Id, r.RiderCode, r.UserId })
            .FirstOrDefaultAsync(ct);
        if (rider is null) return null;

        // Uncleared collections for this rider, oldest cash first.
        var rows = await _db.DeliveryAssignments.AsNoTracking()
            .Where(d => d.BrandId == query.BrandId
                     && d.RiderId == query.RiderId
                     && d.CodAmount > 0m
                     && d.CodCollectedAt != null
                     && d.SettlementId == null)
            .OrderBy(d => d.CodCollectedAt)
            .Select(d => new { d.Id, d.OrderId, d.CodAmount, d.CodCollectedAt })
            .ToListAsync(ct);

        // Resolve order numbers set-based (orders is partitioned on (id, created_at); filtering by
        // id alone is fine for a display-name lookup).
        var orderIds = rows.Where(r => r.OrderId != null).Select(r => r.OrderId!.Value).Distinct().ToList();
        var orderNumbers = orderIds.Count == 0
            ? []
            : await _db.Orders.AsNoTracking()
                .Where(o => orderIds.Contains(o.Id))
                .Select(o => new { o.Id, o.OrderNumber })
                .ToListAsync(ct);
        string? OrderNumberFor(Guid? id) =>
            id is null ? null : orderNumbers.FirstOrDefault(o => o.Id == id.Value)?.OrderNumber;

        // Rider display name (riders → user profile). Cross-schema, so a separate lookup.
        var name = await _db.UserProfiles.AsNoTracking()
            .Where(p => p.UserId == rider.UserId)
            .Select(p => ((p.FirstName ?? "") + " " + (p.LastName ?? "")).Trim())
            .FirstOrDefaultAsync(ct);
        var riderName = string.IsNullOrWhiteSpace(name) ? null : name;

        var collections = rows
            .Select(r => new CodCollection(
                r.Id, r.OrderId, OrderNumberFor(r.OrderId),
                r.CodAmount ?? 0m, r.CodCollectedAt!.Value))
            .ToList();

        return new RiderCodDetail(
            rider.Id, rider.RiderCode, riderName,
            collections.Sum(c => c.Amount), collections.Count, collections);
    }
}
